using System;
using UnityEngine;

namespace MateEngine.Platform
{
    public readonly struct ForeignWindow
    {
        public readonly IntPtr Handle;
        public readonly int Pid;
        public readonly Rect Bounds;
        public readonly int Layer;
        public readonly byte Alpha;
        public readonly bool IsTaskbar;
        public readonly string OwnerName;

        public ForeignWindow(IntPtr handle, int pid, Rect bounds, int layer, byte alpha, bool isTaskbar, string ownerName)
        {
            Handle = handle;
            Pid = pid;
            Bounds = bounds;
            Layer = layer;
            Alpha = alpha;
            IsTaskbar = isTaskbar;
            OwnerName = ownerName;
        }
    }

    public readonly struct TrayMenuItem
    {
        public readonly string Label;
        public readonly Action Action;
        public TrayMenuItem(string label, Action action) { Label = label; Action = action; }
    }

    public readonly struct CaptureSource
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly Rect Bounds;
        public readonly bool IsMonitor;

        public CaptureSource(string id, string displayName, Rect bounds, bool isMonitor)
        {
            Id = id;
            DisplayName = displayName;
            Bounds = bounds;
            IsMonitor = isMonitor;
        }
    }
}
