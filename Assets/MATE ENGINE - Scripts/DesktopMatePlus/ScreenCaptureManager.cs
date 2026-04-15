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
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdcMon, ref rect, _) =>
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
            }, IntPtr.Zero);
            return result;
        }

        // ── 창 열거 (IsAltTabWindow 패턴) ─────────────────────────────

        public static List<ScreenCaptureSource> EnumerateWindows()
        {
            var result = new List<ScreenCaptureSource>();
            EnumWindows((hWnd, _) =>
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
    }
}
