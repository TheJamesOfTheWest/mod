using System;
using System.Collections.Generic;
using UnityEngine;

namespace SotfPassthrough
{
    /// <summary>
    /// Game-specific hooks, found from the interop dump. UNTESTED: every call is wrapped so a managed exception only logs.
    ///  - input blocking: Sons.Input.InputSystem.GetInputActionFromName(name).Disable()/Enable()
    ///  - TNT: instantiate the game's own explosion prefab (class Explode) at the Minecraft blast
    ///  - axe: Sons.Gameplay.TreeCutting.TreeCutManager (OnHit / InstantCutForceFall) on the tree in front of the camera
    /// </summary>
    public static class Hooks
    {
        static void Log(string m) => Plugin.Instance.Log.LogInfo(m);

        // ---- input ----
        static readonly string[] Blocked =
        {
            "PrimaryAction", "SecondaryAction", "TertiaryAction",
            "HotKey1", "HotKey2", "HotKey3", "HotKey4", "HotKey5", "HotKey6", "HotKey7", "HotKey8", "HotKey9", "HotKey0",
            "CycleForward", "CycleBack", "MouseScrollWheel", "ScrollY",
        };
        static bool _loggedMissing;

        public static void SetInputBlocked(bool blocked)
        {
            foreach (var name in Blocked)
            {
                try
                {
                    var a = Sons.Input.InputSystem.GetInputActionFromName(name);
                    if (a == null) { if (!_loggedMissing) Log("input action not found: " + name); continue; }
                    if (blocked) a.Disable(); else a.Enable();
                }
                catch (Exception e) { if (!_loggedMissing) Log("input action " + name + ": " + e.GetType().Name + " " + e.Message); }
            }
            _loggedMissing = true;
        }

        // ---- explosions ----
        static GameObject _explosionPrefab;
        static bool _prefabSearched;

        static void FindExplosionPrefab()
        {
            _prefabSearched = true;
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Explode>();
                Log("Explode components found: " + all.Length);
                GameObject best = null; int bestScore = -1;
                for (int i = 0; i < all.Length; i++)
                {
                    var e = all[i];
                    if (e == null) continue;
                    var go = e.gameObject;
                    bool inScene = go.scene.IsValid();
                    int score = (inScene ? 0 : 10) + (go.name.ToLowerInvariant().Contains("grenade") ? 5 : 0) + (go.name.ToLowerInvariant().Contains("explosion") ? 2 : 0);
                    Log("  Explode[" + i + "] " + go.name + (inScene ? " (scene)" : " (prefab/asset)") + " score=" + score);
                    if (score > bestScore) { bestScore = score; best = go; }
                }
                _explosionPrefab = best;
                Log("using explosion prefab: " + (best == null ? "none" : best.name));
            }
            catch (Exception e) { Log("FindExplosionPrefab failed: " + e); }
        }

        public static void SpawnExplosion(Vector3 pos)
        {
            try
            {
                if (!_prefabSearched) FindExplosionPrefab();
                if (_explosionPrefab == null) return;
                var go = UnityEngine.Object.Instantiate(_explosionPrefab, pos, Quaternion.identity);
                go.SetActive(true);
                var ex = go.GetComponent<Explode>();
                if (ex != null) ex.SetIsOwner(true);
                Log("explosion at " + pos);
            }
            catch (Exception e) { Log("SpawnExplosion failed: " + e); }
        }

        // ---- trees ----
        static readonly Dictionary<int, int> ChopHits = new Dictionary<int, int>();
        const int HitsToFell = 4;

        public static void TryChop(Camera cam)
        {
            try
            {
                var o = cam.transform.position; var f = cam.transform.forward;
                if (!Physics.Raycast(o, f, out RaycastHit hit, 5f, ~(1 << 2), QueryTriggerInteraction.Collide)) return;
                var t = hit.collider.GetComponentInParent<Sons.Gameplay.TreeCutting.TreeCutManager>();
                if (t == null) { Log("chop: hit " + hit.collider.name + " (no TreeCutManager)"); return; }
                int id = t.GetInstanceID();
                ChopHits.TryGetValue(id, out int n); n++; ChopHits[id] = n;
                Log("chop " + t.name + " " + n + "/" + HitsToFell);
                t.OnHit();
                if (n >= HitsToFell)
                {
                    ChopHits.Remove(id);
                    var dir = new Vector3(f.x, 0f, f.z).normalized;
                    t.InstantCutForceFall(dir);
                }
            }
            catch (Exception e) { Log("TryChop failed: " + e); }
        }
    }
}
