// The production bridge is compiled unchanged. These doubles model only Unity object state;
// they do not simulate rendering, physics, audio, or the game's death callbacks.
namespace UnityEngine
{
    public class GameObject
    {
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf;
        public void SetActive(bool value) => activeSelf = value;
        public Dictionary<Type, object> Components = new();
    }
    public class Component
    {
        public GameObject gameObject = new();
        public T? GetComponent<T>() where T : class => gameObject.Components.GetValueOrDefault(typeof(T)) as T;
    }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class Camera : Component { }
    public enum CursorLockMode { None, Locked, Confined }
    public static class Cursor { public static CursorLockMode lockState; public static bool visible; }
}
namespace EFT
{
    public enum EPointOfView { FirstPerson, FreeCamera, ThirdPerson }
    public class Player : UnityEngine.MonoBehaviour { public EPointOfView PointOfView; }
    public class GamePlayerOwner : UnityEngine.MonoBehaviour { }
    public class LocalGame
    {
        public Player LocalPlayer = new();
        public GamePlayerOwner PlayerOwner = new();
        public UnityEngine.MonoBehaviour GameUi = new();
    }
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
public class DeathFade { public int Clears; public void DisableEffect() => Clears++; }
public class FastBlur { public int Clears; public void Reset() => Clears++; }
