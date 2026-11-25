using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EFT;

namespace Horizon;

// Only reads existing components and public telemetry. Never subscribes to or drains RR/ORBIT events.
// Optional assemblies are supplied by the loader, so their absence cannot break HORIZON loading.
internal sealed class OptionalBotData
{
    private readonly Dictionary<(Type, string), MemberInfo?> _members = new();
    private readonly HashSet<string> _warnings = new();
    private readonly Action<string> _warning;
    private Type? _sainBot, _sainManager, _qbMonitor, _qbObjective;
    private Func<BotOwner, string>? _sainPersonality;
    private MethodInfo? _orbitObjective, _orbitDormant;
    private FieldInfo? _qbLiveAssignment;
    private Func<string, string>? _cometCurrent, _cometCrossfire, _cometPostCombat;
    private bool _lootingBots;

    public OptionalBotData(Action<string> warning) => _warning = warning;

    public void Bind(IEnumerable<Assembly> assemblies, bool lootingBots)
    {
        _lootingBots = lootingBots;
        foreach (var assembly in assemblies)
        {
            _sainBot ??= assembly.GetType("SAIN.Components.BotComponent");
            _sainManager ??= assembly.GetType("SAIN.Components.BotManagerComponent");
            var personality = assembly.GetType("SAIN.Interop.SAINExternal")?.GetMethod("GetPersonality", new[] { typeof(BotOwner) });
            if (personality is { IsStatic: true } && personality.ReturnType == typeof(string))
                _sainPersonality = (Func<BotOwner, string>)personality.CreateDelegate(typeof(Func<BotOwner, string>));
            _qbMonitor ??= assembly.GetType("QuestingBots.BotLogic.BotMonitor.BotMonitorController");
            var orbit = assembly.GetType("Orbit.Api.OrbitTelemetry");
            if (orbit != null)
            {
                _orbitObjective = orbit.GetMethod("GetBotObjective", new[] { typeof(string) });
                _orbitDormant = orbit.GetMethod("IsBotDormant", new[] { typeof(string) });
            }
            var comet = assembly.GetType("Comet.Api.Decisions");
            if (comet != null)
            {
                _cometCurrent = StringReader(comet, "GetCurrentDecision");
                _cometCrossfire = StringReader(comet, "GetCrossfireDecision");
                _cometPostCombat = StringReader(comet, "GetPostCombatDecision");
            }
            var objective = assembly.GetType("QuestingBots.Components.BotObjectiveManager");
            if (objective != null)
            {
                _qbObjective = objective;
                _qbLiveAssignment = objective.GetField("assignment", BindingFlags.Instance | BindingFlags.NonPublic);
            }
        }
    }

    private static Func<string, string>? StringReader(Type type, string name)
    {
        var method = type.GetMethod(name, new[] { typeof(string) });
        return method?.ReturnType == typeof(string) ? (Func<string, string>)Delegate.CreateDelegate(typeof(Func<string, string>), method) : null;
    }

    private object? Get(object? instance, string name)
    {
        if (instance == null) return null;
        var key = (instance.GetType(), name);
        if (!_members.TryGetValue(key, out var member))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            member = (MemberInfo?)key.Item1.GetProperty(name, flags) ?? key.Item1.GetField(name, flags);
            _members.Add(key, member);
        }
        return member is PropertyInfo property ? property.GetValue(instance) : (member as FieldInfo)?.GetValue(instance);
    }

    private string Text(object? instance, string name) => Get(instance, name)?.ToString() ?? "";

    private T Read<T>(string integration, Func<T> read, T fallback)
    {
        // A bot can temporarily lose its native layer during a transition. Suppress
        // duplicate warnings, not future reads or the actions of unrelated bots.
        // BotLabels already bounds refresh frequency and the number of reads per frame.
        try { return read(); }
        catch (Exception e)
        {
            if (_warnings.Add(integration)) _warning($"Bot labels: {integration} data unavailable for this sample: {e.GetBaseException().Message}. Retrying on the next refresh; other bots remain readable.");
            return fallback;
        }
    }

    public string Description => $"SAIN={_sainBot != null}, ORBIT={_orbitObjective != null}, COMET={_cometCurrent != null}, QB={_qbMonitor != null || _qbObjective != null}, LB={_lootingBots}";

    public (string personality, string action, bool ghost) ReadBot(Player player, GameWorld world, BotLabelFields fields = BotLabelFields.All)
    {
        if ((fields & BotLabelFields.Behavior) == 0) return ("", "", false);
        bool wantAction = (fields & BotLabelFields.Actions) != 0;
        bool wantPersonality = (fields & BotLabelFields.Personality) != 0;
        bool ghost = (wantAction || (fields & BotLabelFields.Ghost) != 0)
            && Read("ORBIT Ghost", () => _orbitDormant?.Invoke(null, new object[] { player.ProfileId }) is true, false);
        if (!wantAction && !wantPersonality) return ("", "", ghost);
        var owner = player.AIData?.BotOwner;
        string layer = wantAction ? Read("native layer", () => owner?.Brain?.Agent == null ? "" : owner.Brain.ActiveLayerName(), "") : "";
        object? sain = wantPersonality || BehaviorOwnership.IsSainLayer(layer) ? Read<object?>("SAIN component", () =>
        {
            if (_sainBot == null) return null;
            var component = player.gameObject.GetComponent(_sainBot) ?? owner?.gameObject.GetComponent(_sainBot);
            if (component != null) return component;
            var manager = _sainManager != null ? world.GetComponent(_sainManager) : null;
            return Get(manager, "Bots") is IDictionary bots ? bots[player.ProfileId] : null;
        }, null) : null;
        string personality = wantPersonality ? Read("SAIN personality", () => Text(Get(sain, "Info"), "Personality"), "") : "";
        if (wantPersonality && personality.Length == 0 && owner != null && _sainPersonality != null)
            personality = Read("SAIN personality API", () => _sainPersonality(owner) ?? "", "");
        if (!wantAction) return (personality, "", ghost);
        string native = Read("native decision", () => owner?.Brain?.LastDecision.ToString() ?? "", "");
        string decision = native;
        if (BehaviorOwnership.IsSainLayer(layer))
        {
            string sainDecision = Read("SAIN action", () =>
            {
                var decisions = Get(sain, "Decision");
                foreach (var name in DecisionFields)
                {
                    string value = Text(decisions, name);
                    if (BotPresentation.HasDecision(value)) return value;
                }
                string action = Text(Get(sain, "CurrentAction"), "Name");
                return BotPresentation.HasDecision(action) ? action : Text(sain, "ActiveLayer");
            }, "");
            decision = BehaviorOwnership.Resolve(native, layer, sainDecision);
        }
        else decision = BehaviorOwnership.Resolve(native, layer, null!);

        if (layer == "OrbitBrainLayer")
        {
            var objective = Read<object?>("ORBIT objective", () => _orbitObjective?.Invoke(null, new object[] { player.ProfileId }), null);
            decision = OrbitBehaviorDecision.Resolve(decision, layer, objective != null ? Text(objective, "Status") : null!, Text(objective, "Category"));
        }
        else if (!ghost && layer == "COMET : Recover magazine") decision = "COMET:RecoverMagazine";
        else if (!ghost && layer == "COMET : Crossfire")
            decision = Read("COMET crossfire", () => CometBehaviorDecision.ResolveCrossfire(_cometCrossfire?.Invoke(player.ProfileId)!, layer, decision, false), decision);
        else if (!ghost && layer == "COMET : Post-combat")
            decision = Read("COMET recovery", () => CometBehaviorDecision.ResolvePostCombat(_cometPostCombat?.Invoke(player.ProfileId)!, layer, decision, false), decision);
        else if (!ghost && layer == "SAIN : Combat Layer")
            decision = Read("COMET action", () => CometBehaviorDecision.Resolve(_cometCurrent?.Invoke(player.ProfileId)!, decision), decision);
        else if (layer.IndexOf("Loot", StringComparison.OrdinalIgnoreCase) >= 0 && layer != "LootPatrol" && _lootingBots)
            decision = "LootingBots:" + layer;
        else if (BotPresentation.Idle(native) || layer.IndexOf("Quest", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Combat/medical owners must win over an old quest retained by QB.
            if (!BehaviorOwnership.IsSainLayer(layer) && !layer.StartsWith("COMET", StringComparison.Ordinal))
                decision = Read("QuestingBots", () => QuestAction(player, owner, decision), decision);
        }
        return (personality, BotPresentation.Action(decision), ghost);
    }

    private static readonly string[] DecisionFields = { "CurrentSelfDecision", "CurrentCombatDecision", "CurrentSquadDecision" };

    private string QuestAction(Player player, BotOwner? owner, string fallback)
    {
        var monitor = _qbMonitor != null ? player.gameObject.GetComponent(_qbMonitor) ?? owner?.gameObject.GetComponent(_qbMonitor) : null;
        string action = Text(monitor, "CurrentDecision");
        // QB's public getter can create a component; its factory can insert a job-list entry.
        // Inspect only the existing objective instead, with a decision-only fallback on other versions.
        var objective = _qbObjective != null ? player.gameObject.GetComponent(_qbObjective) ?? owner?.gameObject.GetComponent(_qbObjective) : null;
        var assignment = objective != null ? _qbLiveAssignment?.GetValue(objective) : null;
        string status = Text(assignment, "Status");
        string quest = (status == "Active" || status == "Pending") ? Text(Get(assignment, "QuestAssignment"), "Name") : "";
        if (quest.Length > 0) return "QB:" + quest + (BotPresentation.HasDecision(action) ? " / " + action : "");
        return BotPresentation.HasDecision(action) ? "QB:" + action : fallback;
    }

    public void Clear() => _warnings.Clear();
}
