using EFT;
using Fika.Core.Main.FreeCamera;
using Fika.Core.Main.Players;
using Fika.Core.Main.Utils;
using Horizon;
using UnityEngine;

internal static class FikaCameraTests
{
    private sealed class Config
    {
        public bool AllowSpectateFreeCam { get; set; } = true;
        public bool AllowSpectateBots { get; set; } = true;
    }
    private sealed class Provider { public Config Settings { get; } = new(); }

    internal static void Run(Action<bool, string> check)
    {
        var provider = new Provider();
        var fika = new FikaSpectator(provider);
        var native = new FreeCamera();
        var local = new FikaPlayer { IsYourPlayer = true };
        bool Watch(bool death = true, bool extract = true) => fika.CanWatch(native, local, death, extract);
        check(!Watch(), "No HORIZON on an inactive co-op spectator camera");
        native.IsActive = true;
        check(!Watch(), "Live-player debug freecam does not start HORIZON");
        local.HealthController.IsAlive = false;
        check(Watch() && !Watch(death: false), "Local death respects the HORIZON death setting");
        native.DisableInput(true);
        check(!Watch(), "Native input toggle hands the controls back to Fika");
        native.DisableInput(false);
        local.IsYourPlayer = false;
        check(!Watch(), "Remote deaths do not activate HORIZON");
        local.IsYourPlayer = true;
        local.HealthController.IsAlive = true;
        check(!Watch(), "Revive returns control to Fika");
        native.Extracted = true;
        check(Watch() && !Watch(extract: false), "Native extraction respects the extraction setting");
        provider.Settings.AllowSpectateFreeCam = false;
        check(!Watch(), "Server follow-only mode takes precedence");
        provider.Settings.AllowSpectateBots = false;
        check(!Watch(), "With both permissions off Fika keeps its camera");
        provider.Settings.AllowSpectateFreeCam = true;
        check(Watch() && !fika.AllowSpectateBots(), "An extracted player can use HORIZON's camera menu with bot tools off");
        native.Extracted = false;
        check(!Watch(), "Freecam permission alone does not activate HORIZON for a living player");
        local.HealthController.IsAlive = false;
        check(Watch() && !fika.AllowSpectateBots(), "Freecam-only access also works after death");
        var freeOnlyCamera = new FreecamBridge();
        freeOnlyCamera.AttachFika(native, new Camera(), rotation => fika.Align(native, rotation));
        freeOnlyCamera.SetMenu(true);
        check(native.IsActive && Cursor.visible && !native.Detached, "Freecam-only menu opens without selecting a target");
        freeOnlyCamera.Release();
        provider.Settings.AllowSpectateBots = true;
        check(Watch() && fika.AllowSpectateBots(), "Enabling bot spectating adds bot tools to the same camera");
        FikaBackendUtils.IsHeadless = true;
        check(FikaSpectator.IsHeadless(typeof(Provider).Assembly) && !Watch(), "Headless process stays idle");
        FikaBackendUtils.IsHeadless = false;
        FikaBackendUtils.IsTransit = true;
        check(!Watch(), "Transit preserves Fika's camera");
        FikaBackendUtils.IsTransit = false;
        FikaBackendUtils.IsServer = true;
        check(fika.IsHost() && Watch(), "Graphical host can spectate");
        FikaBackendUtils.IsServer = false;
        check(!fika.IsHost() && Watch(), "Graphical client can spectate");
        PmcNavigator.IsObservedBot = fika.IsObservedBot;
        var replica = new FikaPlayer { IsObservedAI = true };
        replica.Profile.Nickname = "<color=red>Решала</color>";
        check(PmcNavigator.IsLivingBot(replica) && !PmcNavigator.IsLivingBot(new FikaPlayer()), "Remote AI is selectable and remote humans are excluded");
        var roster = new BotRoster();
        var world = new GameWorld();
        world.RegisterPlayer(replica);
        roster.Refresh(world, 1);
        check(roster.Bots.Single().Name == "Reshala", "The joining player's roster uses the English boss name");
        check(roster.Bots.Single().GroupLabel == "Unknown squad", "Missing host squad data stays unknown");
        replica.Profile.Info.GroupId = "alpha";
        replica.Profile.Nickname = "Саня Заводской";
        roster.Refresh(world, 2);
        check(roster.Bots.Single().Name == "Sanja Zavodskoy", "A replicated nickname update uses Raid Review's guard spelling");
        check(roster.Bots.Single().GroupLabel == "Squad alpha", "A replicated profile group can identify the bot squad");
        roster.ReadHostSquad = _ => (true, 0);
        roster.Refresh(world, 3);
        check(roster.Bots.Single().GroupLabel == "Squad 0", "Roster uses the host's squad without rendering a label");
        roster.ReadHostSquad = _ => (true, null);
        roster.Refresh(world, 4);
        check(roster.Bots.Single().GroupLabel == "Solo bots", "Host-confirmed solo overrides the replicated profile group");
        var camera = new Camera();
        var bridge = new FreecamBridge();
        bridge.AttachFika(native, camera, rotation => fika.Align(native, rotation));
        Cursor.lockState = CursorLockMode.Confined; Cursor.visible = false;
        bridge.SetMenu(true);
        check(native.IsActive && Cursor.visible, "Menu leaves the Fika camera active");
        var pose = new Quaternion { eulerAngles = new Vector3(350, 123, 0) };
        bridge.MoveTo(new Vector3(7, 8, 9), pose);
        check(native.Detached && native.Yaw == 123 && native.Pitch == -10, "Bot visit detaches from native follow and aligns mouse look");
        var navigator = new PmcNavigator();
        navigator.SetFilter(BotFilter.AllBots);
        check(navigator.Visit(world, bridge, replica, 3f) && navigator.Caption == "Sanja Zavodskoy" &&
            navigator.SelectedId == replica.ProfileId, "Clicking the English name still selects the same replicated bot");
        navigator.Move(world, bridge, 1);
        check(navigator.Caption == "Sanja Zavodskoy (1/1)" && replica.Profile.Nickname == "Саня Заводской",
            "Cycling uses the same display name and preserves the network profile");
        var teammate = new FikaPlayer { ProfileId = "human-A" };
        teammate.Profile.Nickname = "Teammate";
        world.RegisterPlayer(teammate);
        native.Select(teammate, true);
        check(fika.Selection(native, true) == teammate, "Native follow identifies the human distance origin");
        navigator.ToggleFollow(world, bridge, 3, fika.Selection(native));
        check(navigator.FollowedTarget == teammate && navigator.Caption == "Teammate" && native.Detached,
            "F takes over the native human selection even with another HORIZON bot selected");
        navigator.ToggleFollow(world, bridge, 3);
        native.Select(replica, false);
        check(fika.Selection(native, true) == null, "Jumping without native follow leaves the camera as the distance origin");
        navigator.ToggleFollow(world, bridge, 3, fika.Selection(native));
        check(navigator.FollowedTarget == replica, "F also adopts a highlighted Fika bot after a jump");
        navigator.ToggleFollow(world, bridge, 3);
        native.Select(teammate, false);
        navigator.Visit(world, bridge, replica, 3);
        check(fika.Selection(native) == null, "An explicit HORIZON visit supersedes a stale native selection");
        roster.Refresh(world, 5, includeHumans: true);
        check(roster.Labels.Count == 2 && roster.Bots.Single().Player == replica, "Human labels leave the bot list and Fika polling bot-only");
        int samples = 0;
        var labels = new BotLabels((_, _) => { samples++; return new BotLabelSample(("Rat", "Taking cover", false)); }, _ => {});
        teammate.transform.position = new Vector3(10, 0, 0);
        replica.transform.position = new Vector3(30, 0, 0);
        camera.transform.position = new Vector3(4, 0, 0);
        void Refresh(float t, Player? followed)
        {
            for (int i = 0; i < 3; i++) labels.Update(roster, world, camera, t, 0, true, true, followed);
        }
        Refresh(5, null);
        var human = roster.Labels.Single(p => p.Human);
        var ai = roster.Bots.Single();
        check(human.Label == "Teammate | Player | 6 m\nHealth: 350/440" && samples == 1, "Freecam human label uses replicated health without asking the host for AI data");
        Refresh(6, teammate);
        check(ai.Label.Contains("20 m") && human.Label.Contains("0 m"), "Both labels measure from the followed actor");
        check(BotLabels.Caption(ai, false, null).Contains("Taking cover") && !BotLabels.Caption(ai, true, null).Contains("Taking cover") &&
            BotLabels.Caption(ai, true, replica).Contains("Taking cover") && BotLabels.Caption(ai, true, teammate).Contains("Health:") &&
            !BotLabels.Caption(ai, true, teammate).Contains("Taking cover"), "Follow-only changes just the action line, immediately");
        for (int i = 0; i < 3; i++) labels.Update(roster, world, camera, 7, 0, true, false, teammate);
        check(!human.Label.Contains(" m") && !ai.Label.Contains(" m") && ai.Label.Contains("Taking cover"), "Distance can be disabled independently");
        teammate.HealthController.IsAlive = false;
        roster.Refresh(world, 7, includeHumans: true);
        check(roster.Labels.All(p => !p.Human), "Dead or removed humans no longer appear in the overlay");
        teammate.HealthController.IsAlive = true;
        roster.Refresh(world, 8, includeHumans: true, includeBots: false);
        check(roster.Labels.Single().Human && roster.Bots.Count == 0, "Freecam-only Fika permissions expose human labels without bot data");
        roster.Refresh(world, 8, includeHumans: false, includeBots: false);
        check(roster.Labels.Count == 0, "Disabling player labels removes humans on the same update");
        bridge.SetSpectatorFov(true, 95);
        check(camera.fieldOfView == 95, "Fika freecam FOV override");
        PmcNavigator.IsObservedBot = null;
        bridge.Release();
        check(camera.fieldOfView == 60 && native.enabled && native.IsActive && !bridge.Ready && !Cursor.visible && Cursor.lockState == CursorLockMode.Confined,
            "Detach releases references and restores the cursor without turning off Fika");
    }
}

namespace Fika.Core.Main.Utils
{
    public static class FikaBackendUtils
    {
        public static bool IsHeadless { get; set; }
        public static bool IsTransit { get; set; }
        public static bool IsServer { get; set; }
    }
}
namespace Fika.Core.Main.Players { public class FikaPlayer : Player { public bool IsObservedAI; } }
namespace Fika.Core.Main.FreeCamera
{
    public class FreeCamera : MonoBehaviour
    {
        public bool IsActive { get; set; }
        public bool Extracted { get; set; }
        private FikaPlayer? _currentPlayer;
        public void Select(FikaPlayer player, bool follow) { _currentPlayer = player; _isFollowing = follow; }
        private bool _isFollowing = true, _disableInput;
        public void DisableInput(bool disabled) => _disableInput = disabled;
        public bool InputDisabled => _disableInput;
        private float _yaw = 0, _pitch = 0;
        public bool Detached => !_isFollowing;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public void DetachCamera() { _isFollowing = false; _currentPlayer = null; }
    }
}
