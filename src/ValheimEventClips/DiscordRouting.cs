using System;

namespace ValheimEventClips
{
    internal static class DiscordRouting
    {
        // A clip must still belong to the same locally hosted session. A client
        // leaving a remote server and starting a local world never gains authority
        // to submit the previous server's clips through its own webhook.
        internal static bool CanSubmit(object capturedSession, bool capturedAsHost, object currentSession, bool currentlyHost)
        {
            return capturedSession != null && capturedAsHost && currentlyHost &&
                ReferenceEquals(capturedSession, currentSession);
        }

        internal static string Destination(string kind, string fallback,
            bool bossOverride, string boss, bool lootOverride, string loot, bool deathOverride, string death)
        {
            if (kind == "boss" && bossOverride) return boss;
            if (kind == "loot" && lootOverride) return loot;
            if (kind == "death" && deathOverride) return death;
            return fallback;
        }
    }
}
