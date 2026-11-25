using System;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace Horizon;

// DynamicMaps owns the mask, its corner anchor and its two saved offsets.
// Preview movement directly on the mask and write the native settings once on drop.
internal sealed class DynamicMiniMapDrag
{
    private readonly object _plugin;
    private readonly FieldInfo _map, _mask;
    private readonly PropertyInfo _showingMiniMap;
    private readonly ConfigEntry<float> _x, _y;
    private readonly Vector3[] _corners = new Vector3[4];
    private RectTransform? _drag;
    private Vector2 _start, _mouse;
    private int _control;
    private bool _moved;

    internal DynamicMiniMapDrag(object plugin)
    {
        _plugin = plugin;
        _map = plugin.GetType().GetField("Map") ?? throw new MissingFieldException("DynamicMaps.Map");
        const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        _mask = _map.FieldType.GetField("_scrollMask", privateInstance) ?? throw new MissingFieldException("_scrollMask");
        _showingMiniMap = _map.FieldType.GetProperty("ShowingMiniMap", privateInstance) ?? throw new MissingMemberException("ShowingMiniMap");
        var settings = plugin.GetType().Assembly.GetType("DynamicMaps.Config.Settings", true)!;
        ConfigEntry<float> Offset(string axis) => settings.GetField("MiniMapScreenOffset" + axis)?.GetValue(null) as ConfigEntry<float>
            ?? throw new MissingMemberException("MiniMapScreenOffset" + axis);
        _x = Offset("X");
        _y = Offset("Y");
    }

    private RectTransform? VisibleMask()
    {
        var map = _map.GetValue(_plugin) as Component;
        if (map == null || !map.gameObject.activeInHierarchy || !(bool)_showingMiniMap.GetValue(map)) return null;
        return (_mask.GetValue(map) as Component)?.transform as RectTransform;
    }

    internal void Handle(bool interactive, bool panelUnderMouse)
    {
        int id = GUIUtility.GetControlID(82144, FocusType.Passive);
        var ev = Event.current;
        if (!interactive) { Cancel(); return; }
        bool begin = ev.type == EventType.MouseDown && ev.button == 0 && !panelUnderMouse && GUIUtility.hotControl == 0;
        if (!begin && (_control == 0 || (ev.rawType != EventType.MouseUp && ev.type != EventType.MouseDrag))) return;
        var mask = VisibleMask();
        if (mask == null || mask.parent is not RectTransform parent || (_control != 0 && (mask != _drag || GUIUtility.hotControl != _control)))
        { Cancel(); return; }
        var canvas = mask.GetComponentInParent<Canvas>()?.rootCanvas;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 screen = new(ev.mousePosition.x, Screen.height - ev.mousePosition.y);
        if (begin)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(mask, screen, camera)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out _mouse)) return;
            _start = mask.anchoredPosition;
            _drag = mask;
            _control = GUIUtility.hotControl = id;
        }
        else if (ev.rawType == EventType.MouseUp)
        {
            var position = mask.anchoredPosition;
            var pivot = mask.pivot;
            if (_moved)
            {
                _x.Value = pivot.x > .5f ? -position.x : position.x;
                _y.Value = pivot.y > .5f ? -position.y : position.y;
            }
            Release();
        }
        else
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out var local)) return;
            _moved = true;
            mask.anchoredPosition = _start + local - _mouse;
            // Clamp the actual viewport, including canvas scaling and all four native corner choices.
            mask.GetWorldCorners(_corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, _corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, _corners[2]);
            var clamped = new Vector2(Mathf.Clamp(min.x, 0, Mathf.Max(0, Screen.width - (max.x - min.x))),
                Mathf.Clamp(min.y, 0, Mathf.Max(0, Screen.height - (max.y - min.y))));
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen + clamped - min, camera, out var corrected))
                mask.anchoredPosition += corrected - local;
        }
        ev.Use();
    }

    internal void Cancel()
    {
        if (_control == 0) return;
        if (_drag != null && VisibleMask() == _drag) _drag.anchoredPosition = _start;
        Release();
    }

    private void Release()
    {
        if (GUIUtility.hotControl == _control) GUIUtility.hotControl = 0;
        _control = 0;
        _moved = false;
        _drag = null;
    }
}
