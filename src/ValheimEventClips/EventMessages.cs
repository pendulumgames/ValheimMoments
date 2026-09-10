using System;
using System.Text.RegularExpressions;

namespace ValheimEventClips
{
    internal static class EventMessages
    {
        internal static string Heading(string text, int level)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return new string('#', level) + " " + Regex.Replace(text.TrimStart(), @"^#{1,6}[ \t]+", "");
        }

        internal static string FormatPost(string message)
        {
            string text = (message ?? "Valheim moment").TrimStart();
            // Only bare line-leading labels are changed; existing user Markdown stays intact.
            text = Regex.Replace(text, @"^(Kill credit:|Final blow:)", "**$1**", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            text = Heading(text, 1);
            if (text.Length <= 2000) return text;
            return text.Substring(0, char.IsHighSurrogate(text[1999]) ? 1999 : 2000);
        }

        internal static string Loot(string template, string enemy, string player, string loot, string itemCount = "")
        {
            string pattern = string.IsNullOrWhiteSpace(template) ? "Great loot from {enemy}!" : template;
            return Boss(pattern.Replace("{enemy}", "{boss}"), string.IsNullOrWhiteSpace(enemy) ? "a creature" : enemy,
                player, BossNameMode.KillCredit, loot: loot, itemCount: itemCount);
        }
        internal static string Boss(string template, string boss, string player, BossNameMode? mode = null, string finalBlow = null, string loot = null, string itemCount = "")
        {
            string pattern = string.IsNullOrWhiteSpace(template) ? "\uD83C\uDFC6 {boss} defeated!" : template;
            bool credit = mode == BossNameMode.KillCredit || mode == BossNameMode.Both;
            bool killer = mode == BossNameMode.FinalBlow || mode == BossNameMode.Both;
            string creditName = string.IsNullOrWhiteSpace(player) ? "A player" : player;
            string killerName = string.IsNullOrWhiteSpace(finalBlow) ? "unavailable" : finalBlow;
            string message = pattern
                .Replace("{boss}", string.IsNullOrWhiteSpace(boss) ? "Boss" : boss)
                .Replace("{player}", mode == BossNameMode.FinalBlow ? killerName : creditName)
                .Replace("{credit}", credit ? creditName : "")
                .Replace("{killer}", killer ? killerName : "")
                .Replace("{loot}", loot ?? "")
                .Replace("{item_count}", loot == null ? "" : itemCount);
            if (credit && !pattern.Contains("{credit}") && !pattern.Contains("{player}")) message += "\nKill credit: " + creditName;
            if (killer && !pattern.Contains("{killer}") && !(mode == BossNameMode.FinalBlow && pattern.Contains("{player}"))) message += "\nFinal blow: " + killerName;
            if (loot != null && !pattern.Contains("{loot}")) message += "\n\n" + loot;
            if (message.Length <= 2000) return message;
            return message.Substring(0, char.IsHighSurrogate(message[1999]) ? 1999 : 2000);
        }

        internal static string Death(string template, bool includeName, string nameOverride, string characterName, bool includeCause = false, string cause = "unknown cause")
        {
            string name = includeName ? (string.IsNullOrWhiteSpace(nameOverride) ? characterName : nameOverride) : "A player";
            if (string.IsNullOrWhiteSpace(name)) name = "A player";
            string pattern = string.IsNullOrWhiteSpace(template) ? "\uD83D\uDC80 {player} died!" : template;
            string label = string.IsNullOrWhiteSpace(cause) ? "unknown cause" : cause;
            string message = pattern.Replace("{cause}", includeCause ? label : "").Replace("{player}", name);
            if (includeCause && !pattern.Contains("{cause}")) message += "\nCause: " + label;
            if (message.Length <= 2000) return message;
            int length = char.IsHighSurrogate(message[1999]) ? 1999 : 2000;
            return message.Substring(0, length);
        }
    }
}
