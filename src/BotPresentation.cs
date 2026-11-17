using System;
using System.Text;

namespace Horizon;

internal static class BotPresentation
{
    public const int ScavColor = -1, BossColor = -2, GuardColor = -3, SpecialColor = -4;

    public static int RoleColorIndex(string role, string group)
    {
        if (role == "pmcUSEC" || role == "pmcBEAR") return ColorIndex(group);
        if (role == "assault" || role == "assaultGroup" || role == "cursedAssault" || role == "marksman") return ScavColor;
        if (role.StartsWith("boss", StringComparison.OrdinalIgnoreCase) || role == "followerBigPipe" || role == "followerBirdEye") return BossColor;
        return role.StartsWith("follower", StringComparison.OrdinalIgnoreCase) ? GuardColor : SpecialColor;
    }

    public static string Kind(string role)
    {
        switch (role)
        {
            case "pmcBEAR": return "PMC / BEAR";
            case "pmcUSEC": return "PMC / USEC";
            case "assault": case "assaultGroup": case "cursedAssault": return "Scav";
            case "marksman": return "Sniper Scav";
            case "pmcBot": return "Raider";
            case "exUsec": return "Rogue";
            case "bossKnight": case "followerBigPipe": case "followerBirdEye": return "Goon";
        }
        if (role.StartsWith("boss", StringComparison.OrdinalIgnoreCase)) return "Boss / " + Words(role.Substring(4));
        if (role.StartsWith("follower", StringComparison.OrdinalIgnoreCase)) return "Guard / " + Words(role.Substring(8));
        if (role.StartsWith("sectant", StringComparison.OrdinalIgnoreCase)) return "Cultist";
        if (role.StartsWith("infected", StringComparison.OrdinalIgnoreCase)) return "Infected";
        return Words(role);
    }

    public static string Words(string value)
    {
        if (string.IsNullOrEmpty(value)) return "Unknown";
        var result = new StringBuilder(value.Length + 16);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(value[i - 1])) result.Append(' ');
            result.Append(c == '_' ? ' ' : c);
        }
        return result.ToString();
    }

    public static int ColorIndex(string group)
    {
        uint hash = 2166136261;
        foreach (char c in group) hash = unchecked((hash ^ c) * 16777619);
        return (int)(hash % 8);
    }

    public static bool HasDecision(string? value) => !string.IsNullOrEmpty(value) && value != "None" && value != "0";
    public static bool Idle(string? value) => string.IsNullOrEmpty(value) || value == "None" || value == "peaceful" ||
        value == "simplePatrol" || value == "standBy" || value == "SAIN:Peace";

    public static string Action(string value)
    {
        if (string.IsNullOrEmpty(value) || int.TryParse(value, out _)) return "Action unavailable";
        return Words(value.Replace("BL:", "").Replace(":", ": "));
    }
}
