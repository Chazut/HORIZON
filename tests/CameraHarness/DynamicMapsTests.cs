using System.Reflection;
using EFT;
using Horizon;
using UnityEngine;

internal static class DynamicMapsTests
{
    internal static void Run(Action<bool, string> check)
    {
        var world = new GameWorld();
        var local = new Player { IsYourPlayer = true, Rotation = new(10, 0) };
        var bot = new Player { ProfileId = "bot", IsAI = true, Rotation = new(75, 0) };
        local.transform.position = new(13, 1, 25);
        bot.transform.position = new(30, 5, 41);
        world.RegisterPlayer(bot);
        var camera = new Camera();
        camera.transform.position = new(25, 8, 55);
        camera.transform.rotation = new Quaternion { eulerAngles = new(45, 130, 0) };
        Player? followed = null;
        bool tracking = false, allowBots = true;
        Func<IPlayer, (Camera?, Player?)> observer = marker => tracking && ReferenceEquals(marker, local)
            ? (camera, DynamicMapsHooks.EligibleTarget(world, followed, allowBots)) : default;
        var hooks = typeof(DynamicMapsHooks);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var provider = hooks.GetField("_observer", flags)!;
        var pos = hooks.GetMethod("Position", flags)!.CreateDelegate<Func<IPlayer, Vector3>>();
        var rot = hooks.GetMethod("Rotation", flags)!.CreateDelegate<Func<IPlayer, Vector2>>();
        var hasCamera = hooks.GetMethod("HasCamera", flags)!.CreateDelegate<Func<IPlayer?, bool>>();
        provider.SetValue(null, observer);
        try
        {
            check(pos(local).Equals(local.Position) && rot(local).x == 10 && !hasCamera(local), "Ordinary local map reads fall through");
            tracking = true;
            check(pos(local).Equals(camera.transform.position) && rot(local).x == 130 && hasCamera(local), "Freecam map follows camera position, height and yaw");
            check(pos(bot).Equals(bot.Position) && rot(bot).x == 75 && !hasCamera(bot), "Other markers retain their own position and heading");
            followed = bot;
            check(pos(local).Equals(bot.Position) && rot(local).x == 75, "Orbit view uses the followed actor even while the camera looks elsewhere");
            allowBots = false;
            check(pos(local).Equals(camera.transform.position), "A Fika bot restriction falls back to the camera");
            bot.IsAI = false;
            check(pos(local).Equals(bot.Position), "Following a human is still permitted");
            bot.HealthController.IsAlive = false;
            check(pos(local).Equals(camera.transform.position), "A dead target falls back to freecam");
            bot.HealthController.IsAlive = true;
            world.UnregisterPlayer(bot);
            check(pos(local).Equals(camera.transform.position), "An extracted target falls back even when still marked alive");
            tracking = false;
            check(pos(local).Equals(local.Position) && !hasCamera(null), "Stopping spectator tracking restores native coordinates");
        }
        finally { provider.SetValue(null, null); }

        var battleUi = new GameObject("BattleUI", typeof(Canvas));
        battleUi.GetComponent<Canvas>().scaleFactor = 1.5f;
        var peek = new GameObject().transform;
        peek.SetParent(battleUi.transform, false);
        peek.SetSiblingIndex(3);
        var mapHud = new SpectatorMapHud(() => peek);
        mapHud.Update(true, 0);
        check(peek.parent == battleUi.transform, "An active battle HUD keeps the native minimap hierarchy");
        battleUi.SetActive(false);
        mapHud.Update(true, 2);
        check(peek.gameObject.activeInHierarchy && !battleUi.activeSelf && peek.parent!.gameObject.GetComponent<Canvas>().scaleFactor == 1.5f,
            "Spectating can show the map at its own UI scale while Fika hides BattleUIScreen");
        mapHud.Update(false, 3);
        check(peek.parent == battleUi.transform && peek.GetSiblingIndex() == 3, "Opt-out restores the map's parent and sibling order");
        peek.gameObject.SetActive(false);
        mapHud.Update(true, 4);
        check(peek.parent == battleUi.transform, "A disabled map controller stays disabled");
        peek.gameObject.SetActive(true);
        mapHud.Update(true, 5);
        battleUi.SetActive(true);
        mapHud.Update(true, 6);
        check(peek.parent == battleUi.transform, "The live battle HUD takes back its minimap as soon as it returns");
        mapHud.Restore();
    }
}
