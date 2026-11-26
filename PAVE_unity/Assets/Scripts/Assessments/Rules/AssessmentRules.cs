using System;
using System.Collections.Generic;

namespace Pave.Assessments
{
    public enum AssessmentKind
    {
        BoxAndBlock,
        TargetedBoxAndBlock,
        ClothespinUp,
        ClothespinDown
    }

    public enum TrialState
    {
        Ready,
        Running,
        Completed,
        Failed,
        Aborted,
        Invalid
    }

    /// <summary>Host elapsed time governs assessment deadlines. A sample at the deadline is too late.</summary>
    public sealed class AssessmentClock
    {
        public TrialState State { get; private set; } = TrialState.Ready;
        public double StartedAt { get; private set; }
        public double EndedAt { get; private set; }
        public double Duration { get; }

        public AssessmentClock(double duration)
        {
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            Duration = duration;
        }

        public void Start(double now)
        {
            if (State != TrialState.Ready)
            {
                throw new InvalidOperationException("Reset before another trial.");
            }

            StartedAt = now;
            State = TrialState.Running;
        }

        public bool Accepts(double now) => State == TrialState.Running && now >= StartedAt && now < StartedAt + Duration;
        public double Elapsed(double now) => State == TrialState.Ready ? 0 : Math.Max(0, Math.Min(State == TrialState.Running ? now : EndedAt, StartedAt + Duration) - StartedAt);
        public void Finish(TrialState state, double now)
        {
            if (State != TrialState.Running)
            {
                return;
            }

            if (state == TrialState.Ready || state == TrialState.Running)
            {
                throw new ArgumentException("Expected a terminal state.");
            }

            EndedAt = Math.Max(StartedAt, Math.Min(now, StartedAt + Duration));
            State = state;
        }
    }

    /// <summary>One point per hand transport episode, including a simultaneous multi-block carry.
    /// The scene supplies evidence of a source-to-destination transfer and fingertip crossing.</summary>
    public sealed class BoxAndBlockScore
    {
        readonly HashSet<int> transferred = new HashSet<int>();
        readonly HashSet<int> creditedTrips = new HashSet<int>();
        public int Score => creditedTrips.Count;

        public bool RegisterTransfer(int blockId, int tripId, bool fingertipsCrossed)
        {
            if (!fingertipsCrossed || tripId < 1 || !transferred.Add(blockId))
            {
                return false;
            }

            return creditedTrips.Add(tripId);
        }
    }

    /// <summary>The nine-block protocol records unsuccessful transports too; it does not
    /// silently require nine perfect placements or let failed placements disappear from the result.</summary>
    public sealed class TargetedBlockScore
    {
        public int ReleasedCount { get; private set; }
        public int UnsuccessfulTransports { get; private set; }
        public int BlockCount { get; }
        public bool Complete => ReleasedCount == BlockCount;

        public TargetedBlockScore(int blockCount = 9)
        {
            if (blockCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(blockCount));
            }

            BlockCount = blockCount;
        }

        public bool RegisterRelease(int blockIndex, bool fullyWithinTarget)
        {
            if (Complete || blockIndex != ReleasedCount)
            {
                return false;
            }

            ReleasedCount++;
            if (!fullyWithinTarget)
            {
                UnsuccessfulTransports++;
            }

            return true;
        }
    }

    /// <summary>Contact loss must persist briefly to reject single-step contact flicker.
    /// Keep the first loss time so confirmation does not add the grace interval to task time.</summary>
    public sealed class ReleaseObservation
    {
        public bool WasTouched { get; private set; }
        public double LastContactAt { get; private set; }

        public void Observe(bool touching, double now)
        {
            if (!touching)
            {
                return;
            }

            WasTouched = true;
            LastContactAt = now;
        }

        public bool IsReleased(bool touching, double now, double graceSeconds) => WasTouched && !touching && now - LastContactAt >= graceSeconds;
    }
}
