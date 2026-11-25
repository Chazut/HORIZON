using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Horizon;

internal static class DynamicMapsHooks
{
    internal const string PluginId = "com.mpstark.dynamicmaps";
    private static readonly Harmony Patches = new("com.chazut.horizon.dynamicmaps");
    private const BindingFlags Methods = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly MethodInfo NativePosition = typeof(IPlayer).GetProperty(nameof(IPlayer.Position))!.GetMethod!;
    private static readonly MethodInfo NativeRotation = typeof(IPlayer).GetProperty(nameof(IPlayer.Rotation))!.GetMethod!;
    private static Func<IPlayer, (Camera? Camera, Player? Actor)>? _observer;
    internal static bool Installed => _observer != null;

    internal static void Install(Assembly maps, Func<IPlayer, (Camera?, Player?)> observer)
    {
        void Patch(string typeName, params string[] methods)
        {
            var type = maps.GetType(typeName, true)!;
            foreach (string name in methods)
            {
                var method = type.GetMethod(name, Methods) ?? throw new MissingMethodException(typeName, name);
                Patches.Patch(method, transpiler: new HarmonyMethod(typeof(DynamicMapsHooks), nameof(ReplaceDisplayReads)));
            }
        }

        try
        {
            Patch("DynamicMaps.UI.ModdedMapScreen", "OnShowInRaid", "CenterOnPlayer", "OnZoomMain", "OnZoomMini");
            Patch("DynamicMaps.UI.Components.PlayerMapMarker", "Create", "LateUpdate");
            Patch("DynamicMaps.UI.Controls.PlayerPositionText", "Update");
            Patch("DynamicMaps.PlayerDotSpawner", "Update");
            _observer = observer;
        }
        catch (Exception installError)
        {
            try { Remove(); }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Could not install or fully remove the DynamicMaps display hooks", installError, cleanupError);
            }
            throw;
        }
    }

    internal static void Remove()
    {
        _observer = null;
        Patches.UnpatchSelf();
    }

    internal static Player? EligibleTarget(GameWorld world, Player? followed, bool allowBots) =>
        PmcNavigator.IsLivingRemote(followed) && (allowBots || !PmcNavigator.IsBot(followed!)) &&
        ReferenceEquals(world.GetAlivePlayerByProfileID(followed!.ProfileId), followed) ? followed : null;

    private static Vector3 Position(IPlayer player)
    {
        var (camera, actor) = _observer?.Invoke(player) ?? default;
        return actor != null ? actor.Position : camera != null ? camera.transform.position : player.Position;
    }

    private static Vector2 Rotation(IPlayer player)
    {
        var (camera, actor) = _observer?.Invoke(player) ?? default;
        return actor != null ? actor.Rotation : camera != null ? new Vector2(camera.transform.rotation.eulerAngles.y, 0f) : player.Rotation;
    }

    private static bool HasCamera(IPlayer? player) => player != null && _observer?.Invoke(player).Camera != null;

    // Only replace reads by DynamicMaps' local UI. Its coordinate conversion, floor
    // selection, update intervals and marker visibility still run in the original methods.
    internal static IEnumerable<CodeInstruction> ReplaceDisplayReads(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var code = instructions.Select(i => new CodeInstruction(i)).ToList();
        bool markerUpdate = original.Name == "LateUpdate";
        int positions = 0, rotations = 0;
        foreach (var i in code)
        {
            string replacement;
            if (i.Calls(NativePosition)) { replacement = nameof(Position); positions++; }
            else if (i.Calls(NativeRotation)) { replacement = nameof(Rotation); rotations++; }
            else continue;
            i.opcode = OpCodes.Call;
            i.operand = typeof(DynamicMapsHooks).GetMethod(replacement, Methods);
        }
        if (positions != 1 || rotations != (markerUpdate ? 1 : 0))
            throw new InvalidOperationException("Unrecognized DynamicMaps coordinate reads in " + original);
        if (markerUpdate)
        {
            // The body transform can disappear after death. Reuse the native marker
            // update and callback timer even then, but only for the local spectator.
            int guard = code.FindIndex(i => i.opcode == OpCodes.Ret);
            var getPlayer = original.DeclaringType!.GetProperty("Player", Methods)?.GetMethod ?? throw new MissingMemberException("DynamicMaps marker Player");
            if (guard < 2 || guard + 1 >= code.Count || !code[1].Calls(getPlayer) ||
                (code[guard - 1].opcode != OpCodes.Brfalse_S && code[guard - 1].opcode != OpCodes.Brfalse) ||
                code[guard - 1].operand is not Label update)
                throw new InvalidOperationException("Unrecognized DynamicMaps marker body guard");
            code.InsertRange(0, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, getPlayer),
                new CodeInstruction(OpCodes.Call, typeof(DynamicMapsHooks).GetMethod(nameof(HasCamera), Methods)),
                new CodeInstruction(OpCodes.Brtrue, update)
            });
        }
        return code;
    }
}
