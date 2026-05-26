# MateScreenCapture (macOS native screen capture plugin)

A small native Swift bundle that lets Mate-Engine capture macOS displays and
windows via ScreenCaptureKit, exposed to Unity through `@_cdecl` C entry
points and consumed by `ScreenCaptureManager.cs`.

The folder is suffixed `~` so Unity ignores it during asset import. The built
bundle lives at `Assets/Plugins/macOS/MateScreenCapture.bundle/`.

## Requirements

- Xcode Command Line Tools (`xcode-select --install`)
- macOS 12.3+ at runtime (enumeration); macOS 14.0+ for `SCScreenshotManager` capture

## Build

```bash
cd Mate-Engine/Plugins~/MateScreenCapture
./build.sh
```

This produces a universal (arm64 + x86_64) bundle at
`Mate-Engine/Assets/Plugins/macOS/MateScreenCapture.bundle/`.

After (re)building, in Unity: `unity-cli editor refresh --compile`.

## Permissions

The first time `mate_capture_*` runs, macOS will prompt to grant Unity.app
Screen Recording permission. Approve it in **System Settings → Privacy &
Security → Screen Recording**, then restart the Editor.

## Exported symbols

```c
char*  mate_capture_list_displays(void);           // JSON: {"items":[{id,width,height}]}
char*  mate_capture_list_windows(void);            // JSON: {"items":[{id,title,width,height}]}
void   mate_capture_free_string(char*);

int32_t mate_capture_display_png(uint32_t id, void** outBuf);  // returns PNG length, 0 on fail
int32_t mate_capture_window_png(uint32_t id, void** outBuf);
void    mate_capture_free_bytes(void*);
```

Memory ownership: all `out` pointers are malloc'd; the C# side must free with
the matching `mate_capture_free_*` symbol.
