using System;
using System.Diagnostics;
using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using UnityEngine;

namespace Horizon;

internal sealed class BotLabels
{
    private readonly Func<Player, GameWorld, BotLabelSample> _read;
    private readonly Action<string> _warning;
    private int _cursor;
    private bool _failed;
    private bool _valueFailed;
    public BotLabels(Func<Player, GameWorld, BotLabelSample> read, Action<string> warning)
    {
        _read = read;
        _warning = warning;
    }

    public static Vector3 Anchor(Player player)
    {
        var root = player.Position;
        var head = player.PlayerBones?.Head?.Original;
        if (head != null)
        {
            var position = head.position;
            var offset = position - root;
            // A culled skeleton can still have a head transform at an old world position.
            float distanceSquared = offset.sqrMagnitude;
            if (offset.y >= 0 && distanceSquared > .01f && distanceSquared <= 9f)
                return position + Vector3.up * .22f;
        }
        return root + Vector3.up * 1.8f;
    }

    public static bool InView(Camera camera, Player player, float distance, out Vector3 viewport)
    {
        viewport = camera.WorldToViewportPoint(Anchor(player));
        return viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1 &&
            (distance <= 0 || (player.Position - camera.transform.position).sqrMagnitude <= distance * distance);
    }

    private static bool ClearLine(Camera camera, Player player, Vector3 target) =>
        !Physics.Linecast(camera.transform.position, target, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
        hit.transform.IsChildOf(player.Transform.Original);

    // Up to two labels per frame; never gather inventories or mod state from OnGUI.
    // The time budget is checked between bots, all Unity access stays on the main thread.
    public void Update(BotRoster roster, GameWorld world, Camera camera, float now, float distance, bool showOccluded = false, bool showDistance = false, Player? followed = null)
    {
        if (roster.Labels.Count == 0) return;
        long start = Stopwatch.GetTimestamp();
        int refreshed = 0, checkedBots = 0;
        while (checkedBots++ < roster.Labels.Count && refreshed < 2)
        {
            _cursor %= roster.Labels.Count;
            var bot = roster.Labels[_cursor++];
            if (now < bot.NextDetails) continue;
            bot.NextDetails = now + .5f;
            refreshed++;
            try
            {
                var player = bot.Player;
                bot.Visible = PmcNavigator.IsLivingRemote(player) && InView(camera, player, distance, out _) &&
                    (showOccluded || ClearLine(camera, player, player.Position + Vector3.up * 1.8f) || ClearLine(camera, player, player.Position + Vector3.up));
                string range = "";
                if (bot.Visible && showDistance)
                {
                    Vector3 origin = followed != null ? followed.Position : camera.transform.position;
                    range = $" | {(player.Position - origin).magnitude:0} m";
                }
                if (bot.Visible && bot.Human)
                {
                    var hp = Health(player);
                    bot.Label = bot.LabelWithoutAction = $"{bot.Name} | Player{range}\nHealth: {hp.current:0}/{hp.maximum:0}";
                }
                else if (bot.Visible)
                {
                    var sample = _read(player, world);
                    var detail = sample.Behavior;
                    var health = sample.FromHost ? sample.Health : Health(player);
                    if (sample.FromHost) bot.Value = sample.Value;
                    else if (now >= bot.NextValue)
                    {
                        bot.NextValue = now + 5f;
                        try { bot.Value = InventoryValue(player); }
                        catch (Exception e)
                        {
                            bot.Value = null;
                            if (!_valueFailed) _warning("Bot labels: inventory estimate unavailable: " + e.GetBaseException().Message);
                            _valueFailed = true;
                        }
                    }
                    string personality = detail.personality.Length > 0 ? " | " + detail.personality : "";
                    string value = bot.Value.HasValue ? bot.Value.Value.ToString("N0") + " RUB" : "unavailable";
                    string hp = health is { } h ? $"{h.current:0}/{h.maximum:0}" : "unavailable";
                    bot.LabelWithoutAction = $"{bot.Name} | {bot.Kind}{personality}{range}\nHealth: {hp}\nLoot value (carried): {value}";
                    bot.Label = $"{bot.LabelWithoutAction}\n{(detail.ghost ? "Ghost | " : "")}{detail.action}";
                }
            }
            catch (Exception e)
            {
                bot.Visible = false;
                if (!_failed) _warning("Bot labels: snapshot unavailable: " + e.GetBaseException().Message);
                _failed = true;
            }
            if ((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency >= 1.5) break;
        }
    }

    internal static (float current, float maximum) Health(Player player)
    {
        var hp = player.HealthController.GetBodyPartHealth(EBodyPart.Common, true);
        return (hp.Current, hp.Maximum);
    }

    internal static double? InventoryValue(Player player)
    {
        var handbook = Singleton<Handbook>.Instance;
        var items = player.Inventory?.GetPlayerItems(EPlayerItems.Equipment);
        if (handbook == null || items == null) return null;
        double total = 0;
        foreach (var item in items)
            if (item != null) total += (double)handbook.GetBasePrice(item.TemplateId) * Math.Max(0, item.StackObjectsCount);
        return total;
    }

    internal static string Caption(SpectatorBot bot, bool followedOnly, Player? followed) =>
        followedOnly && bot.Player != followed ? bot.LabelWithoutAction : bot.Label;

    public void Hide(BotRoster roster) { foreach (var bot in roster.Labels) bot.Visible = false; }
    public void Clear() { _cursor = 0; _failed = _valueFailed = false; }
}
