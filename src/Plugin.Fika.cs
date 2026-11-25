using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace Horizon;

public sealed partial class Plugin
{
    private bool _fikaExtracted;

    private (string, string, bool) ReadAuthorityLabel(Player bot, GameWorld world)
    {
        BindBotInformation();
        return _botData.ReadBot(bot, world);
    }

    private BotLabelSample ReadBotLabel(Player bot, GameWorld world, BotLabelFields fields) =>
        _fika != null && !_fika.IsHost()
            ? _fikaFeed?.ReadRemote(bot, world) ?? new BotLabelSample(("", "Host action unavailable", false))
            : new BotLabelSample(_botData.ReadBot(bot, world, fields));

    private void InstallFikaCamera()
    {
        var update = AccessTools.DeclaredMethod(_fika!.ScriptType, "Update") ?? throw new MissingMethodException("Fika FreeCamera.Update");
        _harmony.Patch(update, prefix: new HarmonyMethod(typeof(Plugin), nameof(FikaCameraPrefix)),
            transpiler: new HarmonyMethod(typeof(Plugin), nameof(FikaWheelTranspiler)));
        PmcNavigator.IsObservedBot = _fika.IsObservedBot;
    }

    private static bool FikaCameraPrefix(Behaviour __instance)
    {
        var plugin = _instance;
        if (plugin == null || !plugin.enabled || plugin._fika == null) return true;
        try { return plugin.UpdateFikaCamera(__instance); }
        catch (Exception e)
        {
            plugin.Logger.LogError($"HORIZON spectator controls unavailable; returning to Fika: {e}");
            plugin.Reset();
            plugin.enabled = false;
            return true;
        }
    }

    private bool UpdateFikaCamera(Behaviour script)
    {
        var world = Singleton<GameWorld>.Instance;
        if (!_settings.Enabled.Value || world == null ||
            !_fika!.CanWatch(script, world.MainPlayer, _settings.AfterDeath.Value, _settings.AfterExtraction.Value) ||
            Singleton<AbstractGame>.Instance is not BaseLocalGame<EftGamePlayerOwner> game ||
            game.Status != GameStatus.Started || !ReferenceEquals(game.GameWorld, world) ||
            MonoBehaviourSingleton<PreloaderUI>.Instance?.Console?.IsConsoleVisible == true || _killcam.IsBusy())
        {
            if (_cameraActive && ReferenceEquals(script, _camera.Script)) Reset();
            return true;
        }
        bool allowBots = _fika.AllowSpectateBots();
        if (!_cameraActive || _camera.Script != script || _world != world || _botToolsEnabled != allowBots)
        {
            if (_cameraActive) Reset();
            var camera = script.GetComponent<Camera>();
            if (camera == null || !camera.isActiveAndEnabled || !CameraManager.Exist || CameraManager.Instance.Camera != camera) return true;
            _camera.AttachFika(script, camera, rotation => _fika.Align(script, rotation));
            _world = world;
            _cameraActive = true;
            _botToolsEnabled = allowBots;
            if (allowBots)
            {
                BindBotInformation();
                if (CanUseBotDebug)
                {
                    _botDebugView.Attach(world, world.MainPlayer, camera);
                    _botDebug.BeginSession();
                }
            }
            _lastOverlay = _settings.Overlay.Value;
            ApplyOverlay(_lastOverlay);
        }
        _fikaExtracted = _fika.Extracted(script);
        bool wasFollowing = _navigator.Following;
        UpdateCamera(world);
        // Fika handles its own Home input toggle even while HORIZON is following a bot.
        return Input.GetKeyDown(KeyCode.Home) || (!CameraMenu && !wasFollowing && !_navigator.Following);
    }

    private static IEnumerable<CodeInstruction> FikaWheelTranspiler(IEnumerable<CodeInstruction> instructions) =>
        ReplaceWheelInput(instructions, nameof(Input.GetAxisRaw), nameof(ReadFikaAxis));

    private static float ReadFikaAxis(string axis) => ConsumeWheel(axis) ? 0f : Input.GetAxisRaw(axis);
}
