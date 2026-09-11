using System;
using HarmonyLib;
using ValheimMoments;

internal static class RaidIdentityTests
{
    internal static void Run()
    {
        var harmony = new Harmony("moments.tests.raid.identity");
        RaidIdentity.Install(harmony);
        try
        {
            var host = new ZNet { Server = true }; ZNet.instance = host; RaidIdentity.Tick();
            var first = new RandomEvent(); string id = RaidIdentity.For(first);
            if (!HighlightDirector.ValidId(id) || RaidIdentity.For(first) != id || RaidIdentity.For(new RandomEvent()) == id)
                throw new Exception("Host raid occurrence identity unstable or reused");
            var client = new ZNet(); var rpc = new ZRpc(); client.Peers.Add(new ZNetPeer { m_rpc = rpc });
            ZNet.instance = client; RaidIdentity.Tick();
            var system = new RandEventSystem { Current = new RandomEvent() };
            string payload = id + "|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("raid")) + "|0|0|0|0";
            var receive = rpc.Handlers["ValheimMoments_RaidIdentity_v1"];
            receive(new ZRpc(), payload); system.RPC_SetEvent();
            if (RaidIdentity.For(system.Current) != null) throw new Exception("Non-host identity accepted");
            receive(rpc, payload); system.RPC_SetEvent();
            if (RaidIdentity.For(system.Current) != id) throw new Exception("Exact authenticated snapshot rejected");
            system.Current = new RandomEvent { m_time = 1 };
            receive(rpc, payload); system.RPC_SetEvent();
            if (RaidIdentity.For(system.Current) != null) throw new Exception("Unmatched raid snapshot grouped");
            system.Current.m_time = 0; system.RPC_SetEvent();
            if (RaidIdentity.For(system.Current) != null) throw new Exception("Consumed token reused");
            Console.WriteLine("Raid identity: stable host IDs, authenticated exact pairing, mismatch and replay checks passed.");
        }
        finally { harmony.UnpatchSelf(); RaidIdentity.Clear(); ZNet.instance = null; }
    }
}
