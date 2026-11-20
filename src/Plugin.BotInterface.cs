using UnityEngine;

namespace Horizon;

public sealed partial class Plugin
{
    private readonly DraggablePanel _listPanel = new();
    private Vector2 _listScroll;
    private GUIStyle? _labelStyle;
    private GUIStyle? _labelOutlineStyle;
    private Texture2D? _labelDashes;
    private readonly GUIContent _labelContent = new();
    private readonly GUIContent _rowContent = new();
    private readonly BotLabelLayout _labelLayout = new();
    private static readonly Color[] SquadColors =
    {
        new(.32f, .86f, .81f), new(1f, .71f, .36f), new(.63f, .72f, 1f), new(.92f, .59f, .8f),
        new(.61f, .87f, .43f), new(.99f, .62f, .47f), new(.56f, .83f, .98f), new(.82f, .67f, 1f)
    };

    private static Color BotColor(SpectatorBot bot) => bot.ColorIndex switch
    {
        BotPresentation.ScavColor => Color.white,
        BotPresentation.BossColor => new Color(1f, .36f, .36f),
        BotPresentation.GuardColor => new Color(1f, .64f, .28f),
        BotPresentation.SpecialColor => new Color(.79f, .55f, 1f),
        _ => SquadColors[bot.ColorIndex]
    };

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
        if (mode == SpectatorOverlay.BotDebug && _fika != null && !CanUseBotDebug) mode = SpectatorOverlay.BotInfo;
        _overlay.Select(mode, CanUseBotDebug);
        SyncOverlay();
    }

    private void SyncOverlay()
    {
        if (CanUseBotDebug) SetBotDebug(_overlay.Mode == SpectatorOverlay.BotDebug);
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
        var screen = new Vector2(Screen.width, Screen.height);
        if (screen.x <= 20 * scale + 16 || screen.y <= 12 * scale + 16) return;
        if (_labelDashes == null)
        {
            // Sample one row or column of a repeating checker for each stroke.
            _labelDashes = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            _labelDashes.SetPixels(new[] { Color.white, Color.clear, Color.clear, Color.white });
            _labelDashes.Apply(false, true);
        }
        int depth = GUI.depth;
        var color = GUI.color;
        GUI.depth = -90;
        _labelLayout.Labels.Clear();
        foreach (var bot in _roster.Bots)
        {
            if (!bot.Visible || !PmcNavigator.IsLivingBot(bot.Player) ||
                !BotLabels.InView(_camera.Camera, bot.Player, _settings.EffectiveLabelDistance, out var point)) continue;
            _labelContent.text = bot.Label;
            float width = Mathf.Min(310 * scale, screen.x - 16);
            width = Mathf.Min(width, Mathf.Ceil(_labelStyle.CalcSize(_labelContent).x) + 20 * scale);
            float height = _labelStyle.CalcHeight(_labelContent, width - 20 * scale) + 12 * scale;
            var head = new Vector2(point.x * screen.x, (1 - point.y) * screen.y);
            var tint = BotColor(bot);
            var rect = _labelLayout.Add(head, new Vector2(width, height), screen, 12 * scale, bot.Label, tint);
            DrawLabelGuide(head, rect, tint, scale, _labelDashes);
        }
        // Draw all captions over the connectors so a later line cannot cover earlier text.
        foreach (var label in _labelLayout.Labels)
        {
            var rect = label.Bounds;
            _labelContent.text = label.Text;
            GUI.color = Color.white;
            var textRect = new Rect(rect.x + 10 * scale, rect.y + 6 * scale, rect.width - 20 * scale, rect.height - 12 * scale);
            float outline = Mathf.Max(1f, scale);
            for (int dx = -1; dx <= 1; dx += 2)
                for (int dy = -1; dy <= 1; dy += 2)
                    GUI.Label(new Rect(textRect.x + dx * outline, textRect.y + dy * outline, textRect.width, textRect.height), _labelContent, _labelOutlineStyle);
            GUI.color = label.Tint;
            GUI.Label(textRect, _labelContent, _labelStyle);
        }
        GUI.color = color;
        GUI.depth = depth;
    }

    private static void DrawLabelGuide(Vector2 head, Rect label, Color tint, float scale, Texture2D dashes)
    {
        float stroke = Mathf.Max(1f, Mathf.Round(1.5f * scale));
        GUI.color = tint;
        var delta = BotLabelLayout.ConnectorEnd(label, head) - head;
        if (delta.sqrMagnitude > 1)
        {
            float length = delta.magnitude;
            var matrix = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, head);
                GUI.DrawTextureWithTexCoords(new Rect(head.x, head.y - stroke / 2, length, stroke),
                    dashes, BotLabelLayout.DashUV(length, scale, false));
            }
            finally { GUI.matrix = matrix; }
        }
        var horizontal = BotLabelLayout.DashUV(label.width, scale, false);
        var vertical = BotLabelLayout.DashUV(label.height, scale, true);
        GUI.DrawTextureWithTexCoords(new Rect(label.xMin, label.yMin, label.width, stroke), dashes, horizontal);
        GUI.DrawTextureWithTexCoords(new Rect(label.xMin, label.yMax - stroke, label.width, stroke), dashes, horizontal);
        GUI.DrawTextureWithTexCoords(new Rect(label.xMin, label.yMin, stroke, label.height), dashes, vertical);
        GUI.DrawTextureWithTexCoords(new Rect(label.xMax - stroke, label.yMin, stroke, label.height), dashes, vertical);
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(head.x - 3 * scale, head.y - 3 * scale, 6 * scale, 6 * scale), Texture2D.whiteTexture);
        GUI.color = tint;
        GUI.DrawTexture(new Rect(head.x - 2 * scale, head.y - 2 * scale, 4 * scale, 4 * scale), Texture2D.whiteTexture);
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
            string group = bot.GroupLabel;
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
            string group = bot.GroupLabel;
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
