using System;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace Horizon;

internal sealed class SpectatorSettings
{
    public ConfigEntry<bool> Enabled { get; }
    public ConfigEntry<bool> AfterDeath { get; }
    public ConfigEntry<bool> AfterExtraction { get; }
    public ConfigEntry<bool> CycleBots { get; }
    public ConfigEntry<BotFilter> Targets { get; }
    public ConfigEntry<bool> ReverseWheel { get; }
    public ConfigEntry<float> VisitDistance { get; }
    public ConfigEntry<bool> ShowHelp { get; }
    public ConfigEntry<bool> AutoBotDebug { get; }
    public ConfigEntry<KeyboardShortcut> MenuKey { get; }
    public ConfigEntry<KeyboardShortcut> FollowKey { get; }
    public ConfigEntry<KeyboardShortcut> OrientationKey { get; }
    public ConfigEntry<float> OrbitSensitivity { get; }
    public ConfigEntry<Vector2> HelpPosition { get; }
    public ConfigEntry<Vector2> MenuPosition { get; }

    public static bool ImportLegacyConfig(ConfigFile config)
    {
        string destination = config.ConfigFilePath;
        if (!string.Equals(Path.GetFileName(destination), "com.chazut.horizon.cfg", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(destination)) return false;
        string legacy = Path.Combine(Path.GetDirectoryName(destination)!, "com.chazut.raidspectator.cfg");
        if (!File.Exists(legacy)) return false;
        File.Copy(legacy, destination, overwrite: false);
        config.Reload();
        return true;
    }

    public SpectatorSettings(ConfigFile config)
    {
        bool save = config.SaveOnConfigSet;
        config.SaveOnConfigSet = false;
        try
        {
            Enabled = config.Bind("General", "Enabled", true, "Keep a solo raid running after death/extraction and enter Freecam. Disabling while spectating saves your original raid result.");
            AfterDeath = config.Bind("General", "Spectate after death", true, "Continue watching after death. If Killcam is installed, its replay finishes first. Changes apply to the next death, not the current spectator session.");
            AfterExtraction = config.Bind("General", "Spectate after extraction", true, "Continue watching after a completed extraction, including run-throughs. Keeps the original result, exit and play time. Transit is unchanged. Changes apply to the next extraction.");
            // Migrate the renamed wheel setting. An existing new setting always
            // wins over the legacy value.
            var legacy = config.Bind("General", "Cycle PMCs with mouse wheel", true);
            CycleBots = config.Bind("General", "Cycle bots with mouse wheel", legacy.Value, "Scroll up/down to move only the camera to the next/previous living AI bot in the selected filter. Disable to restore Freecam wheel zoom. Menu buttons remain available.");
            config.Remove(legacy.Definition);
            Targets = config.Bind("General", "Camera targets", BotFilter.PmcOnly, "PmcOnly: living AI USEC/BEAR. AllBots: also Scavs, bosses, guards, raiders and other AI. Humans, dead and inactive characters are excluded. Does not change bot behavior.");
            ReverseWheel = config.Bind("General", "Reverse mouse wheel", false, "Reverse the previous/next direction of the mouse wheel. Does not change the menu buttons.");
            VisitDistance = config.Bind("General", "Camera visit distance", 2.5f, new ConfigDescription("Distance in metres from a selected bot when visiting or following. Nearby walls shorten this distance.", new AcceptableValueRange<float>(1f, 10f)));
            ShowHelp = config.Bind("General", "Show spectator help", true, "Show the small HORIZON panel while moving the camera. The spectator menu remains accessible when hidden.");
            AutoBotDebug = config.Bind("General", "Automatic BotDebug overlay", false, "If BotDebug is installed, enable its overlay when spectating starts. OFF by default; ignored when BotDebug is absent. You can also toggle it for the current raid in the spectator menu. The overlay is turned off when leaving, without rewriting BotDebug's saved configuration.");
            MenuKey = config.Bind("General", "Spectator menu", new KeyboardShortcut(KeyCode.Escape), "Release the mouse and open camera controls, overlay options and Leave raid. Escape always works as a fallback. The raid keeps running while the menu is open.");
            FollowKey = config.Bind("General", "Follow selected bot", new KeyboardShortcut(KeyCode.F), "Toggle a camera following the selected bot. Scroll or use Next/Previous to switch targets. Press again to return to Freecam. Only active while spectating, outside menus.");
            OrientationKey = config.Bind("General", "Follow camera orientation", new KeyboardShortcut(KeyCode.R), "While following, toggle between the bot's look direction and free mouse rotation around the bot.");
            OrbitSensitivity = config.Bind("General", "Orbit mouse sensitivity", 3f, new ConfigDescription("Mouse sensitivity when rotating around the followed bot.", new AcceptableValueRange<float>(0.1f, 10f)));
            HelpPosition = config.Bind("Interface", "Help panel position", new Vector2(1f, 0f), "Saved relative position. Hold Left Alt and drag the HORIZON header to move the small panel.");
            MenuPosition = config.Bind("Interface", "Menu position", new Vector2(1f, 0f), "Saved relative position. Drag the HORIZON header to move the open menu.");
        }
        finally { config.SaveOnConfigSet = save; }
        if (save) config.Save();
    }
}
