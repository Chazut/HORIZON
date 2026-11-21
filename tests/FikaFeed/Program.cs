using System.Diagnostics;
using System.Net;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using Horizon.Fika.V1;

void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
}
var hostEvents = new EventBasedNetListener();
var guestEvents = new EventBasedNetListener();
var bystanderEvents = new EventBasedNetListener();
var host = new NetManager(hostEvents);
var guest = new NetManager(guestEvents);
var bystander = new NetManager(bystanderEvents);
var authority = new NetPacketProcessor();
var spectator = new NetPacketProcessor();
var cache = new ClientActions();
bool ready = false, modInstalled = false, extracted = false, alive = true, allowBots = true;
int probes = 0, replies = 0, otherPackets = 0, queries = 0;
float now = 1;
string action = "COMET: Crossfire";
bool ghost = false;
Reply lastReply = default;
const string profile = "bot-01";

void Send<T>(NetPacketProcessor processor, NetPeer peer, bool fromGuest, T packet) where T : INetSerializable
{
    var writer = new NetDataWriter();
    if (fromGuest) writer.Put(false); // Fika client's broadcast flag.
    writer.Put((byte)0); // Fika EPacketType.Serializable
    processor.WriteNetSerializable(writer, ref packet);
    peer.Send(writer, DeliveryMethod.Unreliable);
}
hostEvents.ConnectionRequestEvent += request => request.AcceptIfKey("horizon-test");
hostEvents.NetworkReceiveEvent += (peer, reader, _, _) =>
{
    if (modInstalled && Discovery.Matches(reader)) { probes++; Send(authority, peer, false, new Ready()); return; }
    Check(reader.GetByte() == 0, "requests must never broadcast");
    if (reader.GetByte() == 0) authority.ReadAllPackets(reader, peer);
    else probes++; // Fika 2.4.2 ignores unknown envelope types.
};
guestEvents.NetworkReceiveEvent += (peer, reader, _, _) =>
{
    Check(reader.GetByte() == 0, "server packet header");
    spectator.ReadAllPackets(reader, peer);
};
bystanderEvents.NetworkReceiveEvent += (_, _, _, _) => otherPackets++;
authority.SubscribeNetSerializable<Query, NetPeer>((request, peer) =>
{
    queries++;
    if (!Audience.Allows(true, allowBots, false, extracted, alive)) return;
    lastReply = new Reply { Query = request, Profile = profile, Action = action, Personality = "Aggressive", Ghost = ghost,
        Health = ghost ? (200f, 440f) : (395f, 440f), Value = ghost ? 0d : 51000d };
    Send(authority, peer, false, lastReply);
});
spectator.SubscribeNetSerializable<Ready, NetPeer>((_, _) => ready = true);
spectator.SubscribeNetSerializable<Reply, NetPeer>((data, _) => { replies++; cache.Receive(data, now); });

void WaitFor(Func<bool> condition, string name)
{
    var timeout = Stopwatch.StartNew();
    while (!condition() && timeout.ElapsedMilliseconds < 5000)
    {
        host.PollEvents(); guest.PollEvents(); bystander.PollEvents();
        Thread.Sleep(2);
    }
    Check(condition(), name);
}
void Request(float time)
{
    now = time;
    cache.Read(42, profile, now, out var request);
    Check(request.HasValue, "new or retried request");
    Send(spectator, guest.FirstPeer, true, request!.Value);
}
try
{
    foreach (var endpoint in new[] { host, guest, bystander })
        Check(endpoint.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0), "bind loopback");
    guest.Connect("127.0.0.1", host.LocalPort, "horizon-test");
    bystander.Connect("127.0.0.1", host.LocalPort, "horizon-test");
    WaitFor(() => host.ConnectedPeersCount == 2 && guest.ConnectedPeersCount == 1 && bystander.ConnectedPeersCount == 1, "two peers");
    guest.FirstPeer.Send(Discovery.Probe, DeliveryMethod.Unreliable);
    WaitFor(() => probes == 1, "unmodified host ignores probe");
    Check(!ready && queries == 0, "no custom packets sent to absent mod");
    modInstalled = true;
    guest.FirstPeer.Send(Discovery.Probe, DeliveryMethod.Unreliable);
    WaitFor(() => ready, "capability handshake");
    Request(1);
    WaitFor(() => queries == 1, "living client checked");
    Check(replies == 0, "living client cannot see bot data");
    alive = false;
    Request(1.6f);
    WaitFor(() => replies == 1, "spectator received host action");
    var first = cache.Read(42, profile, now, out _);
    Check(first.FromHost && first.Behavior == ("Aggressive", action, false) && first.Value == 51000d &&
        first.Health == (395f, 440f), "initial host label including health and loot");
    var old = lastReply;
    action = "ORBIT: Moving to extraction"; ghost = true;
    alive = true; extracted = true;
    Request(2.2f);
    WaitFor(() => replies == 2, "extracted spectator received updated action");
    cache.Receive(old, now);
    var second = cache.Read(42, profile, now, out _);
    Check(second.FromHost && second.Behavior == ("Aggressive", action, true) && second.Value == 0d &&
        second.Health == (200f, 440f), "updated label survives out-of-order replies");
    extracted = false;
    Request(5.4f);
    WaitFor(() => queries == 4, "revived client checked");
    Check(replies == 2 && !cache.Read(42, profile, now, out _).FromHost, "expired host action disappears");
    cache.Clear();
    cache.Read(42, profile, now, out var fresh);
    Check(fresh.HasValue && fresh.Value.Ticket != lastReply.Query.Ticket, "new raid has a new request identity");
    cache.Receive(lastReply, now);
    Check(!cache.Read(42, profile, now, out _).FromHost, "late raid data discarded");
    var wrong = lastReply; wrong.Query = fresh!.Value; wrong.Profile = "despawned-bot";
    cache.Receive(wrong, now);
    Check(!cache.Read(42, profile, now, out _).FromHost, "another profile cannot inherit the label");
    allowBots = false;
    extracted = true;
    Send(spectator, guest.FirstPeer, true, fresh.Value);
    WaitFor(() => queries == 5, "host checked a freecam-only spectator request");
    Check(lastReply.Query.Ticket != fresh.Value.Ticket && replies == 2, "freecam-only spectator received no bot action");
    Check(!Audience.Allows(false, true, false, true, true) && !Audience.Allows(true, false, false, false, false) &&
          !Audience.Allows(true, true, true, true, false) && !Audience.Allows(true, true, false, false, null),
          "server permissions, transit and unknown viewer");
    Check(otherPackets == 0, "uninvolved client received no bot information");
    Console.WriteLine("Fika UDP loopback passed: discovery, unicast, death/extraction, action changes, packet ordering, expiry and session reset.");
}
finally { bystander.Stop(); guest.Stop(); host.Stop(); }
