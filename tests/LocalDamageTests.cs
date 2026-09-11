using System;
using HarmonyLib;
using ValheimMoments;

internal static class LocalDamageTests
{
    public static void Run()
    {
        var harmony = new Harmony("moments.tests.localdamage");
        var player = new Player(); Player.m_localPlayer = player;
        int count = 0, errors = 0;
        LocalDamageDetector.OnError = () => errors++;
        LocalDamageDetector.OnDamage = (p, before, after, maxBefore, maxAfter, alive) =>
        {
            if (p != player || before != 100 || after != 4 || maxBefore != 100 || maxAfter != 100 || !alive)
                throw new Exception("Incorrect damage snapshot");
            count++;
        };
        LocalDamageDetector.Install(harmony);
        try
        {
            player.ApplyAction = () => player.Health = 4;
            player.ApplyDamage(new HitData(), true, true, HitData.DamageModifier.Normal);
            if (count != 1 || errors != 0) throw new Exception("Actual local damage missing");
            player.ApplyDamage(new HitData(), true, true, HitData.DamageModifier.Normal);
            if (count != 1) throw new Exception("Unchanged health emitted damage");
            var remote = new Player { ApplyAction = () => { } };
            remote.ApplyDamage(new HitData(), true, true, HitData.DamageModifier.Normal);
            if (count != 1) throw new Exception("Remote damage emitted");
            player.Health = 100; player.Owner = false;
            player.ApplyDamage(new HitData(), true, true, HitData.DamageModifier.Normal);
            if (count != 1) throw new Exception("Nonowner damage emitted");
            player.Owner = true; player.Health = 100;
            LocalDamageDetector.OnDamage = (p, b, a, mb, ma, alive) => { throw new Exception("Observer failed"); };
            player.ApplyDamage(new HitData(), true, true, HitData.DamageModifier.Normal);
            if (player.Health != 4 || errors != 1) throw new Exception("Observer interrupted original damage");
            Console.WriteLine("Local damage hook: 5 assertions passed.");
        }
        finally { harmony.UnpatchSelf(); LocalDamageDetector.OnDamage = null; LocalDamageDetector.OnError = null; Player.m_localPlayer = null; }
    }
}
