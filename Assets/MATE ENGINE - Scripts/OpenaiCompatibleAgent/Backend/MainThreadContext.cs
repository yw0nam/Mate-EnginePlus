using System;
using System.Threading;
using UnityEngine;

namespace OpenaiCompatibleAgent
{
    /// <summary>
    /// Minimal Unity main-thread marshaling. Replaces the <c>SyncContextUtility</c> helper that used
    /// to ship bundled with the (now removed) com.openai.unity package.
    /// </summary>
    /// <remarks>
    /// Unity installs a <see cref="SynchronizationContext"/> on its main thread (both play and edit
    /// mode). We capture it once and post to it to hop back onto the main thread from worker-thread
    /// continuations (HttpClient / Task.Delay), where Unity API calls would otherwise fail.
    /// </remarks>
    public static class MainThreadContext
    {
        private static SynchronizationContext _context;
        private static int _threadId = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void CaptureOnLoad()
        {
            _context = SynchronizationContext.Current;
            _threadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// Captures the calling thread as the Unity main thread if not already captured. Call from a
        /// guaranteed-main-thread entry point (e.g. the start of a send) so edit-mode
        /// <c>[ExecuteAlways]</c> paths work without relying on the play-mode load hook.
        /// </summary>
        public static void EnsureCaptured()
        {
            if (_threadId != -1 && _context != null) return;
            _context = SynchronizationContext.Current ?? _context;
            _threadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static int UnityThreadId => _threadId;

        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _threadId;

        /// <summary>Runs <paramref name="action"/> on the Unity main thread: synchronously if already
        /// there, otherwise posted to the captured context (executed on the next update tick).</summary>
        public static void RunOnUnityThread(Action action)
        {
            if (action == null) return;
            if (IsMainThread || _context == null)
            {
                action();
                return;
            }
            _context.Post(_ => action(), null);
        }
    }
}
