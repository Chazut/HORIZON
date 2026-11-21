using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using Fika.Core;
using Fika.Core.Main.Players;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using Fika.Core.Networking.Packets.Backend;
using HarmonyLib;
using Horizon.Fika.V2;
using UnityEngine;

namespace Horizon.Fika;

internal sealed class BotFeed : FikaBotFeed
{
    private sealed class Viewer
    {
        internal int? NetId;
        internal int Count;
        internal float WindowEnd;
        internal bool SentFirstReply;
        internal readonly HashSet<string> Stages = new();
    }

    private sealed class HostSample
    {
        internal string Profile = "";
        internal float NextAction, NextValue;
        internal Reply Reply;
    }

    private static BotFeed? _active;
    private readonly Harmony _hooks = new("com.chazut.horizon.fika.feed");
    private ConditionalWeakTable<NetPeer, Viewer> _viewers = new();
    private readonly ClientActions _received = new();
    private readonly NetDataWriter _requestWriter = new();
    private readonly Func<Player, GameWorld, (string, string, bool)> _read;
    private readonly Action<string> _info, _warn;
    private readonly Dictionary<int, HostSample> _samples = new();
    private readonly HashSet<string> _clientStages = new();
    private GameWorld? _sampleWorld, _viewWorld;
    private FikaServer? _server;
    private FikaClient? _client;
    private NetPeer? _connection;
    private float _nextRequest, _lastReply, _nextRoster;
    private int _rosterIndex;
    private bool _hasReply;

    public BotFeed(Func<Player, GameWorld, (string, string, bool)> read, Action<string> info, Action<string> warn)
    {
        _read = read;
        _info = info;
        _warn = warn;
        try
        {
            _hooks.Patch(AccessTools.DeclaredMethod(typeof(FikaServer), "OnNetworkSettingsPacketReceived"),
                postfix: new HarmonyMethod(typeof(BotFeed), nameof(IdentifyPeer)));
            _hooks.Patch(AccessTools.DeclaredMethod(typeof(FikaServer), nameof(FikaServer.OnNetworkReceive)),
                prefix: new HarmonyMethod(typeof(BotFeed), nameof(ReceiveRequest)));
            _active = this;
            FikaEventDispatcher.OnFikaEvent += OnEvent;
        }
        catch { Dispose(); throw; }
    }

    // Fika assigns the player's NetId in this handler, including on reconnect.
    private static void IdentifyPeer(FikaServer __instance, NetworkSettingsPacket packet, NetPeer peer,
        Dictionary<string, int> ____cachedConnections)
    {
        if (_active == null || string.IsNullOrEmpty(packet.ProfileId) ||
            !____cachedConnections.TryGetValue(packet.ProfileId, out int id)) return;
        var viewer = _active.ViewerFor(__instance, peer);
        if (viewer.NetId != id) viewer.SentFirstReply = false;
        viewer.NetId = id;
        _active.HostLog(viewer, "identity", $"Fika assigned viewer={id}");
    }

    private Viewer ViewerFor(FikaServer server, NetPeer peer)
    {
        if (!ReferenceEquals(_server, server))
        {
            _server = server;
            _viewers = new();
            _samples.Clear();
            _sampleWorld = null;
        }
        return _viewers.GetValue(peer, _ => new Viewer());
    }

    private static bool ReceiveRequest(FikaServer __instance, NetPeer peer, NetPacketReader reader)
    {
        if (_active == null || !Requests.TryRead(reader, out var query)) return true;
        _active.Answer(__instance, peer, query);
        return false;
    }

    private void OnEvent(FikaEvent e)
    {
        if (e is PeerDisconnectedEvent disconnected)
        {
            _viewers.Remove(disconnected.Peer);
            if (ReferenceEquals(disconnected.Peer, _connection)) ClearClient();
        }
        if (e is FikaNetworkManagerDestroyedEvent destroyed)
        {
            if (ReferenceEquals(destroyed.Manager, _client)) { _client = null; ClearClient(); }
            if (ReferenceEquals(destroyed.Manager, _server))
            {
                _server = null;
                _sampleWorld = null;
                _samples.Clear();
                _viewers = new();
            }
        }
    }

    private void HostLog(Viewer viewer, string stage, string message)
    {
        if (viewer.Stages.Add(stage)) _info($"Fika bot sync [host] {message}.");
    }

    private void Answer(FikaServer server, NetPeer peer, Query query)
    {
        var viewer = ViewerFor(server, peer);
        try
        {
            float now = Time.unscaledTime;
            if (now >= viewer.WindowEnd) { viewer.WindowEnd = now + 1f; viewer.Count = 0; }
            if (viewer.Count >= 128) { HostLog(viewer, "limit", "request limit reached"); return; }
            viewer.Count++;
            if (viewer.Stages.Add("query")) _info($"Fika bot sync [host] request received: bot={query.BotId}, ticket={query.Ticket}.");
            if (viewer.NetId is not int id) { HostLog(viewer, "unbound", "waiting for Fika to identify requesting peer"); return; }
            var coop = server.CoopHandler;
            var game = coop?.LocalGameInstance;
            var world = Singleton<GameWorld>.Instance;
            if (world == null || coop?.Players == null || game?.GameController?.RaidStarted != true)
            { HostLog(viewer, "raid", "waiting for active host raid and bot registry"); return; }
            var settings = FikaPlugin.Instance.Settings;
            bool extracted = game.ExtractedPlayers.Contains(id) || coop.ExtractedPlayers?.Contains(id) == true;
            bool? alive = null;
            if (!extracted)
            {
                coop.Players.TryGetValue(id, out var human);
                // Fika may have removed the spectator from the active player registry.
                if (human == null)
                    foreach (var player in world.AllPlayersEverExisted)
                        if (player is ObservedPlayer observed && observed != null && observed.NetId == id && !observed.IsObservedAI)
                        { human = observed; break; }
                if (human is ObservedPlayer remote && !remote.IsObservedAI)
                    alive = remote.NetworkHealthController?.IsAlive ?? remote.HealthController?.IsAlive;
            }
            if (!Audience.Allows(settings.AllowSpectateFreeCam, settings.AllowSpectateBots, FikaBackendUtils.IsTransit, extracted, alive))
            {
                if (viewer.Stages.Add("audience:" + alive + ":" + extracted))
                    _info($"Fika bot sync [host] viewer={id} denied: freecam={settings.AllowSpectateFreeCam}, bots={settings.AllowSpectateBots}, transit={FikaBackendUtils.IsTransit}, extracted={extracted}, alive={alive?.ToString() ?? "unknown"}.");
                return;
            }
            coop.Players.TryGetValue(query.BotId, out var bot);
            if (bot == null)
                foreach (var player in world.AllAlivePlayersList)
                    if (player is FikaBot liveBot && liveBot.NetId == query.BotId) { bot = liveBot; break; }
            if (bot == null || !bot.IsAI ||
                bot.HealthController?.IsAlive != true || string.IsNullOrEmpty(bot.ProfileId))
            {
                if (viewer.Stages.Add("bot")) _info($"Fika bot sync [host] bot={query.BotId} missing, inactive or no longer alive.");
                return;
            }
            if (!ReferenceEquals(world, _sampleWorld)) { _samples.Clear(); _sampleWorld = world; }
            if (!_samples.TryGetValue(query.BotId, out var sample) || sample.Profile != bot.ProfileId)
                _samples[query.BotId] = sample = new HostSample { Profile = bot.ProfileId };
            if (now >= sample.NextAction)
            {
                sample.Reply.Profile = sample.Profile;
                (sample.Reply.Personality, sample.Reply.Action, sample.Reply.Ghost) = _read(bot, world);
                var owner = bot.AIData?.BotOwner;
                sample.Reply.SquadKnown = owner != null;
                sample.Reply.SquadId = owner?.BotsGroup?.Id;
                try { sample.Reply.Health = BotLabels.Health(bot); }
                catch (Exception e) { sample.Reply.Health = null; WarnOnce(viewer, "health", e); }
                sample.NextAction = now + .35f;
            }
            if (now >= sample.NextValue)
            {
                try { sample.Reply.Value = BotLabels.InventoryValue(bot); }
                catch (Exception e) { sample.Reply.Value = null; WarnOnce(viewer, "loot", e); }
                sample.NextValue = now + 5f;
            }
            sample.Reply.Query = query;
            var delivery = viewer.SentFirstReply ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered;
            server.SendDataToPeer(ref sample.Reply, delivery, peer);
            viewer.SentFirstReply = true;
            TraceSample(viewer.Stages, "host", "sent", sample.Reply);
        }
        catch (Exception e) { WarnOnce(viewer, "reply", e); }
    }

    private void WarnOnce(Viewer viewer, string stage, Exception e)
    {
        if (viewer.Stages.Add("error:" + stage)) _warn($"Fika bot sync [host] {stage} failed: {e.GetBaseException().Message}");
    }

    private void TraceSample(HashSet<string> stages, string side, string verb, Reply reply)
    {
        bool hasPersonality = !string.IsNullOrEmpty(reply.Personality);
        if (stages.Add(hasPersonality ? "with-personality" : "without-personality"))
        {
            string squad = reply.SquadKnown ? reply.SquadId?.ToString() ?? "solo" : "unknown";
            _info($"Fika bot sync [{side}] {verb}: bot={reply.Query.BotId}, ticket={reply.Query.Ticket}, action={reply.Action}, personality={(hasPersonality ? reply.Personality : "unavailable")}, squad={squad}.");
        }
    }

    private void ReceiveReply(Reply reply, NetPeer peer)
    {
        if (!ReferenceEquals(peer, _connection)) return;
        float now = Time.unscaledTime;
        var receipt = _received.Receive(reply, now);
        if (receipt != ClientActions.Receipt.Accepted)
        {
            if (_clientStages.Add(receipt.ToString())) _info($"Fika bot sync [client] discarded reply: reason={receipt}, bot={reply.Query.BotId}, ticket={reply.Query.Ticket}.");
            return;
        }
        _lastReply = now;
        _hasReply = true;
        TraceSample(_clientStages, "client", "accepted", reply);
    }

    internal override BotLabelSample ReadRemote(Player bot, GameWorld world)
    {
        var client = Singleton<FikaClient>.Instance;
        if (client == null || bot is not FikaPlayer remote || client.ServerConnection == null)
        {
            if (_clientStages.Add("connection")) _info("Fika bot sync [client] waiting for server connection or replicated bot.");
            return ClientActions.Unavailable;
        }
        var peer = client.ServerConnection;
        if (!ReferenceEquals(client, _client))
        {
            // Register before sending, even if the Fika manager was created before our listener.
            client.RegisterPacket<Reply, NetPeer>(ReceiveReply);
            _client = client;
            _info("Fika bot sync [client] registered RC3 reply handler.");
        }
        if (!ReferenceEquals(world, _viewWorld) || !ReferenceEquals(peer, _connection))
        {
            ClearClient();
            _viewWorld = world;
            _connection = peer;
        }
        float now = Time.unscaledTime;
        if (!_hasReply && now < _nextRequest) return ClientActions.Unavailable;
        var result = _received.Read(remote.NetId, bot.ProfileId, now, out var request);
        if (request is Query query)
        {
            Requests.Write(_requestWriter, query);
            peer.Send(_requestWriter, _hasReply ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered);
            if (_clientStages.Add("request"))
            {
                _lastReply = now;
                _info($"Fika bot sync [client] requesting bot={query.BotId}, ticket={query.Ticket}.");
            }
            if (now - _lastReply > 6f && _clientStages.Add("timeout"))
                _warn("Fika bot sync [client] no current host reply for 6s. Compare the host's Fika bot sync lines and check both HORIZON versions.");
            _nextRequest = now + 1f;
        }
        return result;
    }

    internal override (bool known, int? id) ReadSquad(Player bot) =>
        bot is FikaPlayer remote ? _received.Squad(remote.NetId, bot.ProfileId) : default;

    internal override void PollRoster(BotRoster roster, GameWorld world, float now)
    {
        if (now < _nextRoster || roster.Bots.Count == 0) return;
        for (int i = 0; i < roster.Bots.Count; i++)
        {
            _rosterIndex %= roster.Bots.Count;
            var bot = roster.Bots[_rosterIndex++].Player;
            if (bot is not FikaPlayer remote || !_received.NeedsSquad(remote.NetId, bot.ProfileId, now)) continue;
            ReadRemote(bot, world);
            break;
        }
        _nextRoster = now + .125f;
    }

    internal override void ClearClient()
    {
        _received.Clear();
        _clientStages.Clear();
        _viewWorld = null;
        _connection = null;
        _hasReply = false;
        _nextRequest = _lastReply = _nextRoster = 0;
        _rosterIndex = 0;
    }

    public override void Dispose()
    {
        if (ReferenceEquals(_active, this)) _active = null;
        FikaEventDispatcher.OnFikaEvent -= OnEvent;
        _hooks.UnpatchSelf();
        ClearClient();
        _viewers = new();
        _samples.Clear();
        _sampleWorld = null;
        _server = null;
        if (_client != null) _client.UnregisterPacket<Reply>();
        _client = null;
    }
}
