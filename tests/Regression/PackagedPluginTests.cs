using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;

internal static class PackagedPluginTests
{
    internal static void Run(string zipPath, string gameRoot, string fikaPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var plugin = zip.Entries.Single(e => e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        if (plugin.FullName.Replace('\\', '/') != "BepInEx/plugins/HORIZON/HORIZON.dll")
            throw new InvalidDataException("Expected exactly one installable HORIZON.dll in the plugin folder.");

        bool enableFika = false;
        var context = AssemblyLoadContext.Default;
        string[] referenceDirs = {
            Path.Combine(gameRoot, "BepInEx/core"),
            Path.Combine(gameRoot, "EscapeFromTarkov_Data/Managed"),
            Path.Combine(gameRoot, "BepInEx/plugins/spt")
        };
        Assembly? Resolve(AssemblyLoadContext owner, AssemblyName name)
        {
            if (name.Name == "Fika.Core")
                return enableFika ? owner.LoadFromAssemblyPath(Path.GetFullPath(fikaPath)) :
                    throw new InvalidOperationException("HORIZON tried to load Fika during solo plugin discovery.");
            var path = referenceDirs.Select(dir => Path.Combine(dir, name.Name + ".dll")).FirstOrDefault(File.Exists);
            return path == null ? null : owner.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
        context.Resolving += Resolve;
        try
        {
            using var bytes = new MemoryStream();
            using (var input = plugin.Open()) input.CopyTo(bytes);
            bytes.Position = 0;
            var horizon = context.LoadFromStream(bytes);
            _ = horizon.GetTypes().Single(t => t.FullName == "Horizon.Plugin").GetCustomAttributesData();
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "Fika.Core" or "HORIZON.Fika.Integration"))
                throw new InvalidOperationException("Optional integration loaded during solo plugin discovery.");

            enableFika = true;
            var feed = horizon.GetType("Horizon.FikaBotFeed", true)!;
            var load = feed.GetMethod("LoadTransportType", BindingFlags.Static | BindingFlags.NonPublic)!;
            var transport = (Type)load.Invoke(null, null)!;
            var identify = transport.GetMethod("IdentifyPeer", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (transport.BaseType != feed || transport.Assembly.Location.Length != 0 ||
                identify.GetParameters()[0].ParameterType.Assembly.GetName().Name != "Fika.Core")
                throw new InvalidOperationException("Packaged transport failed to bind to HORIZON and Fika in memory.");
            Console.WriteLine("Single-DLL ZIP passed solo discovery and loaded its embedded transport with the supplied Fika runtime.");
        }
        finally { context.Resolving -= Resolve; }
    }
}
