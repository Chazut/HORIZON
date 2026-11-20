// The production bridge is compiled unchanged. These doubles model only Unity object state;
// they do not simulate rendering, physics, audio, or the game's death callbacks.
namespace UnityEngine
{
    public static class Mathf { public static float DeltaAngle(float from, float to) => (to - from + 540f) % 360f - 180f; }
    public static class Shader { public static float Blindness; public static void SetGlobalFloat(string name, float value) { if (name == "_BlindnessCoef") Blindness = value; } }
    public class Object { public static void Destroy(object obj) { } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new(0, 1, 0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public Vector3 normalized => this * (1f / MathF.Sqrt(sqrMagnitude));
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x, a.y+b.y, a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x, a.y-b.y, a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b, a.y*b, a.z*b);
    }
    public struct Quaternion { public Vector3 eulerAngles; public static Quaternion LookRotation(Vector3 v) => new(); }
    public class Transform
    {
        public GameObject gameObject = null!;
        public Transform? parent;
        public Vector3 position, forward = new(0, 0, 1);
        public Quaternion rotation;
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
        public void SetParent(Transform? p, bool world) => parent = p;
        public bool IsChildOf(Transform other) => this == other || parent?.IsChildOf(other) == true;
    }
    public class GameObject
    {
        public Transform transform;
        public GameObject() { transform = new Transform { gameObject = this }; }
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public void SetActive(bool value) => activeSelf = value;
        public Dictionary<Type, object> Components = new();
        public object? GetComponent(Type type) => Components.GetValueOrDefault(type);
    }
    public class Component
    {
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
        public T? GetComponent<T>() where T : class => gameObject.Components.GetValueOrDefault(typeof(T)) as T;
        public object? GetComponent(Type type) => gameObject.GetComponent(type);
        public T[] GetComponentsInChildren<T>(bool inactive) => gameObject.Components.Values.OfType<T>().ToArray();
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
    public static class Time { public static float unscaledTime; }
    public class Camera : MonoBehaviour
    {
        public static Camera main { get; set; } = new();
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
        public Vector3 Projection = new(.5f, .5f, 1);
        public Vector3 WorldToViewportPoint(Vector3 value) => Projection;
    }
    public class Collider : MonoBehaviour { }
    public class Renderer : MonoBehaviour { }
    public enum QueryTriggerInteraction { Ignore }
    public struct RaycastHit { public Transform transform; public Vector3 point; }
    public static class Physics
    {
        public const int DefaultRaycastLayers = -5;
        public static RaycastHit? Hit;
        public static int Casts;
        public static bool Linecast(Vector3 a, Vector3 b, out RaycastHit hit, int mask, QueryTriggerInteraction q)
        { Casts++; hit = Hit ?? default; return Hit.HasValue; }
    }
    public enum CursorLockMode { None, Locked, Confined }
    public static class Cursor { public static CursorLockMode lockState; public static bool visible; }
}
namespace EFT
{
    public enum EPointOfView { FirstPerson, FreeCamera, ThirdPerson }
    public enum WildSpawnType { pmcBEAR, pmcUSEC, assault, boss, follower, pmcBot }
    public class Settings { public WildSpawnType Role; }
    public class Info { public Settings Settings = new(); public string? GroupId; }
    public class Profile { public string Nickname = "test"; public Info Info = new(); }
    public enum EBodyPart { Common }
    public struct ValueStruct { public float Current, Maximum; }
    public class Health
    {
        public bool IsAlive = true;
        public int Reads;
        public ValueStruct GetBodyPartHealth(EBodyPart part, bool rounded) { Reads++; return new() { Current = 350, Maximum = 440 }; }
    }
    public class BifacialTransform
    {
        public UnityEngine.Transform Original;
        public BifacialTransform(UnityEngine.Transform original) => Original = original;
        public UnityEngine.Vector3 forward => Original.forward;
        public UnityEngine.Vector3 position => Original.position;
        public UnityEngine.Quaternion rotation => Original.rotation;
    }
    public interface IPlayer { string ProfileId { get; } }
    public class AIData { public BotOwner? BotOwner; }
    public class PlayerBones { public BifacialTransform? Head; }
    public class BotGroup { public int Id; }
    public class Brain
    {
        public string Layer = "", LastDecision = "simplePatrol";
        public bool ThrowLayer;
        public object? Agent = new();
        public string ActiveLayerName() => ThrowLayer || Agent == null ? throw new NullReferenceException("Native layer is transitioning") : Layer;
    }
    public class BotOwner : UnityEngine.MonoBehaviour { public bool Active = true; public BotGroup? BotsGroup; public Brain Brain = new(); }
    public class Player : UnityEngine.MonoBehaviour, IPlayer
    {
        public EPointOfView PointOfView;
        public bool IsAI, IsYourPlayer;
        public Health HealthController = new();
        public Profile Profile = new();
        public string ProfileId { get; set; } = "local";
        public AIData AIData = new();
        public PlayerBones? PlayerBones;
        public EFT.InventoryLogic.Inventory? Inventory = new();
        public UnityEngine.Vector3 Position => gameObject.transform.position;
        public UnityEngine.Vector3 LookDirection = new(0, 0, 1);
        public BifacialTransform Transform => new(gameObject.transform);
    }
    public class GameWorld : UnityEngine.MonoBehaviour
    {
        public List<Player> RegisteredPlayers = new(), AllAlivePlayersList = new();
        public void RegisterPlayer(Player p) { RegisteredPlayers.Add(p); AllAlivePlayersList.Add(p); }
        public void UnregisterPlayer(Player p) { RegisteredPlayers.Remove(p); AllAlivePlayersList.Remove(p); }
        public Player? GetAlivePlayerByProfileID(string id) => AllAlivePlayersList.FirstOrDefault(p => p.ProfileId == id);
    }
    public class BotSpawner
    {
        public List<Player> Players = new();
        public void DeletePlayer(Player p) => Players.Remove(p);
        public void AddPlayer(Player p) => Players.Add(p);
        public int NativeReads;
        public DebugBotStruct NativeResult;
        public DebugBotStruct GetBotDebugData(IPlayer observer, string profileId)
        {
            NativeReads++;
            return Players.Count == 0 ? default : NativeResult;
        }
    }
    public class BotsController { public BotSpawner BotSpawner = new(); }
    public interface IBotGame { BotsController BotsController { get; } }
    public class BotGame : IBotGame { public BotsController BotsController { get; } = new(); }
    public class GamePlayerOwner : UnityEngine.MonoBehaviour { }
    public class LocalGame
    {
        public Player LocalPlayer = new();
        public GamePlayerOwner PlayerOwner = new();
        public UnityEngine.MonoBehaviour GameUi = new();
    }
}
namespace Comfort.Common { public static class Singleton<T> { public static T Instance = default!; } }
namespace BepInEx.Configuration
{
    public class ConfigFile { public bool SaveOnConfigSet = true; public int Saves; }
    public class ConfigEntry<T>
    {
        private T _value = default!;
        public ConfigFile ConfigFile = new();
        public Action<T>? Changed;
        public T Value { get => _value; set { _value = value; if (ConfigFile.SaveOnConfigSet) ConfigFile.Saves++; Changed?.Invoke(value); } }
    }
}
namespace DrakiaXYZ.BotDebug.Helpers
{
    internal static class Settings { public static BepInEx.Configuration.ConfigEntry<bool> Enable = new(); }
}
namespace DrakiaXYZ.BotDebug.Components
{
    internal static class BotDebugComponent { public static int Starts; public static void Enable() => Starts++; }
}
namespace EFT.CameraControl
{
    public class PlayerCameraController : UnityEngine.MonoBehaviour { public UnityEngine.Camera Camera = new(); }
}
namespace Terkoiz.Freecam
{
    public class FreecamController : UnityEngine.MonoBehaviour { }
    public class Freecam : UnityEngine.MonoBehaviour { public bool IsActive; }
    public static class FreecamPlugin { public static FreecamController? FreecamControllerInstance; }
}
public class DeathFade : UnityEngine.Behaviour { public int Clears; public void DisableEffect() => Clears++; }
public class FastBlur : UnityEngine.Behaviour { public int Clears; public void Reset() => Clears++; }
public class PainScreen : UnityEngine.Behaviour { }
public class GrenadeFlashScreenEffect : UnityEngine.Behaviour { public float EffectStrength; }
public class WorldLighting : UnityEngine.Behaviour { }
public struct DebugBotStruct
{
    public bool InitedBotData;
    public EFT.BotOwner? Owner;
    public EFT.IPlayer? Observer;
    public DebugBotStruct(EFT.BotOwner owner, float dist, EFT.IPlayer observer)
    { InitedBotData = owner.Active; Owner = owner; Observer = observer; }
}
