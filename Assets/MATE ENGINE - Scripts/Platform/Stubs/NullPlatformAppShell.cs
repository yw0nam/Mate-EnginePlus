namespace MateEngine.Platform.Stubs
{
    internal sealed class NullPlatformAppShell : IPlatformAppShell
    {
        public bool IsSupported => false;
        public bool IsHiddenFromTaskbar => false;
        public void HideFromTaskbar() { }
        public void ShowInTaskbar() { }
        public void BringToFront() { }
    }
}
