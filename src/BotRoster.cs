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
    internal Func<Player, (bool known, int? id)>? ReadHostSquad;

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
            string role = player.Profile?.Info?.Settings?.Role.ToString() ?? "Unknown";
            bot.Kind = BotPresentation.Kind(role);
            var group = player.AIData?.BotOwner?.BotsGroup;
            bool replicated = group == null && PmcNavigator.IsObservedBot?.Invoke(player) == true;
            string? groupId = group?.Id.ToString();
            bool unknown = false;
            if (replicated)
            {
                var host = ReadHostSquad?.Invoke(player) ?? default;
                groupId = host.known ? host.id?.ToString() : player.Profile?.Info?.GroupId;
                unknown = !host.known && string.IsNullOrEmpty(groupId);
            }
            if (!string.IsNullOrEmpty(groupId))
            {
                bot.Group = "group:" + groupId;
                bot.GroupLabel = "Squad " + groupId;
            }
            else
            {
                bot.Group = (unknown ? "unknown:" : "solo:") + player.ProfileId;
                bot.GroupLabel = unknown ? "Unknown squad" : "Solo bots";
            }
            bot.ColorIndex = BotPresentation.RoleColorIndex(role, bot.Group);
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
