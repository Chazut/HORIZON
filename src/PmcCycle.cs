using System;
using System.Collections.Generic;

namespace Horizon;

internal sealed class PmcCycle
{
    public string? SelectedId { get; private set; }
    public void Select(string id) => SelectedId = id;
    // IDs are sorted, so spawn-list reordering does not change the cycle direction.
    public string? Next(List<string> ids, int direction)
    {
        if (ids.Count == 0) { SelectedId = null; return null; }
        ids.Sort(StringComparer.Ordinal);
        int current = SelectedId == null ? -1 : ids.IndexOf(SelectedId);
        int next = current < 0 ? (direction < 0 ? ids.Count - 1 : 0) :
            (current + (direction < 0 ? -1 : 1) + ids.Count) % ids.Count;
        return SelectedId = ids[next];
    }
    public void Reset() => SelectedId = null;
}
