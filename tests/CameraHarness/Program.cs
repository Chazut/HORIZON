using EFT;
using EFT.CameraControl;
using RaidSpectator;
using Terkoiz.Freecam;
using UnityEngine;

int count = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); count++; }
var bridge = new FreecamBridge();
var game = new LocalGame();
Check(!FreecamBridge.CanAttach(game), "Readiness refuses missing Freecam");
Check(!bridge.Attach(game) && !bridge.Ready, "Missing Freecam leaves native death flow available");
Check(game.PlayerOwner.enabled && game.GameUi.gameObject.activeSelf, "Entry refusal does not change UI or owner");
FreecamPlugin.FreecamControllerInstance = new FreecamController();
Check(!bridge.Attach(game), "No camera is not a spectator session");
var cameraOwner = new PlayerCameraController();
game.LocalPlayer.gameObject.Components[typeof(PlayerCameraController)] = cameraOwner;
Check(!bridge.Attach(game), "A camera without initialized Freecam is not sufficient");
var freecam = new Freecam();
var deathFade = new DeathFade();
var blur = new FastBlur();
cameraOwner.Camera.gameObject.Components[typeof(Freecam)] = freecam;
cameraOwner.Camera.gameObject.Components[typeof(DeathFade)] = deathFade;
cameraOwner.Camera.gameObject.Components[typeof(FastBlur)] = blur;
Cursor.lockState = CursorLockMode.Confined;
Cursor.visible = true;
Check(FreecamBridge.CanAttach(game), "Camera can be checked before death is deferred");
Check(game.PlayerOwner.enabled && game.GameUi.gameObject.activeSelf && !freecam.IsActive &&
    game.LocalPlayer.PointOfView == EPointOfView.FirstPerson && Cursor.visible && Cursor.lockState == CursorLockMode.Confined &&
    deathFade.Clears == 0 && blur.Clears == 0, "Readiness check leaves Killcam camera, HUD, death effects and input untouched");
var killcam = new KillcamBridge();
Check(!killcam.IsBusy(), "No Killcam allows Freecam after death dispatch");
killcam.Bind(typeof(FakeKillcamPlugin));
Check(!killcam.IsBusy(), "Idle optional Killcam does not delay Freecam");
FakeKillcamController.PendingReplayKiller = new object();
Check(killcam.IsBusy(), "Pending replay blocks takeover before its controller starts playing");
FakeKillcamPlugin.ReplayCtrl = new FakeKillcamController();
Check(killcam.IsBusy(), "Killcam preparation delay is respected even though IsPlaying is false");
FakeKillcamPlugin.ReplayCtrl.IsPlaying = true;
Check(killcam.IsBusy(), "Active replay retains camera ownership");
FakeKillcamController.PendingReplayKiller = null;
Check(killcam.IsBusy(), "Playing replay still owns camera even if pending marker was cleared");
FakeKillcamPlugin.ReplayCtrl.IsPlaying = false;
FakeKillcamController.PendingReplayKiller = new object();
Check(killcam.IsBusy(), "Wait for replay coroutine cleanup after playback ends");
FakeKillcamController.PendingReplayKiller = null;
Check(!killcam.IsBusy(), "Finished replay and cleanup permit Freecam");
Check(game.PlayerOwner.enabled && !freecam.IsActive && deathFade.Clears == 0, "Observing the whole replay never alters the camera");
try { new KillcamBridge().Bind(typeof(object)); throw new Exception("Changed Killcam API must not silently take the camera"); }
catch (MissingFieldException) { count++; }
FakeKillcamPlugin.ReplayCtrl.ThrowOnRead = true;
try { killcam.IsBusy(); throw new Exception("Reader failure must not be interpreted as replay complete"); }
catch (System.Reflection.TargetInvocationException) { count++; }
FakeKillcamPlugin.ReplayCtrl.ThrowOnRead = false;
Check(bridge.Attach(game) && bridge.Ready, "Ready Freecam adopted");
Check(game.LocalPlayer.PointOfView == EPointOfView.FreeCamera, "Native camera switches to locked Freecam state");
Check(!game.PlayerOwner.enabled && !game.GameUi.gameObject.activeSelf, "No dead-player controls or death HUD");
Check(freecam.IsActive && freecam.enabled, "Dependency controls the camera");
Check(deathFade.Clears == 1 && blur.Clears == 1, "Death visual effects cleared");
bridge.ClearDeathEffects();
Check(deathFade.Clears == 2 && blur.Clears == 2, "Later death subscribers cannot leave the initial black/blur screen");
bridge.SetMenu(true);
Check(!freecam.IsActive && Cursor.visible && Cursor.lockState == CursorLockMode.None, "Menu pauses motion and releases cursor");
Check(bridge.Ready, "Paused controls still own a valid camera");
bridge.SetMenu(false);
Check(freecam.IsActive && !Cursor.visible && Cursor.lockState == CursorLockMode.Locked, "Resume captures camera input");
bridge.Release();
Check(!bridge.Ready && bridge.Controller == null && bridge.Script == null, "Release drops scene references");
Check(game.PlayerOwner.enabled && game.GameUi.gameObject.activeSelf, "Release restores original UI/owner state");
Check(game.LocalPlayer.PointOfView == EPointOfView.FirstPerson && !freecam.IsActive, "Release restores normal pre-entry state");
Check(Cursor.visible && Cursor.lockState == CursorLockMode.Confined, "Restore previous cursor policy");
bridge.Release();
Check(Cursor.visible && Cursor.lockState == CursorLockMode.Confined, "Repeated cleanup is harmless");
// Entering with Freecam/HUD already changed must preserve those values on rollback,
// but ending the raid must never return control to Freecam's teleport shortcut.
freecam.IsActive = true;
freecam.enabled = false;
game.PlayerOwner.enabled = false;
game.GameUi.gameObject.SetActive(false);
game.LocalPlayer.PointOfView = EPointOfView.FreeCamera;
Check(bridge.Attach(game), "Already-freecam player supported");
bridge.Release();
Check(freecam.IsActive && !freecam.enabled && !game.PlayerOwner.enabled && !game.GameUi.gameObject.activeSelf, "Rollback preserves pre-existing settings");
Check(bridge.Attach(game), "Second raid can attach again");
bridge.Release(ending: true);
Check(!freecam.IsActive, "Actual shutdown disables Freecam teleport/movement");
Check(bridge.Attach(game), "Camera-loss setup");
freecam.gameObject.SetActive(false);
Check(!FreecamBridge.CanAttach(game) && !new FreecamBridge().Attach(game), "Inactive camera cannot enter spectator mode");
Check(!bridge.Ready, "Lost camera triggers normal Killed exit instead of trapping the player");
bridge.Release(ending: true);
Console.WriteLine($"PASS: {count} production camera-bridge checks with Unity doubles (not a rendered raid).");

public static class FakeKillcamPlugin { public static FakeKillcamController? ReplayCtrl; }
public class FakeKillcamController
{
    public static object? PendingReplayKiller;
    private bool _playing;
    public bool ThrowOnRead;
    public bool IsPlaying { get => ThrowOnRead ? throw new InvalidOperationException("Replay destroyed") : _playing; set => _playing = value; }
}
