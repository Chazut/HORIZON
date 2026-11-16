using BepInEx.Configuration;
using Horizon;
using UnityEngine;
using System.Globalization;

// Unity's native JsonUtility is unavailable in this console harness. Keep the
// real BepInEx config pipeline and substitute only its Vector2 codec.
var vectorCodec = TomlTypeConverter.GetConverter(typeof(Vector2));
vectorCodec.ConvertToString = (value, _) =>
{
    var v = (Vector2)value;
    return v.x.ToString(CultureInfo.InvariantCulture) + "," + v.y.ToString(CultureInfo.InvariantCulture);
};
vectorCodec.ConvertToObject = (value, _) =>
{
    var parts = value.Split(',');
    return new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
};

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
    Check(fresh.VisitDistance.Value == 2.5f && fresh.Overlay.Value != SpectatorOverlay.BotDebug && fresh.ShowHelp.Value, "Default camera/help preferences and BotDebug OFF");
    Check(!fresh.AutoSwitchOnDeath.Value && !fresh.ShowBotList.Value && fresh.Overlay.Value == SpectatorOverlay.BotInfo, "Default detached camera on death, optional list OFF and HORIZON labels selected");
    fresh.AutoSwitchOnDeath.Value = true; fresh.ShowBotList.Value = true;
    fresh.Overlay.Value = SpectatorOverlay.Off;
    fresh.BotListPosition.Value = new Vector2(.3f, .4f);
    var displayReload = new SpectatorSettings(new ConfigFile(path, false));
    Check(displayReload.AutoSwitchOnDeath.Value && displayReload.ShowBotList.Value && displayReload.Overlay.Value == SpectatorOverlay.Off, "Explicit list, overlay and death preferences survive restart");
    Check(displayReload.BotListPosition.Value.x == .3f && displayReload.BotListPosition.Value.y == .4f, "Dragged list position persists independently");
    Check(fresh.MenuKey.Value.MainKey == KeyCode.Escape, "Escape opens spectator menu by default");
    Check(fresh.FollowKey.Value.MainKey == KeyCode.F && fresh.OrientationKey.Value.MainKey == KeyCode.R, "Independent follow and orientation keys");
    Check(fresh.AutomaticScale.Value && fresh.InterfaceScale.Value == 1f, "New and existing configs use resolution-aware UI scaling");
    Check(fresh.ZoomKey.Value.MainKey == KeyCode.LeftControl && fresh.ZoomSpeed.Value == 5f, "Ctrl and wheel zoom is enabled by default");
    fresh.AutomaticScale.Value = false;
    fresh.InterfaceScale.Value = 1.75f;
    fresh.ZoomKey.Value = new KeyboardShortcut(KeyCode.Z);
    fresh.ZoomSpeed.Value = 3f;
    fresh.HelpPosition.Value = new Vector2(.25f, .75f);
    fresh.MenuPosition.Value = new Vector2(.6f, .2f);
    fresh.OrientationKey.Value = new KeyboardShortcut(KeyCode.F8);
    fresh.AfterDeath.Value = false;
    fresh.AfterExtraction.Value = false;
    fresh.Targets.Value = BotFilter.AllBots;
    fresh.ReverseWheel.Value = true;
    fresh.Overlay.Value = SpectatorOverlay.BotInfo;
    fresh.ShowHelp.Value = false;
    fresh.VisitDistance.Value = 5f;
    var reload = new SpectatorSettings(new ConfigFile(path, false));
    Check(!reload.AutomaticScale.Value && reload.InterfaceScale.Value == 1.75f, "Manual scale survives restart");
    Check(reload.ZoomKey.Value.MainKey == KeyCode.Z && reload.ZoomSpeed.Value == 3f, "Zoom key and speed survive restart");
    reload.InterfaceScale.Value = 100;
    Check(reload.InterfaceScale.Value == 2.5f, "Maximum interface scale bounded");
    reload.InterfaceScale.Value = .1f;
    Check(reload.InterfaceScale.Value == .75f, "Minimum interface scale bounded");
    Check(reload.HelpPosition.Value.x == .25f && reload.HelpPosition.Value.y == .75f && reload.MenuPosition.Value.x == .6f && reload.MenuPosition.Value.y == .2f, "Separate dragged panel positions survive restart");
    Check(reload.OrientationKey.Value.MainKey == KeyCode.F8, "Customized orientation key survives restart");
    Check(!reload.AfterDeath.Value && !reload.AfterExtraction.Value && !reload.ShowHelp.Value && reload.Overlay.Value == SpectatorOverlay.BotInfo, "Independent toggles survive restart");
    Check(reload.Targets.Value == BotFilter.AllBots && reload.ReverseWheel.Value && reload.VisitDistance.Value == 5f, "Navigation preferences survive restart");
    reload.VisitDistance.Value = 1000f;
    Check(reload.VisitDistance.Value == 10f, "Camera distance is bounded");
    path = Path.Combine(dir, "legacy.cfg");
    File.WriteAllText(path, "[General]\nEnabled = false\nSpectate after extraction = false\nCycle PMCs with mouse wheel = false\nAutomatic BotDebug overlay = false\nSpectator menu = F7\n");
    var migrated = new SpectatorSettings(new ConfigFile(path, false));
    Check(migrated.AutomaticScale.Value && migrated.InterfaceScale.Value == 1 && migrated.ZoomKey.Value.MainKey == KeyCode.LeftControl, "Old config receives UI and zoom defaults without changing existing controls");
    Check(!migrated.Enabled.Value && !migrated.AfterExtraction.Value && !migrated.CycleBots.Value && migrated.Overlay.Value == SpectatorOverlay.BotInfo && migrated.MenuKey.Value.MainKey == KeyCode.F7, "Legacy preferences are kept when updating Raid Spectator");
    Check(!File.ReadAllText(path).Contains("Cycle PMCs with mouse wheel") && File.ReadAllText(path).Contains("Cycle bots with mouse wheel = false"), "Legacy wheel entry is replaced without conflicting duplicate");
    Check(!new SpectatorSettings(new ConfigFile(path, false)).CycleBots.Value, "Migration is idempotent");
    File.WriteAllText(path, "[General]\nCycle PMCs with mouse wheel = false\nCycle bots with mouse wheel = true\nCamera targets = AllBots\n");
    var mixed = new SpectatorSettings(new ConfigFile(path, false));
    Check(mixed.CycleBots.Value && mixed.Targets.Value == BotFilter.AllBots, "Existing new preferences take priority over legacy key");
    string before = File.ReadAllText(path);
    var noSave = new ConfigFile(path, false) { SaveOnConfigSet = false };
    _ = new SpectatorSettings(noSave);
    Check(!noSave.SaveOnConfigSet && File.ReadAllText(path) == before, "Configuration initialization respects disabled automatic saving");

    string horizonPath = Path.Combine(dir, "com.chazut.horizon.cfg");
    var horizonConfig = new ConfigFile(horizonPath, false);
    Check(!SpectatorSettings.ImportLegacyConfig(horizonConfig), "Fresh HORIZON install requires no legacy config");
    string oldPath = Path.Combine(dir, "com.chazut.raidspectator.cfg");
    const string oldSettings = "[General]\nEnabled = false\nSpectate after extraction = false\nCycle PMCs with mouse wheel = false\nCamera targets = AllBots\nAutomatic BotDebug overlay = true\nSpectator menu = F7\n";
    File.WriteAllText(oldPath, oldSettings);
    Check(SpectatorSettings.ImportLegacyConfig(horizonConfig), "First HORIZON launch imports the old GUID config");
    var imported = new SpectatorSettings(horizonConfig);
    Check(imported.Overlay.Value == SpectatorOverlay.BotDebug, "Existing automatic BotDebug preference migrates into the exclusive selector");
    imported.Overlay.Value = SpectatorOverlay.BotInfo;
    Check(new SpectatorSettings(new ConfigFile(horizonPath, false)).Overlay.Value == SpectatorOverlay.BotInfo, "New overlay choice takes priority over old automatic BotDebug setting");
    Check(!imported.Enabled.Value && !imported.AfterExtraction.Value && !imported.CycleBots.Value && imported.Targets.Value == BotFilter.AllBots && imported.MenuKey.Value.MainKey == KeyCode.F7,
        "GUID migration preserves user preferences and the old wheel key");
    Check(File.ReadAllText(oldPath) == oldSettings, "Original Raid Spectator config is preserved");
    imported.Targets.Value = BotFilter.PmcOnly;
    string savedHorizon = File.ReadAllText(horizonPath);
    Check(!SpectatorSettings.ImportLegacyConfig(horizonConfig) && File.ReadAllText(horizonPath) == savedHorizon,
        "Existing HORIZON config takes priority and migration is idempotent");
    Check(new SpectatorSettings(new ConfigFile(horizonPath, false)).Targets.Value == BotFilter.PmcOnly, "HORIZON preferences survive reload after migration");
    Console.WriteLine($"PASS: {checks} configuration checks using the installed BepInEx serializer.");
}
finally { Directory.Delete(dir, true); }
