using System;
using System.Runtime.InteropServices;

namespace OpenaiCompatibleAgent
{
    // RECT를 여기서 선언해 ScreenCaptureManager에서 재사용
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    public enum CaptureType { Monitor, Window }

    public class ScreenCaptureSource
    {
        public CaptureType Type;
        public int  MonitorIndex;   // CaptureType.Monitor 시 사용
        public RECT MonitorRect;    // EnumDisplayMonitors에서 획득한 물리 RECT
        public IntPtr WindowHandle; // CaptureType.Window 시 사용
        public string DisplayName;  // 상태바 표시용 ("Monitor 1 (1920×1080)", "Chrome - YouTube")
    }
}
