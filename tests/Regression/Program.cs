using Mono.Cecil;
using Mono.Cecil.Cil;
using Horizon;

if (args.Length == 4 && args[0] == "--package")
{
    PackagedPluginTests.Run(args[1], args[2], args[3]);
    return;
}

int assertions = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    assertions++;
}

BotLabelLayoutTests.Run(Check);
Check(SpectatorInterface.Scale(1080, true, 1) == 1, "1080p preserves the original UI size");
Check(SpectatorInterface.Scale(2160, true, 1) == 2, "4K doubles text, panels and controls");
Check(Math.Abs(SpectatorInterface.Scale(1440, true, 1) - 4f / 3) < .001f, "1440p scales by height, including ultrawide");
Check(SpectatorInterface.Scale(2160, false, 1.5f) == 1.5f, "Manual size works independently of resolution");
Check(SpectatorInterface.Scale(2160, true, float.NaN) == 2, "Malformed scale cannot hide the interface");
foreach (var screen in new[] { (1920, 1080), (2560, 1440), (3440, 1440), (3840, 2160), (1280, 720) })
foreach (float preference in new[] { .75f, 1f, 1.5f, 2.5f })
foreach (var panel in new[] { (330f, 185f), (400f, 660f) })
{
    float scale = SpectatorInterface.Scale(screen.Item2, true, preference);
    foreach (var axis in new[] { ((float)screen.Item1, panel.Item1 * scale, 16 * scale), ((float)screen.Item2, panel.Item2 * scale, 20 * scale) })
    {
        float size = Math.Min(axis.Item2, axis.Item1 - 2 * axis.Item3);
        float available = SpectatorInterface.Available(axis.Item1, size, axis.Item3);
        foreach (float saved in new[] { 0f, .25f, 1f })
        {
            float position = SpectatorInterface.Position(saved, available, axis.Item3);
            Check(position >= 0 && position + size <= axis.Item1, "Scaled panel remains visible at saved edge/center positions");
            float normalized = SpectatorInterface.Normalize(position, available, axis.Item3);
            Check(available == 0 || Math.Abs(saved - normalized) < .0001, "Physical drag coordinates preserve relative placement at every scale");
        }
    }
}
Check(SpectatorInterface.Normalize(-100, 100, 16) == 0 && SpectatorInterface.Normalize(10000, 100, 16) == 1, "Dragging outside the screen clamps to its edges");
foreach (bool cycling in new[] { false, true })
foreach (bool zoom in new[] { false, true })
{
    Check(SpectatorWheel.Route(false, false, zoom, cycling) == SpectatorWheelAction.Native, "Normal gameplay retains native wheel input");
    Check(SpectatorWheel.Route(true, true, zoom, cycling) == SpectatorWheelAction.Blocked, "Escape/F12/Alt capture wheel without zooming or cycling");
}
Check(SpectatorWheel.Route(true, false, true, true) == SpectatorWheelAction.Zoom, "Zoom modifier overrides bot cycling");
Check(SpectatorWheel.Route(true, false, true, false) == SpectatorWheelAction.Zoom, "Explicit zoom works with cycling disabled");
Check(SpectatorWheel.Route(true, false, false, true) == SpectatorWheelAction.Cycle, "Releasing the modifier restores bot cycling");
Check(SpectatorWheel.Route(true, false, false, false) == SpectatorWheelAction.Native, "Cycling disabled leaves ordinary Freecam wheel behavior intact");

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
using var plugin = AssemblyDefinition.ReadAssembly(Path.Combine(project, "bin/Release/netstandard2.1/HORIZON.dll"), parameters);
Check(plugin.Name.Name == "HORIZON", "Plugin assembly uses the HORIZON name");
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(root, "EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"), parameters);
using var freecam = AssemblyDefinition.ReadAssembly(Path.Combine(root, "BepInEx/plugins/Terkoiz.Freecam/Terkoiz.Freecam.dll"), parameters);
var entry = plugin.MainModule.GetType("Horizon.Plugin");
var stepPlayer = game.MainModule.GetType("EFT.Player");
var stepSource = stepPlayer.Fields.Single(f => f.Name == "NestedStepSoundSource");
var surfaceGate = stepPlayer.Methods.Single(m => m.Name == "CheckSurface");
Check(surfaceGate.Parameters.Single().ParameterType.FullName == "System.Single" && surfaceGate.ReturnType.FullName == "System.Boolean",
    "Native movement range gate exposes a passive pass/fail observation");
var stepBank = game.MainModule.GetType("EFT.SoundBank").Methods.Single(m => m.Name == "Play");
Check(stepBank.ReturnType.FullName == "System.Void" && stepBank.Parameters[0].ParameterType.FullName == stepSource.FieldType.FullName &&
    stepBank.Parameters[2].ParameterType.FullName == "System.Single" && stepBank.Parameters[3].ParameterType.FullName == "System.Single",
    "Footstep observer binds to the native source, distance and volume");
bool UsesStepBank(MethodDefinition m) => m.HasBody &&
    m.Body.Instructions.Any(i => i.Operand is FieldReference f && f.FullName == stepSource.FullName) &&
    m.Body.Instructions.Any(i => i.Operand is MethodReference target && target.FullName == stepBank.FullName);
Check(UsesStepBank(stepPlayer.Methods.Single(m => m.Name == "PlayStepSound")), "Native walking uses the observed bank and source");
Check(stepPlayer.NestedTypes.SelectMany(t => t.Methods).Any(m => m.Name == "MoveNext" && UsesStepBank(m)),
    "Native player coroutine also dispatches to the observed movement source");
var handoff = entry.NestedTypes.SelectMany(t => t.Methods).Where(m => m.HasBody)
    .Single(m => m.Body.Instructions.Any(i => i.Operand is MethodReference { DeclaringType.Name: "SpectatorFootsteps", Name: "Begin" }));
var handoffIl = handoff.Body.Instructions.ToList();
int attach = handoffIl.FindIndex(i => i.Operand is MethodReference { DeclaringType.Name: "FreecamBridge", Name: "Attach" });
int beginWatch = handoffIl.FindIndex(i => i.Operand is MethodReference { DeclaringType.Name: "SpectatorFootsteps", Name: "Begin" });
Check(attach >= 0 && beginWatch > attach, "Watch begins after camera handoff");
Check(!handoffIl.Skip(attach + 1).Take(beginWatch - attach - 1)
    .Any(i => i.Operand is MethodReference { Name: "get_Extracted" } or MethodReference { DeclaringType.Name: "KillcamBridge" }),
    "Solo extraction and death both start footstep observation independently of Killcam");
var lateUpdate = entry.Methods.Single(m => m.Name == "LateUpdate").Body.Instructions;
var observeSteps = lateUpdate.Select(i => i.Operand).OfType<MethodReference>()
    .Single(m => m.DeclaringType.Name == "SpectatorFootsteps" && m.Name == "Observe");
Check(observeSteps.Parameters.Any(p => p.ParameterType.Name == "FollowTransition"),
    "Automatic target loss reaches the audio observer on the same update");
var identity = entry.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin");
Check((string)identity.ConstructorArguments[0].Value == "com.chazut.horizon" && (string)identity.ConstructorArguments[1].Value == "HORIZON", "HORIZON exposes its public plugin GUID");
Check(plugin.MainModule.GetType("Horizon.BotDebugHooks").Methods.Single(m => m.Name == ".cctor").Body.Instructions.Any(i =>
    i.Operand is string value && value == "com.chazut.horizon.botdebug"), "BotDebug patches use the HORIZON identity");
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
Check(Convert.ToInt32(dep.ConstructorArguments[1].Value) == 2, "Fika clients can load without Terkoiz Freecam");
var incompatibilities = entry.CustomAttributes.Where(a => a.AttributeType.Name == "BepInIncompatibility")
    .Select(a => (string)a.ConstructorArguments[0].Value).ToArray();
Check(!incompatibilities.Contains("com.fika.core") && entry.CustomAttributes.Any(a => a.AttributeType.Name == "BepInDependency" &&
    (string)a.ConstructorArguments[0].Value == "com.fika.core" && Convert.ToInt32(a.ConstructorArguments[1].Value) == 2), "Fika is an optional camera provider");
Check(!plugin.MainModule.AssemblyReferences.Any(a => a.Name == "Fika.Core" || a.Name == "Terkoiz.Freecam"), "Mono can load HORIZON with either provider absent");
Check(!entry.Methods.Single(m => m.Name == "InstallFikaCamera").Body.Instructions.Any(i =>
    i.Operand is TypeReference t && (t.FullName.Contains("LocalGame") || t.FullName.Contains("HealthController"))), "Co-op does not install native solo lifecycle patches");
string fikaPath = args.Length > 1 ? args[1] : Path.Combine(root, "BepInEx/plugins/Fika.Core.dll");
if (args.Length > 1 || File.Exists(fikaPath))
{
    using var fika = AssemblyDefinition.ReadAssembly(fikaPath);
    var camera = fika.MainModule.GetType("Fika.Core.Main.FreeCamera.FreeCamera");
    var backend = fika.MainModule.GetType("Fika.Core.Main.Utils.FikaBackendUtils");
    bool BoolGetter(TypeDefinition t, string name, bool isStatic = false) => t.Properties.Any(p =>
        p.Name == name && p.GetMethod.IsPublic && p.GetMethod.IsStatic == isStatic && p.PropertyType.FullName == "System.Boolean");
    foreach (var name in new[] { "IsHeadless", "IsServer", "IsTransit" }) Check(BoolGetter(backend, name, true), "Fika role " + name);
    foreach (var name in new[] { "IsActive", "Extracted" }) Check(BoolGetter(camera, name), "Fika camera state " + name);
    var settings = fika.MainModule.GetType("Fika.Core.FikaPlugin").Properties.Single(p => p.Name == "Settings").PropertyType.Resolve();
    Check(BoolGetter(settings, "AllowSpectateBots") && BoolGetter(settings, "AllowSpectateFreeCam"), "Fika server spectator settings");
    Check(camera.Methods.Any(m => m.Name == "DetachCamera" && m.IsPublic && !m.IsStatic && m.Parameters.Count == 0), "Fika camera detach API");
    Check(camera.Fields.Any(f => f.Name == "_currentPlayer" && f.FieldType.FullName == "Fika.Core.Main.Players.FikaPlayer"), "Selected Fika spectator actor field");
    Check(new[] { "_isFollowing", "_disableInput" }.All(n => camera.Fields.Any(f => f.Name == n && f.FieldType.FullName == "System.Boolean")) &&
        new[] { "_yaw", "_pitch" }.All(n => camera.Fields.Any(f => f.Name == n && f.FieldType.FullName == "System.Single")), "Fika native look fields");
    Check(camera.Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand is MethodReference m &&
        m.DeclaringType.FullName == "UnityEngine.Input" && m.Name == "GetAxisRaw"), "Fika camera exposes the wheel used by the patch");
    Check(fika.MainModule.GetType("Fika.Core.Main.Players.FikaPlayer").Fields.Any(f =>
        f.Name == "IsObservedAI" && f.IsPublic && f.FieldType.FullName == "System.Boolean"), "Fika replicates the AI marker");
    var server = fika.MainModule.GetType("Fika.Core.Networking.FikaServer");
    var settingsHandler = server.Methods.Single(m => m.Name == "OnNetworkSettingsPacketReceived");
    Check(settingsHandler.Parameters.Select(p => p.Name).SequenceEqual(new[] { "packet", "peer" }) &&
        server.Fields.Any(f => f.Name == "_cachedConnections" && f.FieldType.FullName == "System.Collections.Generic.Dictionary`2<System.String,System.Int32>") &&
        settingsHandler.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_cachedConnections"),
        "Fika connects the actual peer and its assigned player ID before the HORIZON postfix");
    Check(server.Methods.Single(m => m.Name == "OnNetworkReceive").Parameters.Take(2).Select(p => p.Name)
        .SequenceEqual(new[] { "peer", "reader" }), "Fika server discovery hook parameters");
    var packetKinds = fika.MainModule.GetType("Fika.Core.Networking.NetworkUtils").NestedTypes.Single(t => t.Name == "EPacketType");
    Check(!packetKinds.Fields.Any(f => f.HasConstant && Convert.ToInt32(f.Constant) == 0xDA), "HORIZON discovery envelope is unused by this Fika build");
    var startup = entry.Methods.Single(m => m.Name == "Awake").Body.Instructions
        .Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).ToList();
    int feedStartup = startup.FindIndex(m => m.DeclaringType.Name == "FikaBotFeed" && m.Name == "Load");
    Check(feedStartup >= 0 && feedStartup < startup.FindIndex(m => m.Name == "get_isBatchMode"),
        "Headless hosts initialize action sync before the camera is disabled");
    Console.WriteLine("Checked co-op camera and network contracts against " + fikaPath);
}
Check(incompatibilities.Contains("com.chazut.raidspectator"), "Loader refuses concurrent legacy Raid Spectator installation");
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
// Reflection bridges must match real binaries as well as the isolated behavior doubles.
Check(!plugin.MainModule.AssemblyReferences.Any(a => new[] { "SAIN", "ORBIT", "COMET", "SPTQuestingBots", "LootingBots" }.Contains(a.Name)),
    "Bot information has no hard assembly reference to optional AI mods");
TypeReference PublicMember(TypeDefinition type, string name)
{
    for (var current = type; current != null; current = current.BaseType?.Resolve())
    {
        var property = current.Properties.FirstOrDefault(p => p.Name == name && p.GetMethod?.IsPublic == true);
        if (property != null) { assertions++; return property.PropertyType; }
        var field = current.Fields.FirstOrDefault(f => f.Name == name && f.IsPublic);
        if (field != null) { assertions++; return field.FieldType; }
    }
    throw new Exception("Missing reflected public member: " + type.FullName + "." + name);
}
foreach (string mod in new[] { "SAIN", "ORBIT", "COMET" })
{
    string directory = Path.Combine(root, "BepInEx/plugins", mod);
    string binary = Path.Combine(directory, mod + ".dll");
    if (!File.Exists(binary)) continue;
    resolver.AddSearchDirectory(directory);
    using var optional = AssemblyDefinition.ReadAssembly(binary, parameters);
    if (mod == "SAIN")
    {
        var bot = optional.MainModule.GetType("SAIN.Components.BotComponent");
        PublicMember(PublicMember(bot, "Info").Resolve(), "Personality");
        var decisions = PublicMember(bot, "Decision").Resolve();
        foreach (string name in new[] { "CurrentSelfDecision", "CurrentCombatDecision", "CurrentSquadDecision" }) PublicMember(decisions, name);
        PublicMember(bot, "CurrentAction"); PublicMember(bot, "ActiveLayer");
        Check(PublicMember(optional.MainModule.GetType("SAIN.Components.BotManagerComponent"), "Bots").FullName.StartsWith("System.Collections.Generic.Dictionary`2<System.String,"),
            "SAIN fallback roster supports profile-ID dictionary lookup");
    }
    else
    {
        var api = optional.MainModule.GetType(mod == "ORBIT" ? "Orbit.Api.OrbitTelemetry" : "Comet.Api.Decisions");
        var names = mod == "ORBIT" ? new[] { "GetBotObjective", "IsBotDormant" } : new[] { "GetCurrentDecision", "GetCrossfireDecision", "GetPostCombatDecision" };
        foreach (string name in names)
        {
            var method = api.Methods.Single(m => m.Name == name && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.String");
            Check(method.IsPublic && method.IsStatic, mod + " exposes " + name);
            if (mod == "COMET") Check(method.ReturnType.FullName == "System.String", "COMET decision delegate signature");
            else if (name == "IsBotDormant") Check(method.ReturnType.FullName == "System.Boolean", "ORBIT dormancy signature");
            else { PublicMember(method.ReturnType.Resolve(), "Status"); PublicMember(method.ReturnType.Resolve(), "Category"); }
        }
    }
}
foreach (var member in plugin.MainModule.GetMemberReferences().Where(m => new[] { "Assembly-CSharp", "Terkoiz.Freecam", "BepInEx", "Comfort", "0Harmony" }.Contains(m.DeclaringType.GetElementType().Scope.Name)))
{
    bool resolved = member switch { MethodReference m => m.Resolve() != null, FieldReference f => f.Resolve() != null, _ => true };
    Check(resolved, "Unresolved API " + member.FullName);
}
Console.WriteLine($"PASS: {assertions} lifecycle and installed-binary API checks. A real raid is still required for camera/save validation.");
