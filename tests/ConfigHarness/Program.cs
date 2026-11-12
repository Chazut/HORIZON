using BepInEx.Configuration;
using RaidSpectator;
using UnityEngine;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
string dir = Path.Combine(Path.GetTempPath(), "HORIZON-config-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    string path = Path.Combine(dir, "fresh.cfg");
    var fresh = new SpectatorSettings(new ConfigFile(path, false));
    Check(fresh.Enabled.Value && fresh.AfterDeath.Value && fresh.AfterExtraction.Value, "Fresh install continues after death and extraction");
    Check(fresh.Targets.Value == BotFilter.PmcOnly && fresh.CycleBots.Value && !fresh.ReverseWheel.Value, "PMC-only wheel navigation by default");
    Check(fresh.VisitDistance.Value == 2.5f && !fresh.AutoBotDebug.Value && fresh.ShowHelp.Value, "Default camera/help preferences and BotDebug OFF");
    Check(fresh.MenuKey.Value.MainKey == KeyCode.Escape, "Escape opens spectator menu by default");
    fresh.AfterDeath.Value = false;
    fresh.AfterExtraction.Value = false;
    fresh.Targets.Value = BotFilter.AllBots;
    fresh.ReverseWheel.Value = true;
    fresh.AutoBotDebug.Value = false;
    fresh.ShowHelp.Value = false;
    fresh.VisitDistance.Value = 5f;
    var reload = new SpectatorSettings(new ConfigFile(path, false));
    Check(!reload.AfterDeath.Value && !reload.AfterExtraction.Value && !reload.ShowHelp.Value && !reload.AutoBotDebug.Value, "Independent toggles survive restart");
    Check(reload.Targets.Value == BotFilter.AllBots && reload.ReverseWheel.Value && reload.VisitDistance.Value == 5f, "Navigation preferences survive restart");
    reload.VisitDistance.Value = 1000f;
    Check(reload.VisitDistance.Value == 10f, "Camera distance is bounded");
    path = Path.Combine(dir, "legacy.cfg");
    File.WriteAllText(path, "[General]\nEnabled = false\nSpectate after extraction = false\nCycle PMCs with mouse wheel = false\nAutomatic BotDebug overlay = false\nSpectator menu = F7\n");
    var migrated = new SpectatorSettings(new ConfigFile(path, false));
    Check(!migrated.Enabled.Value && !migrated.AfterExtraction.Value && !migrated.CycleBots.Value && !migrated.AutoBotDebug.Value && migrated.MenuKey.Value.MainKey == KeyCode.F7, "Legacy preferences are kept when updating Raid Spectator");
    Check(!File.ReadAllText(path).Contains("Cycle PMCs with mouse wheel") && File.ReadAllText(path).Contains("Cycle bots with mouse wheel = false"), "Legacy wheel entry is replaced without conflicting duplicate");
    Check(!new SpectatorSettings(new ConfigFile(path, false)).CycleBots.Value, "Migration is idempotent");
    File.WriteAllText(path, "[General]\nCycle PMCs with mouse wheel = false\nCycle bots with mouse wheel = true\nCamera targets = AllBots\n");
    var mixed = new SpectatorSettings(new ConfigFile(path, false));
    Check(mixed.CycleBots.Value && mixed.Targets.Value == BotFilter.AllBots, "Existing new preferences take priority over legacy key");
    string before = File.ReadAllText(path);
    var noSave = new ConfigFile(path, false) { SaveOnConfigSet = false };
    _ = new SpectatorSettings(noSave);
    Check(!noSave.SaveOnConfigSet && File.ReadAllText(path) == before, "Configuration initialization respects disabled automatic saving");
    Console.WriteLine($"PASS: {checks} configuration checks using the installed BepInEx serializer.");
}
finally { Directory.Delete(dir, true); }
