using System;
using System.Diagnostics;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using UnityEngine;

namespace Horizon;

internal sealed class BotLabels
{
    private readonly Func<Player, GameWorld, BotLabelFields, BotLabelSample> _read;
    private readonly Action<string> _warning;
    private readonly StringBuilder _text = new(160);
    private BotLabelFields _fields;
    private int _cursor;
    private bool _failed;
    private bool _valueFailed;
    public BotLabels(Func<Player, GameWorld, BotLabelFields, BotLabelSample> read, Action<string> warning)
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
    public void Update(BotRoster roster, GameWorld world, Camera camera, float now, float distance, bool showOccluded = false, BotLabelFields fields = BotLabelFields.All, Player? followed = null)
    {
        if (_fields != fields)
        {
            foreach (var bot in roster.Labels)
            {
                bot.Visible = false;
                bot.NextDetails = 0;
                if ((_fields & BotLabelFields.CarriedValue) == 0) bot.NextValue = 0;
            }
            _fields = fields;
        }
        if (fields == BotLabelFields.None || roster.Labels.Count == 0) return;
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
                if (bot.Visible)
                    RefreshCaption(bot, world, camera, now, bot.Human ? fields & BotLabelFields.Human : fields, followed);
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

    private void RefreshCaption(SpectatorBot bot, GameWorld world, Camera camera, float now, BotLabelFields fields, Player? followed)
    {
        var player = bot.Player;
        var sample = !bot.Human && (fields & BotLabelFields.HostData) != 0 ? _read(player, world, fields) : default;
        _text.Clear();
        if ((fields & BotLabelFields.Name) != 0) Append(bot.Name);
        var kindFields = fields & (BotLabelFields.Type | BotLabelFields.RoleDetail);
        if (kindFields != 0)
        {
            string kind = bot.Human ? "Player" : bot.Kind;
            int split = kind.IndexOf(" / ", StringComparison.Ordinal);
            if (kindFields == (BotLabelFields.Type | BotLabelFields.RoleDetail)) Append(kind);
            else if (kindFields == BotLabelFields.Type) Append(split < 0 ? kind : kind.Substring(0, split));
            else if (split >= 0) Append(kind.Substring(split + 3));
        }
        if ((fields & BotLabelFields.Personality) != 0) Append(sample.Behavior.personality);
        if ((fields & BotLabelFields.Distance) != 0)
        {
            Vector3 origin = followed != null ? followed.Position : camera.transform.position;
            Append($"{(player.Position - origin).magnitude:0} m");
        }
        if ((fields & BotLabelFields.Health) != 0)
        {
            var health = sample.FromHost ? sample.Health : Health(player);
            Append(health is { } hp ? $"Health: {hp.current:0}/{hp.maximum:0}" : "Health: unavailable", "\n");
        }
        if ((fields & BotLabelFields.CarriedValue) != 0)
        {
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
            string value = bot.Value.HasValue ? bot.Value.Value.ToString("N0") + " RUB" : "unavailable";
            Append("Loot value (carried): " + value, "\n");
        }
        bool ghost = (fields & BotLabelFields.Ghost) != 0 && sample.Behavior.ghost;
        if (ghost) Append("Ghost", "\n");
        bot.LabelWithoutAction = _text.ToString();
        string? action = sample.Behavior.action;
        if (!string.IsNullOrEmpty(action) && (fields & ActionField(action!)) != 0)
        {
            Append(action, ghost ? " | " : "\n");
            bot.Label = _text.ToString();
        }
        else bot.Label = bot.LabelWithoutAction;
        bot.Visible = bot.Label.Length != 0;
    }

    private void Append(string? value, string separator = " | ")
    {
        if (string.IsNullOrEmpty(value)) return;
        if (_text.Length != 0) _text.Append(separator);
        _text.Append(value);
    }

    // The Fika feed carries the same resolved action string as solo. Squad roles occupy
    // the current action line only while the corresponding tactic is actually active.
    private static BotLabelFields ActionField(string action) =>
        action.StartsWith("COMET: Crossfire", StringComparison.Ordinal) ||
        action.StartsWith("COMET: Siege", StringComparison.Ordinal) ||
        action.StartsWith("COMET: Grenade suppression", StringComparison.Ordinal)
            ? BotLabelFields.SquadTactic : BotLabelFields.Action;

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
