// MateScreenCapture — macOS native screen/window capture bundle for Unity.
//
// Exposes C-callable entry points (@_cdecl) that Unity's C# can P/Invoke against.
// Uses ScreenCaptureKit (macOS 12.3+ for enumeration, macOS 14.0+ for SCScreenshotManager).
//
// All async APIs are wrapped synchronously via DispatchSemaphore so callers
// (typically a background thread from C#) get a simple blocking interface.
//
// Memory ownership:
//   - String return values are allocated with strdup; caller must free via
//     mate_capture_free_string.
//   - PNG byte buffers are allocated with malloc; caller must free via
//     mate_capture_free_bytes.

import Foundation
import ScreenCaptureKit
import AppKit
import CoreImage

// MARK: - Shared helpers

@inline(__always)
private func log(_ msg: String) {
    NSLog("[MateCapture] %@", msg)
}

/// Synchronously fetches SCShareableContent. Returns nil on error/timeout.
private func fetchContent(timeout: TimeInterval = 5.0) -> SCShareableContent? {
    let sem = DispatchSemaphore(value: 0)
    var result: SCShareableContent? = nil
    Task {
        defer { sem.signal() }
        do {
            result = try await SCShareableContent.excludingDesktopWindows(
                false,
                onScreenWindowsOnly: true
            )
        } catch {
            log("SCShareableContent error: \(error.localizedDescription)")
        }
    }
    if sem.wait(timeout: .now() + timeout) == .timedOut {
        log("SCShareableContent timed out after \(timeout)s")
        return nil
    }
    return result
}

/// Serializes a JSON-compatible object to a strdup'd C string. Caller frees.
private func jsonCString(_ obj: Any) -> UnsafeMutablePointer<CChar>? {
    guard JSONSerialization.isValidJSONObject(obj),
          let data = try? JSONSerialization.data(withJSONObject: obj),
          let str = String(data: data, encoding: .utf8) else {
        return strdup("{\"items\":[]}")
    }
    return strdup(str)
}

// MARK: - Enumeration

@_cdecl("mate_capture_list_displays")
public func mate_capture_list_displays() -> UnsafeMutablePointer<CChar>? {
    guard let content = fetchContent() else {
        return strdup("{\"items\":[]}")
    }
    let items: [[String: Any]] = content.displays.map { d in
        [
            "id": UInt(d.displayID),
            "width": d.width,
            "height": d.height
        ]
    }
    return jsonCString(["items": items])
}

@_cdecl("mate_capture_list_windows")
public func mate_capture_list_windows() -> UnsafeMutablePointer<CChar>? {
    guard let content = fetchContent() else {
        return strdup("{\"items\":[]}")
    }
    let items: [[String: Any]] = content.windows.compactMap { w in
        guard let title = w.title, !title.isEmpty else { return nil }
        let app = w.owningApplication?.applicationName ?? ""
        let label = app.isEmpty ? title : "\(app) — \(title)"
        return [
            "id": UInt(w.windowID),
            "title": label,
            "width": Int(w.frame.width),
            "height": Int(w.frame.height)
        ]
    }
    return jsonCString(["items": items])
}

@_cdecl("mate_capture_free_string")
public func mate_capture_free_string(_ p: UnsafeMutablePointer<CChar>?) {
    free(p)
}

// MARK: - Capture

/// Wraps SCScreenshotManager.captureImage in a synchronous call.
@available(macOS 14.0, *)
private func captureImage(filter: SCContentFilter,
                          width: Int,
                          height: Int,
                          timeout: TimeInterval = 10.0) -> CGImage? {
    let cfg = SCStreamConfiguration()
    cfg.width = max(1, width)
    cfg.height = max(1, height)
    cfg.showsCursor = false

    let sem = DispatchSemaphore(value: 0)
    var img: CGImage? = nil
    Task {
        defer { sem.signal() }
        do {
            img = try await SCScreenshotManager.captureImage(
                contentFilter: filter,
                configuration: cfg
            )
        } catch {
            log("captureImage error: \(error.localizedDescription)")
        }
    }
    if sem.wait(timeout: .now() + timeout) == .timedOut {
        log("captureImage timed out after \(timeout)s")
        return nil
    }
    return img
}

private func cgImageToPNG(_ image: CGImage) -> Data? {
    let rep = NSBitmapImageRep(cgImage: image)
    return rep.representation(using: .png, properties: [:])
}

/// Allocates a malloc'd buffer with the data's bytes. Caller frees via mate_capture_free_bytes.
/// Returns 0 on failure, otherwise the byte length.
private func emitPNG(_ data: Data,
                     _ outBuf: UnsafeMutablePointer<UnsafeMutableRawPointer?>) -> Int32 {
    let len = data.count
    guard len > 0, let buf = malloc(len) else {
        outBuf.pointee = nil
        return 0
    }
    data.copyBytes(to: buf.assumingMemoryBound(to: UInt8.self), count: len)
    outBuf.pointee = buf
    return Int32(len)
}

@_cdecl("mate_capture_display_png")
public func mate_capture_display_png(
    _ displayId: UInt32,
    _ outBuf: UnsafeMutablePointer<UnsafeMutableRawPointer?>
) -> Int32 {
    outBuf.pointee = nil
    guard #available(macOS 14.0, *) else {
        log("SCScreenshotManager requires macOS 14.0+")
        return 0
    }
    guard let content = fetchContent(),
          let display = content.displays.first(where: { $0.displayID == displayId }) else {
        log("display \(displayId) not found")
        return 0
    }
    let filter = SCContentFilter(display: display, excludingWindows: [])
    guard let img = captureImage(filter: filter,
                                 width: display.width,
                                 height: display.height) else {
        return 0
    }
    guard let png = cgImageToPNG(img) else {
        log("PNG encode failed (display)")
        return 0
    }
    return emitPNG(png, outBuf)
}

@_cdecl("mate_capture_window_png")
public func mate_capture_window_png(
    _ windowId: UInt32,
    _ outBuf: UnsafeMutablePointer<UnsafeMutableRawPointer?>
) -> Int32 {
    outBuf.pointee = nil
    guard #available(macOS 14.0, *) else {
        log("SCScreenshotManager requires macOS 14.0+")
        return 0
    }
    guard let content = fetchContent(),
          let window = content.windows.first(where: { $0.windowID == windowId }) else {
        log("window \(windowId) not found")
        return 0
    }
    let filter = SCContentFilter(desktopIndependentWindow: window)
    let w = Int(window.frame.width)
    let h = Int(window.frame.height)
    guard let img = captureImage(filter: filter, width: w, height: h) else {
        return 0
    }
    guard let png = cgImageToPNG(img) else {
        log("PNG encode failed (window)")
        return 0
    }
    return emitPNG(png, outBuf)
}

@_cdecl("mate_capture_free_bytes")
public func mate_capture_free_bytes(_ p: UnsafeMutableRawPointer?) {
    free(p)
}
