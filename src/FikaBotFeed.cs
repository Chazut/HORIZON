using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;

[assembly: InternalsVisibleTo("HORIZON.Fika.Integration")]

namespace Horizon;

internal abstract class FikaBotFeed : IDisposable
{
    internal static FikaBotFeed Load(Func<Player, GameWorld, (string, string, bool)> read, Action<string> info, Action<string> warn)
    {
        return (FikaBotFeed)Activator.CreateInstance(LoadTransportType(), read, info, warn)!;
    }

    private static Type LoadTransportType()
    {
        const string resource = "HORIZON.Fika.Integration.dll";
        using var stream = typeof(FikaBotFeed).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Missing embedded transport: " + resource);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Assembly.Load(bytes.ToArray()).GetType("Horizon.Fika.BotFeed", true)!;
    }

    internal abstract BotLabelSample ReadRemote(Player bot, GameWorld world);
    internal abstract (bool known, int? id) ReadSquad(Player bot);
    internal abstract void PollRoster(BotRoster roster, GameWorld world, float now);
    internal abstract void ClearClient();
    public abstract void Dispose();
}
