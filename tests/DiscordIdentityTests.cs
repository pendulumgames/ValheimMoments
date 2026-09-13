using System;
using System.Collections.Generic;
using ValheimMoments;
internal static class DiscordIdentityTests
{
    private static int checks;
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
    internal static void Run()
    {
        const string mec = "123456789012345678", ren = "234567890123456789";
        Check(DiscordMentionText.ValidId(mec) && !DiscordMentionText.ValidId("Mecradore") && !DiscordMentionText.ValidId("<@" + mec + ">") && !DiscordMentionText.ValidId("99999999999999999999"), "Only bounded numeric user IDs accepted");
        var identities = new Dictionary<string,string> { { "Mec", mec }, { "Ren", ren } };
        string[] allowed;
        string text = DiscordMentionText.Format("# Mec died!\n**Recorded by:** Mec\n**Kill credit:** Mec, Ren\nFinal blow: Mec\n@everyone <@999999999999999999>", identities, out allowed);
        Check(text.Contains("**Recorded by:** Mec (<@" + mec + ">)") && text.Contains("Mec (<@" + mec + ">), Ren (<@" + ren + ">)"), "Recorder and each credited character retain names and get mentions");
        Check(text.StartsWith("# Mec died!") && text.Contains("Final blow: Mec"), "Character title and final blow unchanged");
        Check(allowed.Length == 2 && !text.Contains("<@999999999999999999>"), "Only generated opted-in mentions are allowed");
        Check(DiscordMentionText.Format("* 1. **Recorded by:** Mec (first kill for this character)", identities, out allowed).Contains(">) (first kill"), "Director first-kill suffix preserved");
        Check(DiscordMentionText.Format("**Kill credit:** Mec (full list unavailable)", identities, out allowed).Contains(">) (full list unavailable)"), "Unavailable roster suffix preserved");
        Check(DiscordMentionText.Format("**Recorded by:** Unknown", identities, out allowed).EndsWith("Unknown") && allowed.Length == 0, "Unmapped player gets no invented mention");
        var host = new ZNet { Server = true }; var client = new ZNet();
        var hr = new ZRpc { World = host }; var cr = new ZRpc { World = client }; hr.Other = cr; cr.Other = hr;
        host.Peers.Add(new ZNetPeer { m_rpc = hr, m_playerName = "Ren" }); client.Peers.Add(new ZNetPeer { m_rpc = cr });
        var hostIdentity = new DiscordIdentity(); var clientIdentity = new DiscordIdentity();
        try {
            ZNet.instance = host; hostIdentity.Tick(0, mec, "Mec");
            ZNet.instance = client; clientIdentity.Tick(0, ren, "Spoofed character");
            ZNet.instance = host; hr.Drain();
            var options = new DiscordOptions { Message = "**Recorded by:** Ren\n**Kill credit:** Mec, Ren" }; hostIdentity.Apply(options);
            Check(options.MentionUsers.Length == 2 && options.Message.Contains("Ren (<@" + ren), "Host binds user ID to actual peer name, ignoring claimed client name");
            host.Peers.Add(new ZNetPeer { m_rpc = new ZRpc { World = host }, m_playerName = "Ren" });
            options = new DiscordOptions { Message = "**Recorded by:** Ren" }; hostIdentity.Apply(options);
            Check(options.MentionUsers.Length == 0, "Duplicate character names never select an arbitrary Discord account");
            host.Peers.Clear(); hostIdentity.Tick(1, mec, "Mec");
            options = new DiscordOptions { Message = "**Recorded by:** Ren" }; hostIdentity.Apply(options);
            Check(options.MentionUsers.Length == 0, "Disconnected peer cannot retain a mention mapping");
        } finally { ZNet.instance = null; }
        Console.WriteLine("PASS: " + checks + " Discord identity/mention assertions; no messages sent.");
    }
}
