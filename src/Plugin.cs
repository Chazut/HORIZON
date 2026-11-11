using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Terkoiz.Freecam;
using UnityEngine;

namespace RaidSpectator;

[BepInPlugin(Id, "Raid Spectator", "0.1.0")]
[BepInDependency("com.terkoiz.freecam", "1.4.8")]
[BepInIncompatibility("com.fika.core")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "com.chazut.raidspectator";
    private static Plugin? _instance;
    private readonly SpectatorSession _session = new();
    private readonly FreecamBridge _camera = new();
    private readonly Harmony _harmony = new(Id);
    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<KeyboardShortcut> _menuKey = null!;
    private LocalGame? _game;
    private Player? _player;
    private GameWorld? _world;
    private FieldInfo _sessionStart = null!;
    private Func<bool>? _configurationVisible;
    private bool _menuOpen, _preparedEnd, _quitting;
    private GUIStyle? _titleStyle, _textStyle, _buttonStyle;
    private Texture2D? _panelTexture;

    private void Awake()
    {
        _enabled = Config.Bind("General", "Enabled", true, "Keep a solo raid running after you die and enter Freecam. Disabling this while spectating ends the raid as Killed.");
        _menuKey = Config.Bind("General", "Spectator menu", new KeyboardShortcut(KeyCode.Escape), "Release the mouse and show Leave raid. Escape always works as a fallback. The raid keeps running while this menu is open.");
        if (Chainloader.PluginInfos.ContainsKey("com.fika.core"))
        {
            Logger.LogError("Fika is incompatible with Raid Spectator. No patches applied.");
            enabled = false;
            return;
        }
        try
        {
            _sessionStart = AccessTools.Field(typeof(BaseLocalGame<EftGamePlayerOwner>), "SessionStartTime")
                ?? throw new MissingFieldException("BaseLocalGame.SessionStartTime");
            if (_sessionStart.FieldType != typeof(DateTime)) throw new InvalidOperationException("Unexpected raid clock type");
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
            Logger.LogInfo("Ready. Solo only; Freecam 1.4.8 required. Spectator menu: Escape.");
        }
        catch (Exception e)
        {
            _harmony.UnpatchSelf();
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

    private bool TryWatch(BaseLocalGame<EftGamePlayerOwner> candidate)
    {
        if (_session.Watching && ReferenceEquals(_game, candidate)) return true;
        if (!_enabled.Value || _session.Phase != SessionPhase.Idle ||
            candidate.GetType() != typeof(LocalGame) || candidate.Status != GameStatus.Started ||
            Chainloader.PluginInfos.ContainsKey("com.fika.core")) return false;
        var game = (LocalGame)candidate;
        var player = game.LocalPlayer;
        if (player == null || !player.IsYourPlayer || player.IsAI || player.HealthController == null ||
            player.HealthController.IsAlive || game.GameWorld == null || game.ProfileId != player.ProfileId) return false;
        try
        {
            var deathTime = DateTimeExtensions.Now;
            var start = (DateTime)_sessionStart.GetValue(game);
            if (!_camera.Attach(game))
            {
                Logger.LogWarning("Freecam is not ready; keeping the normal death/exit flow.");
                return false;
            }
            if (!_session.Begin(true, true, _camera.Ready, player.ProfileId, game.PastTime, start, deathTime))
            {
                _camera.Release();
                return false;
            }
            _game = game;
            _player = player;
            _world = game.GameWorld;
            _menuOpen = false;
            _preparedEnd = false;
            _camera.SetMenu(false);
            StartCoroutine(AfterDeathDispatch());
            Logger.LogInfo($"Spectating after death: profile={player.ProfileId}, played={_session.PlayedSeconds:F1}s. Native Killed result deferred until exit/timer.");
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError($"Could not start Freecam; keeping the normal death/exit flow: {e}");
            Reset();
            return false;
        }
    }

    private IEnumerator AfterDeathDispatch()
    {
        var world = _world;
        yield return null;
        if (!_session.Watching || !ReferenceEquals(world, _world)) yield break;
        try { _camera.ClearDeathEffects(); }
        catch (Exception e) { Logger.LogError($"Death effects could not be cleared: {e}"); LeaveRaid("camera-error"); }
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
        if (!_enabled.Value || !_camera.Ready)
        {
            LeaveRaid(_enabled.Value ? "camera-lost" : "disabled");
            return;
        }
        if (!ConfigurationIsOpen() && (Input.GetKeyDown(KeyCode.Escape) || _menuKey.Value.IsDown())) _menuOpen = !_menuOpen;
    }

    private void LateUpdate()
    {
        if (_session.Watching) _camera.SetMenu(_menuOpen || ConfigurationIsOpen());
    }

    private bool ConfigurationIsOpen()
    {
        try { return _configurationVisible?.Invoke() ?? false; }
        catch { _configurationVisible = null; return false; }
    }

    private void LeaveRaid(string reason)
    {
        if (_game == null || !_session.RequestExit()) return;
        Logger.LogInfo($"Leaving spectator mode: reason={reason}, result=Killed, played={_session.PlayedSeconds:F1}s.");
        _game.Stop(_session.ProfileId, ExitStatus.Killed, null, 0f);
    }

    private void PrepareEnd()
    {
        if (_preparedEnd || _game == null) return;
        // GameEnd derives profile playTime and its result callback from this clock. Do this only
        // once ending begins, so spectating never changes the live raid timer or bot timelines.
        _preparedEnd = true;
        try { _sessionStart.SetValue(_game, _session.StartForSave(DateTimeExtensions.Now)); }
        catch (Exception e)
        {
            // A clock/visual compatibility problem must never prevent native profile saving.
            Logger.LogError($"Could not remove spectator time from the result clock; native shutdown will continue: {e}");
        }
        try { _camera.Release(ending: true); }
        catch (Exception e) { Logger.LogError($"Could not fully restore the camera; native shutdown will continue: {e}"); }
    }

    private void Reset()
    {
        try { _camera.Release(); }
        catch (Exception e) { Logger.LogWarning($"Spectator cleanup: {e.Message}"); }
        _session.Reset();
        _game = null;
        _world = null;
        _player = null;
        _preparedEnd = false;
        _menuOpen = false;
    }

    private static bool DeathPrefix(BaseLocalGame<EftGamePlayerOwner> __instance) =>
        _instance == null || !_instance.TryWatch(__instance);

    private static bool StopPrefix(BaseLocalGame<EftGamePlayerOwner> __instance,
        string profileId, ref ExitStatus exitStatus, ref string exitName, ref float delay)
    {
        var plugin = _instance;
        if (plugin == null) return true;
        if (exitStatus == ExitStatus.Killed && profileId == __instance.ProfileId &&
            plugin._session.Phase == SessionPhase.Idle && plugin.TryWatch(__instance)) return false;
        var decision = plugin._session.DecideStop(ReferenceEquals(plugin._game, __instance), profileId, exitStatus == ExitStatus.Killed);
        if (decision == StopDecision.Defer) return false;
        if (decision == StopDecision.Killed)
        {
            if (!plugin._preparedEnd) plugin.Logger.LogInfo($"Completing native raid shutdown: requested={exitStatus}, saved=Killed.");
            exitStatus = ExitStatus.Killed;
            exitName = null!;
            delay = 0f;
            plugin.PrepareEnd();
        }
        return true;
    }

    private static void PlayerEndPrefix(Player __instance, ref ExitStatus exitStatus, ref float pastTime, ref string exitName)
    {
        var plugin = _instance;
        if (plugin == null || plugin._session.Phase != SessionPhase.Ending || !ReferenceEquals(plugin._player, __instance)) return;
        exitStatus = ExitStatus.Killed;
        exitName = null!;
        pastTime = plugin._session.PlayedSeconds;
        plugin.Logger.LogInfo($"Player result: Killed at {pastTime:F1}s; spectator time excluded from player statistics.");
    }

    private static bool FreecamInputPrefix(FreecamController __instance) =>
        _instance == null || !_instance._session.Watching || !ReferenceEquals(_instance._camera.Controller, __instance);

    private static bool FreecamActivePrefix(FreecamController __instance, ref bool __result)
    {
        if (_instance == null || !_instance._session.Watching || !ReferenceEquals(_instance._camera.Controller, __instance)) return true;
        // Opening our menu pauses Freecam input, not camera ownership (or ForceSetPosition
        // would snap the camera back to the corpse).
        __result = true;
        return false;
    }

    private void OnGUI()
    {
        if (!_session.Watching) return;
        if (_titleStyle == null)
        {
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _textStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            _panelTexture = new Texture2D(1, 1);
            _panelTexture.SetPixel(0, 0, new Color(0.055f, 0.07f, 0.09f, 0.94f));
            _panelTexture.Apply();
        }
        float width = Mathf.Min(330, Screen.width - 24);
        var rect = new Rect(Screen.width - width - 16, 20, width, _menuOpen ? 204 : 68);
        int oldDepth = GUI.depth;
        GUI.depth = -100;
        GUI.DrawTexture(rect, _panelTexture);
        GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 12, rect.width - 32, rect.height - 20));
        GUILayout.Label("SPECTATING", _titleStyle);
        if (_menuOpen)
        {
            GUILayout.Label("You died. The raid continues.", _textStyle);
            GUILayout.Label("Leaving will save your raid as Killed.", _textStyle);
            GUILayout.Space(12);
            if (GUILayout.Button("Resume Freecam", _buttonStyle, GUILayout.Height(34))) _menuOpen = false;
            if (GUILayout.Button("Leave raid", _buttonStyle, GUILayout.Height(34))) LeaveRaid("button");
        }
        else GUILayout.Label("Esc: menu / Leave raid", _textStyle);
        GUILayout.EndArea();
        GUI.depth = oldDepth;
    }

    private void OnApplicationQuit() => _quitting = true;

    private void OnDestroy()
    {
        if (!_quitting && _session.Watching && _game != null) LeaveRaid("plugin-unload");
        Reset();
        _harmony.UnpatchSelf();
        if (_panelTexture != null) Destroy(_panelTexture);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
