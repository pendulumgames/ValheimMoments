using System;
using System.Collections.Generic;

namespace ValheimMoments
{
    internal sealed class MomentRateLimit
    {
        private readonly Queue<double> captures = new Queue<double>();
        internal bool TryTake(double now, int maximum, double window)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return false;
            maximum = Math.Max(1, Math.Min(20, maximum));
            window = double.IsNaN(window) || double.IsInfinity(window) ? 60 : Math.Max(1, Math.Min(3600, window));
            while (captures.Count > 0 && captures.Peek() <= now - window) captures.Dequeue();
            if (captures.Count >= maximum) return false;
            captures.Enqueue(now); return true;
        }
        internal void Clear() { captures.Clear(); }
    }

    internal sealed class DeathMoments
    {
        internal sealed class Ticket
        {
            internal long Number, Additional;
            internal int Epoch;
        }
        private long observed, shared;
        private Ticket pending;
        private int epoch;
        private readonly MomentRateLimit quota = new MomentRateLimit();
        internal void Observe() { if (observed < long.MaxValue) observed++; }
        internal Ticket Reserve()
        {
            if (pending != null || observed <= shared) return null;
            pending = new Ticket { Number = observed, Additional = Math.Max(0, observed - shared - 1), Epoch = epoch };
            return pending;
        }
        internal bool TakeSlot(double now, int maximum, double window) { return quota.TryTake(now, maximum, window); }
        internal void Complete(Ticket ticket, bool delivered)
        {
            if (ticket == null || !ReferenceEquals(ticket, pending)) return;
            if (delivered) shared = Math.Max(shared, ticket.Number);
            pending = null;
        }
        internal bool Reopen(Ticket ticket)
        {
            if (ticket == null || pending != null || ticket.Epoch != epoch || ticket.Number <= shared || ticket.Number > observed) return false;
            pending = ticket; return true;
        }
        internal void Clear() { observed = shared = 0; pending = null; quota.Clear(); epoch++; }
    }

    internal sealed class DeathFlavor
    {
        // Neutral flavor never asserts an unverified cause, weapon or food status.
        internal static readonly string[] Lines = {
            "A tactical donation to the local gravestone collection.",
            "The corpse run has acquired a sequel.",
            "Confidence: legendary. Survival: common.",
            "An ambitious approach to becoming a landmark.",
            "Valhalla put you on hold.",
            "Your equipment awaits your return. Again.",
            "The bees are happy. You are not.",
            "That was almost a plan.",
            "An unexpected appointment with the respawn screen.",
            "A bold strategy with a very short conclusion.",
            "Your inventory has become a destination.",
            "Adventure has requested a brief intermission.",
            "The saga takes an inconvenient turn.",
            "Another chapter for the cautionary tale.",
            "The adventure continues. Slightly less clothed.",
            "A memorable contribution to local history.",
            "You found the difficulty setting.",
            "This shortcut includes a respawn.",
            "The gravestone business is booming.",
            "Odin saw that. Unfortunately."
        };
        private int previous = -1;
        internal string Next(Random random)
        {
            int index = random.Next(previous < 0 ? Lines.Length : Lines.Length - 1);
            if (previous >= 0 && index >= previous) index++;
            previous = index; return Lines[index];
        }
    }
}
