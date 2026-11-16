using EFT;

namespace EFT.InventoryLogic
{
    public enum EPlayerItems { Equipment }
    public sealed class Item { public string TemplateId = "item"; public int StackObjectsCount = 1; }
    public sealed class Inventory
    {
        public int Reads;
        public bool Throw;
        public readonly List<Item> Items = new();
        public IEnumerable<Item> GetPlayerItems(EPlayerItems kind)
        { Reads++; if (Throw) throw new InvalidOperationException("Missing item template"); return Items; }
    }
}
namespace EFT.HandBook
{
    public sealed class Handbook { public double GetBasePrice(string template) => template == "ammo" ? 100 : 5000; }
}
namespace SAIN.Components
{
    public sealed class BotComponent
    {
        public BotInfo Info { get; } = new();
        public BotDecisions Decision { get; } = new();
        public ActionInfo CurrentAction { get; } = new();
        public string ActiveLayer { get; set; } = "Peace";
    }
    public sealed class BotInfo { public string Personality { get; set; } = "Rat"; }
    public sealed class BotDecisions
    {
        public string CurrentSelfDecision { get; set; } = "None";
        public string CurrentCombatDecision { get; set; } = "None";
        public string CurrentSquadDecision { get; set; } = "None";
    }
    public sealed class ActionInfo { public string Name { get; set; } = ""; }
    public sealed class BotManagerComponent { public Dictionary<string, BotComponent> Bots { get; } = new(); }
}
namespace Orbit.Api
{
    public static class OrbitTelemetry
    {
        public static bool Ghost;
        public static bool Throw;
        public static int Reads;
        public static Objective? Current;
        public static bool IsBotDormant(string id) => Ghost;
        public static Objective? GetBotObjective(string id)
        { Reads++; if (Throw) throw new InvalidOperationException("Unsupported ORBIT version"); return Current; }
    }
    public sealed class Objective { public string Status = "Moving", Category = "ContainerLoot"; }
}
namespace Comet.Api
{
    public static class Decisions
    {
        public static string Current = "", Crossfire = "", Recovery = "";
        public static string GetCurrentDecision(string id) => Current;
        public static string GetCrossfireDecision(string id) => Crossfire;
        public static string GetPostCombatDecision(string id) => Recovery;
    }
}
namespace QuestingBots.BotLogic.BotMonitor
{
    public sealed class BotMonitorController { public string CurrentDecision { get; set; } = "MoveToPosition"; }
}
namespace QuestingBots.Controllers
{
    public sealed class Job { public string Status { get; set; } = "Active"; public Quest QuestAssignment { get; set; } = new(); }
    public sealed class Quest { public string Name { get; set; } = "Visit dorms"; }
    public static class BotJobAssignmentFactory
    {
        public static Job? Assignment;
        public static bool Updated;
        public static Job? GetCurrentJobAssignment(BotOwner owner, bool allowUpdate = true)
        { Updated |= allowUpdate; return Assignment; }
    }
}
namespace QuestingBots.Components
{
    public sealed class BotObjectiveManager
    {
        private QuestingBots.Controllers.Job? assignment;
        public BotObjectiveManager(QuestingBots.Controllers.Job job) => assignment = job;
    }
}
