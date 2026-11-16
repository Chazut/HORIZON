using System;
using System.Collections.Generic;
using EFT;

namespace Horizon;

internal sealed class SpectatorBot
{
    public Player Player = null!;
    public string Id = "", Name = "", Kind = "", Group = "", GroupLabel = "";
    public int ColorIndex;
    public string Label = "";
    public float NextDetails, NextValue;
    public double? Value;
    public bool Visible;
}

internal sealed class BotRoster
{
    public readonly List<SpectatorBot> Bots = new();
    private readonly Dictionary<string, SpectatorBot> _byId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _removed = new();
    private float _nextRefresh;

    public void Refresh(GameWorld world, float now)
    {
        if (now < _nextRefresh) return;
        _nextRefresh = now + 1f;
        _seen.Clear();
        Bots.Clear();
        foreach (var player in world.AllAlivePlayersList)
        {
            if (!PmcNavigator.IsLivingBot(player) || !_seen.Add(player.ProfileId)) continue;
            if (!_byId.TryGetValue(player.ProfileId, out var bot))
                _byId.Add(player.ProfileId, bot = new SpectatorBot { Id = player.ProfileId });
            if (bot.Player != player) { bot.Visible = false; bot.NextDetails = bot.NextValue = 0; bot.Value = null; }
            bot.Player = player;
            bot.Name = player.Profile?.Nickname ?? player.ProfileId;
            bot.Kind = BotPresentation.Kind(player.Profile?.Info?.Settings?.Role.ToString() ?? "Unknown");
            var group = player.AIData?.BotOwner?.BotsGroup;
            bot.Group = group != null ? "group:" + group.Id : "solo:" + player.ProfileId;
            bot.GroupLabel = group != null ? "Squad " + group.Id : "Solo bots";
            bot.ColorIndex = group != null ? BotPresentation.ColorIndex(bot.Group) : -1;
            Bots.Add(bot);
        }
        _removed.Clear();
        foreach (var id in _byId.Keys) if (!_seen.Contains(id)) _removed.Add(id);
        foreach (var id in _removed) _byId.Remove(id);
        Bots.Sort((a, b) =>
        {
            int group = string.Compare(a.Group, b.Group, StringComparison.Ordinal);
            if (group != 0) return group;
            int name = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return name != 0 ? name : string.CompareOrdinal(a.Id, b.Id);
        });
    }

    public void Clear()
    {
        Bots.Clear(); _byId.Clear(); _seen.Clear(); _removed.Clear(); _nextRefresh = 0;
    }
}
