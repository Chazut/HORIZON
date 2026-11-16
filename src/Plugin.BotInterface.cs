using System.Collections.Generic;
using UnityEngine;

namespace Horizon;

public sealed partial class Plugin
{
    private readonly DraggablePanel _listPanel = new();
    private Vector2 _listScroll;
    private GUIStyle? _labelStyle;
    private readonly GUIContent _labelContent = new();
    private readonly GUIContent _rowContent = new();
    private readonly List<Rect> _drawnLabels = new();
    private static readonly Color[] SquadColors =
    {
        new(.32f, .86f, .81f), new(1f, .71f, .36f), new(.63f, .72f, 1f), new(.92f, .59f, .8f),
        new(.61f, .87f, .43f), new(.99f, .62f, .47f), new(.56f, .83f, .98f), new(.82f, .67f, 1f)
    };

    private static Color BotColor(SpectatorBot bot) => bot.ColorIndex >= 0 ? SquadColors[bot.ColorIndex] : Color.white;

    private void BindBotInformation()
    {
        if (_integrationsBound) return;
        _integrationsBound = true;
        // Awake can run before COMET/ORBIT/QB. Resolve optional plugins at spectator entry,
        // after the loader has completed, without imposing new plugin dependencies.
        bool lb = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("me.skwizzy.lootingbots");
        foreach (string id in new[] { "me.sol.sain", "com.chazut.orbit", "com.chazut.comet", "com.danw.questingbots", "me.skwizzy.lootingbots" })
        {
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(id, out var plugin)) continue;
            try { _botData.Bind(new[] { plugin.Instance.GetType().Assembly }, lb); }
            catch (System.Exception e) { Logger.LogWarning($"Bot label integration skipped for {id}: {e.Message}"); }
        }
        Logger.LogInfo("Bot label integrations: " + _botData.Description);
    }

    private Rect MainPanelRect(float scale)
    {
        float mx = 16 * scale, my = 20 * scale;
        float width = Mathf.Max(1, Mathf.Min((_menuOpen ? 400 : 330) * scale, Screen.width - mx * 2));
        float height = Mathf.Max(1, Mathf.Min((_menuOpen ? 660 : 185) * scale, Screen.height - my * 2));
        var position = _dragPosition ?? (_menuOpen ? _settings.MenuPosition.Value : _settings.HelpPosition.Value);
        return new Rect(SpectatorInterface.Position(position.x, SpectatorInterface.Available(Screen.width, width, mx), mx),
            SpectatorInterface.Position(position.y, SpectatorInterface.Available(Screen.height, height, my), my), width, height);
    }

    private void ApplyOverlay(SpectatorOverlay mode)
    {
        _overlay.Select(mode, _botDebugReady);
        if (_botDebugReady) SetBotDebug(_overlay.Mode == SpectatorOverlay.BotDebug);
        _labels.Hide(_roster);
        Logger.LogInfo($"Spectator overlay: {_overlay.Mode}.");
    }

    private void ChooseOverlay(SpectatorOverlay mode)
    {
        _settings.Overlay.Value = mode;
        _lastOverlay = mode;
        ApplyOverlay(mode);
    }

    private void DrawBotLabels(float scale)
    {
        if (Event.current.type != EventType.Repaint || _camera.Camera == null || _labelStyle == null) return;
        int depth = GUI.depth;
        var color = GUI.color;
        GUI.depth = -90;
        _drawnLabels.Clear();
        foreach (var bot in _roster.Bots)
        {
            if (!bot.Visible || !PmcNavigator.IsLivingBot(bot.Player) ||
                !BotLabels.InView(_camera.Camera, bot.Player, _settings.LabelDistance.Value, out var point)) continue;
            float width = Mathf.Min(310 * scale, Screen.width - 16);
            _labelContent.text = bot.Label;
            float height = _labelStyle.CalcHeight(_labelContent, width - 20 * scale) + 12 * scale;
            var rect = new Rect(Mathf.Clamp(point.x * Screen.width - width / 2, 8, Screen.width - width - 8),
                Mathf.Clamp((1 - point.y) * Screen.height - height - 8 * scale, 8, Mathf.Max(8, Screen.height - height - 8)), width, height);
            // Keep neighbouring squad members legible. Move overlapping labels when space allows.
            foreach (var previous in _drawnLabels)
                if (rect.Overlaps(previous) && previous.yMax + height + 4 < Screen.height)
                    rect.y = previous.yMax + 4;
            _drawnLabels.Add(rect);
            GUI.color = Color.white;
            GUI.DrawTexture(rect, _panelTexture);
            GUI.color = BotColor(bot);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3 * scale, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 10 * scale, rect.y + 6 * scale, width - 20 * scale, height - 12 * scale), _labelContent, _labelStyle);
        }
        GUI.color = color;
        GUI.depth = depth;
    }

    private void DrawBotList(float scale)
    {
        bool interactive = CameraMenu && !((_menuOpen || _settings.ShowHelp.Value) && MainPanelRect(scale).Contains(Event.current.mousePosition));
        var rect = _listPanel.Place(82143, 360, 520, scale, _settings.BotListPosition, interactive);
        int depth = GUI.depth;
        var color = GUI.color;
        GUI.depth = -100;
        GUI.DrawTexture(rect, _panelTexture);
        GUI.Label(new Rect(rect.x + 12 * scale, rect.y + 8 * scale, rect.width - 24 * scale, 28 * scale), $"BOTS ({_roster.Bots.Count})", _titleStyle);
        GUI.Label(new Rect(rect.x + 12 * scale, rect.yMax - 39 * scale, rect.width - 24 * scale, 35 * scale), "Hold Left Alt or open Esc to select / drag", _textStyle);
        float rowHeight = 36 * scale, headerHeight = 26 * scale, total = 0;
        string previousGroup = "";
        foreach (var bot in _roster.Bots)
        {
            string group = bot.GroupLabel == "Solo bots" ? "solo" : bot.Group;
            if (group != previousGroup) total += headerHeight;
            total += rowHeight;
            previousGroup = group;
        }
        var viewport = new Rect(rect.x + 10 * scale, rect.y + 43 * scale, rect.width - 20 * scale, Mathf.Max(1, rect.height - 86 * scale));
        float innerWidth = Mathf.Max(1, viewport.width - 22 * scale);
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && interactive;
        _listScroll = GUI.BeginScrollView(viewport, _listScroll, new Rect(0, 0, innerWidth, Mathf.Max(viewport.height, total)), false, false, _horizontalScrollbar, _verticalScrollbar);
        previousGroup = "";
        float y = 0;
        foreach (var bot in _roster.Bots)
        {
            string group = bot.GroupLabel == "Solo bots" ? "solo" : bot.Group;
            if (group != previousGroup)
            {
                GUI.color = BotColor(bot);
                if (y + headerHeight >= _listScroll.y && y < _listScroll.y + viewport.height)
                    GUI.Label(new Rect(3 * scale, y, innerWidth, headerHeight), bot.GroupLabel, _textStyle);
                y += headerHeight;
            }
            GUI.color = Color.white;
            GUI.enabled = previousEnabled && interactive && PmcNavigator.IsLivingBot(bot.Player);
            if (y + rowHeight >= _listScroll.y && y < _listScroll.y + viewport.height)
            {
                _rowContent.text = (_navigator.SelectedId == bot.Id ? "> " : "") + bot.Name + " | " + bot.Kind;
                _rowContent.tooltip = _rowContent.text;
                if (GUI.Button(new Rect(0, y, innerWidth, rowHeight - 3 * scale), _rowContent, _buttonStyle) && _world != null)
                {
                    if (_navigator.Visit(_world, _camera, bot.Player, _settings.VisitDistance.Value))
                        Logger.LogInfo($"Camera list visit: {bot.Name}, type={bot.Kind}, {bot.GroupLabel}.");
                }
            }
            y += rowHeight;
            previousGroup = group;
        }
        GUI.EndScrollView();
        GUI.enabled = previousEnabled;
        GUI.color = color;
        GUI.depth = depth;
    }
}
