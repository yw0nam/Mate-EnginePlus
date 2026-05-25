using MateEngine.Platform.Stubs;

namespace MateEngine.Platform
{
    public static class PlatformFactory
    {
        static IPlatformWindow _window = new NullPlatformWindow();
        static IPlatformWindowEnumerator _windowEnumerator = new NullPlatformWindowEnumerator();
        static IPlatformTray _tray = new NullPlatformTray();
        static IPlatformScreenCapture _screenCapture = new NullPlatformScreenCapture();
        static IPlatformAppShell _appShell = new NullPlatformAppShell();

        public static IPlatformWindow Window => _window;
        public static IPlatformWindowEnumerator WindowEnumerator => _windowEnumerator;
        public static IPlatformTray Tray => _tray;
        public static IPlatformScreenCapture ScreenCapture => _screenCapture;
        public static IPlatformAppShell AppShell => _appShell;

        public static void RegisterWindow(IPlatformWindow impl) => _window = impl ?? new NullPlatformWindow();
        public static void RegisterWindowEnumerator(IPlatformWindowEnumerator impl) => _windowEnumerator = impl ?? new NullPlatformWindowEnumerator();
        public static void RegisterTray(IPlatformTray impl) => _tray = impl ?? new NullPlatformTray();
        public static void RegisterScreenCapture(IPlatformScreenCapture impl) => _screenCapture = impl ?? new NullPlatformScreenCapture();
        public static void RegisterAppShell(IPlatformAppShell impl) => _appShell = impl ?? new NullPlatformAppShell();
    }
}
