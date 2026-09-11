using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;

namespace ValheimMoments
{
    // Snapshot before binding: BepInEx may save the file after each new binding.
    // Only migration keys are retained; credentials are never copied or logged.
    internal sealed class ConfigurationMigration
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();
        internal ConfigurationMigration(string path)
        {
            if (!File.Exists(path)) return;
            string section = "";
            foreach (string line in File.ReadAllLines(path))
            {
                string text = line.Trim();
                if (text.StartsWith("#") || text.Length == 0) continue;
                if (text.StartsWith("[") && text.EndsWith("]")) { section = text.Substring(1, text.Length - 2); continue; }
                int equal = text.IndexOf('=');
                if (equal < 1) continue;
                string key = section + "/" + text.Substring(0, equal).Trim();
                if (key == "Discord/SaveLocalCopy" || key == "Capture/Width" || key == "Capture/Height" ||
                    key == "Boss Kill/FirstKillOnly" || key == "Boss Kill/OnlyCaptureIfLootMeetsRarity" || key == "Boss Kill/FirstKillBypassesRarity")
                    values[key] = text.Substring(equal + 1).Trim();
            }
        }
        private bool Flag(string key, bool fallback)
        { string text; bool flag; return values.TryGetValue(key, out text) && bool.TryParse(text, out flag) ? flag : fallback; }
        internal bool SaveLocalCopy { get { return Flag("Discord/SaveLocalCopy", false); } }
        internal bool CustomSize { get { return values.ContainsKey("Capture/Width") || values.ContainsKey("Capture/Height"); } }
        internal BossCaptureMode BossMode
        {
            get
            {
                if (!values.ContainsKey("Boss Kill/FirstKillOnly") && !values.ContainsKey("Boss Kill/OnlyCaptureIfLootMeetsRarity"))
                    return BossCaptureMode.FirstKillThenRarity;
                bool first = Flag("Boss Kill/FirstKillOnly", false), filter = Flag("Boss Kill/OnlyCaptureIfLootMeetsRarity", false);
                bool bypass = Flag("Boss Kill/FirstKillBypassesRarity", true);
                if (first) return filter && !bypass ? BossCaptureMode.FirstKillWithRarity : BossCaptureMode.FirstKillOnly;
                return !filter ? BossCaptureMode.AllKills : bypass ? BossCaptureMode.FirstKillThenRarity : BossCaptureMode.RarityOnly;
            }
        }
        internal static void Retire(ConfigFile file)
        {
            bool save = file.SaveOnConfigSet;
            file.SaveOnConfigSet = false;
            try
            {
                foreach (string key in new[] { "Discord/SaveLocalCopy", "Discord/UploadClips", "Discord/EnableClientRelay",
                    "Boss Kill/FirstKillOnly", "Boss Kill/OnlyCaptureIfLootMeetsRarity", "Boss Kill/FirstKillBypassesRarity" })
                {
                    string[] parts = key.Split('/');
                    // Binding consumes the orphan value, then Remove retires the entry.
                    file.Bind(parts[0], parts[1], false, "Retired setting");
                    file.Remove(new ConfigDefinition(parts[0], parts[1]));
                }
                file.Save();
            }
            finally { file.SaveOnConfigSet = save; }
        }
    }
    internal enum BossCaptureMode { AllKills, FirstKillOnly, RarityOnly, FirstKillThenRarity, FirstKillWithRarity }
    internal static class BossCaptureRules
    {
        internal static bool FirstOnly(BossCaptureMode mode) { return mode == BossCaptureMode.FirstKillOnly || mode == BossCaptureMode.FirstKillWithRarity; }
        internal static bool UsesRarity(BossCaptureMode mode) { return mode == BossCaptureMode.RarityOnly || mode == BossCaptureMode.FirstKillThenRarity || mode == BossCaptureMode.FirstKillWithRarity; }
        internal static bool FirstBypasses(BossCaptureMode mode) { return mode == BossCaptureMode.FirstKillThenRarity; }
    }
}
