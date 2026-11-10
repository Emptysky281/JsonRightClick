using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace JsonRightClick
{
    /// <summary>原生高精度帧时钟；后台线程只投递消息，控件始终由 UI 线程访问。</summary>
    sealed class AnimationFramePump : IDisposable
    {
        public const int MessageId = 0x8000 + 0x192;
        static int nextId;
        sealed class Run
        {
            public readonly IntPtr Window;
            public readonly int Id, Interval;
            public int Active = 1, Pending;
            public Run(IntPtr window, int interval)
            { Window = window; Id = Interlocked.Increment(ref nextId); Interval = Math.Max(1, interval); }
        }
        sealed class FrameTimer : WaitHandle
        {
            public readonly bool HighResolution;
            FrameTimer(IntPtr handle, bool highResolution)
            { SafeWaitHandle = new SafeWaitHandle(handle, true); HighResolution = highResolution; }
            public static FrameTimer Create()
            {
                // Win10 1803+。System.Threading.Timer 的时钟粒度不受 timeBeginPeriod 影响，
                // 请求 16ms 实测会变成约 31ms，不能拿它驱动 60Hz 滚动。
                IntPtr handle = CreateWaitableTimerEx(IntPtr.Zero, null, 2 /* HIGH_RESOLUTION */, 0x100002);
                if (handle != IntPtr.Zero) return new FrameTimer(handle, true);
                handle = CreateWaitableTimerEx(IntPtr.Zero, null, 0, 0x100002);
                return handle == IntPtr.Zero ? null : new FrameTimer(handle, false);
            }
            public bool Arm(int interval)
            {
                long due = -interval * 10000L;
                return SetWaitableTimer(SafeWaitHandle, ref due, interval, IntPtr.Zero, IntPtr.Zero, false);
            }
            public void Cancel() { CancelWaitableTimer(SafeWaitHandle); }
        }
        readonly object gate = new object();
        readonly AutoResetEvent changed = new AutoResetEvent(false);
        Thread worker;
        Run run;
        bool disposed;

        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll")]
        static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, IntPtr callback, IntPtr argument, bool resume);
        [DllImport("kernel32.dll")]
        static extern bool CancelWaitableTimer(SafeWaitHandle timer);

        static void Tick(Run frame)
        {
            if (Thread.VolatileRead(ref frame.Active) == 0 || Interlocked.CompareExchange(ref frame.Pending, 1, 0) != 0) return;
            if (Thread.VolatileRead(ref frame.Active) == 0 || !PostMessage(frame.Window, MessageId, (IntPtr)frame.Id, IntPtr.Zero))
                Interlocked.Exchange(ref frame.Pending, 0);
        }
        void Work()
        {
            FrameTimer timer = FrameTimer.Create();
            bool precise = false;
            Run armed = null;
            var waits = timer == null ? null : new WaitHandle[] { changed, timer };
            try
            {
                while (true)
                {
                    Run frame;
                    lock (gate) { if (disposed) return; frame = run; }
                    if (frame == null)
                    {
                        if (timer != null && armed != null) timer.Cancel();
                        armed = null;
                        if (precise) { Native.timeEndPeriod(1); precise = false; }
                        changed.WaitOne(); continue; // 空闲时没有轮询或周期唤醒。
                    }
                    if (!precise && (timer == null || !timer.HighResolution))
                    { Native.timeBeginPeriod(1); precise = true; }
                    if (frame != armed)
                    {
                        if (timer != null && !timer.Arm(frame.Interval))
                        {
                            timer.Dispose(); timer = null;
                            if (!precise) { Native.timeBeginPeriod(1); precise = true; }
                        }
                        armed = frame;
                    }
                    if (timer != null)
                    { if (WaitHandle.WaitAny(waits) == 0) continue; }
                    else if (changed.WaitOne(frame.Interval)) continue;
                    Tick(frame);
                }
            }
            finally
            {
                if (timer != null) { timer.Cancel(); timer.Dispose(); }
                if (precise) Native.timeEndPeriod(1);
                changed.Dispose();
            }
        }
        public void Start(IntPtr window, int interval)
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("AnimationFramePump");
                if (run != null) Interlocked.Exchange(ref run.Active, 0);
                run = new Run(window, interval);
                if (worker == null)
                { worker = new Thread(Work) { IsBackground = true, Name = "JSON scroll frames" }; worker.Start(); }
                changed.Set();
            }
        }
        public bool Accept(IntPtr id)
        {
            lock (gate)
            {
                var frame = run;
                if (frame == null || frame.Id != id.ToInt64() || Thread.VolatileRead(ref frame.Active) == 0) return false;
                Interlocked.Exchange(ref frame.Pending, 0);
                return true;
            }
        }
        public void Stop()
        {
            lock (gate)
            {
                if (run != null) { Interlocked.Exchange(ref run.Active, 0); run = null; }
                if (!disposed && worker != null) changed.Set();
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (run != null) { Interlocked.Exchange(ref run.Active, 0); run = null; }
                if (worker == null) changed.Dispose(); else changed.Set();
            }
        }
    }
}
