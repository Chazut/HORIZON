using System;
using System.Collections.Generic;
using Fika.Core.Networking.LiteNetLib.Utils;

namespace Horizon.Fika.V2;

// Carry the request itself in the non-broadcast extension envelope. A vanilla
// or older host ignores it. Only a compatible requester receives a typed reply.
internal static class Requests
{
    private static readonly byte[] Header = { 0, 0xDA, (byte)'H', (byte)'O', (byte)'R', (byte)'I', (byte)'Z', (byte)'O', (byte)'N', 2 };

    internal static void Write(NetDataWriter writer, Query query)
    {
        writer.Reset();
        writer.Put(Header);
        query.Serialize(writer);
    }

    internal static bool TryRead(NetDataReader reader, out Query query)
    {
        query = default;
        if (reader.AvailableBytes != Header.Length + sizeof(int) + sizeof(long) ||
            !new ReadOnlySpan<byte>(reader.RawData, reader.Position, Header.Length).SequenceEqual(Header)) return false;
        reader.SkipBytes(Header.Length);
        query.Deserialize(reader);
        return true;
    }
}

internal struct Query : INetSerializable
{
    public int BotId;
    public long Ticket;
    public void Serialize(NetDataWriter writer) { writer.Put(BotId); writer.Put(Ticket); }
    public void Deserialize(NetDataReader reader) { BotId = reader.GetInt(); Ticket = reader.GetLong(); }
}

internal struct Reply : INetSerializable
{
    public Query Query;
    public string Profile, Personality, Action;
    public bool Ghost;
    public bool SquadKnown;
    public int? SquadId;
    public (float current, float maximum)? Health;
    public double? Value;
    public void Serialize(NetDataWriter writer)
    {
        Query.Serialize(writer);
        writer.Put(Profile, 64);
        writer.Put(Personality, 64);
        writer.Put(Action, 180);
        writer.Put(Ghost);
        writer.Put(SquadKnown);
        writer.Put(SquadId.HasValue);
        if (SquadId.HasValue) writer.Put(SquadId.Value);
        writer.Put(Health.HasValue);
        if (Health is { } hp) { writer.Put(hp.current); writer.Put(hp.maximum); }
        writer.Put(Value.HasValue);
        if (Value.HasValue) writer.Put(Value.Value);
    }
    public void Deserialize(NetDataReader reader)
    {
        Query.Deserialize(reader);
        Profile = reader.GetString(64);
        Personality = reader.GetString(64);
        Action = reader.GetString(180);
        Ghost = reader.GetBool();
        SquadKnown = reader.GetBool();
        SquadId = reader.GetBool() ? reader.GetInt() : null;
        Health = reader.GetBool() ? (reader.GetFloat(), reader.GetFloat()) : null;
        Value = reader.GetBool() ? reader.GetDouble() : null;
    }
}

internal sealed class ClientActions
{
    internal enum Receipt { Accepted, UnknownBot, ProfileMismatch, ObsoleteTicket, Expired }
    private sealed class Entry
    {
        internal string Profile = "";
        internal long Ticket;
        internal float Requested, NextQuery, ValidUntil, NextSquadCheck;
        internal Reply Value;
        internal bool SquadKnown;
        internal int? SquadId;
    }

    private readonly Dictionary<int, Entry> _bots = new();
    private long _ticket;
    private const float Lifetime = 3f;
    internal static readonly BotLabelSample Unavailable = new(("", "Host action unavailable", false));

    internal BotLabelSample Read(int bot, string profile, float now, out Query? query)
    {
        query = null;
        if (!_bots.TryGetValue(bot, out var entry) || entry.Profile != profile)
            _bots[bot] = entry = new Entry { Profile = profile };
        if (now >= entry.NextQuery)
        {
            if (entry.Ticket == 0 || now - entry.Requested >= Lifetime)
            {
                entry.Ticket = ++_ticket;
                entry.Requested = now;
            }
            entry.NextQuery = now + .5f;
            query = new Query { BotId = bot, Ticket = entry.Ticket };
        }
        var value = entry.Value;
        return entry.ValidUntil > now ? new BotLabelSample((value.Personality, value.Action, value.Ghost),
            true, value.Health, value.Value) : Unavailable;
    }

    internal (bool known, int? id) Squad(int bot, string profile) =>
        _bots.TryGetValue(bot, out var entry) && entry.Profile == profile ? (entry.SquadKnown, entry.SquadId) : default;

    internal bool NeedsSquad(int bot, string profile, float now) =>
        !_bots.TryGetValue(bot, out var entry) || entry.Profile != profile || now >= entry.NextSquadCheck;

    internal Receipt Receive(Reply reply, float now)
    {
        if (!_bots.TryGetValue(reply.Query.BotId, out var entry)) return Receipt.UnknownBot;
        if (entry.Profile != reply.Profile) return Receipt.ProfileMismatch;
        if (entry.Ticket == 0 || entry.Ticket != reply.Query.Ticket) return Receipt.ObsoleteTicket;
        if (now >= entry.Requested + Lifetime) return Receipt.Expired;
        entry.Value = reply;
        if (reply.SquadKnown)
        {
            entry.SquadKnown = true;
            entry.SquadId = reply.SquadId;
        }
        entry.NextSquadCheck = now + (reply.SquadKnown ? 15f : 3f);
        entry.ValidUntil = entry.Requested + Lifetime;
        entry.Ticket = 0;
        return Receipt.Accepted;
    }

    internal void Clear() => _bots.Clear(); // Keep tickets unique across raids and camera sessions.
}

internal static class Audience
{
    internal static bool Allows(bool freeCamera, bool bots, bool transit, bool extracted, bool? alive) =>
        freeCamera && bots && !transit && (extracted || alive == false);
}
