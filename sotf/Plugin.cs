using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace SotfPassthrough
{
    [BepInPlugin("dev.passthrough.sotf", "Minecraft Passthrough", "0.1.0")]
    public class Plugin : BasePlugin
    {
        public static Plugin Instance;

        public override void Load()
        {
            Instance = this;
            Dbg.Init(BepInEx.Paths.BepInExRootPath);
            ClassInjector.RegisterTypeInIl2Cpp<PassthroughBehaviour>();
            var go = new GameObject("MCPassthrough");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<PassthroughBehaviour>();
            Log.LogInfo("Minecraft passthrough loaded; connecting to ws://127.0.0.1:25599");
        }
    }
}
