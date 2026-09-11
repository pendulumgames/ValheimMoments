using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace ValheimMoments
{
    internal enum BossNameMode { KillCredit, FinalBlow, Both }

    // Attribution is supplemental metadata. Only the vanilla credit observer can
    // create a clip; a received custom RPC can never trigger capture by itself.
    internal static class BossAttribution
    {
        private const string RpcName = "ValheimMoments_BossFinalBlow_v1";
        private const string CreditRpcName = "ValheimMoments_KillCredits_v1";
        private const string EventRpcName = "ValheimMoments_KillEvent_v1";
        private static readonly FieldInfo LastHit = typeof(Character).GetField("m_lastHit", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NetworkView = AccessTools.Field(typeof(Character), "m_nview");
        private sealed class Context { internal string Enemy, Name, Credits, EventId; }
        [ThreadStatic] private static Context current;
        private static ZRoutedRpc registered;
        private static readonly AttributionInbox inbox = new AttributionInbox();
        private static readonly AttributionInbox creditInbox = new AttributionInbox(1024);
        private static readonly AttributionInbox eventInbox = new AttributionInbox(32);
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        internal static Action<string> OnDiagnostic;

        internal static string ResolveCredits(Character victim)
        {
            try
            {
                var view = NetworkView?.GetValue(victim) as ZNetView;
                var zdo = view?.GetZDO();
                if (zdo == null || ZNet.instance == null) return null;
                var names = new List<string>();
                // Exactly the key and connected-player filter used by Character.OnDeath
                // when it sends vanilla kill credit. Do not substitute nearby players.
                string prefix = ZDOVars.s_attackers.ToString();
                foreach (var player in ZNet.instance.GetPlayerList())
                    if (!string.IsNullOrEmpty(player.m_name) && zdo.GetBool(prefix + player.m_name, false)) names.Add(player.m_name);
                return FormatCredits(names);
            }
            catch { return null; }
        }
        internal static string FormatCredits(IEnumerable<string> players)
        {
            var names = new List<string>();
            foreach (string player in players)
            {
                string name = (player ?? "").Replace("\r", " ").Replace("\n", " ").Replace("*", "").Replace("`", "").Trim();
                if (name.Length > 80) name = name.Substring(0, char.IsHighSurrogate(name[79]) ? 79 : 80);
                if (name.Length != 0 && !names.Contains(name)) names.Add(name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            string result = "";
            for (int i = 0; i < names.Count; i++)
            {
                if (result.Length + names[i].Length + 2 > 980) return result + " (+" + (names.Count - i) + " more)";
                result += (result.Length == 0 ? "" : ", ") + names[i];
            }
            return result.Length == 0 ? null : result;
        }

        internal static string CreditLabel(string credits, string local)
        { return string.IsNullOrWhiteSpace(credits) ? (string.IsNullOrWhiteSpace(local) ? "A player" : local) + " (full list unavailable)" : credits; }

        internal static string Resolve(HitData hit, out string reason)
        {
            if (hit == null) { reason = "no recorded damage"; return null; }
            if (hit.m_attacker.IsNone())
            {
                string periodic = PeriodicAttribution.Resolve(hit);
                if (periodic != null) { reason = "resolved single-source periodic damage"; return periodic; }
            }
            var attacker = hit.GetAttacker();
            var player = attacker as Player;
            if (player != null) { reason = "resolved player object"; return player.GetPlayerName(); }
            if (attacker == null && !hit.m_attacker.IsNone() && ZNet.instance != null)
            {
                foreach (var peer in ZNet.instance.GetPlayerList())
                    if (peer.m_characterID == hit.m_attacker) { reason = "resolved exact player network ID"; return peer.m_name; }
            }
            reason = "hit=" + hit.m_hitType + ", " + (attacker != null ? "non-player attacker" : hit.m_attacker.IsNone() ? "no attacker ID" : "attacker ID not in player list");
            return null;
        }

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Character), "OnDeath", Type.EmptyTypes),
                prefix: new HarmonyMethod(typeof(BossAttribution), nameof(BeforeDeath)),
                finalizer: new HarmonyMethod(typeof(BossAttribution), nameof(AfterDeath)));
            harmony.Patch(AccessTools.Method(typeof(Game), "RegisterKill", new[] { typeof(long), typeof(string), typeof(int), typeof(KillModifiers), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(BossAttribution), nameof(BeforeSendCredit)));
            foreach (var constructor in AccessTools.GetDeclaredConstructors(typeof(ZRoutedRpc)))
                harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(BossAttribution), nameof(AfterRouterCreated)));
            EnsureRegistered(ZRoutedRpc.instance);
        }

        private static void AfterRouterCreated(ZRoutedRpc __instance) { EnsureRegistered(__instance); }
        private static void EnsureRegistered(ZRoutedRpc router)
        {
            try
            {
                if (router == null || ReferenceEquals(registered, router)) return;
                router.Register<string, string>(RpcName, Receive);
                router.Register<string, string>(CreditRpcName, (sender, enemy, names) => creditInbox.Add(sender, enemy, names, clock.Elapsed.TotalSeconds));
                router.Register<string, string>(EventRpcName, (sender, enemy, id) => {
                    if (HighlightDirector.ValidId(id)) eventInbox.Add(sender, enemy, id, clock.Elapsed.TotalSeconds);
                });
                registered = router;
                inbox.Clear(); creditInbox.Clear(); eventInbox.Clear();
            }
            catch { } // A missing channel yields unavailable attribution, not lost gameplay.
        }

        private static void Receive(long sender, string enemy, string name)
        {
            inbox.Add(sender, enemy, name, clock.Elapsed.TotalSeconds);
        }

        private static void BeforeDeath(Character __instance, out Context __state)
        {
            __state = current;
            current = null;
            try
            {
                if (!__instance.IsOwner() || __instance is Player) return;
                current = new Context { Enemy = __instance.m_name, EventId = Guid.NewGuid().ToString("N") };
                if (!__instance.IsBoss()) return;
                var hit = LastHit?.GetValue(__instance) as HitData;
                string reason;
                string name = Resolve(hit, out reason);
                current.Name = name; current.Credits = ResolveCredits(__instance);
                try { OnDiagnostic?.Invoke(reason); } catch { }
            }
            catch { }
        }

        private static void AfterDeath(Context __state) { current = __state; }

        private static void BeforeSendCredit(long playerPeerID, string enemyName, int bossNumber)
        {
            try
            {
                if (current == null || current.Enemy != enemyName) return;
                // Sent to exactly the recipient of the immediately following vanilla
                // credit, over the same ordered routed-RPC connection.
                try { ZRoutedRpc.instance.InvokeRoutedRPC(playerPeerID, EventRpcName, new object[] { enemyName, current.EventId }); }
                catch { } // Optional director metadata must not suppress existing attribution.
                if (bossNumber <= 0) return;
                ZRoutedRpc.instance.InvokeRoutedRPC(playerPeerID, RpcName, new object[] { enemyName, current.Name ?? "" });
                ZRoutedRpc.instance.InvokeRoutedRPC(playerPeerID, CreditRpcName, new object[] { enemyName, current.Credits ?? "" });
            }
            catch { }
        }

        internal static string Take(long sender, string enemy)
        {
            if (sender == 0 && current != null && current.Enemy == enemy) return current.Name;
            string name = inbox.Take(sender, enemy, clock.Elapsed.TotalSeconds);
            try { OnDiagnostic?.Invoke(name == null ? "owner metadata missing or expired" : name.Length == 0 ? "owner reported no player attacker" : "received owner attribution"); } catch { }
            return name;
        }

        internal static string TakeCredits(long sender, string enemy)
        {
            if (sender == 0 && current != null && current.Enemy == enemy) return current.Credits;
            return creditInbox.Take(sender, enemy, clock.Elapsed.TotalSeconds);
        }

        internal static string TakeEvent(long sender, string enemy)
        {
            if (sender == 0 && current != null && current.Enemy == enemy) return current.EventId;
            return eventInbox.Take(sender, enemy, clock.Elapsed.TotalSeconds);
        }

        internal static void Clear() { current = null; inbox.Clear(); creditInbox.Clear(); eventInbox.Clear(); OnDiagnostic = null; }
    }

    // Bounded, short-lived and sender-scoped: never reuse another participant's
    // metadata or carry an unmatched attribution across a later fight.
    internal sealed class AttributionInbox
    {
        private sealed class Entry { internal long Sender; internal string Enemy, Name; internal double Time; }
        private readonly List<Entry> entries = new List<Entry>();
        private readonly int maximumName;
        internal AttributionInbox(int maximumName = 256) { this.maximumName = maximumName; }
        internal void Add(long sender, string enemy, string name, double now)
        {
            if (string.IsNullOrEmpty(enemy) || enemy.Length > 256 || name == null || name.Length > maximumName) return;
            entries.RemoveAll(e => now - e.Time > 5 || (e.Sender == sender && e.Enemy == enemy));
            if (entries.Count >= 64) entries.RemoveAt(0);
            entries.Add(new Entry { Sender = sender, Enemy = enemy, Name = name, Time = now });
        }
        internal string Take(long sender, string enemy, double now)
        {
            entries.RemoveAll(e => now - e.Time > 5);
            int index = entries.FindIndex(e => e.Sender == sender && e.Enemy == enemy);
            if (index < 0) return null;
            string name = entries[index].Name; entries.RemoveAt(index); return name;
        }
        internal void Clear() { entries.Clear(); }
    }
}
