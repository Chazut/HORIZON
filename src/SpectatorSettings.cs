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
    public ConfigEntry<bool> UseSpectatorFov { get; }
    public ConfigEntry<float> SpectatorFov { get; }
    public ConfigEntry<bool> ShowHumanLabels { get; }
    public ConfigEntry<bool> ShowDistance { get; }
    public (BotLabelFields Field, ConfigEntry<bool> Setting)[] LabelOptions { get; }
    public BotLabelFields VisibleLabelFields
    {
        get
        {
            var fields = BotLabelFields.None;
            foreach (var option in LabelOptions)
                if (option.Setting.Value) fields |= option.Field;
            return fields;
        }
    }
    public ConfigEntry<bool> ActionsOnlyWhenFollowing { get; }
    public ConfigEntry<bool> UseFikaSelection { get; }
    public ConfigEntry<bool> HideExtractedWeapon { get; }
    public ConfigEntry<float> VisitDistance { get; }
    public ConfigEntry<bool> ShowHelp { get; }
    public ConfigEntry<SpectatorOverlay> Overlay { get; }
    public ConfigEntry<bool> AutoSwitchOnDeath { get; }
    public ConfigEntry<bool> ShowBotList { get; }
    public ConfigEntry<float> LabelDistance { get; }
    public ConfigEntry<bool> LimitLabelDistance { get; }
    public float EffectiveLabelDistance => LimitLabelDistance.Value ? LabelDistance.Value : 0f;
    public ConfigEntry<bool> ShowOccludedBots { get; }
    public ConfigEntry<Vector2> BotListPosition { get; }
    public ConfigEntry<KeyboardShortcut> MenuKey { get; }
    public ConfigEntry<KeyboardShortcut> BotInfoKey { get; }
    public ConfigEntry<KeyboardShortcut> FollowKey { get; }
    public ConfigEntry<KeyboardShortcut> OrientationKey { get; }
    public ConfigEntry<float> OrbitSensitivity { get; }
    public ConfigEntry<KeyboardShortcut> ZoomKey { get; }
    public ConfigEntry<float> ZoomSpeed { get; }
    public ConfigEntry<bool> AutomaticScale { get; }
    public ConfigEntry<float> InterfaceScale { get; }
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
            Enabled = config.Bind("General", "Enabled", true, "Watch after death or extraction. Disabling ends solo spectating or returns control to Fika's spectator camera.");
            AfterDeath = config.Bind("General", "Spectate after death", true, "Continue watching after death. If Killcam is installed, its replay finishes first. Changes apply to the next death, not the current spectator session.");
            AfterExtraction = config.Bind("General", "Spectate after extraction", true, "Continue watching after a completed extraction, including run-throughs. Keeps the original result, exit and play time. Transit is unchanged. Changes apply to the next extraction.");
            // Migrate the renamed wheel setting. An existing new setting always
            // wins over the legacy value.
            var legacy = config.Bind("General", "Cycle PMCs with mouse wheel", true);
            CycleBots = config.Bind("General", "Cycle bots with mouse wheel", legacy.Value, "Scroll up/down to move only the camera to the next/previous living AI bot in the selected filter. Disable to restore Freecam wheel zoom. Menu buttons remain available.");
            config.Remove(legacy.Definition);
            Targets = config.Bind("General", "Camera targets", BotFilter.PmcOnly, "PmcOnly: living AI USEC/BEAR. AllBots: also Scavs, bosses, guards, raiders and other AI. Humans, dead and inactive characters are excluded. Does not change bot behavior.");
            ReverseWheel = config.Bind("General", "Reverse mouse wheel", false, "Reverse the previous/next direction of the mouse wheel. Does not change the menu buttons.");
            UseSpectatorFov = config.Bind("Camera", "Override spectator FOV", false, "Use a custom field of view while spectating. Turn off to return to the camera's original field of view.");
            SpectatorFov = config.Bind("Camera", "Spectator FOV", 60f, new ConfigDescription("Vertical field of view in degrees while the override is on.", new AcceptableValueRange<float>(30f, 110f)));
            UseFikaSelection = config.Bind("Camera", "Follow Fika selection", true, "Press the follow key to orbit the living player or bot selected in Fika's camera.");
            HideExtractedWeapon = config.Bind("Interface", "Hide extracted weapon", true, "Hide your held item model locally while watching after extraction.");
            ShowHumanLabels = config.Bind("Interface", "Show player labels", true, "Show Bot info labels for other living human players using your selected fields.");
            ShowDistance = config.Bind("Interface", "Show distance on labels", true, "Distance in metres from the free camera, or from the actor you are following.");
            (BotLabelFields, ConfigEntry<bool>) Label(BotLabelFields field, string name, string help) =>
                (field, config.Bind("Bot info fields", name, true, help));
            LabelOptions = new[]
            {
                Label(BotLabelFields.Name, "Name", "Show the character's name."),
                Label(BotLabelFields.Type, "Character type", "Show the type, such as PMC, Scav, Boss or Player."),
                Label(BotLabelFields.RoleDetail, "Faction or role detail", "Show the bot's faction or named role when available, such as BEAR or Killa."),
                Label(BotLabelFields.Personality, "SAIN personality", "Show the personality when SAIN provides it."),
                (BotLabelFields.Distance, ShowDistance), // Keep the existing Interface key and saved preference.
                Label(BotLabelFields.Health, "Health", "Show current and maximum health."),
                Label(BotLabelFields.CarriedValue, "Carried loot value", "Show the estimated value of the bot's carried equipment in roubles."),
                Label(BotLabelFields.Ghost, "ORBIT Ghost status", "Show Ghost while ORBIT marks the bot as dormant."),
                Label(BotLabelFields.Action, "Current action", "Show the active AI action. COMET squad tactics have their own switch."),
                Label(BotLabelFields.SquadTactic, "COMET squad tactic", "Show the active COMET squad tactic and role when reported in the action line.")
            };
            ActionsOnlyWhenFollowing = config.Bind("Interface", "Show actions only for followed bot", false, "Hide action lines for other bots. In free camera this hides all bot actions. Other label details stay visible.");
            VisitDistance = config.Bind("General", "Camera visit distance", 2.5f, new ConfigDescription("Distance in metres from a selected bot when visiting or following. Nearby walls shorten this distance.", new AcceptableValueRange<float>(1f, 10f)));
            ShowHelp = config.Bind("General", "Show spectator help", true, "Show the small HORIZON panel while moving the camera. The spectator menu remains accessible when hidden.");
            var oldOverlay = config.Bind("General", "Automatic BotDebug overlay", false);
            Overlay = config.Bind("Interface", "Bot overlay", SpectatorOverlay.BotInfo, "Choose HORIZON information labels (default), BotDebug (if installed), or Off. Only one overlay is displayed. Existing choices in this selector are preserved. BotDebug is turned off when leaving, without changing its saved configuration.");
            config.Remove(oldOverlay.Definition);
            AutoSwitchOnDeath = config.Bind("General", "Auto-switch when followed bot dies", false, "OFF: return to detached Freecam at the current camera position when the followed bot dies, extracts or disappears. ON: automatically follow the next living bot in the wheel filter.");
            ShowBotList = config.Bind("Interface", "Show bot list", false, "Show all living AI bots grouped by squad. Hold Left Alt or open the spectator menu to click a bot and move the camera. Independent of the wheel filter; no player body is moved.");
            LimitLabelDistance = config.Bind("Interface", "Limit bot label distance", false, "Limit labels to the configured distance. Off by default: all on-screen bots can have labels, including distant bots. Off-screen bots remain hidden.");
            LabelDistance = config.Bind("Interface", "Bot label distance", 150f, new ConfigDescription("Maximum distance in metres when Limit bot label distance is enabled.", new AcceptableValueRange<float>(20f, 5000f)));
            ShowOccludedBots = config.Bind("Interface", "Show bots through scenery", true, "Show Bot info through walls and scenery, like BotDebug. Disable to label only bots with an unobstructed head or torso. Bots outside the camera view remain hidden in both modes.");
            BotListPosition = config.Bind("Interface", "Bot list position", new Vector2(0f, 0f), "Saved relative position of the bot list. Hold Left Alt or open the menu, then drag its header.");
            MenuKey = config.Bind("General", "Spectator menu", new KeyboardShortcut(KeyCode.Escape), "Open HORIZON camera and overlay controls. The default is Escape in solo and F6 in Fika. In co-op, use Fika's extract shortcut to leave.");
            BotInfoKey = config.Bind("Interface", "Toggle Bot info", new KeyboardShortcut(KeyCode.F3), "Show or hide Bot info for this spectator session. The Bot overlay setting controls the next raid. None disables the shortcut.");
            FollowKey = config.Bind("General", "Follow selected bot", new KeyboardShortcut(KeyCode.F), "Attach the camera in mouse orbit around the selected bot. Scroll or use Next/Previous to switch targets. Press again to return to Freecam. Only active while spectating, outside menus.");
            OrientationKey = config.Bind("General", "Follow camera orientation", new KeyboardShortcut(KeyCode.R), "While following, toggle between the bot's look direction and free mouse rotation around the bot.");
            OrbitSensitivity = config.Bind("General", "Orbit mouse sensitivity", 3f, new ConfigDescription("Mouse sensitivity when rotating around the followed bot.", new AcceptableValueRange<float>(0.1f, 10f)));
            ZoomKey = config.Bind("General", "Camera zoom modifier", new KeyboardShortcut(KeyCode.LeftControl), "Hold this key and scroll to move the camera closer/further, without switching bots. Also works while following a bot. Menus keep the wheel for scrolling. Left Alt is reserved for dragging the panel. None disables this shortcut.");
            ZoomSpeed = config.Bind("General", "Camera zoom speed", 5f, new ConfigDescription("Camera movement per mouse-wheel input. Follow distance stays between 1 and 10 metres; Freecam moves along its viewing direction. Does not change the field of view.", new AcceptableValueRange<float>(0.5f, 20f)));
            AutomaticScale = config.Bind("Interface", "Automatic interface scale", true, "Scale the HORIZON help panel and menu for your screen height: 100% at 1080p, 133% at 1440p and 200% at 4K. Interface scale applies on top. Disable for a fixed size.");
            InterfaceScale = config.Bind("Interface", "Interface scale", 1f, new ConfigDescription("Size of HORIZON text, panels and buttons. 1 = 100%, 1.5 = 150%, 2 = 200%. Applied on top of automatic resolution scaling. Changes immediately; does not resize F12 or other mods.", new AcceptableValueRange<float>(0.75f, 2.5f)));
            HelpPosition = config.Bind("Interface", "Help panel position", new Vector2(1f, 0f), "Saved relative position. Hold Left Alt and drag the HORIZON header to move the small panel.");
            MenuPosition = config.Bind("Interface", "Menu position", new Vector2(1f, 0f), "Saved relative position. Drag the HORIZON header to move the open menu.");
        }
        finally { config.SaveOnConfigSet = save; }
        if (save) config.Save();
    }
}
