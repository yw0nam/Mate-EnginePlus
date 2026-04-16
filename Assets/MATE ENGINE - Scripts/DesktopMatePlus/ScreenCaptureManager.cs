using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace DesktopMatePlus
{
    public static class ScreenCaptureManager
    {
        // ── P/Invoke 선언 ──────────────────────────────────────────────

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

        // ── 모니터 열거 ────────────────────────────────────────────────

        public static List<ScreenCaptureSource> EnumerateMonitors()
        {
            var result = new List<ScreenCaptureSource>();
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
            return result;
        }

        // ── 창 열거 (IsAltTabWindow 패턴) ─────────────────────────────

        public static List<ScreenCaptureSource> EnumerateWindows()
        {
            var result = new List<ScreenCaptureSource>();
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
            return result;
        }

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

        // ── 캡처 ──────────────────────────────────────────────────────

        /// <summary>
        /// 백그라운드 STA 스레드에서 캡처 후 메인 스레드에서 Texture2D 반환.
        /// 실패 시 null 반환.
        /// </summary>
        public static async Awaitable<Texture2D> CaptureAsync(ScreenCaptureSource src)
        {
            byte[] rawPng = null;

            // GDI 캡처는 STA 스레드에서 실행
            var tcs = new System.Threading.Tasks.TaskCompletionSource<byte[]>();
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    rawPng = src.Type == CaptureType.Monitor
                        ? CaptureMonitor(src.MonitorRect)
                        : CaptureWindow(src.WindowHandle);
                    tcs.SetResult(rawPng);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DMP-Capture] 캡처 실패: {e.Message}");
                    tcs.SetResult(null);
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();

            byte[] bytes = await tcs.Task;
            if (bytes == null) return null;

            // Texture2D 생성은 메인 스레드에서
            await Awaitable.MainThreadAsync();
            var tex = new Texture2D(2, 2);
            tex.LoadImage(bytes);
            return tex;
        }

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
