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
    Check(fresh.DynamicMapsTracking.Value, "DynamicMaps spectator tracking defaults to on");
    fresh.DynamicMapsTracking.Value = false;
    Check(!new SpectatorSettings(new ConfigFile(path, false)).DynamicMapsTracking.Value, "DynamicMaps opt-out survives a restart");
    Check(!fresh.UseSpectatorFov.Value && fresh.ShowHumanLabels.Value && fresh.ShowDistance.Value &&
        !fresh.ActionsOnlyWhenFollowing.Value && fresh.UseFikaSelection.Value && fresh.HideExtractedWeapon.Value, "Spectator roadmap defaults, including actions for everyone");
    Check(fresh.LabelOptions.Length == 10 && fresh.LabelOptions.Select(x => x.Field).Distinct().Count() == 10
        && fresh.VisibleLabelFields == BotLabelFields.All, "Every existing label field has an independent switch, on by default");
    foreach (var (field, setting) in fresh.LabelOptions)
    {
        setting.Value = false;
        Check(fresh.VisibleLabelFields == (BotLabelFields.All & ~field), $"Only {field} is hidden");
        Check(new SpectatorSettings(new ConfigFile(path, false)).VisibleLabelFields == fresh.VisibleLabelFields, $"{field} survives restart");
        setting.Value = true;
    }
    var previous = Path.Combine(dir, "previous-horizon.cfg");
    File.WriteAllText(previous, "[Interface]\nShow distance on labels = false\nShow actions only for followed bot = true\n");
    var upgraded = new SpectatorSettings(new ConfigFile(previous, false));
    Check(upgraded.VisibleLabelFields == (BotLabelFields.All & ~BotLabelFields.Distance) && upgraded.ActionsOnlyWhenFollowing.Value,
        "Upgrade keeps existing distance and followed-only preferences while enabling new fields");
    fresh.UseSpectatorFov.Value = true; fresh.SpectatorFov.Value = 89;
    fresh.ActionsOnlyWhenFollowing.Value = true; fresh.ShowHumanLabels.Value = false;
    fresh.ShowDistance.Value = false; fresh.UseFikaSelection.Value = false; fresh.HideExtractedWeapon.Value = false;
    var preferences = new SpectatorSettings(new ConfigFile(path, false));
    Check(preferences.UseSpectatorFov.Value && preferences.SpectatorFov.Value == 89 && preferences.ActionsOnlyWhenFollowing.Value &&
        !preferences.ShowHumanLabels.Value && !preferences.ShowDistance.Value && !preferences.UseFikaSelection.Value && !preferences.HideExtractedWeapon.Value,
        "All spectator preferences persist across restarts");
    Check(fresh.Enabled.Value && fresh.AfterDeath.Value && fresh.AfterExtraction.Value, "Fresh install continues after death and extraction");
    Check(fresh.Targets.Value == BotFilter.PmcOnly && fresh.CycleBots.Value && !fresh.ReverseWheel.Value, "PMC-only wheel navigation by default");
    Check(fresh.VisitDistance.Value == 2.5f && fresh.Overlay.Value != SpectatorOverlay.BotDebug && fresh.ShowHelp.Value, "Default camera/help preferences and BotDebug OFF");
    Check(!fresh.AutoSwitchOnDeath.Value && !fresh.ShowBotList.Value && fresh.Overlay.Value == SpectatorOverlay.BotInfo, "Default detached camera on death, optional list OFF and HORIZON labels selected");
    Check(fresh.ShowOccludedBots.Value, "Through-scenery labels are enabled by default");
    Check(!fresh.LimitLabelDistance.Value && fresh.EffectiveLabelDistance == 0, "Distant labels enabled by default, including upgrades with a saved 150m range");
    fresh.LimitLabelDistance.Value = true; fresh.LabelDistance.Value = 700;
    Check(fresh.EffectiveLabelDistance == 700, "Optional distance limit uses the saved range");
    fresh.ShowOccludedBots.Value = false;
    fresh.AutoSwitchOnDeath.Value = true; fresh.ShowBotList.Value = true;
    fresh.Overlay.Value = SpectatorOverlay.Off;
    fresh.BotListPosition.Value = new Vector2(.3f, .4f);
    var displayReload = new SpectatorSettings(new ConfigFile(path, false));
    Check(displayReload.LimitLabelDistance.Value && displayReload.EffectiveLabelDistance == 700, "Explicit distance limit survives restart");
    Check(displayReload.AutoSwitchOnDeath.Value && displayReload.ShowBotList.Value && displayReload.Overlay.Value == SpectatorOverlay.Off, "Explicit list, overlay and death preferences survive restart");
    Check(!displayReload.ShowOccludedBots.Value, "Explicit visibility preference survives restart");
    Check(displayReload.BotListPosition.Value.x == .3f && displayReload.BotListPosition.Value.y == .4f, "Dragged list position persists independently");
    Check(fresh.MenuKey.Value.MainKey == KeyCode.Escape, "Escape opens spectator menu by default");
    Check(fresh.FollowKey.Value.MainKey == KeyCode.F && fresh.OrientationKey.Value.MainKey == KeyCode.R, "Independent follow and orientation keys");
    Check(fresh.BotInfoKey.Value.MainKey == KeyCode.F3, "F3 toggles Bot info by default");
    Check(fresh.AutomaticScale.Value && fresh.InterfaceScale.Value == 1f, "New and existing configs use resolution-aware UI scaling");
    Check(fresh.ZoomKey.Value.MainKey == KeyCode.LeftControl && fresh.ZoomSpeed.Value == 5f, "Ctrl and wheel zoom is enabled by default");
    fresh.AutomaticScale.Value = false;
    fresh.InterfaceScale.Value = 1.75f;
    fresh.ZoomKey.Value = new KeyboardShortcut(KeyCode.Z);
    fresh.ZoomSpeed.Value = 3f;
    fresh.HelpPosition.Value = new Vector2(.25f, .75f);
    fresh.MenuPosition.Value = new Vector2(.6f, .2f);
    fresh.OrientationKey.Value = new KeyboardShortcut(KeyCode.F8);
    fresh.BotInfoKey.Value = new KeyboardShortcut(KeyCode.J, KeyCode.LeftShift);
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
    Check(reload.BotInfoKey.Value.Equals(fresh.BotInfoKey.Value), "Configured Bot info shortcut and modifier survive restart");
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
    Check(imported.Overlay.Value == SpectatorOverlay.BotInfo, "Legacy automatic BotDebug flag does not override the new BotInfo default");
    imported.Overlay.Value = SpectatorOverlay.BotDebug;
    Check(new SpectatorSettings(new ConfigFile(horizonPath, false)).Overlay.Value == SpectatorOverlay.BotDebug, "Explicit BotDebug selector remains available and survives restart");
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
