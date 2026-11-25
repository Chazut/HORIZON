using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.InventoryLogic;
using Horizon;
using SAIN.Components;
using UnityEngine;
using OrbitApi = Orbit.Api.OrbitTelemetry;
using CometApi = Comet.Api.Decisions;
using QuestApi = QuestingBots.Controllers.BotJobAssignmentFactory;

internal static class BotInfoTests
{
    public static void Run(Action<bool, string> check)
    {
        check(BotNames.Display("Дед Мороз", "id") == "Santa Claus" && BotNames.Display("Партизан", "id") == "Partizan",
            "Established English names take priority over transliteration");
        check(BotNames.Display("Shadow_ЮЛИЯ-ЩУКА42", "id") == "Shadow_YULIYA-SHCHUKA42" &&
            BotNames.Display("Журавлёв Тестовый", "id") == "Zhuravlyov Testovyy" &&
            BotNames.Display("Ілля Ґринь", "id") == "Illya Grin", "Unknown names retain casing, separators and mixed scripts");
        check(BotNames.Display("René 日本", "id") == "René 日本" && BotNames.Display("<b></b>", "id") == "id",
            "Other scripts and missing-name fallbacks are preserved");
        var world = new GameWorld();
        Player Bot(string id, string name, int? group, WildSpawnType role = WildSpawnType.pmcUSEC)
        {
            var p = new Player { IsAI = true, ProfileId = id };
            p.Profile.Nickname = name; p.Profile.Info.Settings.Role = role;
            p.AIData.BotOwner = new BotOwner { BotsGroup = group.HasValue ? new BotGroup { Id = group.Value } : null };
            world.AllAlivePlayersList.Add(p);
            return p;
        }
        var a = Bot("a", "Alpha", 1);
        var b = Bot("b", "Bravo", 1);
        var c = Bot("c", "Scav", 2, WildSpawnType.assault);
        var solo = Bot("solo", "Solo", null);
        var solo2 = Bot("solo2", "Solo 2", null);
        var dead = Bot("dead", "Dead", 1); dead.HealthController.IsAlive = false;
        var inactive = Bot("inactive", "Inactive", 1); inactive.gameObject.SetActive(false);
        world.AllAlivePlayersList.Add(new Player());
        world.AllAlivePlayersList.Add(a); // Do not show duplicates from transitional world lists.
        var roster = new BotRoster();
        roster.Refresh(world, 0);
        check(roster.Bots.Count == 5, "Roster excludes humans, corpses, inactive bots and duplicate IDs");
        check(roster.Bots[0].Id == "a" && roster.Bots[1].Id == "b" && roster.Bots[0].ColorIndex == roster.Bots[1].ColorIndex, "Squad members stay together and share a color");
        check(roster.Bots.Single(x => x.Id == "c").Kind == "Scav", "List includes non-PMC types independently of wheel filter");
        check(roster.Bots.Single(x => x.Id == "c").ColorIndex == BotPresentation.ScavColor, "Scav squads remain white");
        check(BotPresentation.RoleColorIndex("bossKilla", "group:1") == BotPresentation.BossColor && BotPresentation.RoleColorIndex("followerBigPipe", "group:1") == BotPresentation.BossColor, "Bosses and Goons use the boss color");
        check(BotPresentation.RoleColorIndex("followerBully", "group:1") == BotPresentation.GuardColor, "Boss guards have their own color");
        check(new[] { "exUsec", "pmcBot", "sectantWarrior", "infectedAssault", "customFaction" }.All(r => BotPresentation.RoleColorIndex(r, "group:1") == BotPresentation.SpecialColor), "Other factions use the special color");
        check(roster.Bots.Single(x => x.Id == "solo").Group != roster.Bots.Single(x => x.Id == "solo2").Group, "Unassigned bots are not invented squad mates");
        int color = roster.Bots[0].ColorIndex;
        world.AllAlivePlayersList.Reverse(); roster.Refresh(world, 2);
        check(roster.Bots[0].Id == "a" && roster.Bots[0].ColorIndex == color, "World-list reordering does not change group order or colors");
        b.AIData.BotOwner!.BotsGroup = new BotGroup { Id = 2 }; roster.Refresh(world, 3);
        check(roster.Bots.Single(x => x.Id == "b").Group == roster.Bots.Single(x => x.Id == "c").Group, "Live group changes are reflected");
        world.AllAlivePlayersList.Remove(c); roster.Refresh(world, 4);
        check(roster.Bots.All(x => x.Id != "c"), "Extracted/despawned bots leave the list");

        var warnings = new List<string>();
        var data = new OptionalBotData(warnings.Add);
        var native = data.ReadBot(a, world);
        check(native.personality == "" && native.action == "simple Patrol" && !native.ghost, "No optional assemblies: native action remains available");
        a.AIData.BotOwner!.Brain.Layer = "SAIN : Combat Layer";
        check(data.ReadBot(a, world).action.Contains("Action Unavailable"), "Missing SAIN data is explicit, not a fabricated decision");
        data.Bind(new[] { typeof(BotInfoTests).Assembly }, true);
        check(data.ReadBot(a, world).personality == "GigaChad", "SAIN's host API supplies personality when component discovery misses");
        var sain = new BotComponent();
        var manager = new BotManagerComponent(); manager.Bots[a.ProfileId] = sain;
        world.gameObject.Components.Add(typeof(BotManagerComponent), manager);
        sain.Decision.CurrentCombatDecision = "StandAndShoot";
        CometApi.Current = "COMET:Shoot";
        var combat = data.ReadBot(a, world);
        check(combat.personality == "Rat" && combat.action == "COMET: Shoot", "Manager lookup reads SAIN personality and matching COMET execution");
        CometApi.Current = "COMET:Push";
        check(data.ReadBot(a, world).action == "SAIN: Stand And Shoot", "Stale COMET plan cannot claim a different native action");
        foreach (var (role, action) in new[] {
            ("Siege - watch exit", "Freeze"),
            ("Grenade suppression - throw", "ThrowGrenade"),
            ("Grenade suppression - hold angle", "Freeze"),
            ("Grenade suppression - hold angle", "StandAndShoot"),
            ("Flank", "ShiftCover")
        })
        {
            CometApi.Current = "COMET:" + role;
            sain.Decision.CurrentCombatDecision = action;
            check(data.ReadBot(a, world).action == "COMET: " + role, $"BotInfo shows the active {role} role for {action}");
            sain.Decision.CurrentSelfDecision = "Reload";
            check(data.ReadBot(a, world).action == "SAIN: Reload", $"{role} yields to an actual reload");
            sain.Decision.CurrentSelfDecision = "None";
            sain.Decision.CurrentCombatDecision = "SeekCover";
            check(data.ReadBot(a, world).action == "SAIN: Seek Cover", $"BotInfo drops {role} when the bot changes action");
        }
        CometApi.Current = "";
        sain.Decision.CurrentCombatDecision = "Freeze";
        check(data.ReadBot(a, world).action == "SAIN: Freeze", "Holding an angle alone does not imply a siege or grenade plan");
        CometApi.Current = "COMET:Siege - watch exit";
        OrbitApi.Ghost = true;
        check(data.ReadBot(a, world).action == "SAIN: Freeze", "Dormant bots do not claim an active siege");
        OrbitApi.Ghost = false;
        sain.Decision.CurrentCombatDecision = "StandAndShoot";
        sain.Decision.CurrentSelfDecision = "Reload";
        CometApi.Current = "COMET:Reload";
        check(data.ReadBot(a, world).action == "COMET: Reload", "Self action takes priority over combat");
        a.AIData.BotOwner.Brain.Layer = "OrbitBrainLayer";
        OrbitApi.Current = new Orbit.Api.Objective();
        check(data.ReadBot(a, world).action == "Orbit: Container Loot", "ORBIT active objective wins over stale SAIN/COMET decisions");
        OrbitApi.Current.Status = "Extracting";
        check(data.ReadBot(a, world).action == "Orbit: Exfil", "ORBIT extraction display");
        OrbitApi.Current.Status = "Finished"; OrbitApi.Current.Category = "SquadCover";
        check(data.ReadBot(a, world).action == "Orbit: Squad Cover", "ORBIT cover after arrival display");
        a.AIData.BotOwner.Brain.Layer = "COMET : Recover magazine";
        check(data.ReadBot(a, world).action == "COMET: Recover Magazine", "Actual recovery layer identifies magazine retrieval");
        OrbitApi.Ghost = true;
        check(data.ReadBot(a, world).ghost && !data.ReadBot(a, world).action.Contains("COMET: Recover"), "Dormant bot cannot claim active COMET magazine recovery");
        OrbitApi.Ghost = false;
        a.AIData.BotOwner.Brain.Layer = "COMET : Crossfire";
        CometApi.Crossfire = "COMET:CrossfireCoverMateHolding";
        check(data.ReadBot(a, world).action == "COMET: Crossfire Cover Mate Holding", "COMET support phase display");
        CometApi.Crossfire = "COMET:CrossfireUnknown";
        check(!data.ReadBot(a, world).action.Contains("Crossfire Unknown"), "Unsupported COMET plan does not become actual behavior");
        a.AIData.BotOwner.Brain.Layer = "COMET : Post-combat"; CometApi.Recovery = "COMET:PostCombatMedical";
        check(data.ReadBot(a, world).action == "COMET: Post Combat Medical", "COMET postcombat display");
        a.AIData.BotOwner.Brain.Layer = "Looting";
        check(data.ReadBot(a, world).action == "Looting Bots: Looting", "LootingBots active layer display");
        a.AIData.BotOwner.Brain.Layer = "LootPatrol";
        check(!data.ReadBot(a, world).action.StartsWith("Looting"), "Vanilla LootPatrol is not misattributed to LootingBots");
        a.AIData.BotOwner.Brain.Layer = "Questing";
        a.gameObject.Components.Add(typeof(QuestingBots.BotLogic.BotMonitor.BotMonitorController), new QuestingBots.BotLogic.BotMonitor.BotMonitorController());
        QuestApi.Assignment = new QuestingBots.Controllers.Job();
        a.gameObject.Components.Add(typeof(QuestingBots.Components.BotObjectiveManager), new QuestingBots.Components.BotObjectiveManager(QuestApi.Assignment));
        check(data.ReadBot(a, world).action.Contains("QB: Visit dorms") && !QuestApi.Updated, "QB quest read never updates the job or adds a component");
        a.AIData.BotOwner.Brain.Layer = "SAIN : Combat Layer";
        check(data.ReadBot(a, world).action == "COMET: Reload", "Old QB quest cannot overwrite combat/medical execution");
        a.AIData.BotOwner.Brain.Layer = "OrbitBrainLayer";
        OrbitApi.Throw = true; int reads = OrbitApi.Reads;
        data.ReadBot(a, world); data.ReadBot(a, world);
        check(warnings.Count == 1 && OrbitApi.Reads == reads + 2, "Transient integration failures warn once but remain eligible for the next bounded refresh");
        OrbitApi.Throw = false;
        check(data.ReadBot(a, world).action == "Orbit: Squad Cover", "Transient objective reads recover in the same raid without clearing the overlay");
        b.AIData.BotOwner!.Brain.Layer = "COMET : Recover magazine";
        b.AIData.BotOwner.Brain.LastDecision = "180";
        a.AIData.BotOwner.Brain.LastDecision = "181";
        a.AIData.BotOwner.Brain.ThrowLayer = true;
        check(data.ReadBot(a, world).action == "Action unavailable", "A missing native layer does not invent an action for that sample");
        check(data.ReadBot(b, world).action == "COMET: Recover Magazine", "One bot's native layer exception cannot disable action reads for other bots");
        data.ReadBot(a, world);
        check(warnings.Count == 2, "Repeated native layer errors are logged only once per raid");
        a.AIData.BotOwner.Brain.ThrowLayer = false;
        check(data.ReadBot(a, world).action == "Orbit: Squad Cover", "The same bot resumes objective display after its native layer recovers");
        a.AIData.BotOwner.Brain.Agent = null;
        check(data.ReadBot(a, world).action == "Action unavailable" && warnings.Count == 2, "An absent native brain agent is a normal unavailable sample, without an exception");
        a.AIData.BotOwner.Brain.Agent = new();
        a.AIData.BotOwner.Brain.Layer = "SAIN : Combat Layer";
        check(data.ReadBot(a, world).action == "COMET: Reload", "Combat action ownership still resolves after a transient layer error");
        data.Clear();

        var overlay = new OverlaySelection();
        overlay.Select(SpectatorOverlay.BotInfo, false);
        check(overlay.Mode == SpectatorOverlay.BotInfo, "Bot info does not require BotDebug");
        overlay.ToggleBotInfo(); overlay.ObserveBotDebug(false);
        check(overlay.Mode == SpectatorOverlay.Off, "Shortcut hides the default overlay for this raid");
        overlay.ToggleBotInfo();
        check(overlay.Mode == SpectatorOverlay.BotInfo, "The same shortcut shows labels again");
        overlay.Select(SpectatorOverlay.BotDebug, false);
        check(overlay.Mode == SpectatorOverlay.Off, "Missing BotDebug is not reported as active");
        overlay.Select(SpectatorOverlay.BotInfo, true); overlay.ObserveBotDebug(true);
        check(overlay.Mode == SpectatorOverlay.BotDebug, "External BotDebug activation suppresses info labels");
        overlay.ToggleBotInfo(); overlay.ObserveBotDebug(false);
        check(overlay.Mode == SpectatorOverlay.BotInfo, "The shortcut can temporarily show labels over a BotDebug selection");
        overlay.ToggleBotInfo(); overlay.ObserveBotDebug(true);
        check(overlay.Mode == SpectatorOverlay.BotDebug, "Hiding the temporary labels restores BotDebug");
        overlay.ObserveBotDebug(false);
        check(overlay.Mode == SpectatorOverlay.Off, "External BotDebug disable respected");
        overlay.Select(SpectatorOverlay.BotInfo, true); overlay.ObserveBotDebug(false);
        check(overlay.Mode == SpectatorOverlay.BotInfo, "Selecting info does not get undone by disabled BotDebug");
        overlay.ToggleBotInfo();
        check(overlay.Mode == SpectatorOverlay.Off, "An explicit Bot info selection clears any temporary return to BotDebug");
        overlay.Select(SpectatorOverlay.BotInfo, true);
        check(overlay.Mode == SpectatorOverlay.BotInfo, "Starting the next raid reapplies its saved overlay");

        Singleton<Handbook>.Instance = new Handbook();
        var camera = new Camera(); Physics.Hit = null;
        a.Inventory!.Items.Add(new Item { TemplateId = "ammo", StackObjectsCount = 30 });
        a.Inventory.Items.Add(new Item { TemplateId = "weapon" });
        var labels = new BotLabels((p, w, fields) => new BotLabelSample(data.ReadBot(p, w, fields)), warnings.Add);
        roster.Refresh(world, 10);
        labels.Update(roster, world, camera, 10, 150);
        // The stopwatch can intentionally stop after one bot on slow machines.
        check(roster.Bots.Count(x => x.Visible) is >= 1 and <= 2, "Information collection is bounded to two bots per frame");
        var alpha = roster.Bots.Single(x => x.Id == "a");
        check(alpha.Visible && alpha.Value == 8000 && alpha.Label.Contains("350/440"), "Visible label includes health and stack-aware carried inventory value");
        int inventoryReads = a.Inventory.Reads;
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 11, 150);
        check(a.Inventory.Reads == inventoryReads, "Inventory is not enumerated every frame or every health update");
        a.Inventory.Items.Clear();
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 16, 150);
        check(alpha.Value == 0, "Inventory changes appear at the next value refresh");
        a.Inventory.Throw = true;
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 22, 150);
        check(alpha.Visible && alpha.Label.Contains("unavailable") && alpha.Label.Contains("350/440"), "Missing valuation preserves other bot information");
        var syncedLabels = new BotLabels((_, _, _) => new BotLabelSample(("Reckless", "COMET: Crossfire", true),
            true, (301f, 440f), 1234d), warnings.Add);
        int beforeHostLabel = a.Inventory.Reads;
        for (int i = 0; i < 10; i++) syncedLabels.Update(roster, world, camera, 22.5f, 150);
        check(alpha.Visible && alpha.Label.Contains("301/440") && alpha.Label.Contains("Ghost | COMET: Crossfire") &&
            alpha.Label.Contains("Reckless") && alpha.Value == 1234d && a.Inventory.Reads == beforeHostLabel,
            "A joining player's label uses the complete host sample even with an unreadable local inventory");
        Physics.Hit = new RaycastHit { transform = new GameObject().transform };
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 23, 150);
        check(roster.Bots.All(x => !x.Visible), "Opaque occlusion hides labels behind walls");
        int casts = Physics.Casts;
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 23.6f, 150, true);
        check(alpha.Visible && Physics.Casts == casts, "Through-scenery mode shows occluded bots without visibility raycasts");
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 24.2f, 150, false);
        check(!alpha.Visible, "Turning through-scenery mode off restores obstruction checks");
        Physics.Hit = new RaycastHit { transform = a.transform };
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 25, 150);
        check(alpha.Visible, "Hitting the target's own collider counts as visibility");
        Physics.Hit = null; camera.Projection = new(.5f, .5f, -1);
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 26, 150, true);
        check(roster.Bots.All(x => !x.Visible), "Bots behind the camera have no labels");
        camera.Projection = new(1.2f, .5f, 1);
        check(!BotLabels.InView(camera, a, 150, out _), "Bots outside the viewport have no labels");
        camera.Projection = new(.5f, .5f, 1); a.transform.position = new(0, 0, 151);
        check(!BotLabels.InView(camera, a, 150, out _), "Label distance limit applies to world distance");
        a.transform.position = new(0, 0, 2500);
        var skull = new GameObject().transform;
        a.PlayerBones = new() { Head = new BifacialTransform(skull) };
        foreach (var offset in new[] { new Vector3(0, 1.65f, 0), new Vector3(.2f, 1f, 0), new Vector3(0, .25f, .8f) })
        {
            skull.position = a.Position + offset;
            var expected = skull.position + Vector3.up * .22f;
            check((BotLabels.Anchor(a) - expected).sqrMagnitude < .0001f, "The marker follows the head above standing, crouched and prone bots");
        }
        skull.position = new Vector3(0, 1.65f, 12);
        check((BotLabels.Anchor(a) - (a.Position + Vector3.up * 1.8f)).sqrMagnitude < .0001f,
            "A distant bot with a stale skeleton keeps its own world anchor");
        a.PlayerBones = null;
        check((BotLabels.Anchor(a) - (a.Position + Vector3.up * 1.8f)).sqrMagnitude < .0001f,
            "A bot without a loaded skeleton still has a marker");
        check(BotLabels.InView(camera, a, 0, out _), "Unlimited labels include bots beyond the former 150m and 500m limits");
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 28, 0, true);
        check(alpha.Visible && alpha.Label.Contains("Alpha"), "Distant bot information is collected, not just projected");
        a.Profile.Nickname = "Скиф Заводской";
        roster.Refresh(world, 29);
        for (int i = 0; i < 10; i++) labels.Update(roster, world, camera, 29, 0, true);
        check(alpha.Visible && alpha.Label.StartsWith("Skif Zavodskoy |") && alpha.Name == "Skif Zavodskoy",
            "BotInfo labels and the list show the same English guard name");
        camera.Projection = new(1.2f, .5f, 1);
        check(!BotLabels.InView(camera, a, 0, out _), "Unlimited range still excludes off-screen bots");
        camera.Projection = new(.5f, .5f, -1);
        check(!BotLabels.InView(camera, a, 0, out _), "Unlimited range still excludes bots behind the camera");
        CheckFieldChoices(check, a, camera);
        labels.Hide(roster); roster.Clear(); labels.Clear();
        check(roster.Bots.Count == 0, "Leaving a raid clears roster and scene references");
        check(BotPresentation.Kind("bossKilla").StartsWith("Boss") && BotPresentation.Kind("followerBully").StartsWith("Guard") && BotPresentation.Kind("exUsec") == "Rogue", "Roster types cover bosses, guards and rogues");
    }
    private static void CheckFieldChoices(Action<bool, string> check, Player player, Camera camera)
    {
        var world = new GameWorld();
        world.AllAlivePlayersList.Add(player);
        player.Profile.Nickname = "Alpha";
        player.transform.position = new Vector3(20, 0, 0);
        camera.transform.position = default;
        camera.Projection = new Vector3(.5f, .5f, 1);
        var roster = new BotRoster();
        roster.Refresh(world, 0);
        var bot = roster.Bots.Single();
        string action = "COMET: Reload";
        int hostReads = 0;
        var labels = new BotLabels((_, _, _) =>
        {
            hostReads++;
            return new BotLabelSample(("Rat", action, true), true, (225f, 440f), 1234d);
        }, _ => { });
        int tick = 0;
        string Caption(BotLabelFields fields)
        {
            labels.Update(roster, world, camera, ++tick, 0, true, fields);
            return bot.Visible ? BotLabels.Caption(bot, false, null) : "";
        }
        var expected = new (BotLabelFields field, string text)[]
        {
            (BotLabelFields.Name, "Alpha"), (BotLabelFields.Type, "PMC"), (BotLabelFields.RoleDetail, "USEC"),
            (BotLabelFields.Distance, "20 m"), (BotLabelFields.Personality, "Rat"), (BotLabelFields.Difficulty, "Difficulty: hard"),
            (BotLabelFields.Health, "Health: 225/440"), (BotLabelFields.CarriedValue, $"Loot value (carried): {1234d:N0} RUB"),
            (BotLabelFields.Ghost, "Ghost"), (BotLabelFields.Action, action)
        };
        foreach (var (field, text) in expected)
        {
            check(Caption(field) == text, $"Only {field} selected gives a single clean caption");
            check(!Caption(BotLabelFields.All & ~field).Contains(text), $"Hiding {field} removes it independently");
        }
        var defaults = $"Alpha | PMC / USEC | Rat | 20 m | Difficulty: hard\nHealth: 225/440\nLoot value (carried): {1234d:N0} RUB\nGhost | COMET: Reload";
        check(Caption(BotLabelFields.All) == defaults, "All fields preserve the original label order, separators and units");
        var custom = BotInfoRows.Set(BotInfoRows.Defaults, BotInfoRows.Index(BotLabelFields.Name), 8);
        custom = BotInfoRows.Set(custom, BotInfoRows.Index(BotLabelFields.Action), 1);
        labels.Update(roster, world, camera, tick, 0, true, BotLabelFields.Name | BotLabelFields.Action, null, custom);
        check(bot.Label == "COMET: Reload\nAlpha" && bot.LabelWithoutAction == "Alpha",
            "Changing lines at the same timestamp reorders actions, skips empty lines and preserves follow-only labels");
        int before = hostReads;
        check(Caption(BotLabelFields.None) == "" && !bot.Visible && hostReads == before, "All fields off hides the whole label without sampling");
        check(Caption(BotLabelFields.Name) == "Alpha" && hostReads == before, "Identity-only labels do not request host samples");
        foreach (string tactic in new[] { "COMET: Siege - watch exit", "COMET: Grenade suppression - throw", "COMET: Crossfire Maneuver Supporting" })
        {
            action = tactic;
            check(Caption(BotLabelFields.SquadTactic) == tactic && BotLabels.Caption(bot, true, null) == ""
                && BotLabels.Caption(bot, true, player) == tactic, "Squad tactic can be the only field, including follow-only mode");
            check(Caption(BotLabelFields.Action) == "", "Squad tactics can be hidden while ordinary actions stay enabled");
        }
        action = "COMET: Reload";
        check(Caption(BotLabelFields.SquadTactic) == "", "Ordinary action does not leak into a tactic-only label");
        check(Caption(BotLabelFields.Ghost | BotLabelFields.Action) == "Ghost | COMET: Reload"
            && BotLabels.Caption(bot, true, null) == "Ghost", "Ghost status can remain visible when an action is filtered out");

        var inventory = player.Inventory ?? throw new InvalidOperationException("Expected the test bot to have an inventory");
        int inventoryReads = inventory.Reads;
        var local = new BotLabels((_, _, _) => new BotLabelSample(("", "SAIN: Seek Cover", false)), _ => { });
        local.Update(roster, world, camera, ++tick, 0, true, BotLabelFields.Health | BotLabelFields.Action);
        check(inventory.Reads == inventoryReads && !bot.Label.Contains("Loot value"), "Hidden carried value skips local inventory reads");
    }

}

namespace SAIN.Interop
{
    public static class SAINExternal
    {
        public static string GetPersonality(BotOwner owner) => "GigaChad";
    }
}
