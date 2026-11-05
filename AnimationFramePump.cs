using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace JsonRightClick
{
    /// <summary>高刷新率帧时钟：后台线程只投递消息，所有控件操作仍在 UI 线程上完成。</summary>
    sealed class AnimationFramePump : IDisposable
    {
        public const int MessageId = 0x8000 + 0x192;
        static int nextId;
        sealed class Run
        {
            public readonly IntPtr Window;
            public readonly int Id;
            public int Active = 1, Pending;
            public Timer Timer;
            public Run(IntPtr window) { Window = window; Id = Interlocked.Increment(ref nextId); }
        }
        Run run;
        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        static void Tick(object state)
        {
            var frame = (Run)state;
            if (Thread.VolatileRead(ref frame.Active) == 0 || Interlocked.CompareExchange(ref frame.Pending, 1, 0) != 0) return;
            if (Thread.VolatileRead(ref frame.Active) == 0 || !PostMessage(frame.Window, MessageId, (IntPtr)frame.Id, IntPtr.Zero))
                Interlocked.Exchange(ref frame.Pending, 0);
        }
        public void Start(IntPtr window, int interval)
        {
            Stop();
            var frame = new Run(window); run = frame;
            frame.Timer = new Timer(Tick, frame, Math.Max(1, interval), Math.Max(1, interval));
        }
        public bool Accept(IntPtr id)
        {
            var frame = run;
            if (frame == null || frame.Id != id.ToInt64() || Thread.VolatileRead(ref frame.Active) == 0) return false;
            Interlocked.Exchange(ref frame.Pending, 0);
            return true;
        }
        public void Stop()
        {
            var frame = run; run = null;
            if (frame == null) return;
            Interlocked.Exchange(ref frame.Active, 0);
            if (frame.Timer != null) frame.Timer.Dispose();
        }
        public void Dispose() { Stop(); }
    }
}
