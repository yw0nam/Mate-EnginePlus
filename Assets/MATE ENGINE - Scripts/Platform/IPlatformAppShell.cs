namespace MateEngine.Platform
{
    public interface IPlatformAppShell
    {
        bool IsSupported { get; }

        bool IsHiddenFromTaskbar { get; }

        void HideFromTaskbar();
        void ShowInTaskbar();

        void BringToFront();
    }
}
