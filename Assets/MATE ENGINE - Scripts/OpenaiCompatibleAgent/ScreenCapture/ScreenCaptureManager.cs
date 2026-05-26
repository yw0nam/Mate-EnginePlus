using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace OpenaiCompatibleAgent
{
    public static class ScreenCaptureManager
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // ── Win32 P/Invoke ────────────────────────────────────────────

        // RECT는 ScreenCaptureSource.cs에서 namespace 레벨로 선언됨 — 여기서 재선언 불필요

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPLACEMENT
        {
            public uint length;
            public uint flags;
            public uint showCmd;
            public int ptMinPositionX, ptMinPositionY;
            public int ptMaxPositionX, ptMaxPositionY;
            public RECT rcNormalPosition;
        }

        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]  static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        [DllImport("user32.dll")] static extern long GetWindowLongA(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
        [DllImport("gdi32.dll")]  static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]  static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        [DllImport("gdi32.dll")]  static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")]  static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")]  static extern bool DeleteDC(IntPtr hdc);

        const int  LOGPIXELSX      = 88;
        const int  GWL_EXSTYLE     = -20;
        const long WS_EX_TOOLWINDOW = 0x00000080L;
        const long WS_EX_APPWINDOW  = 0x00040000L;
        const uint GA_ROOTOWNER    = 3;
        const uint SW_SHOWMINIMIZED = 2;
        const uint PW_RENDERFULLCONTENT = 0x00000002;
#endif

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        // ── macOS native plugin (Plugins~/MateScreenCapture) ──────────
        // 빌드: Mate-Engine/Plugins~/MateScreenCapture/build.sh
        // 산출물: Assets/Plugins/macOS/MateScreenCapture.bundle

        const string MAC_LIB = "MateScreenCapture";

        [DllImport(MAC_LIB)] static extern IntPtr mate_capture_list_displays();
        [DllImport(MAC_LIB)] static extern IntPtr mate_capture_list_windows();
        [DllImport(MAC_LIB)] static extern void   mate_capture_free_string(IntPtr p);
        [DllImport(MAC_LIB)] static extern int    mate_capture_display_png(uint displayId, out IntPtr buf);
        [DllImport(MAC_LIB)] static extern int    mate_capture_window_png(uint windowId, out IntPtr buf);
        [DllImport(MAC_LIB)] static extern void   mate_capture_free_bytes(IntPtr p);

        [Serializable]
        class MacItem
        {
            public uint id;
            public int width;
            public int height;
            public string title;
        }

        [Serializable]
        class MacItemList
        {
            public MacItem[] items;
        }

        static MacItem[] CallListNative(Func<IntPtr> fn, string label)
        {
            IntPtr p = IntPtr.Zero;
            try { p = fn(); }
            catch (DllNotFoundException)
            {
                Debug.LogError($"[DMP-Capture] {MAC_LIB}.bundle not found. " +
                               "Build it via Mate-Engine/Plugins~/MateScreenCapture/build.sh");
                return Array.Empty<MacItem>();
            }
            if (p == IntPtr.Zero) return Array.Empty<MacItem>();
            try
            {
                string json = Marshal.PtrToStringUTF8(p) ?? "{\"items\":[]}";
                var list = JsonUtility.FromJson<MacItemList>(json);
                return list?.items ?? Array.Empty<MacItem>();
            }
            catch (Exception e)
            {
                Debug.LogError($"[DMP-Capture] {label} JSON parse failed: {e.Message}");
                return Array.Empty<MacItem>();
            }
            finally
            {
                mate_capture_free_string(p);
            }
        }

#endif

        // ── 모니터 열거 ────────────────────────────────────────────────

        public static List<ScreenCaptureSource> EnumerateMonitors()
        {
            var result = new List<ScreenCaptureSource>();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            int index = 0;

            bool Callback(IntPtr hMon, IntPtr hdcMon, ref RECT rect, IntPtr dwData)
            {
                var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMon, ref info))
                {
                    var r = info.rcMonitor;
                    int w = r.right - r.left;
                    int h = r.bottom - r.top;
                    result.Add(new ScreenCaptureSource
                    {
                        Type = CaptureType.Monitor,
                        MonitorIndex = index,
                        MonitorRect = r,
                        DisplayName = $"Monitor {index + 1} ({w}×{h})"
                    });
                    index++;
                }
                return true;
            }

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            var items = CallListNative(mate_capture_list_displays, "displays");
            for (int i = 0; i < items.Length; i++)
            {
                var d = items[i];
                result.Add(new ScreenCaptureSource
                {
                    Type = CaptureType.Monitor,
                    MonitorIndex = (int)d.id, // macOS: stash CGDirectDisplayID here
                    MonitorRect = new RECT { left = 0, top = 0, right = d.width, bottom = d.height },
                    DisplayName = $"Monitor {i + 1} ({d.width}×{d.height})"
                });
            }
#endif
            return result;
        }

        // ── 창 열거 ───────────────────────────────────────────────────

        public static List<ScreenCaptureSource> EnumerateWindows()
        {
            var result = new List<ScreenCaptureSource>();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsAltTabWindow(hWnd)) return true;
                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                result.Add(new ScreenCaptureSource
                {
                    Type = CaptureType.Window,
                    WindowHandle = hWnd,
                    DisplayName = sb.ToString()
                });
                return true;
            }, IntPtr.Zero);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            var items = CallListNative(mate_capture_list_windows, "windows");
            foreach (var w in items)
            {
                result.Add(new ScreenCaptureSource
                {
                    Type = CaptureType.Window,
                    WindowHandle = new IntPtr((long)w.id), // macOS: stash CGWindowID here
                    DisplayName = string.IsNullOrEmpty(w.title) ? $"Window {w.id}" : w.title
                });
            }
#endif
            return result;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        static bool IsAltTabWindow(IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd)) return false;
            if (GetWindowTextLength(hWnd) == 0) return false;

            // 최소화 창 제외
            var wp = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
            GetWindowPlacement(hWnd, ref wp);
            if (wp.showCmd == SW_SHOWMINIMIZED) return false;

            // 자식/팝업 창 제외: 루트 소유자가 자신이어야 함
            if (GetAncestor(hWnd, GA_ROOTOWNER) != hWnd) return false;

            long exStyle = GetWindowLongA(hWnd, GWL_EXSTYLE);
            bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
            bool isAppWindow  = (exStyle & WS_EX_APPWINDOW) != 0;

            if (isToolWindow && !isAppWindow) return false;

            return true;
        }
#endif

        // ── 캡처 ──────────────────────────────────────────────────────

        /// <summary>
        /// 백그라운드 스레드에서 캡처 후 메인 스레드에서 Texture2D 반환.
        /// 실패 시 null 반환.
        /// </summary>
        public static async Awaitable<Texture2D> CaptureAsync(ScreenCaptureSource src)
        {
            if (src == null) return null;

            var tcs = new System.Threading.Tasks.TaskCompletionSource<byte[]>();
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                    byte[] png = src.Type == CaptureType.Monitor
                        ? CaptureMonitor(src.MonitorRect)
                        : CaptureWindow(src.WindowHandle);
                    tcs.SetResult(png);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                    byte[] png = src.Type == CaptureType.Monitor
                        ? MacCapture((uint)src.MonitorIndex, isDisplay: true)
                        : MacCapture((uint)src.WindowHandle.ToInt64(), isDisplay: false);
                    tcs.SetResult(png);
#else
                    tcs.SetResult(null);
#endif
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DMP-Capture] 캡처 실패: {e.Message}");
                    tcs.SetResult(null);
                }
            });
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // GDI 캡처는 STA 스레드 필요
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
#endif
            thread.Start();

            byte[] bytes = await tcs.Task;
            if (bytes == null) return null;

            // Texture2D 생성은 메인 스레드에서
            await Awaitable.MainThreadAsync();
            var tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            return tex;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        static byte[] CaptureMonitor(RECT rect)
        {
            // DPI 보정: GetDeviceCaps로 배율 확인
            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            int dpiX = GetDeviceCaps(hdcScreen, LOGPIXELSX);
            ReleaseDC(IntPtr.Zero, hdcScreen);
            float scale = dpiX / 96f;

            int x = (int)(rect.left / scale);
            int y = (int)(rect.top  / scale);
            int w = (int)((rect.right  - rect.left) / scale);
            int h = (int)((rect.bottom - rect.top)  / scale);

            using var bmp = new System.Drawing.Bitmap(w, h);
            using var g   = System.Drawing.Graphics.FromImage(bmp);
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));

            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        static byte[] CaptureWindow(IntPtr hWnd)
        {
            var rect = new RECT();
            if (!GetWindowRect(hWnd, ref rect)) return null;
            int w = rect.right  - rect.left;
            int h = rect.bottom - rect.top;
            if (w <= 0 || h <= 0) return null;

            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            IntPtr hdcMem    = CreateCompatibleDC(hdcScreen);
            IntPtr hBitmap   = CreateCompatibleBitmap(hdcScreen, w, h);
            IntPtr hOld      = SelectObject(hdcMem, hBitmap);

            bool ok = PrintWindow(hWnd, hdcMem, PW_RENDERFULLCONTENT);

            byte[] result = null;
            if (ok)
            {
                using var bmp = System.Drawing.Image.FromHbitmap(hBitmap);
                using var ms  = new System.IO.MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                result = ms.ToArray();
            }

            SelectObject(hdcMem, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);

            return result;
        }
#endif

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        static byte[] MacCapture(uint id, bool isDisplay)
        {
            IntPtr buf;
            int len;
            try
            {
                len = isDisplay
                    ? mate_capture_display_png(id, out buf)
                    : mate_capture_window_png(id, out buf);
            }
            catch (DllNotFoundException)
            {
                Debug.LogError($"[DMP-Capture] {MAC_LIB}.bundle not found. " +
                               "Build it via Mate-Engine/Plugins~/MateScreenCapture/build.sh");
                return null;
            }

            if (len <= 0 || buf == IntPtr.Zero)
            {
                Debug.LogWarning("[DMP-Capture] macOS 캡처 실패 — Screen Recording 권한을 확인하세요 " +
                                 "(System Settings → Privacy & Security → Screen Recording)");
                return null;
            }

            try
            {
                var bytes = new byte[len];
                Marshal.Copy(buf, bytes, 0, len);
                return bytes;
            }
            finally
            {
                mate_capture_free_bytes(buf);
            }
        }
#endif

        // ── Base64 인코딩 ──────────────────────────────────────────────

        /// <summary>
        /// Texture2D → base64 PNG 문자열.
        /// maxBase64Bytes 초과 시 절반씩 최대 3회 리사이즈.
        /// 3회 후에도 초과 시 null 반환.
        /// </summary>
        public static string ToBase64PNG(Texture2D tex, int maxBase64Bytes = 5_000_000)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                byte[] png    = tex.EncodeToPNG();
                string b64    = Convert.ToBase64String(png);
                if (b64.Length <= maxBase64Bytes) return b64;

                if (attempt == 3) break;

                // 절반으로 리사이즈
                int newW = Mathf.Max(1, tex.width  / 2);
                int newH = Mathf.Max(1, tex.height / 2);
                var rt   = new RenderTexture(newW, newH, 0);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var resized = new Texture2D(newW, newH);
                resized.ReadPixels(new Rect(0, 0, newW, newH), 0, 0);
                resized.Apply();
                RenderTexture.active = prev;
                rt.Release();

                if (attempt > 0) UnityEngine.Object.Destroy(tex);
                tex = resized;
            }

            Debug.LogError("[DMP-Capture] 5MB 한도 초과, 캡처 취소");
            return null;
        }
    }
}
