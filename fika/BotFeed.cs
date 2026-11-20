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
using Fika.Core.Networking.Packets.Backend;
using HarmonyLib;
using Horizon.Fika.V1;
using UnityEngine;

namespace Horizon.Fika;

internal sealed class BotFeed : FikaBotFeed
{
    private sealed class Viewer
    {
        internal FikaServer Server = null!;
        internal int NetId, Count;
        internal float WindowEnd, NextReady;
    }
    private static BotFeed? _active;
    private sealed class HostSample
    {
        internal string Profile = "";
        internal float NextAction, NextValue;
        internal Reply Reply;
    }
    private readonly Harmony _hooks = new("com.chazut.horizon.fika.feed");
    private readonly ConditionalWeakTable<NetPeer, Viewer> _viewers = new();
    private readonly ClientActions _received = new();
    private readonly Func<Player, GameWorld, (string, string, bool)> _read;
    private readonly Action<string> _warn;
    private readonly Dictionary<int, HostSample> _samples = new();
    private GameWorld? _sampleWorld, _viewWorld;
    private IFikaNetworkManager? _manager;
    private NetPeer? _connection;
    private bool _ready, _reportedFailure;
    private float _nextProbe;

    public BotFeed(Func<Player, GameWorld, (string, string, bool)> read, Action<string> warn)
    {
        _read = read;
        _warn = warn;
        try
        {
            _hooks.Patch(AccessTools.DeclaredMethod(typeof(FikaServer), "OnNetworkSettingsPacketReceived"),
                postfix: new HarmonyMethod(typeof(BotFeed), nameof(IdentifyPeer)));
            _hooks.Patch(AccessTools.DeclaredMethod(typeof(FikaServer), nameof(FikaServer.OnNetworkReceive)),
                prefix: new HarmonyMethod(typeof(BotFeed), nameof(Discover)));
            _active = this;
            FikaEventDispatcher.OnFikaEvent += OnEvent;
        }
        catch { Dispose(); throw; }
    }

    // Use the identity Fika assigned to this connection, including on reconnect.
    private static void IdentifyPeer(FikaServer __instance, NetworkSettingsPacket packet, NetPeer peer,
        Dictionary<string, int> ____cachedConnections)
    {
        if (_active == null || string.IsNullOrEmpty(packet.ProfileId) ||
            !____cachedConnections.TryGetValue(packet.ProfileId, out int id)) return;
        var viewer = _active._viewers.GetOrCreateValue(peer);
        viewer.Server = __instance;
        viewer.NetId = id;
    }

    private static bool Discover(FikaServer __instance, NetPeer peer, NetPacketReader reader)
    {
        if (_active == null || !Discovery.Matches(reader)) return true;
        _active.OnProbe(__instance, peer);
        return false;
    }

    private void OnProbe(FikaServer server, NetPeer peer)
    {
        if (!ReferenceEquals(server, _manager) || !_viewers.TryGetValue(peer, out var viewer) ||
            !ReferenceEquals(server, viewer.Server) || Time.unscaledTime < viewer.NextReady) return;
        viewer.NextReady = Time.unscaledTime + 1f;
        var ready = new Ready();
        server.SendDataToPeer(ref ready, DeliveryMethod.Unreliable, peer);
    }

    private void OnEvent(FikaEvent e)
    {
        if (e is PeerDisconnectedEvent disconnected)
        {
            _viewers.Remove(disconnected.Peer);
            if (ReferenceEquals(disconnected.Peer, _connection)) ClearClient();
        }
        if (e is FikaNetworkManagerDestroyedEvent destroyed && ReferenceEquals(_manager, destroyed.Manager))
        {
            _manager = null;
            ClearClient();
            _sampleWorld = null;
            _samples.Clear();
        }
        if (e is not FikaNetworkManagerCreatedEvent created) return;
        _manager = created.Manager;
        ClearClient();
        if (created.Manager is FikaServer server)
            server.RegisterPacket<Query, NetPeer>((query, peer) => Answer(server, peer, query));
        else if (created.Manager is FikaClient client)
        {
            client.RegisterPacket<Ready, NetPeer>((_, peer) =>
            {
                if (ReferenceEquals(client, _manager) && ReferenceEquals(peer, _connection)) _ready = true;
            });
            client.RegisterPacket<Reply, NetPeer>((reply, peer) =>
            {
                if (ReferenceEquals(client, _manager) && ReferenceEquals(peer, _connection))
                    _received.Receive(reply, Time.unscaledTime);
            });
        }
    }

    private void Answer(FikaServer server, NetPeer peer, Query query)
    {
        if (!ReferenceEquals(server, _manager) || !_viewers.TryGetValue(peer, out var viewer) ||
            !ReferenceEquals(server, viewer.Server)) return;
        try
        {
            var now = Time.unscaledTime;
            if (now >= viewer.WindowEnd) { viewer.WindowEnd = now + 1f; viewer.Count = 0; }
            if (viewer.Count >= 128) return;
            viewer.Count++;
            var world = Singleton<GameWorld>.Instance;
            var coop = server.CoopHandler;
            if (world == null || coop?.Players == null || coop.ExtractedPlayers == null ||
                Singleton<AbstractGame>.Instance is not BaseLocalGame<EftGamePlayerOwner> game ||
                game.Status != GameStatus.Started || !ReferenceEquals(world, game.GameWorld)) return;
            coop.Players.TryGetValue(viewer.NetId, out var human);
            var settings = FikaPlugin.Instance.Settings;
            if (!Audience.Allows(settings.AllowSpectateFreeCam, settings.AllowSpectateBots, FikaBackendUtils.IsTransit,
                coop.ExtractedPlayers.Contains(viewer.NetId), human != null ? human.HealthController?.IsAlive : null)) return;
            if (!coop.Players.TryGetValue(query.BotId, out var bot) || bot == null || !bot.IsAI ||
                bot.HealthController?.IsAlive != true || string.IsNullOrEmpty(bot.ProfileId)) return;
            if (!ReferenceEquals(world, _sampleWorld)) { _samples.Clear(); _sampleWorld = world; _reportedFailure = false; }
            if (!_samples.TryGetValue(query.BotId, out var sample) || sample.Profile != bot.ProfileId)
                _samples[query.BotId] = sample = new HostSample { Profile = bot.ProfileId };
            if (now >= sample.NextAction)
            {
                sample.Reply.Profile = sample.Profile;
                (sample.Reply.Personality, sample.Reply.Action, sample.Reply.Ghost) = _read(bot, world);
                try { sample.Reply.Health = BotLabels.Health(bot); }
                catch (Exception e) { sample.Reply.Health = null; WarnOnce(e); }
                sample.NextAction = now + .35f;
            }
            if (now >= sample.NextValue)
            {
                try { sample.Reply.Value = BotLabels.InventoryValue(bot); }
                catch (Exception e) { sample.Reply.Value = null; WarnOnce(e); }
                sample.NextValue = now + 5f;
            }
            sample.Reply.Query = query;
            server.SendDataToPeer(ref sample.Reply, DeliveryMethod.Unreliable, peer);
        }
        catch (Exception e)
        {
            WarnOnce(e);
        }
    }

    private void WarnOnce(Exception e)
    {
        if (!_reportedFailure) _warn("Fika host bot data unavailable: " + e.GetBaseException().Message);
        _reportedFailure = true;
    }

    internal override BotLabelSample ReadRemote(Player bot, GameWorld world)
    {
        if (_manager is not FikaClient client || bot is not FikaPlayer remote || client.ServerConnection == null)
            return ClientActions.Unavailable;
        var peer = client.ServerConnection;
        if (!ReferenceEquals(world, _viewWorld) || !ReferenceEquals(peer, _connection))
        {
            ClearClient();
            _viewWorld = world;
            _connection = peer;
        }
        float now = Time.unscaledTime;
        if (!_ready)
        {
            if (now >= _nextProbe)
            {
                _nextProbe = now + 3f;
                peer.Send(Discovery.Probe, DeliveryMethod.Unreliable);
            }
            return ClientActions.Unavailable;
        }
        var result = _received.Read(remote.NetId, bot.ProfileId, now, out var request);
        if (request is Query query) client.SendData(ref query, DeliveryMethod.Unreliable);
        return result;
    }

    internal override void ClearClient()
    {
        _received.Clear();
        _viewWorld = null;
        _connection = null;
        _ready = false;
        _nextProbe = 0;
    }

    public override void Dispose()
    {
        _manager = null;
        if (ReferenceEquals(_active, this)) _active = null;
        FikaEventDispatcher.OnFikaEvent -= OnEvent;
        _hooks.UnpatchSelf();
        ClearClient();
        _samples.Clear();
        _sampleWorld = null;
    }
}
