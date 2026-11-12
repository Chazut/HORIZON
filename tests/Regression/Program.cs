using Mono.Cecil;
using Mono.Cecil.Cil;
using RaidSpectator;

int assertions = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    assertions++;
}

var start = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
// Reproduce the missing synchronization source without pretending to simulate
// Unity collisions: the transform and the physics-query snapshot are separate.
// No local player or visible rigidbody remains to synchronize the snapshot.
var physics = new SpectatorPhysics();
int copies = 0;
float transformPosition = 0, queryPosition = 0;
void Synchronize() { queryPosition = transformPosition; copies++; }
Check(!physics.Sync(false, false, 1, false, 0, Synchronize) && copies == 0, "No physics intervention while playing/ending");
Check(!physics.Sync(true, true, 1, false, 0, Synchronize) && copies == 0, "Respect automatic Unity synchronization");
Check(physics.Sync(true, false, 1, false, 0, Synchronize), "Automatic sync disabled mid-frame can still synchronize");
for (int frame = 2; frame <= 600; frame++)
{
    transformPosition += 0.2f;
    Check(queryPosition != transformPosition, "No local-player sync leaves a stale physics snapshot");
    Check(physics.Sync(true, false, frame, false, 0, Synchronize) && queryPosition == transformPosition, "Spectating keeps physical queries current with no visible rigidbody");
    for (int bot = 0; bot < 30; bot++)
        Check(!physics.Sync(true, false, frame, false, 0, Synchronize), "World/player callbacks share one synchronization, not one per bot");
}
Check(copies == 600 && physics.Count == 600, "One global synchronization per rendered tick");
Check(physics.Sync(true, false, 600, true, 1f, Synchronize), "Fixed update has its own tick");
Check(!physics.Sync(true, false, 601, true, 1f, Synchronize), "Same fixed tick is not repeated across render callbacks");
Check(physics.Sync(true, false, 601, true, 1.02f, Synchronize), "A second fixed step in one frame is synchronized");
Check(!physics.Sync(false, false, 602, false, 0, Synchronize), "Leaving stops sync immediately");
physics.Reset();
Check(physics.Count == 0 && physics.TotalMs == 0 && physics.MaxMs == 0, "Raid cleanup drops timing state");
Check(physics.Sync(true, false, 600, false, 0, Synchronize), "Rollback/new raid may reuse a frame ID");
try { physics.Sync(true, false, 601, false, 0, () => throw new InvalidOperationException("test")); }
catch (InvalidOperationException) { }
Check(physics.Sync(true, false, 601, false, 0, Synchronize), "Failed synchronization remains retryable");
var death = start.AddSeconds(190);
SpectatorSession Begin()
{
    var session = new SpectatorSession();
    Check(session.Begin(true, true, true, "local", 190, start, death), "Can watch a genuine local death");
    return session;
}

// Failed prerequisites must leave the complete native death flow available.
foreach (var precondition in new[] { (false, true, true), (true, false, true), (true, true, false) })
{
    var session = new SpectatorSession();
    Check(!session.Begin(precondition.Item1, precondition.Item2, precondition.Item3, "local", 190, start, death), "Entry rejected safely");
    Check(session.DecideStop(true, "local", true) == StopDecision.Original, "No deferred result after rejected entry");
}
foreach (float time in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    Check(!new SpectatorSession().Begin(true, true, true, "local", time, start, death), "Reject invalid clock");
Check(!new SpectatorSession().Begin(true, true, true, "", 190, start, death), "Require local identity");
var s = Begin();
Check(s.DecideStop(false, "local", true) == StopDecision.Original, "Another game keeps normal death");
Check(s.DecideStop(true, "bot", true) == StopDecision.Original, "Bot death is never delayed");
Check(!s.Begin(true, true, true, "other", 200, start, death), "Do not replace active session");
for (int i = 0; i < 10; i++) Check(s.DecideStop(true, "local", true) == StopDecision.Defer, "Repeated death stays spectating");
Check(s.RequestExit(), "Exit button requests shutdown");
Check(!s.RequestExit(), "Double click cannot submit two saves");
Check(s.DecideStop(true, "local", true) == StopDecision.Captured, "Requested exit allows native Killed stop");
Check(s.DecideStop(true, "local", true) == StopDecision.Captured, "Nested BaseLocalGame.Stop remains permitted");
Check(s.PlayedSeconds == 190 && s.PlayedDuration.TotalSeconds == 190, "Death-time statistics frozen");
var leaveTime = death.AddHours(1);
Check(leaveTime - s.StartForSave(leaveTime) == TimeSpan.FromSeconds(190), "One hour spectating adds no survival time");
Check(s.DecideStop(false, "local", true) == StopDecision.Original, "No ending override in another game");
// Native timer or exit-menu requests must finish the raid, keeping the real death outcome.
foreach (var reason in new[] { "timer", "exit-menu", "extraction", "transit" })
{
    s = Begin();
    Check(s.DecideStop(true, "local", false) == StopDecision.Captured, reason + " ends as Killed");
    Check(!s.Watching && s.Phase == SessionPhase.Ending, "No stuck spectator after external stop");
}
foreach (var reason in new[] { "disabled", "camera-lost", "plugin-unload" })
{
    s = Begin();
    Check(s.RequestExit(), reason + " exits");
    Check(s.DecideStop(true, "local", true) == StopDecision.Captured, reason + " preserves death");
}
for (int raid = 0; raid < 30; raid++)
{
    s.Reset();
    Check(s.Phase == SessionPhase.Idle && s.ProfileId == "" && s.PlayedSeconds == 0, "New raid has no stale result");
    Check(s.DecideStop(true, "local", false) == StopDecision.Original, "Alive extraction unaffected next raid");
    Check(s.Begin(true, true, true, "local", 190, start, death), "Next raid spectating works");
    Check(s.RequestExit(), "Next raid exit works");
}
var skew = new SpectatorSession();
Check(skew.Begin(true, true, true, "local", -1, death, start), "Clock skew cannot trap the raid");
Check(skew.PlayedSeconds == 0 && skew.PlayedDuration == TimeSpan.Zero, "Clock skew clamped");

// Extraction captures the native outcome (including run-through) and never upgrades it.
foreach (var outcome in new[] { WatchOutcome.Survived, WatchOutcome.Runner })
{
    var extracted = new SpectatorSession();
    Check(extracted.Begin(true, true, true, "local", 90, start, start.AddSeconds(90), outcome, "Gate 3"), "Extraction starts");
    Check(extracted.Extracted && extracted.Outcome == outcome && extracted.ExitName == "Gate 3", "Native extraction result retained");
    Check(extracted.DecideStop(true, "local", true) == StopDecision.Defer, "Repeated extraction callback deferred");
    Check(extracted.DecideStop(true, "another-player", true) == StopDecision.Original, "Other extraction not intercepted");
    Check(extracted.DecideStop(true, "local", false) == StopDecision.Captured, "Raid timer completes captured extraction");
    Check(extracted.Outcome == outcome && extracted.ExitName == "Gate 3", "Timer cannot turn extraction into MIA");
    Check(extracted.PlayedSeconds == 90 && leaveTime - extracted.StartForSave(leaveTime) == TimeSpan.FromSeconds(90), "Spectating cannot turn Runner into Survived or add survival time");
    extracted.Reset();
    Check(!extracted.Extracted && extracted.ExitName == null, "Next raid has no extracted player state");
}
var cycle = new PmcCycle();
Check(cycle.Next(new(), 1) == null, "Empty PMC list is harmless");
var ids = new List<string> { "c", "a", "b" };
Check(cycle.Next(ids, 1) == "a" && cycle.Next(ids, 1) == "b", "Forward cycle sorted independent of spawn order");
Check(cycle.Next(new() { "b", "a", "c" }, 1) == "c", "Reordering roster keeps current index by ID");
Check(cycle.Next(ids, 1) == "a" && cycle.Next(ids, -1) == "c", "Cycle wraps in both directions");
Check(cycle.Next(new() { "a", "b" }, -1) == "b", "Death/extraction of selected PMC gives valid remaining target");
Check(cycle.Next(new() { "b" }, 1) == "b", "Single PMC can be revisited");
cycle.Reset();
Check(cycle.SelectedId == null && cycle.Next(ids, -1) == "c", "New raid starts clean; reverse starts at end");

// Verify all native targets and member references against the actual installed binaries.
string root = args.Length > 0 ? args[0] : "C:/Games/SPT-4.1";
string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
using var resolver = new DefaultAssemblyResolver();
foreach (string path in new[] { "BepInEx/core", "EscapeFromTarkov_Data/Managed", "BepInEx/plugins/Terkoiz.Freecam" })
    resolver.AddSearchDirectory(Path.Combine(root, path));
var parameters = new ReaderParameters { AssemblyResolver = resolver };
using var plugin = AssemblyDefinition.ReadAssembly(Path.Combine(project, "bin/Release/netstandard2.1/RaidSpectator.dll"), parameters);
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(root, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"), parameters);
using var freecam = AssemblyDefinition.ReadAssembly(Path.Combine(root, "BepInEx/plugins/Terkoiz.Freecam/Terkoiz.Freecam.dll"), parameters);
var entry = plugin.MainModule.GetType("RaidSpectator.Plugin");
var physicsApi = game.MainModule.GetType("PhysicsExtensions");
Check(new[] { "Update", "FixedUpdate" }.All(n => physicsApi.Methods.Any(m => m.Name == n && m.IsStatic && m.Parameters.Count == 0)), "Always-running application physics targets exist");
var armsLateUpdate = game.MainModule.GetType("EFT.Player").Methods.Single(m => m.Name == "ArmsLateUpdate");
Check(armsLateUpdate.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_IsYourPlayer") &&
    armsLateUpdate.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "SyncTransforms"), "Installed EFT sync source is tied to local-player hands");
var simulation = physicsApi.NestedTypes.Single(t => t.Name == "Simulation");
var updateController = physicsApi.NestedTypes.Single(t => t.Name == "UpdateController");
Check(updateController.Methods.SelectMany(m => m.HasBody ? m.Body.Instructions : Enumerable.Empty<Instruction>()).Any(i => i.Operand is MethodReference m && m.Name == "set_SyncTransformsControlledByUnity"), "Native rigidbody controller can disable automatic transform sync");
Check(simulation.Methods.Single(m => m.Name == "get_CanRunSimulate").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_updateEnabled"), "Physics simulation may stop despite the raid still running");
Check(entry.Methods.Single(m => m.Name == "PlayerTickPrefix").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "MaintainPhysics"), "Physics ready before first AI body update regardless of application order");
Check(entry.Methods.Single(m => m.Name == "TryWatch").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "MaintainPhysics"), "Physics starts at entry, not after Killcam");
Check(!plugin.MainModule.GetMemberReferences().OfType<MethodReference>().Any(m => m.DeclaringType.FullName == "UnityEngine.Physics" && new[] { "Simulate", "set_autoSyncTransforms", "set_simulationMode" }.Contains(m.Name)), "No forced simulation or global physics setting mutation");
var dep = entry.CustomAttributes.Single(a => a.AttributeType.Name == "BepInDependency" && (string)a.ConstructorArguments[0].Value == "com.terkoiz.freecam");
Check((string)dep.ConstructorArguments[0].Value == "com.terkoiz.freecam" && (string)dep.ConstructorArguments[1].Value == "1.4.8", "Required Freecam version");
var incompat = entry.CustomAttributes.Single(a => a.AttributeType.Name == "BepInIncompatibility");
Check((string)incompat.ConstructorArguments[0].Value == "com.fika.core", "Loader refuses Fika");
var killcamDep = entry.CustomAttributes.Single(a => a.AttributeType.Name == "BepInDependency" && (string)a.ConstructorArguments[0].Value == "com.maschine.Killcam");
Check(Convert.ToInt32(killcamDep.ConstructorArguments[1].Value) == 2, "Killcam remains an optional dependency");
string killcamPath = Path.Combine(root, "BepInEx/plugins/maschine-Killcam.dll");
if (File.Exists(killcamPath))
{
    using var killcam = AssemblyDefinition.ReadAssembly(killcamPath, parameters);
    var replayField = killcam.MainModule.GetType("Killcam.Plugin").Fields.Single(f => f.Name == "ReplayCtrl");
    Check(replayField.IsPublic && replayField.IsStatic, "Installed Killcam exposes its replay controller");
    var replay = replayField.FieldType.Resolve();
    var pending = replay.Fields.Single(f => f.Name == "PendingReplayKiller");
    Check(pending.IsPublic && pending.IsStatic, "Installed Killcam pending state covers the startup delay");
    var playing = replay.Properties.Single(p => p.Name == "IsPlaying");
    Check(playing.GetMethod.IsPublic && !playing.GetMethod.IsStatic && playing.PropertyType.FullName == "System.Boolean", "Installed Killcam playback state contract");
}
var baseGame = game.MainModule.GetType("EFT.BaseLocalGame`1");
var localGame = game.MainModule.GetType("EFT.LocalGame");
var deathMethod = baseGame.Methods.Single(m => m.Name == "InitiateGameStopping");
var nativeStop = localGame.Methods.Single(m => m.Name == "Stop");
Check(deathMethod.Parameters.Count == 0 && !deathMethod.IsStatic, "Native death target contract");
Check(nativeStop.Parameters.Select(p => p.Name).SequenceEqual(new[] { "profileId", "exitStatus", "exitName", "delay" }), "Harmony native Stop argument names");
Check(baseGame.Methods.Single(m => m.Name == "Stop").Parameters.Count == 4, "Base Stop fallback contract");
Check(baseGame.Fields.Single(f => f.Name == "SessionStartTime").FieldType.FullName == "System.DateTime", "Native save clock contract");
var playerEnd = game.MainModule.GetType("EFT.Player").Methods.Single(m => m.Name == "OnGameSessionEnd");
Check(playerEnd.Parameters.Select(p => p.Name).SequenceEqual(new[] { "exitStatus", "pastTime", "locationId", "exitName" }), "Native result argument names");
var freecamController = freecam.MainModule.GetType("Terkoiz.Freecam.FreecamController");
Check(freecamController.Methods.Any(m => m.Name == "Update" && m.Parameters.Count == 0), "Freecam input interception target");
Check(freecamController.Methods.Any(m => m.Name == "get_IsFreecamActive" && m.ReturnType.FullName == "System.Boolean"), "Freecam camera ownership target");
Check(freecam.MainModule.GetType("Terkoiz.Freecam.Freecam").Fields.Any(f => f.Name == "IsActive" && f.IsPublic), "Freecam public activation API");
Check(deathMethod.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Stop"), "Death still ends through Stop");
Check(nativeStop.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name.StartsWith("BaseLocalGame") && m.Name == "Stop"), "Outer Stop still calls Base Stop");
var gameEnd = baseGame.Methods.Single(m => m.Name == "GameEnd");
var saves = ((TypeReference)gameEnd.CustomAttributes.Single(a => a.AttributeType.Name == "AsyncStateMachineAttribute").ConstructorArguments[0].Value).Resolve();
Check(saves.Methods.Single(m => m.Name == "MoveNext").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "SessionStartTime"), "Native backend result uses the protected session clock");
var health = game.MainModule.GetType("EFT.HealthSystem.ActiveHealthController");
Check(health.Methods.Single(m => m.Name == "ApplyDamage").ReturnType.FullName == "System.Single", "Extraction damage guard result type");
Check(health.Methods.Single(m => m.Name == "ManualUpdate").Parameters.Count == 1, "Health freeze target exists");
Check(game.MainModule.GetType("EFT.Player").Methods.Single(m => m.Name == "ManualUpdate").Parameters.Count == 3, "Manual player freeze target exists");
var extract = game.MainModule.GetType("CommonAssets.Scripts.Game.EndByExitTrigerScenario");
Check(extract.Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "StopSession"), "Real extraction still routes to StopSession");
var spawner = game.MainModule.GetType("EFT.BotSpawner");
Check(spawner.Methods.Single(m => m.Name == "DeletePlayer").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "DeletePlayerCauseDead"), "Native removal clears bot group targets");
var fcUpdate = freecam.MainModule.GetType("Terkoiz.Freecam.Freecam").Methods.Single(m => m.Name == "Update");
Check(fcUpdate.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Input" && m.Name == "GetAxis"), "Freecam scroll interception has an installed target");
Check(fcUpdate.Body.Instructions.Any(i => (i.Operand as string) == "Mouse ScrollWheel"), "Freecam uses expected wheel axis");
using (var botdebug = AssemblyDefinition.ReadAssembly(Path.Combine(root, "BepInEx/plugins/DrakiaXYZ-BotDebug.dll"), parameters))
{
    var settings = botdebug.MainModule.GetType("DrakiaXYZ.BotDebug.Helpers.Settings");
    Check(settings.Fields.Single(f => f.Name == "Enable").FieldType.FullName.Contains("ConfigEntry`1<System.Boolean>"), "BotDebug runtime toggle contract");
    Check(botdebug.MainModule.GetType("DrakiaXYZ.BotDebug.Components.BotDebugComponent").Methods.Any(m => m.Name == "Enable" && m.IsStatic && m.IsPublic), "BotDebug idempotent overlay creation API");
    var component = botdebug.MainModule.GetType("DrakiaXYZ.BotDebug.Components.BotDebugComponent");
    var update = component.Methods.Single(m => m.Name == "Update");
    var draw = component.Methods.Single(m => m.Name == "OnGUI");
    var behaviour = botdebug.MainModule.GetType("DrakiaXYZ.BotDebug.BotInfo").Methods.Single(m => m.Name == "GetBehaviour");
    Check(update.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "EFT.BotSpawner" && m.Name == "GetBotDebugData") == 1,
        "Installed BotDebug has exactly one supported native snapshot call");
    Check(draw.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Camera" && m.Name == "get_main") == 1,
        "Installed BotDebug has exactly one camera projection source");
    foreach (var method in new[] { draw, behaviour })
    {
        int chains = 0;
        var il = method.Body.Instructions;
        for (int n = 0; n + 1 < il.Count; n++)
            if (il[n].Operand is MethodReference a && a.DeclaringType.FullName == "EFT.Player" && a.Name == "get_Transform" &&
                il[n + 1].Operand is MethodReference b && b.Name == "get_position" && b.DeclaringType.FullName == a.ReturnType.FullName) chains++;
        Check(chains == 1, "Installed BotDebug supports spectator origin rewrite in " + method.Name);
    }
}
foreach (var member in plugin.MainModule.GetMemberReferences().Where(m => new[] { "Assembly-CSharp", "Terkoiz.Freecam", "BepInEx", "Comfort", "0Harmony" }.Contains(m.DeclaringType.GetElementType().Scope.Name)))
{
    bool resolved = member switch { MethodReference m => m.Resolve() != null, FieldReference f => f.Resolve() != null, _ => true };
    Check(resolved, "Unresolved API " + member.FullName);
}
Console.WriteLine($"PASS: {assertions} lifecycle and installed-binary API checks. A real raid is still required for camera/save validation.");
