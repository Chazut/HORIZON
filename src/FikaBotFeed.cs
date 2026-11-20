using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;

[assembly: InternalsVisibleTo("HORIZON.Fika")]

namespace Horizon;

internal abstract class FikaBotFeed : IDisposable
{
    internal static FikaBotFeed Load(string pluginPath, Func<Player, GameWorld, (string, string, bool)> read, Action<string> warn)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(pluginPath)!, "HORIZON.Fika.dll"));
        var type = assembly.GetType("Horizon.Fika.BotFeed", true)!;
        return (FikaBotFeed)Activator.CreateInstance(type, read, warn)!;
    }

    internal abstract BotLabelSample ReadRemote(Player bot, GameWorld world);
    internal abstract void ClearClient();
    public abstract void Dispose();
}
