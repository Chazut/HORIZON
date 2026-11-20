using System;
using System.Collections.Generic;
using Fika.Core.Networking.LiteNetLib.Utils;

namespace Horizon.Fika.V1;

// Fika throws on unregistered serializable packets. Discover a compatible host
// first with a non-broadcast, unhandled Fika envelope. An unmodified host ignores it.
internal static class Discovery
{
    internal static readonly byte[] Probe = { 0, 0xDA, (byte)'H', (byte)'O', (byte)'R', (byte)'I', (byte)'Z', (byte)'O', (byte)'N', 1 };
    internal static bool Matches(NetDataReader reader) => reader.AvailableBytes == Probe.Length &&
        new ReadOnlySpan<byte>(reader.RawData, reader.Position, Probe.Length).SequenceEqual(Probe);
}

internal struct Ready : INetSerializable
{
    public void Serialize(NetDataWriter writer) { }
    public void Deserialize(NetDataReader reader) { }
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
    public (float current, float maximum)? Health;
    public double? Value;
    public void Serialize(NetDataWriter writer)
    {
        Query.Serialize(writer);
        writer.Put(Profile, 64);
        writer.Put(Personality, 64);
        writer.Put(Action, 180);
        writer.Put(Ghost);
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
        Health = reader.GetBool() ? (reader.GetFloat(), reader.GetFloat()) : null;
        Value = reader.GetBool() ? reader.GetDouble() : null;
    }
}

internal sealed class ClientActions
{
    private sealed class Entry
    {
        internal string Profile = "";
        internal long Ticket;
        internal float Requested, NextQuery, ValidUntil;
        internal Reply Value;
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

    internal void Receive(Reply reply, float now)
    {
        if (!_bots.TryGetValue(reply.Query.BotId, out var entry) || entry.Ticket == 0 ||
            entry.Ticket != reply.Query.Ticket || entry.Profile != reply.Profile || now >= entry.Requested + Lifetime) return;
        entry.Value = reply;
        entry.ValidUntil = entry.Requested + Lifetime;
        entry.Ticket = 0;
    }

    internal void Clear() => _bots.Clear(); // Keep tickets unique across raids and camera sessions.
}

internal static class Audience
{
    internal static bool Allows(bool freeCamera, bool bots, bool transit, bool extracted, bool? alive) =>
        freeCamera && bots && !transit && (extracted || alive == false);
}
