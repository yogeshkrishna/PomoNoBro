using System;
using System.Diagnostics;

namespace PomoNoBro
{
    internal enum TimerMode { Focus, Break }
    internal enum TimerPhase { Ready, Running, Paused, Finished }

    internal sealed class FocusTimer
    {
        private readonly Stopwatch clock = new Stopwatch();
        private TimeSpan duration;

        public TimerMode Mode { get; private set; }
        public TimerPhase Phase { get; private set; }
        public string Goal { get; private set; }
        public int PlannedMinutes { get; private set; }

        public bool IsActive
        {
            get { return Phase == TimerPhase.Running || Phase == TimerPhase.Paused; }
        }

        public TimeSpan Remaining
        {
            get { return TimeSpan.FromTicks(Math.Max(0, duration.Ticks - clock.Elapsed.Ticks)); }
        }

        public double RemainingFraction
        {
            get { return duration.Ticks == 0 ? 0 : (double)Remaining.Ticks / duration.Ticks; }
        }

        public void Prepare(TimerMode mode, int minutes)
        {
            Mode = mode;
            PlannedMinutes = minutes;
            duration = TimeSpan.FromMinutes(minutes);
            clock.Reset();
            Goal = "";
            Phase = TimerPhase.Ready;
        }

        public void Start(string goal)
        {
            Goal = goal;
            clock.Restart();
            Phase = TimerPhase.Running;
        }

        public void Pause()
        {
            clock.Stop();
            Phase = TimerPhase.Paused;
        }

        public void Resume()
        {
            clock.Start();
            Phase = TimerPhase.Running;
        }

        public FocusRecord Finish()
        {
            clock.Stop();
            Phase = TimerPhase.Finished;
            if (Mode == TimerMode.Break) return null;

            TimeSpan elapsed = duration - Remaining;
            return new FocusRecord
            {
                Goal = Goal,
                EndedAt = DateTimeOffset.Now,
                ElapsedSeconds = (int)Math.Floor(elapsed.TotalSeconds),
                PlannedMinutes = PlannedMinutes,
                Completed = elapsed == duration
            };
        }
    }
}
