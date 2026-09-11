using System;

namespace ValheimMoments.Core
{
    public enum CloseCallResult { None, Started, Survived, Cancelled }

    // Main-thread, one character in one world. Reset on session/character change.
    // Health observations do not trigger: only the adapter's actual damage scope can.
    public sealed class CloseCall
    {
        private readonly double threshold, recovery, hold, cooldown, followUp;
        private double lastTime = double.NegativeInfinity;
        private double triggeredAt = double.NegativeInfinity;
        private double recoveredAt = double.NaN;
        private bool armed = true;
        public bool Pending { get; private set; }

        public CloseCall(double threshold = .05, double recovery = .20,
            double recoverySeconds = 10, double cooldownSeconds = 120, double followUpSeconds = 20)
        {
            if (!Finite(threshold) || !Finite(recovery) || threshold <= 0 ||
                recovery <= threshold || recovery > 1 || !Finite(recoverySeconds) || recoverySeconds <= 0 ||
                !Finite(cooldownSeconds) || cooldownSeconds < 0 || !Finite(followUpSeconds) || followUpSeconds <= 0)
                throw new ArgumentOutOfRangeException("Invalid close-call settings");
            this.threshold = threshold; this.recovery = recovery; hold = recoverySeconds;
            cooldown = cooldownSeconds; followUp = followUpSeconds;
        }

        public CloseCallResult Damage(double now, double before, double after, double maximumBefore,
            double maximumAfter, bool alive)
        {
            Time(now);
            if (!ValidHealth(before, maximumBefore) || !ValidHealth(after, maximumAfter)) return Cancel();
            if (!alive || after <= 0) return Cancel();
            if (after / maximumAfter < recovery) recoveredAt = double.NaN;
            // A changing denominator (food/mod effects) is not proof of a damage crossing.
            if (!armed || Pending || maximumBefore != maximumAfter || after >= before ||
                before / maximumBefore <= threshold || after / maximumAfter > threshold)
                return CloseCallResult.None;
            armed = false; Pending = true; triggeredAt = now; recoveredAt = double.NaN;
            return CloseCallResult.Started;
        }

        public CloseCallResult Observe(double now, double health, double maximum, bool alive)
        {
            Time(now);
            if (!alive || !ValidHealth(health, maximum) || health <= 0)
            { recoveredAt = double.NaN; return Cancel(); }
            if (health / maximum >= recovery)
            {
                if (double.IsNaN(recoveredAt)) recoveredAt = now;
            }
            else recoveredAt = double.NaN;
            if (Pending && now - triggeredAt >= followUp)
            { Pending = false; return CloseCallResult.Survived; }
            if (!Pending && !armed && now - triggeredAt >= cooldown &&
                !double.IsNaN(recoveredAt) && now - recoveredAt >= hold) armed = true;
            return CloseCallResult.None;
        }

        // Busy capture, disabled policy, death, or lost participation abandons this attempt.
        // Cancellation never bypasses the cooldown/recovery gate.
        public CloseCallResult Cancel()
        {
            bool pending = Pending; Pending = false; recoveredAt = double.NaN;
            return pending ? CloseCallResult.Cancelled : CloseCallResult.None;
        }

        public void Reset()
        {
            Pending = false; armed = true; recoveredAt = double.NaN;
            triggeredAt = lastTime = double.NegativeInfinity;
        }

        private void Time(double now)
        {
            if (!Finite(now) || now < lastTime) throw new ArgumentOutOfRangeException("now");
            lastTime = now;
        }
        private static bool ValidHealth(double health, double maximum)
        { return Finite(health) && Finite(maximum) && maximum > 0 && health >= 0; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
