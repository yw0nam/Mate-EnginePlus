using MateEngine.Platform;
using UnityEngine;

public class RemoveTaskbarApp : MonoBehaviour
{
    public bool IsHidden => PlatformFactory.AppShell.IsHiddenFromTaskbar;

    void Start()
    {
        if (Application.isEditor) return;
        PlatformFactory.AppShell.HideFromTaskbar();
    }

    public void ToggleAppMode()
    {
        if (Application.isEditor) return;
        var shell = PlatformFactory.AppShell;
        if (shell.IsHiddenFromTaskbar) shell.ShowInTaskbar();
        else shell.HideFromTaskbar();
    }
}
