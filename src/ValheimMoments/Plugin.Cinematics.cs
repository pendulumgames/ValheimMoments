using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimMoments
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> renderCinematics;
        private ConfigEntry<bool> cinematicSpecialSpawn, cinematicSpecialKill;
        private ConfigEntry<double> cinematicSpecialDelay;
        private ConfigEntry<CinematicMovement> cinematicSpecialMovement;
        private ConfigEntry<bool> cinematicRaid, cinematicSpawn, cinematicKill, cinematicBiome;
        private ConfigEntry<bool> cinematicFlip, cinematicLetterbox, cinematicBiomeLetterbox;
        private ConfigEntry<double> cinematicDistance, cinematicPan, cinematicSpawnDelay;
        private ConfigEntry<CinematicMovement> cinematicRaidMovement, cinematicBossMovement, cinematicBiomeMovement;
        private CinematicCamera cinematic;
        private Harmony cinematicHarmony;
        private string activeCameraKey, discoveryCameraKey, raidCameraKey;
        private double nextCameraScan, cameraWaitUntil;
        private readonly Dictionary<string, Vector3> bossPositions = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, float> bossHeights = new Dictionary<string, float>();
        private readonly HashSet<string> filmedSpawns = new HashSet<string>();
        private void InitializeCinematics()
        {
            const string help = "Host-controlled experimental offscreen camera; never moves the player's view. Four seconds at 640x360/10 FPS. Requires a nearby rendering client. Obstructed/unavailable shots fall back to player footage. Adds an optional separate Discord attachment, within existing upload limits.";
            renderCinematics = Bind("Cinematic Camera: Experimental", "RenderCinematics", true, "Personal performance preference: allow extra offscreen camera rendering on this computer. Off cancels local cinematic work while normal recording continues. The host still controls allowed cinematic events and appearance.");
            cinematicRaid = Bind("Cinematic Camera: Experimental", "RaidOpening", false, help + " Looks toward the raid circle center; included when the raid ends.");
            cinematicSpawn = Bind("Cinematic Camera: Experimental", "BossSpawnIntro", false, help + " New boss spawns only. Stores up to 30 minutes; attached first when that exact boss dies.");
            cinematicKill = Bind("Cinematic Camera: Experimental", "BossKillEnding", false, help + " Sweeps the boss death location after kill credit.");
            cinematicSpecialSpawn = Bind("Cinematic Camera: Experimental", "SpecialEnemySpawnIntro", false, help + " Fresh spawns matching Special Enemies.EnemyKeys only; attached when that exact enemy dies.");
            cinematicSpecialKill = Bind("Cinematic Camera: Experimental", "SpecialEnemyKillEnding", false, help + " Capture the selected special enemy death location after kill credit.");
            cinematicSpecialMovement = Bind("Cinematic Camera: Experimental", "SpecialEnemyMovement", CinematicMovement.Orbit, "Orbit, RiseAndReveal or ZoomIn for special enemy arrivals and aftermath. Uses the shared distance, pan, letterbox and orientation controls just like bosses.");
            cinematicSpecialDelay = Bind("Cinematic Camera: Experimental", "SpecialEnemySpawnDelaySeconds", 0.0, "Delay after a fresh special enemy spawn, 0-5 seconds. Loaded existing enemies do not trigger arrival footage.");
            cinematicBiome = Bind("Cinematic Camera: Experimental", "BiomeDiscovery", false, help + " Uses BiomeMovement and BiomeLetterbox around the player on eligible first biome discoveries.");
            cinematicBiomeMovement = Bind("Cinematic Camera: Experimental", "BiomeMovement", CinematicMovement.RiseAndReveal, "Movement for biome discovery: RiseAndReveal, Orbit or ZoomIn. Uses the shared DistanceMultiplier and PanDegrees. ZoomIn approaches from 2x to 1x distance over four seconds with a fixed lens, ignoring distance and pan settings.");
            cinematicBiomeLetterbox = Bind("Cinematic Camera: Experimental", "BiomeLetterbox", true, "Show the same animated cinematic black bars as boss shots on biome discoveries. Slides inward over 0.85 seconds to a 2.39:1 viewing window; the biome title fades in afterward. Independent of Letterbox for other event types.");
            cinematicDistance = Bind("Cinematic Camera: Experimental", "DistanceMultiplier", 2.0, "Camera distance relative to the original framing. 1 is the original closest framing; 3 is the maximum (three times as far). Default 2. Geometry/load limits may shorten the requested distance (80 metre maximum offset).");
            cinematicPan = Bind("Cinematic Camera: Experimental", "PanDegrees", 70.0, "Total horizontal sweep, 0-180 degrees. Applies to orbit and rising reveal shots.");
            cinematicRaidMovement = Bind("Cinematic Camera: Experimental", "RaidMovement", CinematicMovement.RiseAndReveal, "RiseAndReveal flies upward and tilts down toward the raid center. Orbit sweeps around the center at a steady elevation. ZoomIn approaches from 2x to 1x distance with a fixed lens, ignoring DistanceMultiplier and PanDegrees.");
            cinematicBossMovement = Bind("Cinematic Camera: Experimental", "BossMovement", CinematicMovement.Orbit, "Movement for boss arrival and aftermath: Orbit, RiseAndReveal or ZoomIn. ZoomIn starts at 2x distance and approaches the boss to 1x over four seconds with a fixed lens, ignoring DistanceMultiplier and PanDegrees. Rising can help clear nearby trees.");
            cinematicSpawnDelay = Bind("Cinematic Camera: Experimental", "BossSpawnDelaySeconds", 0.0, "Delay after the actual boss object appears, 0-5 seconds. Default 0 catches the arrival animation after the altar summon delay. Scan timing adds up to 0.5 seconds; busy clients may miss it.");
            cinematicLetterbox = Bind("Cinematic Camera: Experimental", "Letterbox", true, "Slide top/bottom black bars inward over the first 0.85 seconds for a dramatic 2.39:1 view. The exported canvas remains 16:9. Only extra camera footage is affected. Biome discoveries use BiomeLetterbox instead.");
            cinematicFlip = Bind("Cinematic Camera: Experimental", "FlipVertically", true, "Flip extra camera footage before titles and letterboxing. Default true fixes upside-down experimental footage. Independent of the normal capture orientation.");
            cinematicHarmony = new Harmony("local.valheimmoments.cinematic");
            try { CinematicCamera.Install(cinematicHarmony); }
            catch (Exception e) { cinematicHarmony.UnpatchSelf(); Logger.LogWarning("[Cinematic] Spawn detection unavailable: " + e.GetType().Name); }
            BossAttribution.CinematicSubject = CinematicCamera.Subject;
            ResetCinematics();
        }
        private void ResetCinematics()
        {
            cinematic?.Dispose();
            cinematic = new CinematicCamera(Path.Combine(Path.GetDirectoryName(outputDirectory), "RaidTemp"), encoderPath,
                message => Logger.LogWarning("[Cinematic] " + message));
            bossPositions.Clear(); bossHeights.Clear(); filmedSpawns.Clear(); activeCameraKey = discoveryCameraKey = raidCameraKey = null;
        }
        private void UpdateCinematics(double now, bool active)
        {
            if (cinematic == null) return;
            if (!active || !Value(renderCinematics) || Player.m_localPlayer == null || Player.m_localPlayer.IsDead())
            { if (cinematic.Busy) ResetCinematics(); return; }
            cinematic.Tick(now);
            if (now < nextCameraScan || (!Value(cinematicSpawn) && !Value(cinematicKill) && !Value(cinematicSpecialSpawn) && !Value(cinematicSpecialKill))) return;
            nextCameraScan = now + 0.5;
            specialEnemies.Configure(Value(specialKeys));
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c is Player) continue;
                bool special = !c.IsBoss();
                if (special && (!Value(specialEnabled) || !specialEnemies.Contains(c.m_name))) continue;
                bool spawnEnabled = Value(special ? cinematicSpecialSpawn : cinematicSpawn);
                double delay = Value(special ? cinematicSpecialDelay : cinematicSpawnDelay);
                string id = CinematicCamera.Subject(c); if (id == null) continue;
                if (bossPositions.Count < 128 || bossPositions.ContainsKey(id)) { bossPositions[id] = c.GetCenterPoint(); bossHeights[id] = c.GetHeight(); }
                if (!spawnEnabled || cinematic.Busy || filmedSpawns.Contains(id) || c.IsDead() || filmedSpawns.Count >= 256) continue;
                long ticks = c.GetComponent<ZNetView>()?.GetZDO()?.GetLong(CinematicCamera.SpawnStamp, 0) ?? 0;
                double age = ticks == 0 ? -1 : (ZNet.instance.GetTime().Ticks - ticks) / (double)TimeSpan.TicksPerSecond;
                // The altar already delays creation. Capture fresh arrival immediately by default.
                if (age >= delay && age <= delay + 3 && StartCinematic(id, 1, c.GetCenterPoint(), c.transform, c.GetHeight(), now, BossCaption(c), special)) filmedSpawns.Add(id);
            }
        }
        private void StartBossAftermath(BossKill kill)
        {
            Vector3 center;
            if (Value(kill.Special ? cinematicSpecialKill : cinematicKill) && kill.CinematicSubject != null && bossPositions.TryGetValue(kill.CinematicSubject, out center))
                StartCinematic(kill.CinematicSubject, 2, center, null, bossHeights[kill.CinematicSubject], clock.Elapsed.TotalSeconds, CinematicText(kill.EnemyKey) + "\nDefeated", kill.Special);
            if (kill.CinematicSubject != null) { bossPositions.Remove(kill.CinematicSubject); bossHeights.Remove(kill.CinematicSubject); }
        }
        private bool CinematicPending(string key, double now)
        { return key != null && now < cameraWaitUntil && cinematic?.Pending(key) == true; }
        private static string CinematicText(string text)
        { return EpicLootAdapter.Plain(Localization.instance != null ? Localization.instance.Localize(text ?? "") : text ?? "", 160).Replace("\r", " ").Replace("\n", " "); }
        private static string BossCaption(Character boss)
        {
            try {
            string caption = CinematicText(boss.m_name);
            int stars = Math.Max(0, boss.GetLevel() - 1);
            caption += "\n" + stars + (stars == 1 ? " star" : " stars");
            float health = boss.GetMaxHealth();
            if (!float.IsNaN(health) && !float.IsInfinity(health) && health > 0) caption += "  |  Max health: " + health.ToString("N0");
            return caption;
            } catch { return "Boss arrival"; }
        }
        private static string RaidCaption(RandomEvent raid)
        {
            try {
            string title = CinematicText(string.IsNullOrWhiteSpace(raid.m_startMessage) ? raid.m_name : raid.m_startMessage);
            var enemies = new List<string>();
            if (raid.m_spawn != null) foreach (var spawn in raid.m_spawn)
            {
                var enemy = spawn == null || spawn.m_prefab == null ? null : spawn.m_prefab.GetComponent<Character>();
                if (enemy == null) continue;
                string name = CinematicText(enemy.m_name);
                if (name.Length != 0 && !enemies.Contains(name) && enemies.Count < 3) enemies.Add(name);
            }
            return title + "\nRaid begins" + (enemies.Count == 0 ? "" : "\nPossible foes: " + CinematicText(string.Join(", ", enemies)));
            } catch { return "Raid begins"; }
        }
        private bool StartCinematic(string key, byte kind, Vector3 center, Transform follow, float size, double now, string caption, bool special = false)
        {
            if (!Value(renderCinematics)) return false;
            var movement = kind == 4 ? Value(cinematicBiomeMovement) : kind == 3 ? Value(cinematicRaidMovement) : Value(special ? cinematicSpecialMovement : cinematicBossMovement);
            return cinematic?.Start(key, kind, center, follow, size, now, quality, (float)Value(cinematicDistance), (float)Value(cinematicPan),
                movement, caption, Value(kind == 4 ? cinematicBiomeLetterbox : cinematicLetterbox), Value(cinematicFlip)) == true;
        }
        private void AttachCinematics(string file, string key)
        {
            if (key == null || cinematic == null) return;
            try { CinematicPacket.Attach(file, cinematic.Take(key)); }
            catch (Exception e) { Logger.LogWarning("[Cinematic] Optional footage omitted: " + e.GetType().Name); }
        }

        // Runs on a worker. No player camera, Unity objects, or host settings cross this boundary.
        private static async Task<UploadResult> UploadCinematicBatch(DiscordClip[] originals, DiscordOptions options, long budget,
            string helper, int q, string directory, bool labelPlayers, byte allowed, CancellationToken token)
        {
            var owned = new List<string>();
            try
            {
                Directory.CreateDirectory(directory);
                var players = new List<DiscordClip>(); var candidates = new SortedDictionary<byte, byte[]>();
                long playerBytes = 0;
                foreach (var original in originals)
                {
                    token.ThrowIfCancellationRequested();
                    List<CinematicPacket.Shot> shots;
                    byte[] primary;
                    // Files over relay size cannot contain our bounded packet; retain normal large local uploads.
                    if (new FileInfo(original.File).Length <= CinematicPacket.Limit)
                        primary = CinematicPacket.Strip(File.ReadAllBytes(original.File), out shots);
                    else { primary = null; shots = new List<CinematicPacket.Shot>(); }
                    string path = original.File;
                    if (shots.Count != 0)
                    {
                        path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".webp"); owned.Add(path); File.WriteAllBytes(path, primary);
                        foreach (var shot in shots)
                            if ((allowed & (1 << shot.Kind)) != 0 && !candidates.ContainsKey(shot.Kind)) candidates.Add(shot.Kind, shot.Pixels);
                    }
                    if (labelPlayers)
                    {
                        string labelled = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".webp"); owned.Add(labelled);
                        EncoderClient.Compose(helper, path, null, labelled, q, token, original.Recorder); path = labelled;
                    }
                    players.Add(new DiscordClip(path, original.Recorder, true)); playerBytes += new FileInfo(path).Length;
                }
                var resultClips = new List<DiscordClip>(); long total = playerBytes;
                foreach (var pair in candidates)
                {
                    if (pair.Value.Length > options.MaxUploadBytes || pair.Value.Length > budget - total) continue;
                    // Validate full animation with the bounded helper before accepting a peer's nested media.
                    string source = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".webp"); owned.Add(source); File.WriteAllBytes(source, pair.Value);
                    string validated = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".webp"); owned.Add(validated);
                    try { EncoderClient.Compose(helper, source, null, validated, q, token); }
                    catch (OperationCanceledException) { throw; }
                    catch { continue; }
                    long bytes = new FileInfo(validated).Length;
                    if (bytes > options.MaxUploadBytes || bytes > budget - total) continue;
                    total += bytes; resultClips.Add(new DiscordClip(validated, "Cinematic: " + CinematicPacket.Label(pair.Key), true));
                }
                resultClips.AddRange(players);
                return await DiscordWebhook.UploadManyAsync(resultClips.ToArray(), options, budget, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return UploadResult.Fail("Upload preparation cancelled."); }
            catch (Exception e) { return UploadResult.Fail("Upload preparation failed (" + e.GetType().Name + ")."); }
            finally
            {
                foreach (string path in owned) { try { File.Delete(path); } catch { } }
                try { if (Directory.Exists(directory)) Directory.Delete(directory); } catch { }
            }
        }
        private byte AllowedCinematics(string kind)
        {
            byte flags = 0;
            if (kind == "boss") { if (Value(cinematicSpawn)) flags |= 2; if (Value(cinematicKill)) flags |= 4; }
            if (kind == "special" && Value(specialEnabled)) { if (Value(cinematicSpecialSpawn)) flags |= 2; if (Value(cinematicSpecialKill)) flags |= 4; }
            if (kind == "raid" && Value(cinematicRaid)) flags |= 8;
            if (kind == "discovery" && Value(cinematicBiome)) flags |= 16;
            return flags;
        }
    }
}
