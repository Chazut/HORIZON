using System;

namespace Horizon;

[Flags]
internal enum BotLabelFields
{
    None = 0,
    Name = 1 << 0,
    Type = 1 << 1,
    RoleDetail = 1 << 2,
    Personality = 1 << 3,
    Distance = 1 << 4,
    Health = 1 << 5,
    CarriedValue = 1 << 6,
    Ghost = 1 << 7,
    Action = 1 << 8,
    SquadTactic = 1 << 9,

    Difficulty = 1 << 10,

    Actions = Action | SquadTactic,
    Behavior = Personality | Ghost | Actions,
    HostData = Health | CarriedValue | Behavior,
    Human = Name | Type | Distance | Health,
    All = Difficulty | Human | RoleDetail | CarriedValue | Behavior
}

internal static class BotInfoRows
{
    internal const int Count = 11;
    // One hex digit per enum bit, starting with Name on the right. Identity and distance
    // share line 1, health and value use lines 2 and 3, Ghost and actions share line 4.
    internal const ulong Defaults = 0x14443211111UL;

    internal static int Index(BotLabelFields field)
    {
        int i = 0;
        for (var bit = (int)field; bit > 1; bit >>= 1) i++;
        return i;
    }

    internal static int Get(ulong rows, int index) => (int)((rows >> (4 * index)) & 15);

    internal static ulong Set(ulong rows, int index, int line)
    {
        int shift = 4 * index;
        return (rows & ~(15UL << shift)) | ((ulong)Math.Max(1, Math.Min(Count, line)) << shift);
    }
}
