#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MateEngine.Platform.Win32
{
    internal sealed class Win32AppShell : IPlatformAppShell
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int SW_RESTORE = 9;

        IntPtr _unityHWND = IntPtr.Zero;
        bool _isHidden;

        public bool IsSupported => true;
        public bool IsHiddenFromTaskbar => _isHidden;

        public void HideFromTaskbar()
        {
            EnsureHandle();
            if (_unityHWND == IntPtr.Zero) return;
            int exStyle = GetWindowLong(_unityHWND, GWL_EXSTYLE);
            SetWindowLong(_unityHWND, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            _isHidden = true;
        }

        public void ShowInTaskbar()
        {
            EnsureHandle();
            if (_unityHWND == IntPtr.Zero) return;
            int exStyle = GetWindowLong(_unityHWND, GWL_EXSTYLE);
            SetWindowLong(_unityHWND, GWL_EXSTYLE, exStyle & ~WS_EX_TOOLWINDOW);
            ShowWindow(_unityHWND, SW_RESTORE);
            SetForegroundWindow(_unityHWND);
            _isHidden = false;
        }

        public void BringToFront()
        {
            EnsureHandle();
            if (_unityHWND == IntPtr.Zero) return;
            ShowWindow(_unityHWND, SW_RESTORE);
            SetForegroundWindow(_unityHWND);
        }

        void EnsureHandle()
        {
            if (_unityHWND != IntPtr.Zero) return;
            string title = Application.productName;
            _unityHWND = FindWindow(null, title);
            if (_unityHWND == IntPtr.Zero) _unityHWND = FindWindow("UnityWndClass", null);
        }
    }
}
#endif
