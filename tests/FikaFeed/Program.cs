using System.Diagnostics;
using System.Net;
using System.Runtime.Loader;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using Horizon.Fika.V2;

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
var root = args.Length > 0 ? args[0] : @"C:\Games\SPT-4.1";
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    foreach (var dir in new[] { "EscapeFromTarkov_Data/Managed", "BepInEx/core", "BepInEx/plugins/spt" })
    {
        var path = Path.Combine(root, dir, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(Path.GetFullPath(path));
    }
    return null;
};
var cache = new ClientActions();
bool modInstalled = false, extracted = false, alive = true, allowBots = true;
int ignored = 0, replies = 0, otherPackets = 0, queries = 0;
float now = 1;
string action = "COMET: Crossfire";
bool ghost = false;
Reply lastReply = default;
const string profile = "bot-01";
var endpoints = NativePacketTest.Connect((data, _) => { replies++; cache.Receive(data, now); });

void SendRequest(Query request)
{
    var writer = new NetDataWriter();
    Requests.Write(writer, request);
    guest.FirstPeer.Send(writer, replies == 0 ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
}
hostEvents.ConnectionRequestEvent += request => request.AcceptIfKey("horizon-test");
hostEvents.NetworkReceiveEvent += (peer, reader, channel, delivery) =>
{
    if (!modInstalled || !Requests.TryRead(reader, out var request))
    {
        endpoints.vanillaHost(peer, reader, channel, delivery);
        ignored++;
        return;
    }
    queries++;
    if (!Audience.Allows(true, allowBots, false, extracted, alive)) return;
    lastReply = new Reply { Query = request, Profile = profile, Action = action, Personality = "Rat", Ghost = ghost,
        SquadKnown = true, SquadId = ghost ? null : 0, Health = ghost ? (200f, 440f) : (395f, 440f), Value = ghost ? 0d : 51000d };
    endpoints.send(lastReply, peer, replies == 0 ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
};
guestEvents.NetworkReceiveEvent += (peer, reader, channel, delivery) => endpoints.receive(peer, reader, channel, delivery);
bystanderEvents.NetworkReceiveEvent += (_, _, _, _) => otherPackets++;

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
    SendRequest(request!.Value);
}
try
{
    foreach (var endpoint in new[] { host, guest, bystander })
        Check(endpoint.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0), "bind loopback");
    guest.Connect("127.0.0.1", host.LocalPort, "horizon-test");
    bystander.Connect("127.0.0.1", host.LocalPort, "horizon-test");
    WaitFor(() => host.ConnectedPeersCount == 2 && guest.ConnectedPeersCount == 1 && bystander.ConnectedPeersCount == 1, "two peers");
    SendRequest(new Query { BotId = 42, Ticket = 1 });
    WaitFor(() => ignored == 1, "unmodified host ignores request");
    Check(replies == 0 && queries == 0, "absent mod is harmless");
    modInstalled = true;
    guest.FirstPeer.Send(new byte[] { 0, 0xDA, 72, 79, 82, 73, 90, 79, 78, 1 }, DeliveryMethod.Unreliable);
    WaitFor(() => ignored == 2, "RC2 probe ignored without consuming the reader or sending an incompatible reply");
    Request(1);
    WaitFor(() => queries == 1, "living client checked");
    Check(replies == 0, "living client cannot see bot data");
    alive = false;
    Request(1.6f);
    WaitFor(() => replies == 1, "spectator received host action");
    var first = cache.Read(42, profile, now, out _);
    Check(cache.Squad(42, profile) == (true, 0) && !cache.Squad(42, "other").known, "host squad zero scoped to matching bot");
    Check(!cache.NeedsSquad(42, profile, now + 10) && cache.NeedsSquad(42, profile, now + 16),
        "list-only spectators reuse the host squad and later refresh it");
    Check(first.FromHost && first.Behavior == ("Rat", action, false) && first.Value == 51000d &&
        first.Health == (395f, 440f), "initial host label including health and loot");
    var old = lastReply;
    action = "ORBIT: Moving to extraction"; ghost = true;
    alive = true; extracted = true;
    Request(2.2f);
    WaitFor(() => replies == 2, "extracted spectator received updated action");
    cache.Receive(old, now);
    var second = cache.Read(42, profile, now, out _);
    Check(cache.Squad(42, profile) == (true, (int?)null), "authoritative transition from squad zero to solo");
    Check(second.FromHost && second.Behavior == ("Rat", action, true) && second.Value == 0d &&
        second.Health == (200f, 440f), "updated label survives out-of-order replies");
    extracted = false;
    Request(5.4f);
    WaitFor(() => queries == 4, "revived client checked");
    Check(replies == 2 && !cache.Read(42, profile, now, out _).FromHost, "expired host action disappears");
    cache.Clear();
    Check(!cache.Squad(42, profile).known, "squad does not leak into a new session");
    cache.Read(42, profile, now, out var fresh);
    Check(fresh.HasValue && fresh.Value.Ticket != lastReply.Query.Ticket, "new raid has a new request identity");
    cache.Receive(lastReply, now);
    Check(!cache.Read(42, profile, now, out _).FromHost, "late raid data discarded");
    var wrong = lastReply; wrong.Query = fresh!.Value; wrong.Profile = "despawned-bot";
    cache.Receive(wrong, now);
    Check(!cache.Read(42, profile, now, out _).FromHost, "another profile cannot inherit the label");
    allowBots = false;
    extracted = true;
    SendRequest(fresh.Value);
    WaitFor(() => queries == 5, "host checked a freecam-only spectator request");
    Check(lastReply.Query.Ticket != fresh.Value.Ticket && replies == 2, "freecam-only spectator received no bot action");
    Check(!Audience.Allows(false, true, false, true, true) && !Audience.Allows(true, false, false, false, false) &&
          !Audience.Allows(true, true, true, true, false) && !Audience.Allows(true, true, false, false, null),
          "server permissions, transit and unknown viewer");
    Check(otherPackets == 0, "uninvolved client received no bot information");
    Console.WriteLine("Fika UDP loopback passed: direct requests, unicast, spectator permissions, action, personality, squads and session reset.");
}
finally { bystander.Stop(); guest.Stop(); host.Stop(); }
