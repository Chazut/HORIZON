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
        check(PmcNavigator.IsLivingBot(replica) && !PmcNavigator.IsLivingBot(new FikaPlayer()), "Remote AI is selectable and remote humans are excluded");
        var roster = new BotRoster();
        var world = new GameWorld();
        world.RegisterPlayer(replica);
        roster.Refresh(world, 1);
        check(roster.Bots.Single().GroupLabel == "Unknown squad", "Missing host squad data stays unknown");
        replica.Profile.Info.GroupId = "alpha";
        roster.Refresh(world, 2);
        check(roster.Bots.Single().GroupLabel == "Squad alpha", "A replicated profile group can identify the bot squad");
        PmcNavigator.IsObservedBot = null;
        var camera = new Camera();
        var bridge = new FreecamBridge();
        bridge.AttachFika(native, camera, rotation => fika.Align(native, rotation));
        Cursor.lockState = CursorLockMode.Confined; Cursor.visible = false;
        bridge.SetMenu(true);
        check(native.IsActive && Cursor.visible, "Menu leaves the Fika camera active");
        var pose = new Quaternion { eulerAngles = new Vector3(350, 123, 0) };
        bridge.MoveTo(new Vector3(7, 8, 9), pose);
        check(native.Detached && native.Yaw == 123 && native.Pitch == -10, "Bot visit detaches from native follow and aligns mouse look");
        bridge.Release();
        check(native.enabled && native.IsActive && !bridge.Ready && !Cursor.visible && Cursor.lockState == CursorLockMode.Confined,
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
        private bool _isFollowing = true, _disableInput;
        public void DisableInput(bool disabled) => _disableInput = disabled;
        public bool InputDisabled => _disableInput;
        private float _yaw = 0, _pitch = 0;
        public bool Detached => !_isFollowing;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public void DetachCamera() => _isFollowing = false;
    }
}
