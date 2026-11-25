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
using UnityEngine;

namespace Horizon;

[BepInPlugin(Id, "HORIZON", "1.4.0")]
[BepInDependency("com.terkoiz.freecam", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(KillcamBridge.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(BotDebugBridge.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(DynamicMapsHooks.PluginId, BepInDependency.DependencyFlags.SoftDependency)]
[BepInIncompatibility("com.chazut.raidspectator")]
public sealed partial class Plugin : BaseUnityPlugin
{
    public const string Id = "com.chazut.horizon";
    private static Plugin? _instance;
    private readonly SpectatorSession _session = new();
    private readonly FreecamBridge _camera = new();
    private readonly SpectatorFootsteps _footsteps = new();
    private readonly KillcamBridge _killcam = new();
    private readonly BotDebugBridge _botDebug = new();
    private BotDebugView _botDebugView = null!;
    private readonly ExtractedPlayer _extractedPlayer = new();
    private readonly ExtractedWeaponView _extractedWeapon = new();
    private Player? FollowedActor => _navigator.FollowedTarget ?? (_camera.Script != null ? _fika?.Selection(_camera.Script, followingOnly: true) : null);
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
    private bool _menuOpen, _preparedEnd, _quitting, _cameraActive, _botDebugReady, _integrationsBound;
    private readonly BotRoster _roster = new();
    private readonly OverlaySelection _overlay = new();
    private SpectatorOverlay _lastOverlay;
    private OptionalBotData _botData = null!;
    private BotLabels _labels = null!;
    private FikaSpectator? _fika;
    private FikaBotFeed? _fikaFeed;
    private SpectatorMapHud? _mapHud;
    private DynamicMiniMapDrag? _mapDrag;
    private bool _botToolsEnabled = true; // Solo permits bot spectating; Fika sets this on camera attachment.
    private bool CanUseBotDebug => _botToolsEnabled && _botDebugReady && (_fika == null || _fika.IsHost());
    private bool CameraWatching => _cameraActive && (_fika != null || _session.Watching);
    private bool Extracted => _session.Extracted && _player != null;
    private ExitStatus SavedResult => _session.Outcome == WatchOutcome.Survived ? ExitStatus.Survived :
        _session.Outcome == WatchOutcome.Runner ? ExitStatus.Runner : ExitStatus.Killed;
    private GUIStyle? _titleStyle, _textStyle, _buttonStyle, _toggleStyle;
    private GUIStyle? _verticalScrollbar, _horizontalScrollbar;
    private float _interfaceScale;
    private Texture2D? _panelTexture;
    private Vector2 _menuScroll;
    private int _dragControl;
    private Vector2 _dragOrigin, _dragPanel;
    private Vector2? _dragPosition;
    private bool CameraMenu => _menuOpen || ConfigurationIsOpen() || Input.GetKey(KeyCode.LeftAlt);
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
        _botData = new OptionalBotData(_physicsWarning);
        _labels = new BotLabels(ReadBotLabel, _physicsWarning);
        try
        {
            if (Chainloader.PluginInfos.TryGetValue("com.fika.core", out var fika))
            {
                // Fika polls its network on the Unity thread. The feed also runs on a
                // headless process, where the spectator component itself stays disabled.
                try
                {
                    _fikaFeed = FikaBotFeed.Load(ReadAuthorityLabel, _physicsInfo, _physicsWarning);
                    _roster.ReadHostSquad = _fikaFeed.ReadSquad;
                    Logger.LogInfo("Fika bot action sync ready.");
                }
                catch (Exception e) { Logger.LogWarning("Fika bot action sync unavailable: " + e.GetBaseException().Message); }
                if (Application.isBatchMode || FikaSpectator.IsHeadless(fika.Instance.GetType().Assembly))
                {
                    enabled = false;
                    return;
                }
                _fika = new FikaSpectator(fika.Instance);
            }
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
            if (Chainloader.PluginInfos.TryGetValue(DynamicMapsHooks.PluginId, out var maps))
            {
                try
                {
                    var assembly = maps.Instance.GetType().Assembly;
                    var peekType = assembly.GetType("DynamicMaps.UI.Components.MapPeekComponent", true)!;
                    DynamicMapsHooks.Install(assembly, MapObserver);
                    _mapHud = new SpectatorMapHud(() => {
                        var screen = Singleton<EFT.UI.CommonUI>.Instance?.EftBattleUIScreen;
                        return screen != null && !screen.gameObject.activeInHierarchy
                            ? screen.GetComponentInChildren(peekType, true)?.transform : null;
                    });
                    Logger.LogInfo("DynamicMaps spectator tracking ready.");
                    try { _mapDrag = new DynamicMiniMapDrag(maps.Instance); }
                    catch (Exception e) { Logger.LogWarning("DynamicMaps mini-map dragging unavailable: " + e.Message); }
                }
                catch (Exception e) { Logger.LogWarning("DynamicMaps spectator tracking unavailable: " + e); }
            }
            // Optional: allow F12 configuration without fighting over its mouse cursor.
            if (Chainloader.PluginInfos.TryGetValue("com.bepis.bepinex.configurationmanager", out var manager))
            {
                var getter = AccessTools.PropertyGetter(manager.Instance.GetType(), "DisplayingWindow");
                if (getter != null && getter.ReturnType == typeof(bool))
                    _configurationVisible = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), manager.Instance, getter);
            }
            _instance = this;
            if (_fika != null)
            {
                InstallFikaCamera();
                Logger.LogInfo("Ready for Fika's spectator camera.");
                return;
            }
            if (!Chainloader.PluginInfos.TryGetValue("com.terkoiz.freecam", out var freecam) || freecam.Metadata.Version < new System.Version(1, 4, 8))
                throw new InvalidOperationException("Solo spectating requires Terkoiz Freecam 1.4.8 or later.");
            FreecamBridge.BindSolo(freecam.Instance.GetType());
            _sessionStart = AccessTools.Field(typeof(BaseLocalGame<EftGamePlayerOwner>), "SessionStartTime")
                ?? throw new MissingFieldException("BaseLocalGame.SessionStartTime");
            if (_sessionStart.FieldType != typeof(DateTime)) throw new InvalidOperationException("Unexpected raid clock type");
            Patch(typeof(BaseLocalGame<EftGamePlayerOwner>), "InitiateGameStopping", nameof(DeathPrefix));
            Patch(typeof(LocalGame), "Stop", nameof(StopPrefix));
            Patch(typeof(BaseLocalGame<EftGamePlayerOwner>), "Stop", nameof(StopPrefix));
            Patch(typeof(Player), "OnGameSessionEnd", nameof(PlayerEndPrefix));
            Patch(FreecamBridge.SoloControllerType, "Update", nameof(FreecamInputPrefix));
            Patch(FreecamBridge.SoloControllerType, "get_IsFreecamActive", nameof(FreecamActivePrefix));
            Patch(typeof(Player), "ManualUpdate", nameof(PlayerTickPrefix));
            // World physics must keep receiving transform updates with no active
            // local player, including while waiting for Killcam and in the menu.
            foreach (var name in new[] { "Update", "FixedUpdate" })
                _harmony.Patch(AccessTools.DeclaredMethod(typeof(PhysicsExtensions), name) ?? throw new MissingMethodException("PhysicsExtensions." + name),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(PhysicsTickPostfix)));
            Patch(typeof(ActiveHealthController), "ManualUpdate", nameof(HealthTickPrefix));
            Patch(typeof(ActiveHealthController), "ApplyDamage", nameof(DamagePrefix));
            _harmony.Patch(AccessTools.DeclaredMethod(FreecamBridge.SoloScriptType, "Update") ?? throw new MissingMethodException("Freecam.Update"),
                transpiler: new HarmonyMethod(typeof(Plugin), nameof(FreecamAxisTranspiler)));
            Logger.LogInfo("Ready for solo raids. Spectator menu: Escape.");
        }
        catch (Exception e)
        {
            _harmony.UnpatchSelf();
            BotDebugHooks.Remove();
            if (DynamicMapsHooks.Installed) DynamicMapsHooks.Remove();
            _instance = null;
            Logger.LogError($"Compatibility check failed; normal raid endings remain enabled: {e}");
            enabled = false;
        }
    }

    private (Camera?, Player?) MapObserver(IPlayer player)
    {
        if (!CameraWatching || _quitting || !_settings.DynamicMapsTracking.Value || _world == null ||
            !ReferenceEquals(player, _world.MainPlayer) || !isActiveAndEnabled || !ReferenceEquals(_world, Singleton<GameWorld>.Instance))
            return default;
        var camera = _camera.Camera;
        if (camera == null || !camera.isActiveAndEnabled) return default;
        return (camera, DynamicMapsHooks.EligibleTarget(_world, FollowedActor, _botToolsEnabled));
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
            candidate.GetType() != typeof(LocalGame) || candidate.Status != GameStatus.Started || _fika != null) return false;
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
            try { _footsteps.Begin(_harmony, loggedWait); }
            catch (Exception e) { Logger.LogWarning($"Spectator footstep diagnostics unavailable: {e.GetBaseException().Message}"); }
            _botDebugView.Attach(_world!, _player!, _camera.Camera!);
            _camera.SetMenu(false);
            BindBotInformation();
            if (_botDebugReady) _botDebug.BeginSession();
            _lastOverlay = _settings.Overlay.Value;
            ApplyOverlay(_lastOverlay);
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
        if (_fika != null)
        {
            if (_cameraActive && (!_camera.Ready || _world == null || _world != Singleton<GameWorld>.Instance)) Reset();
            return; // Fika's camera Update supplies the active local view.
        }
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
        UpdateCamera(world);
    }

    private void UpdateCamera(GameWorld world)
    {
        bool menuPressed = _fika != null && _settings.MenuKey.Value.MainKey == KeyCode.Escape
            ? Input.GetKeyDown(KeyCode.F6) : _settings.MenuKey.Value.IsDown();
        if (!ConfigurationIsOpen() && (menuPressed || (_fika == null && Input.GetKeyDown(KeyCode.Escape)))) _menuOpen = !_menuOpen;
        // Apply immediately, before the dependency's Update can process a menu click as camera movement.
        _camera.SetMenu(CameraMenu, _navigator.Following);
        if (_botToolsEnabled) _navigator.SetFilter(_settings.Targets.Value);
        if (!_menuOpen && !ConfigurationIsOpen() && _settings.FollowKey.Value.IsDown()) ToggleFollow();
        if (_botToolsEnabled || _navigator.Following)
        {
            if (!CameraMenu && _navigator.Following && _settings.OrientationKey.Value.IsDown()) _navigator.ToggleOrientation();
            if (!CameraMenu) _navigator.Rotate(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"), _settings.OrbitSensitivity.Value);
        }
        var wheelAction = WheelAction();
        if (wheelAction == SpectatorWheelAction.Zoom || wheelAction == SpectatorWheelAction.Cycle)
        {
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel != 0)
            {
                if (wheelAction == SpectatorWheelAction.Cycle) VisitBot((wheel > 0 ? 1 : -1) * (_settings.ReverseWheel.Value ? -1 : 1));
                else if (_navigator.Following) _navigator.Zoom(wheel, _settings.ZoomSpeed.Value, _settings.VisitDistance.Value);
                else _camera.Zoom(wheel, _settings.ZoomSpeed.Value);
            }
        }
        // A manual BotDebug toggle remains active until the overlay choice changes.
        if (_settings.Overlay.Value != _lastOverlay)
        {
            _lastOverlay = _settings.Overlay.Value;
            ApplyOverlay(_lastOverlay);
        }
        if (CanUseBotDebug) _overlay.ObserveBotDebug(_botDebug.Enabled);
        if (!CameraMenu && _settings.BotInfoKey.Value.IsDown())
        {
            _overlay.ToggleBotInfo();
            SyncOverlay();
        }
        if ((_botToolsEnabled && _settings.ShowBotList.Value) || _overlay.Mode == SpectatorOverlay.BotInfo)
            _roster.Refresh(world, Time.unscaledTime, _fika != null && _settings.ShowHumanLabels.Value && _overlay.Mode == SpectatorOverlay.BotInfo, _botToolsEnabled);
        if (_botToolsEnabled && _settings.ShowBotList.Value && _fika != null && !_fika.IsHost())
            _fikaFeed?.PollRoster(_roster, world, Time.unscaledTime);
        if (_overlay.Mode == SpectatorOverlay.BotInfo && _camera.Camera != null)
            _labels.Update(_roster, world, _camera.Camera, Time.unscaledTime, _settings.EffectiveLabelDistance, _settings.ShowOccludedBots.Value, _settings.VisibleLabelFields, FollowedActor, _settings.LabelRows);
    }

    private void LateUpdate()
    {
        if (!CameraWatching) return;
        if (_mapHud != null)
        {
            try { _mapHud.Update(_settings.DynamicMapsTracking.Value, Time.unscaledTime); }
            catch (Exception e)
            {
                Logger.LogWarning("Could not display DynamicMaps over the hidden HUD: " + e.Message);
                _mapHud.Restore();
                _mapHud = null;
            }
        }
        _camera.SetSpectatorFov(_settings.UseSpectatorFov.Value, _settings.SpectatorFov.Value);
        var extracted = _fikaExtracted ? _world?.MainPlayer : Extracted ? _player : null;
        _extractedWeapon.Update(_settings.HideExtractedWeapon.Value ? extracted : null);
        _camera.MaintainEffects();
        FollowTransition transition = FollowTransition.None;
        if ((_botToolsEnabled || _navigator.Following) && _world != null)
        {
            var previous = _navigator.Caption;
            transition = _navigator.Follow(_world, _camera, _settings.VisitDistance.Value, _botToolsEnabled && _settings.AutoSwitchOnDeath.Value);
            if (transition != FollowTransition.None)
                Logger.LogInfo($"Follow target lost: {transition}, previous={previous}, autoSwitch={_settings.AutoSwitchOnDeath.Value}, target={_navigator.Caption}.");
        }
        if (_footsteps.Active)
        {
            try { _footsteps.Observe(_world!, _camera.Camera!, _navigator.FollowedTarget, transition, _physicsInfo); }
            catch (Exception e)
            {
                _footsteps.Reset();
                Logger.LogWarning($"Spectator footstep diagnostics stopped: {e.GetBaseException().Message}");
            }
        }
        _camera.SetMenu(CameraMenu, _navigator.Following);
    }

    private void ToggleFollow()
    {
        if (_world == null) return;
        var selected = _settings.UseFikaSelection.Value && _camera.Script != null ? _fika?.Selection(_camera.Script) : null;
        if (!_botToolsEnabled && !_navigator.Following &&
            (!PmcNavigator.IsLivingRemote(selected) || PmcNavigator.IsBot(selected!) || !_world.AllAlivePlayersList.Contains(selected!))) return;
        _navigator.ToggleFollow(_world, _camera, _settings.VisitDistance.Value, selected);
        Logger.LogInfo($"Camera follow: {_navigator.Following}, target={_navigator.Caption}");
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
        _mapHud?.Restore();
        _extractedWeapon.Update(null);
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
        _mapHud?.Restore();
        if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        _dragControl = 0; _dragPosition = null;
        StopBotDebug();
        try { _extractedPlayer.Release(rollback); }
        catch (Exception e) { Logger.LogWarning($"Extracted player cleanup: {e.Message}"); }
        _extractedWeapon.Update(null);
        _navigator.Reset();
        _roster.Clear();
        _labels?.Clear();
        _fikaFeed?.ClearClient();
        _botData?.Clear();
        _listPanel.Reset();
        _listScroll = Vector2.zero;
        _botDebugView.Clear();
        _physics.Reset();
        _physicsDiagnostics.Reset();
        _footsteps.Reset();
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
        _fikaExtracted = false;
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

    private static IEnumerable<CodeInstruction> FreecamAxisTranspiler(IEnumerable<CodeInstruction> instructions) =>
        ReplaceWheelInput(instructions, nameof(Input.GetAxis), nameof(ReadFreecamAxis));

    private static IEnumerable<CodeInstruction> ReplaceWheelInput(IEnumerable<CodeInstruction> instructions, string axisMethod, string reader)
    {
        var read = AccessTools.Method(typeof(Input), axisMethod, new[] { typeof(string) });
        var replacement = AccessTools.Method(typeof(Plugin), reader);
        int count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(read)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++; }
            yield return instruction;
        }
        if (count == 0) throw new InvalidOperationException("Freecam input API changed; spectator wheel interception unavailable");
    }

    private SpectatorWheelAction WheelAction()
    {
        var shortcut = _settings.ZoomKey.Value;
        bool held = shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey);
        // Movement keys may stay held while zooming. Only the configured keys
        // are required; unrelated held keys do not cancel the shortcut.
        foreach (var modifier in shortcut.Modifiers) held &= Input.GetKey(modifier);
        return SpectatorWheel.Route(CameraWatching, CameraMenu, held, _botToolsEnabled && _settings.CycleBots.Value);
    }

    private static bool ConsumeWheel(string axis) => axis == "Mouse ScrollWheel" &&
        _instance?.CameraWatching == true && _instance.WheelAction() != SpectatorWheelAction.Native;

    private static float ReadFreecamAxis(string axis) => ConsumeWheel(axis) ? 0f : Input.GetAxis(axis);

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

    private static bool FreecamInputPrefix(Behaviour __instance) =>
        _instance == null || !_instance.CameraWatching || !ReferenceEquals(_instance._camera.Controller, __instance);

    private static bool FreecamActivePrefix(Behaviour __instance, ref bool __result)
    {
        if (_instance == null || !_instance.CameraWatching || !ReferenceEquals(_instance._camera.Controller, __instance)) return true;
        // Opening our menu pauses Freecam input, not camera ownership (or ForceSetPosition
        // would snap the camera back to the corpse).
        __result = true;
        return false;
    }

    private static GUIStyle ScaledStyle(GUIStyle source, float scale, int fontSize = 0)
    {
        RectOffset Offset(RectOffset value) => new(Mathf.RoundToInt(value.left * scale), Mathf.RoundToInt(value.right * scale),
            Mathf.RoundToInt(value.top * scale), Mathf.RoundToInt(value.bottom * scale));
        return new GUIStyle(source)
        {
            fontSize = Mathf.RoundToInt((fontSize > 0 ? fontSize : source.fontSize) * scale),
            padding = Offset(source.padding), margin = Offset(source.margin), border = Offset(source.border),
            fixedWidth = source.fixedWidth * scale, fixedHeight = source.fixedHeight * scale
        };
    }

    private void OnGUI()
    {
        if (!CameraWatching || ConfigurationIsOpen()) { _mapDrag?.Cancel(); return; }
        float scale = SpectatorInterface.Scale(Screen.height, _settings.AutomaticScale.Value, _settings.InterfaceScale.Value);
        if (_titleStyle == null || _interfaceScale != scale)
        {
            _interfaceScale = scale;
            _titleStyle = ScaledStyle(GUI.skin.label, scale, 17); _titleStyle.fontStyle = FontStyle.Bold;
            _textStyle = ScaledStyle(GUI.skin.label, scale, 13); _textStyle.wordWrap = true;
            _buttonStyle = ScaledStyle(GUI.skin.button, scale, 14);
            _buttonStyle.richText = false;
            _toggleStyle = ScaledStyle(GUI.skin.toggle, scale, 13); _toggleStyle.wordWrap = true;
            _verticalScrollbar = ScaledStyle(GUI.skin.verticalScrollbar, scale);
            _horizontalScrollbar = ScaledStyle(GUI.skin.horizontalScrollbar, scale);
            _labelStyle = ScaledStyle(GUI.skin.label, scale, 13);
            _labelStyle.wordWrap = true;
            _labelStyle.richText = false;
            _labelStyle.normal.textColor = Color.white;
            _labelStyle.normal.background = null;
            _labelOutlineStyle = new GUIStyle(_labelStyle);
            _labelOutlineStyle.normal.textColor = Color.black;
            _listPanel.Reset();
            if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
            _dragControl = 0; _dragPosition = null;
        }
        if (_panelTexture == null)
        {
            _panelTexture = new Texture2D(1, 1);
            _panelTexture.SetPixel(0, 0, new Color(0.055f, 0.07f, 0.09f, 0.94f));
            _panelTexture.Apply();
        }
        if (_overlay.Mode == SpectatorOverlay.BotInfo) DrawBotLabels(scale);
        if (_botToolsEnabled && _settings.ShowBotList.Value) DrawBotList(scale);
        else _listPanel.Reset();
        _mapDrag?.Handle(CameraMenu, (_menuOpen || _settings.ShowHelp.Value) && MainPanelRect(scale).Contains(Event.current.mousePosition));
        if (!_menuOpen && !_settings.ShowHelp.Value) return;
        float marginX = 16 * scale, marginY = 20 * scale;
        float width = Mathf.Max(1, Mathf.Min((_menuOpen ? 400 : 330) * scale, Screen.width - marginX * 2));
        float height = Mathf.Max(1, Mathf.Min((_menuOpen ? 660 : 185) * scale, Screen.height - marginY * 2));
        var position = _menuOpen ? _settings.MenuPosition : _settings.HelpPosition;
        var available = new Vector2(SpectatorInterface.Available(Screen.width, width, marginX), SpectatorInterface.Available(Screen.height, height, marginY));
        var normalized = _dragPosition ?? position.Value;
        var rect = new Rect(SpectatorInterface.Position(normalized.x, available.x, marginX), SpectatorInterface.Position(normalized.y, available.y, marginY), width, height);
        int control = GUIUtility.GetControlID(_menuOpen ? 82141 : 82142, FocusType.Passive);
        if (_dragControl != 0 && (_dragControl != control || !CameraMenu))
        {
            if (GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
            _dragControl = 0;
            _dragPosition = null;
        }
        var ev = Event.current;
        if (ev.type == EventType.MouseDown && ev.button == 0 && new Rect(rect.x, rect.y, rect.width, 38 * scale).Contains(ev.mousePosition) && CameraMenu)
        {
            _dragControl = control; GUIUtility.hotControl = control;
            _dragOrigin = ev.mousePosition; _dragPanel = new Vector2(rect.x, rect.y); ev.Use();
        }
        if (_dragControl == control && GUIUtility.hotControl == control)
        {
            if (ev.type == EventType.MouseDrag)
            {
                var p = _dragPanel + ev.mousePosition - _dragOrigin;
                _dragPosition = new Vector2(SpectatorInterface.Normalize(p.x, available.x, marginX), SpectatorInterface.Normalize(p.y, available.y, marginY));
                ev.Use();
            }
            if (ev.type == EventType.MouseUp)
            {
                if (_dragPosition.HasValue) position.Value = _dragPosition.Value;
                _dragPosition = null; _dragControl = 0; GUIUtility.hotControl = 0; ev.Use();
            }
        }
        int oldDepth = GUI.depth;
        GUI.depth = -100;
        GUI.DrawTexture(rect, _panelTexture);
        GUILayout.BeginArea(new Rect(rect.x + 16 * scale, rect.y + 12 * scale, rect.width - 32 * scale, rect.height - 20 * scale));
        GUILayout.Label("HORIZON", _titleStyle);
        if (_menuOpen)
        {
            _menuScroll = GUILayout.BeginScrollView(_menuScroll, _horizontalScrollbar, _verticalScrollbar);
            GUILayout.Label(_fika != null ? "Use Fika's extract shortcut to leave the raid." :
                $"Leave raid to save your original {SavedResult} result.", _textStyle);
            GUILayout.Space(12 * scale);
            if (_botToolsEnabled)
            {
                GUILayout.Label("Visit a bot", _titleStyle);
                int filter = GUILayout.SelectionGrid(_settings.Targets.Value == BotFilter.AllBots ? 1 : 0, TargetLabels, 2, _buttonStyle, GUILayout.Height(30 * scale));
                _settings.Targets.Value = filter == 1 ? BotFilter.AllBots : BotFilter.PmcOnly;
                _navigator.SetFilter(_settings.Targets.Value);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Previous", _buttonStyle, GUILayout.Height(30 * scale))) VisitBot(-1);
                if (GUILayout.Button("Next", _buttonStyle, GUILayout.Height(30 * scale))) VisitBot(1);
                GUILayout.EndHorizontal();
                GUILayout.Label(_navigator.Caption, _textStyle);
                if (GUILayout.Button(_navigator.Following ? "Stop following" : "Follow selected target", _buttonStyle, GUILayout.Height(30 * scale))) ToggleFollow();
                if (_navigator.Following && GUILayout.Button(_navigator.LockOrientation ? "Orientation: bot view" : "Orientation: mouse orbit", _buttonStyle, GUILayout.Height(30 * scale))) _navigator.ToggleOrientation();
                if (_navigator.Following) GUILayout.Label($"{_settings.OrientationKey.Value}: toggle orientation / mouse orbit", _textStyle);
                GUILayout.Label($"{_settings.FollowKey.Value}: follow / free camera. Drag the title to move this menu.", _textStyle);
                _settings.CycleBots.Value = GUILayout.Toggle(_settings.CycleBots.Value, "Switch bots with the mouse wheel", _toggleStyle);
                _settings.AutoSwitchOnDeath.Value = GUILayout.Toggle(_settings.AutoSwitchOnDeath.Value, "Auto-switch when followed bot dies", _toggleStyle);
            }
            else GUILayout.Label("Bot spectating is disabled on this Fika server.", _textStyle);
            if (_fika != null)
                _settings.UseFikaSelection.Value = GUILayout.Toggle(_settings.UseFikaSelection.Value, "Follow Fika selection with the follow key", _toggleStyle);
            _settings.HideExtractedWeapon.Value = GUILayout.Toggle(_settings.HideExtractedWeapon.Value, "Hide my weapon after extraction", _toggleStyle);
            if (DynamicMapsHooks.Installed)
                _settings.DynamicMapsTracking.Value = GUILayout.Toggle(_settings.DynamicMapsTracking.Value, "Track spectator on DynamicMaps", _toggleStyle);
            GUILayout.Label(ZoomHint, _textStyle);
            _settings.UseSpectatorFov.Value = GUILayout.Toggle(_settings.UseSpectatorFov.Value, "Override spectator FOV", _toggleStyle);
            if (_settings.UseSpectatorFov.Value)
            {
                GUILayout.Label($"Field of view: {_settings.SpectatorFov.Value:0} degrees", _textStyle);
                _settings.SpectatorFov.Value = Mathf.Round(GUILayout.HorizontalSlider(_settings.SpectatorFov.Value, 30f, 110f));
            }
            GUILayout.Space(10 * scale);
            GUILayout.Label("Interface size", _titleStyle);
            _settings.AutomaticScale.Value = GUILayout.Toggle(_settings.AutomaticScale.Value, "Adapt to screen resolution", _toggleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Smaller", _buttonStyle, GUILayout.Height(30 * scale))) _settings.InterfaceScale.Value = Mathf.Max(.75f, _settings.InterfaceScale.Value - .1f);
            GUILayout.Label($"{_settings.InterfaceScale.Value:P0}", _textStyle);
            if (GUILayout.Button("Larger", _buttonStyle, GUILayout.Height(30 * scale))) _settings.InterfaceScale.Value = Mathf.Min(2.5f, _settings.InterfaceScale.Value + .1f);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Reset interface size", _buttonStyle, GUILayout.Height(30 * scale))) { _settings.AutomaticScale.Value = true; _settings.InterfaceScale.Value = 1; }
            GUILayout.Space(10 * scale);
            GUILayout.Label("Information overlay", _titleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_overlay.Mode == SpectatorOverlay.Off ? "[Off]" : "Off", _buttonStyle)) ChooseOverlay(SpectatorOverlay.Off);
            if (GUILayout.Button(_overlay.Mode == SpectatorOverlay.BotInfo ? "[Bot info]" : "Bot info", _buttonStyle)) ChooseOverlay(SpectatorOverlay.BotInfo);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && CanUseBotDebug;
            if (GUILayout.Button(_overlay.Mode == SpectatorOverlay.BotDebug ? "[BotDebug]" : "BotDebug", _buttonStyle)) ChooseOverlay(SpectatorOverlay.BotDebug);
            GUI.enabled = oldEnabled;
            GUILayout.EndHorizontal();
            if (_settings.BotInfoKey.Value.MainKey != KeyCode.None)
                GUILayout.Label($"{_settings.BotInfoKey.Value}: show / hide Bot info", _textStyle);
            if (_botToolsEnabled && !CanUseBotDebug) GUILayout.Label(_fika != null && !_fika.IsHost() ? "Use Bot info for replicated bots." : _botDebugStatus, _textStyle);
            if (_overlay.Mode == SpectatorOverlay.BotInfo)
            {
                if (_fika != null)
                    _settings.ShowHumanLabels.Value = GUILayout.Toggle(_settings.ShowHumanLabels.Value, "Show player labels", _toggleStyle);
                GUILayout.Label("Bot info fields", _titleStyle);
                foreach (var (field, setting) in _settings.LabelOptions)
                    if (_botToolsEnabled || (field & BotLabelFields.Human) != 0)
                    {
                        var line = _settings.LabelLines[BotInfoRows.Index(field)];
                        GUILayout.BeginHorizontal();
                        setting.Value = GUILayout.Toggle(setting.Value, setting.Definition.Key, _toggleStyle);
                        if (GUILayout.Button("-", _buttonStyle, GUILayout.Width(25 * scale))) line.Value = Mathf.Max(1, line.Value - 1);
                        GUILayout.Label("Line " + line.Value, _textStyle, GUILayout.Width(55 * scale));
                        if (GUILayout.Button("+", _buttonStyle, GUILayout.Width(25 * scale))) line.Value = Mathf.Min(BotInfoRows.Count, line.Value + 1);
                        GUILayout.EndHorizontal();
                    }
                if (_botToolsEnabled)
                    _settings.ActionsOnlyWhenFollowing.Value = GUILayout.Toggle(_settings.ActionsOnlyWhenFollowing.Value, "Show actions only for the followed bot", _toggleStyle);
                _settings.ShowOccludedBots.Value = GUILayout.Toggle(_settings.ShowOccludedBots.Value, "Show labels through scenery", _toggleStyle);
                _settings.LimitLabelDistance.Value = GUILayout.Toggle(_settings.LimitLabelDistance.Value, "Limit label distance (F12)", _toggleStyle);
                if (_botToolsEnabled) GUILayout.Label("White: Scavs | Squad colors: PMCs | Red: bosses | Orange: guards | Violet: other factions", _textStyle);
            }
            if (_botToolsEnabled) _settings.ShowBotList.Value = GUILayout.Toggle(_settings.ShowBotList.Value, "Show bot list (click to visit)", _toggleStyle);
            _settings.ShowHelp.Value = GUILayout.Toggle(_settings.ShowHelp.Value, "Show help while moving the camera", _toggleStyle);
            GUILayout.Label("More settings: F12 > HORIZON. Movement: Freecam controls.", _textStyle);
            GUILayout.EndScrollView();
            GUILayout.Space(10 * scale);
            if (GUILayout.Button("Resume camera", _buttonStyle, GUILayout.Height(34 * scale))) { _menuOpen = false; _camera.SetMenu(false, _navigator.Following); }
            if (_fika == null && GUILayout.Button("Leave raid", _buttonStyle, GUILayout.Height(34 * scale))) LeaveRaid("button");
        }
        else
        {
            var infoKey = _settings.BotInfoKey.Value;
            string menu = _fika == null ? "Esc: menu / Leave raid" :
                (_settings.MenuKey.Value.MainKey == KeyCode.Escape ? "F6" : _settings.MenuKey.Value.ToString()) + ": HORIZON menu";
            GUILayout.Label(infoKey.MainKey == KeyCode.None ? menu : $"{menu} | {infoKey}: Bot info", _textStyle);
            if (_botToolsEnabled)
            {
                if (_settings.CycleBots.Value) GUILayout.Label(_navigator.Caption, _textStyle);
                GUILayout.Label($"{_settings.FollowKey.Value}: {(_navigator.Following ? "stop following" : "follow target")} | Hold Left Alt to drag", _textStyle);
                if (_navigator.Following) GUILayout.Label($"{_settings.OrientationKey.Value}: {(_navigator.LockOrientation ? "bot view" : "mouse orbit")}", _textStyle);
            }
            else GUILayout.Label("Hold Left Alt to drag", _textStyle);
            GUILayout.Label(ZoomHint, _textStyle);
        }
        GUILayout.EndArea();
        GUI.depth = oldDepth;
    }

    private string ZoomHint => _settings.ZoomKey.Value.MainKey == KeyCode.None ? "Zoom shortcut disabled (F12)" : $"Hold {_settings.ZoomKey.Value} + wheel: zoom";

    private void OnApplicationQuit() => _quitting = true;

    private void OnDestroy()
    {
        _fikaFeed?.Dispose();
        _fikaFeed = null;
        if (!_quitting && _session.Watching && _game != null) LeaveRaid("plugin-unload");
        Reset();
        _harmony.UnpatchSelf();
        BotDebugHooks.Remove();
        if (DynamicMapsHooks.Installed) DynamicMapsHooks.Remove();
        PmcNavigator.IsObservedBot = null;
        if (_panelTexture != null) Destroy(_panelTexture);
        if (_labelDashes != null) Destroy(_labelDashes);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
