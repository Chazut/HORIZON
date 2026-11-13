using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Terkoiz.Freecam;
using UnityEngine;

namespace Horizon;

[BepInPlugin(Id, "HORIZON", "1.0.0")]
[BepInDependency("com.terkoiz.freecam", "1.4.8")]
[BepInDependency(KillcamBridge.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(BotDebugBridge.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
[BepInIncompatibility("com.fika.core")]
[BepInIncompatibility("com.chazut.raidspectator")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "com.chazut.horizon";
    private static Plugin? _instance;
    private readonly SpectatorSession _session = new();
    private readonly FreecamBridge _camera = new();
    private readonly KillcamBridge _killcam = new();
    private readonly BotDebugBridge _botDebug = new();
    private BotDebugView _botDebugView = null!;
    private readonly ExtractedPlayer _extractedPlayer = new();
    private readonly PmcNavigator _navigator = new();
    private readonly SpectatorPhysics _physics = new();
    private readonly PhysicsDiagnostics _physicsDiagnostics = new();
    private bool _physicsFailureLogged;
    private bool _physicsDiagnosticsFailed;
    private Action<string> _physicsInfo = null!, _physicsWarning = null!;
    private readonly Harmony _harmony = new(Id);
    private SpectatorSettings _settings = null!;
    private LocalGame? _game;
    private Player? _player;
    private GameWorld? _world;
    private FieldInfo _sessionStart = null!;
    private Func<bool>? _configurationVisible;
    private bool _menuOpen, _preparedEnd, _quitting, _cameraActive, _lastAutoBotDebug, _botDebugReady;
    private bool CameraWatching => _session.Watching && _cameraActive;
    private bool Extracted => _session.Extracted && _player != null;
    private ExitStatus SavedResult => _session.Outcome == WatchOutcome.Survived ? ExitStatus.Survived :
        _session.Outcome == WatchOutcome.Runner ? ExitStatus.Runner : ExitStatus.Killed;
    private GUIStyle? _titleStyle, _textStyle, _buttonStyle, _toggleStyle;
    private Texture2D? _panelTexture;
    private Vector2 _menuScroll;
    private string _botDebugStatus = "BotDebug is not installed.";
    private static readonly string[] TargetLabels = { "PMCs only", "All bots" };

    private void Awake()
    {
        _physicsInfo = message => Logger.LogInfo(message);
        _physicsWarning = message => Logger.LogWarning(message);
        _botDebugView = new BotDebugView(_physicsInfo, _physicsWarning);
        try
        {
            if (SpectatorSettings.ImportLegacyConfig(Config))
                Logger.LogInfo("Imported Raid Spectator settings into com.chazut.horizon.cfg. Original file preserved.");
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Could not import Raid Spectator settings; original file preserved: {e.Message}");
        }
        _settings = new SpectatorSettings(Config);
        if (Chainloader.PluginInfos.ContainsKey("com.fika.core"))
        {
            Logger.LogError("Fika is incompatible with HORIZON. No patches applied.");
            enabled = false;
            return;
        }
        try
        {
            _sessionStart = AccessTools.Field(typeof(BaseLocalGame<EftGamePlayerOwner>), "SessionStartTime")
                ?? throw new MissingFieldException("BaseLocalGame.SessionStartTime");
            if (_sessionStart.FieldType != typeof(DateTime)) throw new InvalidOperationException("Unexpected raid clock type");
            if (Chainloader.PluginInfos.TryGetValue(KillcamBridge.PluginId, out var killcam))
            {
                _killcam.Bind(killcam.Instance.GetType());
                Logger.LogInfo("Killcam detected. Freecam will start after its replay and live tail finish.");
            }
            if (Chainloader.PluginInfos.TryGetValue(BotDebugBridge.PluginId, out var botDebug))
            {
                try
                {
                    var assembly = botDebug.Instance.GetType().Assembly;
                    _botDebug.Bind(assembly);
                    BotDebugHooks.Install(assembly, _botDebugView);
                    _botDebugReady = true;
                }
                catch (Exception e)
                {
                    _botDebugStatus = "BotDebug integration unavailable. Check the game log.";
                    Logger.LogWarning($"BotDebug integration unavailable; spectating remains enabled: {e.Message}");
                }
            }
            // Optional: allow F12 configuration without fighting over its mouse cursor.
            if (Chainloader.PluginInfos.TryGetValue("com.bepis.bepinex.configurationmanager", out var manager))
            {
                var getter = AccessTools.PropertyGetter(manager.Instance.GetType(), "DisplayingWindow");
                if (getter != null && getter.ReturnType == typeof(bool))
                    _configurationVisible = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), manager.Instance, getter);
            }
            _instance = this;
            Patch(typeof(BaseLocalGame<EftGamePlayerOwner>), "InitiateGameStopping", nameof(DeathPrefix));
            Patch(typeof(LocalGame), "Stop", nameof(StopPrefix));
            Patch(typeof(BaseLocalGame<EftGamePlayerOwner>), "Stop", nameof(StopPrefix));
            Patch(typeof(Player), "OnGameSessionEnd", nameof(PlayerEndPrefix));
            Patch(typeof(FreecamController), "Update", nameof(FreecamInputPrefix));
            Patch(typeof(FreecamController), "get_IsFreecamActive", nameof(FreecamActivePrefix));
            Patch(typeof(Player), "ManualUpdate", nameof(PlayerTickPrefix));
            // World physics must keep receiving transform updates with no active
            // local player, including while waiting for Killcam and in the menu.
            foreach (var name in new[] { "Update", "FixedUpdate" })
                _harmony.Patch(AccessTools.DeclaredMethod(typeof(PhysicsExtensions), name) ?? throw new MissingMethodException("PhysicsExtensions." + name),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(PhysicsTickPostfix)));
            Patch(typeof(ActiveHealthController), "ManualUpdate", nameof(HealthTickPrefix));
            Patch(typeof(ActiveHealthController), "ApplyDamage", nameof(DamagePrefix));
            _harmony.Patch(AccessTools.DeclaredMethod(typeof(Terkoiz.Freecam.Freecam), "Update") ?? throw new MissingMethodException("Freecam.Update"),
                transpiler: new HarmonyMethod(typeof(Plugin), nameof(FreecamAxisTranspiler)));
            Logger.LogInfo("Ready. Solo only; Freecam 1.4.8 required. Spectator menu: Escape.");
        }
        catch (Exception e)
        {
            _harmony.UnpatchSelf();
            BotDebugHooks.Remove();
            _instance = null;
            Logger.LogError($"Compatibility check failed; normal raid endings remain enabled: {e}");
            enabled = false;
        }
    }

    private void Patch(Type type, string targetName, string prefixName)
    {
        var target = AccessTools.DeclaredMethod(type, targetName)
            ?? throw new MissingMethodException(type.FullName, targetName);
        _harmony.Patch(target, prefix: new HarmonyMethod(typeof(Plugin), prefixName) { priority = Priority.First });
    }

    private bool TryWatch(BaseLocalGame<EftGamePlayerOwner> candidate, ExitStatus result = ExitStatus.Killed, string? exitName = null)
    {
        if (_session.Watching && ReferenceEquals(_game, candidate)) return true;
        if (!_settings.Enabled.Value || _session.Phase != SessionPhase.Idle ||
            candidate.GetType() != typeof(LocalGame) || candidate.Status != GameStatus.Started ||
            Chainloader.PluginInfos.ContainsKey("com.fika.core")) return false;
        var game = (LocalGame)candidate;
        bool extracting = result == ExitStatus.Survived || result == ExitStatus.Runner;
        if ((result != ExitStatus.Killed && !extracting) ||
            (extracting ? !_settings.AfterExtraction.Value : !_settings.AfterDeath.Value)) return false;
        var player = game.LocalPlayer;
        if (player == null || !player.IsYourPlayer || player.IsAI || player.HealthController == null ||
            player.HealthController.IsAlive != extracting || game.GameWorld == null || game.ProfileId != player.ProfileId) return false;
        try
        {
            var completionTime = DateTimeExtensions.Now;
            var start = (DateTime)_sessionStart.GetValue(game);
            if (!FreecamBridge.CanAttach(game))
            {
                Logger.LogWarning("Freecam is not ready; keeping the normal death/exit flow.");
                return false;
            }
            var outcome = result == ExitStatus.Survived ? WatchOutcome.Survived : result == ExitStatus.Runner ? WatchOutcome.Runner : WatchOutcome.Killed;
            if (!_session.Begin(true, true, true, player.ProfileId, game.PastTime, start, completionTime, outcome, exitName))
            {
                return false;
            }
            _game = game;
            _player = player;
            _world = game.GameWorld;
            _menuOpen = false;
            _preparedEnd = false;
            _cameraActive = false;
            _menuScroll = Vector2.zero;
            _navigator.SetFilter(_settings.Targets.Value);
            if (extracting) _extractedPlayer.Remove(_world, player);
            MaintainPhysics();
            StartCoroutine(AfterDeathDispatch());
            Logger.LogInfo($"Spectator entry: profile={player.ProfileId}, result={result}, exit={exitName ?? "none"}, played={_session.PlayedSeconds:F1}s. Native save deferred until Leave raid/timer.");
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError($"Could not start Freecam; keeping the normal death/exit flow: {e}");
            Reset(rollback: true);
            return false;
        }
    }

    private IEnumerator AfterDeathDispatch()
    {
        var world = _world;
        // Killcam starts from an OnDead postfix, after our native death callback.
        // Leave its preparation, playback, live tail and cleanup entirely in charge.
        yield return null;
        float deadline = Time.realtimeSinceStartup + 120f;
        bool loggedWait = false;
        while (_session.Watching && ReferenceEquals(world, _world))
        {
            bool busy = false;
            if (!_session.Extracted && !TryReadKillcam(out busy)) yield break;
            if (!busy) break;
            if (!loggedWait)
            {
                Logger.LogInfo("Waiting for Killcam to release the camera; raid stays running.");
                loggedWait = true;
            }
            if (Time.realtimeSinceStartup >= deadline)
            {
                Logger.LogError("Killcam did not finish within 120s; returning to the normal Killed exit instead of taking its camera.");
                LeaveRaid("killcam-timeout");
                yield break;
            }
            yield return null;
        }
        if (!_session.Watching || !ReferenceEquals(world, _world) || _game == null) yield break;
        try
        {
            if (!_camera.Attach(_game)) throw new InvalidOperationException("Freecam camera disappeared during the handoff");
            _cameraActive = true;
            _botDebugView.Attach(_world!, _player!, _camera.Camera!);
            _camera.SetMenu(false);
            _lastAutoBotDebug = _settings.AutoBotDebug.Value;
            if (_botDebugReady)
            {
                _botDebug.BeginSession();
                if (_lastAutoBotDebug) SetBotDebug(true);
            }
            Logger.LogInfo($"Freecam active after {(_session.Extracted ? "extraction" : loggedWait ? "Killcam" : "death callbacks")}. Targets={_settings.Targets.Value}, wheel={_settings.CycleBots.Value}, BotDebug={_botDebugReady && _botDebug.Enabled}. Escape: spectator menu.");
        }
        catch (Exception e) { Logger.LogError($"Could not take over the spectator camera: {e}"); LeaveRaid("camera-error"); }
    }

    private bool TryReadKillcam(out bool busy)
    {
        try { busy = _killcam.IsBusy(); return true; }
        catch (Exception e)
        {
            busy = false;
            Logger.LogError($"Could not read Killcam state; preserving the normal death exit: {e}");
            LeaveRaid("killcam-error");
            return false;
        }
    }

    private void Update()
    {
        if (_session.Phase == SessionPhase.Idle) return;
        var world = Singleton<GameWorld>.Instance;
        if (_game == null || _world == null || !ReferenceEquals(world, _world) || _game.Status == GameStatus.Stopped)
        {
            Reset();
            return;
        }
        if (!_session.Watching) return;
        MaintainPhysics();
        if (!_physicsDiagnosticsFailed)
        {
            try { _physicsDiagnostics.Observe(world, _physics, _physicsInfo, _physicsWarning); }
            catch (Exception e)
            {
                _physicsDiagnosticsFailed = true;
                Logger.LogWarning($"Spectator physics diagnostics unavailable; synchronization remains enabled: {e}");
            }
        }
        if (!_settings.Enabled.Value || (_cameraActive && !_camera.Ready))
        {
            LeaveRaid(_settings.Enabled.Value ? "camera-lost" : "disabled");
            return;
        }
        if (!_cameraActive) return;
        if (!ConfigurationIsOpen() && (Input.GetKeyDown(KeyCode.Escape) || _settings.MenuKey.Value.IsDown())) _menuOpen = !_menuOpen;
        // Apply immediately, before the dependency's Update can process a menu click as camera movement.
        _camera.SetMenu(_menuOpen || ConfigurationIsOpen());
        _navigator.SetFilter(_settings.Targets.Value);
        if (!_menuOpen && !ConfigurationIsOpen() && _settings.CycleBots.Value)
        {
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel != 0) VisitBot((wheel > 0 ? 1 : -1) * (_settings.ReverseWheel.Value ? -1 : 1));
        }
        // A menu/F12 BotDebug toggle must not be undone on the next frame.
        // Only an explicit change to the automatic preference applies it again.
        if (_settings.AutoBotDebug.Value != _lastAutoBotDebug)
        {
            _lastAutoBotDebug = _settings.AutoBotDebug.Value;
            SetBotDebug(_lastAutoBotDebug);
        }
    }

    private void LateUpdate()
    {
        if (CameraWatching) _camera.SetMenu(_menuOpen || ConfigurationIsOpen());
    }

    private bool ConfigurationIsOpen()
    {
        try { return _configurationVisible?.Invoke() ?? false; }
        catch { _configurationVisible = null; return false; }
    }

    private void LeaveRaid(string reason)
    {
        if (_game == null || !_session.RequestExit()) return;
        Logger.LogInfo($"Leaving spectator mode: reason={reason}, result={SavedResult}, played={_session.PlayedSeconds:F1}s.");
        _game.Stop(_session.ProfileId, SavedResult, _session.ExitName, 0f);
    }

    private void PrepareEnd()
    {
        if (_preparedEnd || _game == null) return;
        // GameEnd derives profile playTime and its result callback from this clock. Do this only
        // once ending begins, so spectating never changes the live raid timer or bot timelines.
        _preparedEnd = true;
        _botDebugView.Clear();
        StopBotDebug();
        Logger.LogInfo($"Spectator physics stopped: syncs={_physics.Count}, meanMs={(_physics.Count > 0 ? _physics.TotalMs / _physics.Count : 0):F3}, maxMs={_physics.MaxMs:F3}.");
        try { _sessionStart.SetValue(_game, _session.StartForSave(DateTimeExtensions.Now)); }
        catch (Exception e)
        {
            // A clock/visual compatibility problem must never prevent native profile saving.
            Logger.LogError($"Could not remove spectator time from the result clock; native shutdown will continue: {e}");
        }
        try { _camera.Release(ending: true); }
        catch (Exception e) { Logger.LogError($"Could not fully restore the camera; native shutdown will continue: {e}"); }
    }

    private void Reset(bool rollback = false)
    {
        StopBotDebug();
        try { _extractedPlayer.Release(rollback); }
        catch (Exception e) { Logger.LogWarning($"Extracted player cleanup: {e.Message}"); }
        _navigator.Reset();
        _botDebugView.Clear();
        _physics.Reset();
        _physicsDiagnostics.Reset();
        _physicsFailureLogged = false;
        _physicsDiagnosticsFailed = false;
        try { _camera.Release(); }
        catch (Exception e) { Logger.LogWarning($"Spectator cleanup: {e.Message}"); }
        _session.Reset();
        _game = null;
        _world = null;
        _player = null;
        _preparedEnd = false;
        _menuOpen = false;
        _cameraActive = false;
    }

    private static bool DeathPrefix(BaseLocalGame<EftGamePlayerOwner> __instance) =>
        _instance == null || !_instance.TryWatch(__instance);

    private static bool StopPrefix(BaseLocalGame<EftGamePlayerOwner> __instance,
        string profileId, ref ExitStatus exitStatus, ref string exitName, ref float delay)
    {
        var plugin = _instance;
        if (plugin == null) return true;
        bool completed = exitStatus == ExitStatus.Killed || exitStatus == ExitStatus.Survived || exitStatus == ExitStatus.Runner;
        if (completed && profileId == __instance.ProfileId &&
            plugin._session.Phase == SessionPhase.Idle && plugin.TryWatch(__instance, exitStatus, exitName)) return false;
        var decision = plugin._session.DecideStop(ReferenceEquals(plugin._game, __instance), profileId, completed);
        if (decision == StopDecision.Defer) return false;
        if (decision == StopDecision.Captured)
        {
            if (!plugin._preparedEnd) plugin.Logger.LogInfo($"Completing native raid shutdown: requested={exitStatus}, saved={plugin.SavedResult}.");
            exitStatus = plugin.SavedResult;
            exitName = plugin._session.ExitName!;
            delay = 0f;
            plugin.PrepareEnd();
        }
        return true;
    }

    private static void PlayerEndPrefix(Player __instance, ref ExitStatus exitStatus, ref float pastTime, ref string exitName)
    {
        var plugin = _instance;
        if (plugin == null || plugin._session.Phase != SessionPhase.Ending || !ReferenceEquals(plugin._player, __instance)) return;
        exitStatus = plugin.SavedResult;
        exitName = plugin._session.ExitName!;
        pastTime = plugin._session.PlayedSeconds;
        plugin.Logger.LogInfo($"Player result: {exitStatus} at {pastTime:F1}s; spectator time excluded from player statistics.");
    }

    private void SetBotDebug(bool enabled)
    {
        if (!_botDebugReady) return;
        try
        {
            if (enabled) _botDebug.Start(); else _botDebug.Disable();
            Logger.LogInfo($"BotDebug spectator overlay {(enabled ? "enabled" : "disabled")}.");
        }
        catch (Exception e) { Logger.LogWarning($"Could not change BotDebug overlay: {e.Message}"); }
    }

    private void StopBotDebug()
    {
        try { _botDebug.Stop(); }
        catch (Exception e) { Logger.LogWarning($"Could not disable BotDebug: {e.Message}"); }
    }

    private static bool PlayerTickPrefix(Player __instance)
    {
        // Guarantee the first AI movement query sees synchronized colliders even
        // if its Update runs before AbstractApplication.Update. One sync per tick,
        // not per bot. Native sleeping/disabled bots remain untouched.
        if (__instance.IsAI) _instance?.MaintainPhysics();
        return _instance?.Extracted != true || !ReferenceEquals(_instance._player, __instance);
    }

    private static void PhysicsTickPostfix() => _instance?.MaintainPhysics();

    private void MaintainPhysics()
    {
        if (!_session.Watching || _world == null || !ReferenceEquals(_world, Singleton<GameWorld>.Instance) ||
            _game == null || _game.Status != GameStatus.Started) return;
        try
        {
            _physics.Sync(true, Physics.autoSyncTransforms, Time.frameCount, Time.inFixedTimeStep, Time.fixedTime, Physics.SyncTransforms);
        }
        catch (Exception e)
        {
            if (!_physicsFailureLogged) Logger.LogError($"Spectator physics synchronization failed: {e}");
            _physicsFailureLogged = true;
        }
    }

    private static bool HealthTickPrefix(ActiveHealthController __instance) =>
        _instance?.Extracted != true || !ReferenceEquals(_instance._player?.ActiveHealthController, __instance);

    private static bool DamagePrefix(ActiveHealthController __instance, ref float __result)
    {
        if (HealthTickPrefix(__instance)) return true;
        __result = 0;
        return false;
    }

    private static IEnumerable<CodeInstruction> FreecamAxisTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var read = AccessTools.Method(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) });
        var replacement = AccessTools.Method(typeof(Plugin), nameof(ReadFreecamAxis));
        int count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(read)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++; }
            yield return instruction;
        }
        if (count == 0) throw new InvalidOperationException("Freecam input API changed; spectator wheel interception unavailable");
    }

    private static float ReadFreecamAxis(string axis) => axis == "Mouse ScrollWheel" &&
        _instance?.CameraWatching == true && (_instance._settings.CycleBots.Value || _instance._menuOpen || _instance.ConfigurationIsOpen()) ? 0f : Input.GetAxis(axis);

    private void VisitBot(int direction)
    {
        if (!CameraWatching || _world == null) return;
        try
        {
            _navigator.SetFilter(_settings.Targets.Value);
            _navigator.Move(_world, _camera, direction, _settings.VisitDistance.Value);
        }
        catch (Exception e) { Logger.LogWarning($"Could not visit bot; Freecam remains available: {e.Message}"); }
    }

    private static bool FreecamInputPrefix(FreecamController __instance) =>
        _instance == null || !_instance.CameraWatching || !ReferenceEquals(_instance._camera.Controller, __instance);

    private static bool FreecamActivePrefix(FreecamController __instance, ref bool __result)
    {
        if (_instance == null || !_instance.CameraWatching || !ReferenceEquals(_instance._camera.Controller, __instance)) return true;
        // Opening our menu pauses Freecam input, not camera ownership (or ForceSetPosition
        // would snap the camera back to the corpse).
        __result = true;
        return false;
    }

    private void OnGUI()
    {
        if (!CameraWatching || ConfigurationIsOpen() || (!_menuOpen && !_settings.ShowHelp.Value)) return;
        if (_titleStyle == null)
        {
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _textStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            _toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 13, wordWrap = true };
            _panelTexture = new Texture2D(1, 1);
            _panelTexture.SetPixel(0, 0, new Color(0.055f, 0.07f, 0.09f, 0.94f));
            _panelTexture.Apply();
        }
        float width = Mathf.Min(_menuOpen ? 400 : 330, Screen.width - 32);
        var rect = new Rect(Screen.width - width - 16, 20, width, Mathf.Min(_menuOpen ? 500 : 92, Screen.height - 40));
        int oldDepth = GUI.depth;
        GUI.depth = -100;
        GUI.DrawTexture(rect, _panelTexture);
        GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 12, rect.width - 32, rect.height - 20));
        GUILayout.Label("HORIZON", _titleStyle);
        if (_menuOpen)
        {
            _menuScroll = GUILayout.BeginScrollView(_menuScroll);
            GUILayout.Label(_session.Extracted ? "You extracted. The raid continues." : "You died. The raid continues.", _textStyle);
            GUILayout.Label($"Leave raid to save your original {SavedResult} result.", _textStyle);
            GUILayout.Space(12);
            GUILayout.Label("Visit a bot", _titleStyle);
            int filter = GUILayout.SelectionGrid(_settings.Targets.Value == BotFilter.AllBots ? 1 : 0, TargetLabels, 2, _buttonStyle, GUILayout.Height(30));
            _settings.Targets.Value = filter == 1 ? BotFilter.AllBots : BotFilter.PmcOnly;
            _navigator.SetFilter(_settings.Targets.Value);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous", _buttonStyle, GUILayout.Height(30))) VisitBot(-1);
            if (GUILayout.Button("Next", _buttonStyle, GUILayout.Height(30))) VisitBot(1);
            GUILayout.EndHorizontal();
            GUILayout.Label(_navigator.Caption, _textStyle);
            _settings.CycleBots.Value = GUILayout.Toggle(_settings.CycleBots.Value, "Switch bots with the mouse wheel", _toggleStyle);
            GUILayout.Space(10);
            if (_botDebugReady)
            {
                bool overlay = GUILayout.Toggle(_botDebug.Enabled, "BotDebug overlay (this raid)", _toggleStyle);
                if (overlay != _botDebug.Enabled) SetBotDebug(overlay);
            }
            else GUILayout.Label(_botDebugStatus, _textStyle);
            _settings.ShowHelp.Value = GUILayout.Toggle(_settings.ShowHelp.Value, "Show help while moving the camera", _toggleStyle);
            GUILayout.Label("More settings: F12 > HORIZON. Movement: Freecam controls.", _textStyle);
            GUILayout.EndScrollView();
            GUILayout.Space(10);
            if (GUILayout.Button("Resume Freecam", _buttonStyle, GUILayout.Height(34))) { _menuOpen = false; _camera.SetMenu(false); }
            if (GUILayout.Button("Leave raid", _buttonStyle, GUILayout.Height(34))) LeaveRaid("button");
        }
        else
        {
            GUILayout.Label("Spectating | Esc: menu / Leave raid", _textStyle);
            if (_settings.CycleBots.Value) GUILayout.Label(_navigator.Caption, _textStyle);
        }
        GUILayout.EndArea();
        GUI.depth = oldDepth;
    }

    private void OnApplicationQuit() => _quitting = true;

    private void OnDestroy()
    {
        if (!_quitting && _session.Watching && _game != null) LeaveRaid("plugin-unload");
        Reset();
        _harmony.UnpatchSelf();
        BotDebugHooks.Remove();
        if (_panelTexture != null) Destroy(_panelTexture);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
