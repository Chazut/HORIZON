using BepInEx.Configuration;
using UnityEngine;

namespace Horizon;

internal sealed class DraggablePanel
{
    private int _control;
    private Vector2 _origin, _panel;
    private Vector2? _position;

    public Rect Place(int hint, float width, float height, float scale, ConfigEntry<Vector2> saved, bool interactive)
    {
        float margin = 16 * scale;
        width = Mathf.Max(1, Mathf.Min(width * scale, Screen.width - margin * 2));
        height = Mathf.Max(1, Mathf.Min(height * scale, Screen.height - margin * 2));
        var available = new Vector2(SpectatorInterface.Available(Screen.width, width, margin), SpectatorInterface.Available(Screen.height, height, margin));
        var normalized = _position ?? saved.Value;
        var rect = new Rect(SpectatorInterface.Position(normalized.x, available.x, margin), SpectatorInterface.Position(normalized.y, available.y, margin), width, height);
        int control = GUIUtility.GetControlID(hint, FocusType.Passive);
        if (_control != 0 && (_control != control || !interactive)) Reset();
        var ev = Event.current;
        if (interactive && ev.type == EventType.MouseDown && ev.button == 0 && new Rect(rect.x, rect.y, width, 38 * scale).Contains(ev.mousePosition))
        {
            _control = control; GUIUtility.hotControl = control;
            _origin = ev.mousePosition; _panel = new Vector2(rect.x, rect.y); ev.Use();
        }
        if (_control == control && GUIUtility.hotControl == control)
        {
            if (ev.type == EventType.MouseDrag)
            {
                var p = _panel + ev.mousePosition - _origin;
                _position = new Vector2(SpectatorInterface.Normalize(p.x, available.x, margin), SpectatorInterface.Normalize(p.y, available.y, margin));
                ev.Use();
            }
            if (ev.type == EventType.MouseUp)
            {
                if (_position.HasValue) saved.Value = _position.Value;
                Reset(); ev.Use();
            }
        }
        return rect;
    }

    public void Reset()
    {
        if (_control != 0 && GUIUtility.hotControl == _control) GUIUtility.hotControl = 0;
        _control = 0; _position = null;
    }
}
