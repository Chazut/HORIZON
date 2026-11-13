using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace RaidSpectator;

// Only BotDebug's display code is patched. Native bot lookup and Camera.main
// remain unchanged for the game and every other mod.
internal static class BotDebugHooks
{
    private static readonly Harmony Patches = new("com.chazut.horizon.botdebug");
    private static BotDebugView? _view;
    private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly MethodInfo NativeData = typeof(BotSpawner).GetMethod("GetBotDebugData", Methods)!;
    private static readonly MethodInfo NativeCamera = typeof(Camera).GetProperty("main", Methods)!.GetMethod!;
    private static readonly MethodInfo PlayerTransform = typeof(Player).GetProperty("Transform", Methods)!.GetMethod!;
    private static readonly MethodInfo TransformPosition = PlayerTransform.ReturnType.GetProperty("position", Methods)!.GetMethod!;

    public static void Install(Assembly assembly, BotDebugView view)
    {
        try
        {
            var component = assembly.GetType("DrakiaXYZ.BotDebug.Components.BotDebugComponent", true)!;
            var info = assembly.GetType("DrakiaXYZ.BotDebug.BotInfo", true)!;
            Patch(component, "Update", nameof(DataTranspiler));
            Patch(component, "OnGUI", nameof(DrawTranspiler));
            Patch(info, "GetBehaviour", nameof(DistanceTranspiler));
            _view = view;
        }
        catch { Remove(); throw; }
    }

    private static void Patch(Type type, string name, string transpiler) =>
        Patches.Patch(type.GetMethod(name, Methods | BindingFlags.DeclaredOnly) ?? throw new MissingMethodException(type.FullName, name),
            transpiler: new HarmonyMethod(typeof(BotDebugHooks), transpiler));

    public static void Remove() { Patches.UnpatchSelf(); _view?.Clear(); _view = null; }
    private static DebugBotStruct Read(BotSpawner spawner, IPlayer observer, string profileId) =>
        _view != null ? _view.Read(spawner, observer, profileId) : spawner.GetBotDebugData(observer, profileId);
    private static Camera DisplayCamera() => _view?.Camera ?? Camera.main;
    private static Vector3 Origin(Player player) => _view != null ? _view.Origin(player) : player.Transform.position;

    internal static IEnumerable<CodeInstruction> DataTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(i => new CodeInstruction(i)).ToList();
        int count = 0;
        foreach (var i in code)
            if (i.Calls(NativeData)) { i.opcode = OpCodes.Call; i.operand = typeof(BotDebugHooks).GetMethod(nameof(Read), Methods); count++; }
        if (count != 1) throw new InvalidOperationException("Unsupported BotDebug data accessor: " + count);
        return code;
    }

    internal static IEnumerable<CodeInstruction> DrawTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = ReplaceOrigin(instructions);
        int count = 0;
        foreach (var i in code)
            if (i.Calls(NativeCamera)) { i.operand = typeof(BotDebugHooks).GetMethod(nameof(DisplayCamera), Methods); count++; }
        if (count != 1) throw new InvalidOperationException("Unsupported BotDebug camera accessor: " + count);
        return code;
    }

    internal static IEnumerable<CodeInstruction> DistanceTranspiler(IEnumerable<CodeInstruction> instructions) => ReplaceOrigin(instructions);

    private static List<CodeInstruction> ReplaceOrigin(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(i => new CodeInstruction(i)).ToList();
        int count = 0;
        for (int n = 0; n + 1 < code.Count; n++)
            if (code[n].Calls(PlayerTransform) && code[n + 1].Calls(TransformPosition))
            {
                code[n].opcode = OpCodes.Call;
                code[n].operand = typeof(BotDebugHooks).GetMethod(nameof(Origin), Methods);
                code[n + 1].opcode = OpCodes.Nop;
                code[n + 1].operand = null;
                count++;
            }
        if (count != 1) throw new InvalidOperationException("Unsupported BotDebug observer position: " + count);
        return code;
    }
}
