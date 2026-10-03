using System;
using System.Collections.Generic;

namespace BepInEx
{
    public class BepInPlugin : Attribute { public BepInPlugin(string a, string b, string c) { } }
    public static class Paths { public static string BepInExRootPath = ""; }
    namespace Logging { public class ManualLogSource { public void LogInfo(object o) { } public void LogError(object o) { } public void LogWarning(object o) { } } }
    namespace Unity.IL2CPP { }
}
namespace BepInEx.Unity.IL2CPP
{
    public abstract class BasePlugin { public BepInEx.Logging.ManualLogSource Log = new BepInEx.Logging.ManualLogSource(); public abstract void Load(); }
}
namespace Il2CppInterop.Runtime.Injection { public static class ClassInjector { public static void RegisterTypeInIl2Cpp<T>() { } } }
namespace Il2CppInterop.Runtime.Attributes { public class HideFromIl2Cpp : Attribute { } }
namespace SotfPassthrough { public static class TypeDump { public static void Run(string p) { } public static void RunRequest(string p) { } } }

public class Explode : UnityEngine.MonoBehaviour
{
    public Explode(IntPtr p) : base(p) { }
    public void SetIsOwner(bool b) { }
    public float _radius { get; set; }
    public float _explosiveForceMultiplier { get; set; }
}

namespace Sons.Input
{
    public class InputAction { public void Enable() { } public void Disable() { } }
    public static class InputSystem { public static InputAction GetInputActionFromName(string n) => null; }
}
namespace Sons.Gameplay.TreeCutting
{
    public class TreeCutManager : UnityEngine.MonoBehaviour
    {
        public TreeCutManager(IntPtr p) : base(p) { }
        public void OnHit() { }
        public void InstantCutForceFall(UnityEngine.Vector3 d) { }
    }
}
namespace TheForest.Utils
{
    public class Vitals { public void SetFullHealth() { } }
    public static class LocalPlayer
    {
        public static UnityEngine.Transform Transform => null;
        public static UnityEngine.Rigidbody Rigidbody => null;
        public static UnityEngine.GameObject GameObject => null;
        public static Vitals Vitals => null;
    }
}
namespace UnityEngine.InputSystem
{
    public class Vec2Control { public Vector2 ReadValue() => default; }
    public class Mouse { public static Mouse current => null; public Vec2Control delta => null; public Vec2Control scroll => null; }
}
namespace UnityEngine
{
    public struct Vector2 { public float x, y; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static float Dot(Vector3 a, Vector3 b) => 0f; public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a; public static Vector3 ClampMagnitude(Vector3 v, float m) => v;
        public static Vector3 up => default; public static Vector3 down => default; public static Vector3 forward => default; public static Vector3 zero => default; public static Vector3 one => default;
        public static Vector3 operator +(Vector3 a, Vector3 b) => a; public static Vector3 operator -(Vector3 a, Vector3 b) => a;
        public static Vector3 operator *(Vector3 a, float f) => a; public static Vector3 operator /(Vector3 a, float f) => a;
        public float magnitude => 0; public float sqrMagnitude => 0; public Vector3 normalized => this;
    }
    public struct Ray { public Vector3 origin, direction; }
    public struct Quaternion { public static Quaternion identity => default; }
    public struct Matrix4x4 { public float m00, m11; }
    public struct Scene { public bool IsValid() => true; }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    [Flags] public enum HideFlags { None = 0, HideAndDontSave = 61 }
    public static class Mathf
    {
        public static float Clamp(float v, float a, float b) => v; public static float Max(float a, float b) => a; public static float Min(float a, float b) => a;
        public static float Lerp(float a, float b, float t) => a; public static float DeltaAngle(float a, float b) => a; public static float Atan(float f) => f; public static float Tan(float f) => f;
        public const float Deg2Rad = 0.0174532924f, Rad2Deg = 57.29578f;
    }
    public static class Time { public static float unscaledTime; public static int frameCount; public static float unscaledDeltaTime; }
    public static class Screen { public static int width, height; }
    public class Object
    {
        public string name { get; set; }
        public int GetInstanceID() => 0;
        public static T Instantiate<T>(T o, Vector3 p, Quaternion r) where T : Object => o;
        public static T Instantiate<T>(T o, Vector3 p, Quaternion r, Transform parent) where T : Object => o;
        public static void Destroy(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
        public HideFlags hideFlags { get; set; }
    }
    public class Component : Object
    {
        public GameObject gameObject => null; public Transform transform => null;
        public T GetComponent<T>() => default; public T GetComponentInParent<T>() => default;
        public T[] GetComponentsInChildren<T>(bool b) => null;
    }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { public MonoBehaviour(IntPtr p) { } }
    public class Renderer : Component { public new bool enabled; }
    public class Collider : Component { public new bool enabled; }
    public class BoxCollider : Collider { public Vector3 size; }
    public class Rigidbody : Component { public Vector3 position; public Vector3 velocity; public bool isKinematic; }
    public class GameObject : Object
    {
        public GameObject(string n) { } public GameObject() { }
        public T AddComponent<T>() where T : Component => default; public void SetActive(bool b) { }
        public Scene scene => default; public new Transform transform => null; public T GetComponent<T>() => default; public T[] GetComponentsInChildren<T>(bool b) => null;
    }
    public class Transform : Component
    {
        public Vector3 position { get; set; } public Vector3 forward => default; public Vector3 localScale { get; set; }
        public void SetParent(Transform t, bool worldPositionStays) { } public bool IsChildOf(Transform t) => false;
    }
    public class Camera : Behaviour
    {
        public static Camera main => null; public Ray ViewportPointToRay(Vector3 v) => default;
        public float fieldOfView, nearClipPlane, farClipPlane, aspect; public bool usePhysicalProperties; public Matrix4x4 nonJitteredProjectionMatrix => default;
        public int pixelWidth, pixelHeight, scaledPixelWidth, scaledPixelHeight;
    }
    public struct RaycastHit { public Collider collider; public float distance; public Vector3 point; }
    public static class Physics
    {
        public static RaycastHit[] RaycastAll(Vector3 o, Vector3 d, float dist, int mask, QueryTriggerInteraction q) => null;
        public static void SyncTransforms() { }
    }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() where T : Object => null; }
}
