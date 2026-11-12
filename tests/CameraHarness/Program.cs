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
// Extraction removes an alive body without killing it, while the child camera stays active.
freecam.gameObject.SetActive(true);
var world = new GameWorld();
var botGame = new BotGame();
Comfort.Common.Singleton<IBotGame>.Instance = botGame;
var local = game.LocalPlayer;
local.IsYourPlayer = true;
var collider = new Collider();
var renderer = new Renderer();
local.gameObject.Components[typeof(Collider)] = collider;
local.gameObject.Components[typeof(Renderer)] = renderer;
cameraOwner.Camera.transform.SetParent(local.Transform.Original, true);
// Model the dependency script living on that same camera object.
freecam.gameObject = cameraOwner.Camera.gameObject;
world.RegisterPlayer(local);
botGame.BotsController.BotSpawner.AddPlayer(local);
var extraction = new ExtractedPlayer();
extraction.Remove(world, local);
Check(local.HealthController.IsAlive && !local.gameObject.activeInHierarchy, "Extraction hides/inactivates body without a fake death");
Check(!world.AllAlivePlayersList.Contains(local) && !world.RegisteredPlayers.Contains(local) &&
    !botGame.BotsController.BotSpawner.Players.Contains(local), "Extracted local player leaves world and AI target registries");
Check(cameraOwner.Camera.gameObject.activeInHierarchy && cameraOwner.Camera.transform.parent == null, "Child camera remains independent of disabled body");
Check(!collider.enabled && !renderer.enabled && FreecamBridge.CanAttach(game), "No body collision/rendering and camera remains attachable");
extraction.Release(rollback: true);
Check(local.gameObject.activeSelf && collider.enabled && renderer.enabled && world.AllAlivePlayersList.Contains(local) &&
    botGame.BotsController.BotSpawner.Players.Contains(local), "Entry failure restores body and registries");
Check(cameraOwner.Camera.transform.parent == local.Transform.Original, "Entry rollback restores camera hierarchy");
extraction.Remove(world, local);
extraction.Release(rollback: false);
Check(!local.gameObject.activeSelf && !world.AllAlivePlayersList.Contains(local), "Normal shutdown never resurrects an extracted target");
Check(cameraOwner.Camera.transform.parent == local.Transform.Original, "Shutdown does not leave detached camera behind");
extraction.Release(false);
local.gameObject.SetActive(true);
Check(bridge.Attach(game), "Camera attaches for cycle tests");
var localPosition = local.Position;
Player Bot(string id, WildSpawnType role, bool alive = true, bool ai = true)
{
    var bot = new Player { ProfileId = id, IsAI = ai };
    bot.Profile.Info.Settings.Role = role;
    bot.Profile.Nickname = id;
    bot.HealthController.IsAlive = alive;
    bot.gameObject.transform.position = new Vector3(id.Length * 10, 0, 0);
    world.AllAlivePlayersList.Add(bot);
    return bot;
}
var pmcA = Bot("a", WildSpawnType.pmcUSEC);
var pmcB = Bot("bb", WildSpawnType.pmcBEAR);
Bot("scav", WildSpawnType.assault);
Bot("boss", WildSpawnType.boss);
Bot("guard", WildSpawnType.follower);
Bot("raider", WildSpawnType.pmcBot);
Bot("dead", WildSpawnType.pmcUSEC, alive: false);
Bot("human", WildSpawnType.pmcUSEC, ai: false);
Bot("inactive", WildSpawnType.pmcUSEC).gameObject.SetActive(false);
var navigator = new PmcNavigator();
navigator.Move(world, bridge, 1);
Check(navigator.Caption == "a (1/2)", "Only living active AI PMCs are candidates, not raiders/humans/scavs/bosses");
Check(freecam.transform.position.x == pmcA.Position.x && local.Position.Equals(localPosition), "Cycle teleports camera, never player body");
navigator.Move(world, bridge, 1);
Check(navigator.Caption == "bb (2/2)" && freecam.transform.position.x == pmcB.Position.x, "Wheel advances to second PMC");
navigator.Move(world, bridge, -1);
Check(navigator.Caption == "a (1/2)", "Reverse wheel goes back");
pmcA.HealthController.IsAlive = false;
navigator.Move(world, bridge, 1);
Check(navigator.Caption == "bb (1/1)", "A PMC killed since last scroll is excluded");
Physics.Hit = new RaycastHit { transform = new GameObject().transform, point = new Vector3(20, 1.5f, -1) };
navigator.Move(world, bridge, 1);
Check(freecam.transform.position.z > -1, "Camera stops on the PMC side of an intervening wall");
Physics.Hit = null;
pmcB.HealthController.IsAlive = false;
var previousPosition = freecam.transform.position;
navigator.Move(world, bridge, 1);
Check(navigator.Caption == "No living PMCs to visit" && freecam.transform.position.Equals(previousPosition), "No PMCs leaves camera in place with useful hint");
navigator.Reset();
Check(navigator.Caption == "Wheel: previous / next PMC", "New raid does not retain old PMC name");
bridge.Release();
// Optional overlay: activate via its own callback/API, switch off on leave, never persist.
var overlay = new BotDebugBridge();
Check(!overlay.Start(), "Missing BotDebug is nonfatal");
var toggle = DrakiaXYZ.BotDebug.Helpers.Settings.Enable;
int disableEvents = 0;
toggle.Changed = on => { if (!on) disableEvents++; };
overlay.Bind(typeof(DrakiaXYZ.BotDebug.Helpers.Settings).Assembly);
Check(overlay.Start() && toggle.Value && DrakiaXYZ.BotDebug.Components.BotDebugComponent.Starts == 1, "Start enables actual overlay even if config callback was not enough");
Check(toggle.ConfigFile.Saves == 0 && toggle.ConfigFile.SaveOnConfigSet, "Auto activation never rewrites dependency config");
overlay.Stop();
Check(!toggle.Value && disableEvents == 1 && toggle.ConfigFile.Saves == 0, "Leaving disables overlay without saving temporary state");
overlay.Stop();
Check(disableEvents == 1, "Repeated cleanup is harmless");
toggle.ConfigFile.SaveOnConfigSet = false;
overlay.Start();
overlay.Stop();
Check(!toggle.Value && !toggle.ConfigFile.SaveOnConfigSet, "Existing save policy restored");
toggle.Changed = _ => throw new InvalidOperationException("Destroyed scene");
try { overlay.Start(); } catch (InvalidOperationException) { }
Check(!toggle.ConfigFile.SaveOnConfigSet, "Callback failure cannot enable automatic file writes");
toggle.Changed = null;
overlay.Stop();
Check(!toggle.Value, "Failed activation can still clean up overlay");

Console.WriteLine($"PASS: {count} production camera-bridge checks with Unity doubles (not a rendered raid).");

public static class FakeKillcamPlugin { public static FakeKillcamController? ReplayCtrl; }
public class FakeKillcamController
{
    public static object? PendingReplayKiller;
    private bool _playing;
    public bool ThrowOnRead;
    public bool IsPlaying { get => ThrowOnRead ? throw new InvalidOperationException("Replay destroyed") : _playing; set => _playing = value; }
}
