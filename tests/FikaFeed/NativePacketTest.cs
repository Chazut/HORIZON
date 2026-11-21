using System.Reflection;
using System.Runtime.CompilerServices;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using Horizon.Fika.V2;

internal static class NativePacketTest
{
    // Exercise Fika's actual framing, unicast and receiver registration without
    // starting Unity. Only the packet processor and the server writer are needed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static (Action<Reply, NetPeer, DeliveryMethod> send,
        Action<NetPeer, NetPacketReader, byte, DeliveryMethod> receive,
        Action<NetPeer, NetPacketReader, byte, DeliveryMethod> vanillaHost)
        Connect(Action<Reply, NetPeer> handler)
    {
        var server = (FikaServer)RuntimeHelpers.GetUninitializedObject(typeof(FikaServer));
        var client = (FikaClient)RuntimeHelpers.GetUninitializedObject(typeof(FikaClient));
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(FikaServer).GetField("_packetProcessor", flags)!.SetValue(server, new NetPacketProcessor());
        typeof(FikaServer).GetField("_dataWriter", flags)!.SetValue(server, new NetDataWriter());
        typeof(FikaClient).GetField("_packetProcessor", flags)!.SetValue(client, new NetPacketProcessor());
        client.RegisterPacket<Reply, NetPeer>(handler);
        return ((packet, peer, delivery) => server.SendDataToPeer(ref packet, delivery, peer),
            client.OnNetworkReceive, server.OnNetworkReceive);
    }
}
