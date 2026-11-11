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
Check(s.DecideStop(true, "local", true) == StopDecision.Killed, "Requested exit allows native Killed stop");
Check(s.DecideStop(true, "local", true) == StopDecision.Killed, "Nested BaseLocalGame.Stop remains permitted");
Check(s.PlayedSeconds == 190 && s.PlayedDuration.TotalSeconds == 190, "Death-time statistics frozen");
var leaveTime = death.AddHours(1);
Check(leaveTime - s.StartForSave(leaveTime) == TimeSpan.FromSeconds(190), "One hour spectating adds no survival time");
Check(s.DecideStop(false, "local", true) == StopDecision.Original, "No ending override in another game");
// Native timer or exit-menu requests must finish the raid, keeping the real death outcome.
foreach (var reason in new[] { "timer", "exit-menu", "extraction", "transit" })
{
    s = Begin();
    Check(s.DecideStop(true, "local", false) == StopDecision.Killed, reason + " ends as Killed");
    Check(!s.Watching && s.Phase == SessionPhase.Ending, "No stuck spectator after external stop");
}
foreach (var reason in new[] { "disabled", "camera-lost", "plugin-unload" })
{
    s = Begin();
    Check(s.RequestExit(), reason + " exits");
    Check(s.DecideStop(true, "local", true) == StopDecision.Killed, reason + " preserves death");
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
var dep = entry.CustomAttributes.Single(a => a.AttributeType.Name == "BepInDependency");
Check((string)dep.ConstructorArguments[0].Value == "com.terkoiz.freecam" && (string)dep.ConstructorArguments[1].Value == "1.4.8", "Required Freecam version");
var incompat = entry.CustomAttributes.Single(a => a.AttributeType.Name == "BepInIncompatibility");
Check((string)incompat.ConstructorArguments[0].Value == "com.fika.core", "Loader refuses Fika");
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
foreach (var member in plugin.MainModule.GetMemberReferences().Where(m => new[] { "Assembly-CSharp", "Terkoiz.Freecam", "BepInEx", "Comfort", "0Harmony" }.Contains(m.DeclaringType.GetElementType().Scope.Name)))
{
    bool resolved = member switch { MethodReference m => m.Resolve() != null, FieldReference f => f.Resolve() != null, _ => true };
    Check(resolved, "Unresolved API " + member.FullName);
}
Console.WriteLine($"PASS: {assertions} lifecycle and installed-binary API checks. A real raid is still required for camera/save validation.");
