using System;
using UnityEngine;

namespace Horizon;

// DynamicMaps parents its peek/minimap controller under BattleUIScreen. Fika
// deactivates that entire screen while spectating, including the map's Update.
internal sealed class SpectatorMapHud
{
    private readonly Func<Transform?> _findPeek;
    private Transform? _peek, _originalParent;
    private Canvas? _canvas;
    private int _sibling;
    private float _nextCheck;

    internal SpectatorMapHud(Func<Transform?> findPeek) => _findPeek = findPeek;

    internal void Update(bool tracking, float now)
    {
        if (_canvas != null)
        {
            if (!tracking || _peek == null || _originalParent == null || _originalParent.gameObject.activeInHierarchy)
                Restore();
            return;
        }
        if (!tracking || now < _nextCheck) return;
        _nextCheck = now + 1f;
        var peek = _findPeek();
        if (peek == null || peek.parent == null || peek.parent.gameObject.activeInHierarchy || !peek.gameObject.activeSelf) return;

        _peek = peek;
        _originalParent = peek.parent;
        _sibling = peek.GetSiblingIndex();
        var source = peek.GetComponentInParent<Canvas>(true);
        var root = new GameObject("HORIZON spectator map", typeof(Canvas));
        root.layer = peek.gameObject.layer;
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        if (source != null)
        {
            _canvas.scaleFactor = source.scaleFactor;
            _canvas.sortingOrder = source.sortingOrder;
        }
        peek.SetParent(root.transform, false);
    }

    internal void Restore()
    {
        if (_canvas != null)
        {
            if (_peek != null && _peek.parent == _canvas.transform)
            {
                _peek.SetParent(_originalParent != null ? _originalParent : null, false);
                if (_originalParent != null) _peek.SetSiblingIndex(_sibling);
            }
            UnityEngine.Object.Destroy(_canvas.gameObject);
        }
        _peek = _originalParent = null;
        _canvas = null;
        _nextCheck = 0;
    }
}
