using EFT;
using EFT.CameraControl;
using Horizon;
using Terkoiz.Freecam;
using UnityEngine;

int count = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); count++; }
FreecamBridge.BindSolo(typeof(FreecamPlugin));
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
cameraOwner.Camera.fieldOfView = 71;
bridge.SetSpectatorFov(false, 60);
Check(cameraOwner.Camera.fieldOfView == 71, "Native FOV stays untouched by default");
bridge.SetSpectatorFov(true, 90);
bridge.SetSpectatorFov(true, 75);
Check(cameraOwner.Camera.fieldOfView == 75, "Live FOV slider updates");
bridge.SetSpectatorFov(false, 75);
Check(cameraOwner.Camera.fieldOfView == 71, "Disabling the override restores the previous FOV");
var zoomOrigin = freecam.transform.position;
var playerBeforeZoom = game.LocalPlayer.Position;
bridge.Zoom(.1f, 5f);
Check(freecam.transform.position.z == zoomOrigin.z + .5f && game.LocalPlayer.Position.Equals(playerBeforeZoom), "Zoom moves only the camera forward along its view");
bridge.Zoom(-.1f, 5f);
Check(freecam.transform.position.Equals(zoomOrigin), "Opposite wheel zoom restores camera position");
Check(deathFade.Clears == 1 && blur.Clears == 1, "Death visual effects cleared");
Check(!deathFade.enabled && !blur.enabled, "Player effects disabled for spectator camera");
var lighting = new WorldLighting();
var pain = new PainScreen();
var flash = new GrenadeFlashScreenEffect { EffectStrength = 1f };
Shader.Blindness = 1f;
cameraOwner.Camera.gameObject.Components[typeof(GrenadeFlashScreenEffect)] = flash;
cameraOwner.Camera.gameObject.Components[typeof(WorldLighting)] = lighting;
cameraOwner.Camera.gameObject.Components[typeof(PainScreen)] = pain;
Time.unscaledTime += 2;
deathFade.enabled = true;
bridge.MaintainEffects();
Check(!deathFade.enabled && !pain.enabled && lighting.enabled, "Late player effects suppressed without disabling world effects");
Check(!flash.enabled && flash.EffectStrength == 0f && Shader.Blindness == 0f, "Spectator clears residual grenade blindness shader");
bridge.ClearDeathEffects();
Check(deathFade.Clears == 2 && blur.Clears == 2, "Later death subscribers cannot leave the initial black/blur screen");
bridge.SetMenu(true);
Check(!freecam.IsActive && Cursor.visible && Cursor.lockState == CursorLockMode.None, "Menu pauses motion and releases cursor");
Check(bridge.Ready, "Paused controls still own a valid camera");
bridge.SetMenu(false);
Check(freecam.IsActive && !Cursor.visible && Cursor.lockState == CursorLockMode.Locked, "Resume captures camera input");
bridge.Release();
Check(deathFade.enabled && blur.enabled && pain.enabled && lighting.enabled, "Camera effect state restored on exit");
Check(!bridge.Ready && bridge.Controller == null && bridge.Script == null, "Release drops scene references");
bridge.Zoom(1, 5);
Check(freecam.transform.position.Equals(zoomOrigin), "No zoom after spectator camera is released");
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
var localWeapon = new ExtractedWeaponView();
local.HandsController = new Player.AbstractHandsController();
var gunRenderer = new Renderer();
var alreadyHidden = new Renderer { forceRenderingOff = true };
local.HandsController.ControllerGameObject.Components[typeof(Renderer)] = gunRenderer;
local.HandsController.ControllerGameObject.Components[typeof(object)] = alreadyHidden;
localWeapon.Update(local);
Check(gunRenderer.forceRenderingOff && gunRenderer.enabled && local.gameObject.activeSelf, "Only local extracted weapon rendering is hidden");
var oldModel = local.HandsController;
local.HandsController = null;
localWeapon.Update(local);
Check(!gunRenderer.forceRenderingOff && alreadyHidden.forceRenderingOff, "A released or pooled weapon regains its original rendering flags");
local.HandsController = oldModel;
localWeapon.Update(local);
localWeapon.Update(null);
Check(!gunRenderer.forceRenderingOff && alreadyHidden.forceRenderingOff, "Turning off weapon hiding or closing the session restores its state");
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
// HORIZON: filters can be changed mid-session without retaining a stale target.
navigator.SetFilter(BotFilter.AllBots);
Check(navigator.Caption == "Wheel: previous / next bot", "Changing to all bots clears old target caption");
var allTargets = new List<string>();
for (int i = 0; i < 4; i++)
{
    navigator.Move(world, bridge, 1, 5f);
    allTargets.Add(navigator.Caption.Split(' ')[0]);
}
Check(allTargets.SequenceEqual(new[] { "boss", "guard", "raider", "scav" }), "All bots visits bosses, guards, raiders and Scavs, excluding humans, corpses and inactive objects");
Check(freecam.transform.position.z == -5f && local.Position.Equals(localPosition), "Configured visit distance moves only the camera");
navigator.Move(world, bridge, 1);
Check(navigator.Caption == "boss (1/4)", "All-bot navigation wraps");
navigator.SetFilter(BotFilter.PmcOnly);
navigator.Move(world, bridge, 1);
Check(navigator.Caption == "No living PMCs to visit", "Switching back never visits the previous non-PMC target");
pmcB.HealthController.IsAlive = true;
navigator.Move(world, bridge, -1);
Check(navigator.Caption == "bb (1/1)", "PMC cycling resumes when an eligible bot is available");
navigator.ToggleFollow(world, bridge, 3);
bridge.SetMenu(false, navigator.Following);
Check(navigator.Following && !freecam.IsActive && !Cursor.visible, "Follow mode prevents competing Freecam movement");
Check(!navigator.LockOrientation, "Attaching a selected bot starts in mouse orbit");
pmcB.LookDirection = new Vector3(1, 0, 0);
pmcB.transform.position = new Vector3(50, 2, 60);
navigator.Follow(world, bridge, 3);
Check(Math.Abs(freecam.transform.position.x - 50) < .01f && freecam.transform.position.z < 58,
    "Default orbit follows translation without rotating when the bot turns");
navigator.ToggleOrientation();
navigator.Follow(world, bridge, 3);
Check(freecam.transform.position.x == 47 && freecam.transform.position.z == 60, "Locked camera follows bot position and look direction");
navigator.ToggleOrientation();
navigator.Rotate(30, 0, 3);
navigator.Follow(world, bridge, 3);
Check(Math.Abs(freecam.transform.position.x - 50) < .01f && freecam.transform.position.z > 62, "Mouse orbit rotates around bot independently");
pmcB.LookDirection = new Vector3(0, 0, 1);
previousPosition = freecam.transform.position;
navigator.Follow(world, bridge, 3);
Check(freecam.transform.position.Equals(previousPosition), "Bot turning does not reset free orbit");
navigator.ToggleOrientation();
navigator.Follow(world, bridge, 3);
Check(freecam.transform.position.z == 57, "Relock returns to bot view");
var targetBeforeZoom = navigator.Caption;
navigator.Zoom(.1f, 5, 3);
navigator.Follow(world, bridge, 3);
Check(freecam.transform.position.z == 57.5f && navigator.Caption == targetBeforeZoom, "Following zoom changes distance without switching target");
navigator.Zoom(100, 5, 3);
navigator.Follow(world, bridge, 3);
Check(freecam.transform.position.z == 59, "Following zoom stops at one metre");
navigator.Zoom(-100, 5, 3);
navigator.Follow(world, bridge, 3);
Check(freecam.transform.position.z == 50, "Following zoom stops at ten metres");
navigator.Follow(world, bridge, 4);
Check(freecam.transform.position.z == 56, "Changing the F12 distance takes effect after wheel zoom");
navigator.Zoom(.1f, 5, 4);
Physics.Hit = new RaycastHit { transform = new GameObject().transform, point = new Vector3(50, 3.5f, 59) };
navigator.Follow(world, bridge, 4);
Check(freecam.transform.position.z > 59, "Zoomed follow camera still respects wall collision");
Physics.Hit = null;
navigator.ToggleFollow(world, bridge, 3);
navigator.ToggleFollow(world, bridge, 3);
Check(navigator.Following && !navigator.LockOrientation, "Reattaching after a locked follow restores mouse orbit in the same raid");
navigator.Reset();
navigator.Move(world, bridge, 1, 3);
navigator.ToggleFollow(world, bridge, 3);
navigator.Follow(world, bridge, 3);
Check(!navigator.LockOrientation && Math.Abs(freecam.transform.position.z - (60 - 3 * Math.Cos(12 * Math.PI / 180))) < .001f,
    "A new spectator session starts in orbit at the configured distance");
world.AllAlivePlayersList.Remove(pmcB);
pmcA.HealthController.IsAlive = true;
previousPosition = freecam.transform.position;
Check(navigator.Follow(world, bridge, 3) == FollowTransition.Detached && !navigator.Following && navigator.SelectedId == null, "Extracted bot detaches by default even with another living PMC");
bridge.SetMenu(false, navigator.Following);
Check(freecam.IsActive && freecam.transform.position.Equals(previousPosition), "Detached Freecam resumes without teleporting");
world.AllAlivePlayersList.Add(pmcB);
navigator.Visit(world, bridge, pmcB, 3);
navigator.ToggleFollow(world, bridge, 3);
pmcB.HealthController.IsAlive = false;
previousPosition = freecam.transform.position;
Check(navigator.Follow(world, bridge, 3) == FollowTransition.Detached && freecam.transform.position.Equals(previousPosition), "Death preserves camera position and releases follow by default");
navigator.Visit(world, bridge, pmcA, 3); navigator.ToggleFollow(world, bridge, 3);
navigator.ToggleOrientation();
pmcB.HealthController.IsAlive = true; pmcA.HealthController.IsAlive = false;
Check(navigator.Follow(world, bridge, 3, true) == FollowTransition.Switched && navigator.SelectedId == pmcB.ProfileId && navigator.Following, "Opt-in automatic switching follows the next living PMC");
Check(navigator.LockOrientation, "Switching bots while already following keeps the chosen orientation");
pmcB.HealthController.IsAlive = false; previousPosition = freecam.transform.position;
Check(navigator.Follow(world, bridge, 3, true) == FollowTransition.Detached && freecam.transform.position.Equals(previousPosition), "Auto-switch with no candidates returns to stationary Freecam");
var listScav = world.AllAlivePlayersList.First(p => p.ProfileId == "scav");
Check(navigator.Visit(world, bridge, listScav, 3) && navigator.SelectedId == "scav", "Clicking a Scav works even with the wheel set to PMCs only");
navigator.ToggleFollow(world, bridge, 3);
Check(navigator.Following && navigator.SelectedId == "scav", "List selection becomes the manual follow target");
listScav.gameObject.SetActive(false);
Check(navigator.Follow(world, bridge, 3) == FollowTransition.Detached && !navigator.Visit(world, bridge, listScav, 3), "Inactive/despawned list target cannot be visited or followed");
listScav.gameObject.SetActive(true);
Check(!navigator.Visit(world, bridge, pmcA, 3), "Dead row cannot teleport the camera between roster refreshes");
Check(local.Position.Equals(localPosition), "Follow and orbit never move the player body");
navigator.SetFilter(BotFilter.AllBots);
navigator.Reset();
Check(navigator.Caption == "Wheel: previous / next bot", "Raid reset retains selected filter but no scene references or nickname");
foreach (var bot in world.AllAlivePlayersList) bot.HealthController.IsAlive = false;
previousPosition = freecam.transform.position;
navigator.Move(world, bridge, 1);
Check(navigator.Caption == "No living bots to visit" && freecam.transform.position.Equals(previousPosition), "Empty all-bot roster leaves camera in place");
bridge.Release();
// Optional overlay: activate via its own callback/API, switch off on leave, never persist.
var overlay = new BotDebugBridge();
Check(!overlay.Available && !overlay.Enabled && !overlay.Start(), "Missing BotDebug is nonfatal and is not presented as available");
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
Check(overlay.Available && !overlay.Enabled, "Bound overlay exposes its actual state");
overlay.BeginSession();
Check(!toggle.Value, "Automatic overlay off leaves current state alone at entry");
toggle.Value = true; // Simulate manually enabling BotDebug from its own UI.
overlay.Stop();
Check(!toggle.Value, "Manual overlay enabled during spectating is disabled on leave too");
overlay.BeginSession();
overlay.Start();
overlay.Disable();
Check(!overlay.Enabled, "Spectator menu can turn overlay off for this raid");
overlay.Start();
Check(overlay.Enabled, "Spectator menu can turn overlay on again");
overlay.Stop();
Check(!overlay.Enabled && !toggle.ConfigFile.SaveOnConfigSet, "Leave after repeated toggles disables overlay and preserves dependency save policy");

// Regression: extracting the last human empties BotSpawner's human roster, so
// its debug accessor returns default even though bots are still alive.
var debugWorld = new GameWorld();
var debugSpawner = new BotSpawner();
var observer = new Player { ProfileId = "observer" };
var debugBot = new Player { IsAI = true, ProfileId = "bot" };
debugBot.AIData.BotOwner = new BotOwner();
debugWorld.AllAlivePlayersList.Add(debugBot);
Comfort.Common.Singleton<GameWorld>.Instance = debugWorld;
int debugMessages = 0;
var view = new BotDebugView(_ => debugMessages++, _ => throw new Exception("Unexpected snapshot failure"));
Check(!view.Read(debugSpawner, observer, "bot").InitedBotData, "Native empty-human result reproduced without spectating");
var spectatorCamera = new Camera();
spectatorCamera.transform.position = new Vector3(2000, 10, 500);
view.Attach(debugWorld, observer, spectatorCamera);
observer.gameObject.SetActive(false);
observer.HealthController.IsAlive = false;
var debugSnapshot = view.Read(debugSpawner, observer, "bot");
Check(debugSnapshot.InitedBotData && debugSnapshot.Owner == debugBot.AIData.BotOwner && debugSnapshot.Observer == observer,
    "Extracted/dead human can observe native bot data without being registered");
Check(debugSpawner.Players.Count == 0 && !observer.gameObject.activeSelf, "Overlay never resurrects/registers the spectator");
Check(view.Origin(observer).x == 2000 && view.Camera == spectatorCamera, "Projection and max-distance origin use Freecam, not extracted body");
spectatorCamera.transform.position = new Vector3(-2000, 20, -500);
Check(view.Origin(observer).x == -2000, "Origin follows camera movement and PMC cycling immediately");
view.Read(debugSpawner, observer, "bot");
Check(debugMessages == 1, "Successful snapshot logging is once per session");
Check(!view.Read(debugSpawner, observer, "missing").InitedBotData, "Removed bots produce no stale overlay");
debugBot.HealthController.IsAlive = false;
Check(!view.Read(debugSpawner, observer, "bot").InitedBotData, "Dead bots omitted");
debugBot.HealthController.IsAlive = true;
debugBot.AIData.BotOwner.Active = false;
Check(!view.Read(debugSpawner, observer, "bot").InitedBotData, "Native inactive-bot behavior preserved");
debugBot.AIData.BotOwner.Active = true;
debugSpawner.Players.Add(observer);
debugSpawner.NativeResult = new DebugBotStruct(new BotOwner(), 0, observer);
Comfort.Common.Singleton<GameWorld>.Instance = new GameWorld();
Check(view.Camera == null && view.Read(debugSpawner, observer, "bot").Owner == debugSpawner.NativeResult.Owner,
    "Changed world restores native lookup and camera immediately");
Comfort.Common.Singleton<GameWorld>.Instance = debugWorld;
spectatorCamera.enabled = false;
Check(view.Camera == null, "Lost camera cannot take over BotDebug");
spectatorCamera.enabled = true;
view.Clear();
Check(view.Camera == null && view.Origin(observer).x == observer.Transform.position.x &&
    view.Read(debugSpawner, observer, "bot").Owner == debugSpawner.NativeResult.Owner, "Leaving restores ordinary BotDebug reads and origin");

// Exercise production IL rewrites, including branch/exception metadata and
// refusal of unsupported BotDebug versions. Rendering itself needs a real raid.
var getData = typeof(BotSpawner).GetMethod("GetBotDebugData")!;
var getTransform = typeof(Player).GetProperty("Transform")!.GetMethod!;
var getPosition = typeof(BifacialTransform).GetProperty("position")!.GetMethod!;
var getCamera = typeof(Camera).GetProperty("main")!.GetMethod!;
var il = new System.Reflection.Emit.DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator();
var label = il.DefineLabel();
var dataCall = new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Callvirt, getData);
dataCall.labels.Add(label);
var rewritten = BotDebugHooks.DataTranspiler(new[] { dataCall }).ToArray();
Check(Equals(dataCall.operand, getData) && rewritten[0].labels.Contains(label) &&
    ((System.Reflection.MethodInfo)rewritten[0].operand).GetParameters()[0].ParameterType == typeof(BotSpawner),
    "Data rewrite preserves stack receiver and labels without mutating input");
var transformCall = new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Callvirt, getTransform);
var positionCall = new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Callvirt, getPosition);
positionCall.labels.Add(label);
positionCall.blocks.Add(new HarmonyLib.ExceptionBlock(HarmonyLib.ExceptionBlockType.EndExceptionBlock));
var draw = BotDebugHooks.DrawTranspiler(new[] { new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Call, getCamera), transformCall, positionCall }).ToArray();
Check(((System.Reflection.MethodInfo)draw[0].operand).ReturnType == typeof(Camera) &&
    ((System.Reflection.MethodInfo)draw[1].operand).ReturnType == typeof(Vector3) &&
    draw[2].opcode == System.Reflection.Emit.OpCodes.Nop && draw[2].labels.Contains(label) && draw[2].blocks.Count == 1,
    "Overlay camera/origin rewrites preserve types and control flow");
Check(BotDebugHooks.DistanceTranspiler(new[] { transformCall, positionCall }).Count() == 2, "Text distance uses the same origin as drawing");
try { BotDebugHooks.DataTranspiler(Array.Empty<HarmonyLib.CodeInstruction>()).ToArray(); throw new Exception("Missing native data call accepted"); }
catch (InvalidOperationException) { count++; }
try { BotDebugHooks.DrawTranspiler(new[] { transformCall, positionCall }).ToArray(); throw new Exception("Missing native camera call accepted"); }
catch (InvalidOperationException) { count++; }
try { BotDebugHooks.DistanceTranspiler(new[] { transformCall }).ToArray(); throw new Exception("Incomplete position chain accepted"); }
catch (InvalidOperationException) { count++; }

BotInfoTests.Run(Check);
FikaCameraTests.Run(Check);
Console.WriteLine($"PASS: {count} production camera/overlay checks with Unity doubles (not a rendered raid).");

public static class FakeKillcamPlugin { public static FakeKillcamController? ReplayCtrl; }
public class FakeKillcamController
{
    public static object? PendingReplayKiller;
    private bool _playing;
    public bool ThrowOnRead;
    public bool IsPlaying { get => ThrowOnRead ? throw new InvalidOperationException("Replay destroyed") : _playing; set => _playing = value; }
}
