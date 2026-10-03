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
        static void Log(string m) { Plugin.Instance.Log.LogInfo(m); Dbg.Line("[hook] " + m); }

        // ---- input ----
        static readonly string[] Blocked =
        {
            "PrimaryAction", "SecondaryAction", "TertiaryAction",
            "HotKey1", "HotKey2", "HotKey3", "HotKey4", "HotKey5", "HotKey6", "HotKey7", "HotKey8", "HotKey9", "HotKey0",
            "CycleForward", "CycleBack", "Interact",
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

        // keyboard actions of the game that typing into a Minecraft screen (search box) must not trigger; names that don't exist are skipped
        static readonly string[] ScreenActions =
        {
            "Take", "Use", "Open", "Reload", "Drop", "Lighter", "Utility", "Book", "GpsTracker", "Inventory", "Craft", "Map", "Walkman", "PlayerList",
            "WalkyTalky", "Sleep", "RestKey", "Radial", "ShoulderTap", "Place", "IgniteObject", "Save", "QuickSave", "QuickLoad",
        };

        public static void SetScreenBlocked(bool blocked)
        {
            foreach (var name in ScreenActions)
            {
                try
                {
                    var a = Sons.Input.InputSystem.GetInputActionFromName(name);
                    if (a == null) continue;
                    if (blocked) a.Disable(); else a.Enable();
                }
                catch (Exception) { }
            }
        }

        // ---- explosions ----
        static GameObject _explosionPrefab;
        static float _nextPrefabSearch;

        static void FindExplosionPrefab()
        {
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

        public static void SpawnExplosion(Vector3 pos, float mcRadius = 0f)
        {
            try
            {
                // a search that finds nothing (menu, world still loading) is retried every 5 s; a destroyed prefab reads as null and is found again
                if (_explosionPrefab == null && Time.unscaledTime >= _nextPrefabSearch) { _nextPrefabSearch = Time.unscaledTime + 5f; FindExplosionPrefab(); }
                if (_explosionPrefab == null) return;
                try
                {
                    // the game's blast knocks the player down and locks the camera: keep it at least 7 m away
                    var body = TheForest.Utils.LocalPlayer.Transform.position;
                    var d = pos - body; d.y = 0f;
                    if (d.magnitude < 7f) pos = body + (d.magnitude < 0.01f ? Vector3.forward : d.normalized) * 7f + Vector3.up * (pos.y - body.y);
                }
                catch (Exception) { }
                // bigger Minecraft blasts (TNT r=4) get a bigger game explosion: scale the effect and its damage radius
                float k = mcRadius > 0f ? Mathf.Clamp(mcRadius / 1.5f, 1f, 5f) : 1f;
                // The blast fires in OnEnable, so everything is set up while the copy is still inactive (under an inactive parent), then it is released.
                var holder = new GameObject("MCExplosionHolder");
                holder.SetActive(false);
                var go = UnityEngine.Object.Instantiate(_explosionPrefab, pos, Quaternion.identity, holder.transform);
                go.transform.localScale *= k;
                var ex = go.GetComponent<Explode>();
                if (ex != null)
                {
                    ex.SetIsOwner(true);
                    try
                    {
                        var prop = typeof(Explode).GetProperty("_radius");
                        if (prop != null && prop.GetValue(ex) is float r) { prop.SetValue(ex, r * k); Log("explosion radius " + r + " -> " + r * k); }
                        else Log("explosion: no _radius property to scale");
                        var f = typeof(Explode).GetProperty("_explosiveForceMultiplier");
                        if (f != null && f.GetValue(ex) is float fm) f.SetValue(ex, fm * k);
                    }
                    catch (Exception e) { Log("explosion scaling failed: " + e.Message); }
                }
                go.transform.SetParent(null, true);
                go.SetActive(true);
                UnityEngine.Object.Destroy(holder);
                Log("explosion at " + pos + " scale " + k);
            }
            catch (Exception e) { Log("SpawnExplosion failed: " + e); }
        }

        // ---- Minecraft blocks as invisible colliders, so Sons of the Forest's player stands on them ----
        static readonly Dictionary<long, GameObject> Blocks = new Dictionary<long, GameObject>();
        static readonly Dictionary<long, Vector3> BlockAt = new Dictionary<long, Vector3>();
        const int MaxBlocks = 800;
        static long Key(int x, int y, int z) => ((long)(x & 0x3FFFFF) << 42) | ((long)(y & 0xFFFFF) << 22) | (long)(z & 0x3FFFFF);

        static Vector3 BlockRef()
        {
            try { return TheForest.Utils.LocalPlayer.Transform.position; } catch (Exception) { }
            var c = Camera.main;
            return c != null ? c.transform.position : Vector3.zero;
        }

        static void DropBlock(long k)
        {
            if (Blocks.TryGetValue(k, out var go)) { if (go != null) UnityEngine.Object.Destroy(go); Blocks.Remove(k); }
            BlockAt.Remove(k);
        }

        public static void ApplyBlocks(List<int> set, List<int> clear, float yOffset)
        {
            try
            {
                for (int i = 0; i + 2 < clear.Count; i += 3) DropBlock(Key(clear[i], clear[i + 1], clear[i + 2]));
                Vector3 refPos = BlockRef();
                for (int i = 0; i + 2 < set.Count; i += 3)
                {
                    long k = Key(set[i], set[i + 1], set[i + 2]);
                    if (Blocks.ContainsKey(k)) continue;
                    var at = new Vector3(-(set[i] + 0.5f), set[i + 1] + 0.5f - yOffset, set[i + 2] + 0.5f);
                    if (Blocks.Count >= MaxBlocks)
                    {
                        // full: make room by dropping the collider farthest from the player, but only if it is farther than this block
                        long far = 0; float farD = -1f;
                        foreach (var kv in BlockAt) { float d = (kv.Value - refPos).sqrMagnitude; if (d > farD) { farD = d; far = kv.Key; } }
                        if (farD <= (at - refPos).sqrMagnitude) continue;
                        DropBlock(far);
                    }
                    var go = new GameObject("MCBlock");
                    go.transform.position = at;
                    var box = go.AddComponent<BoxCollider>();
                    box.size = Vector3.one;
                    box.enabled = _blocksEnabled;
                    Blocks[k] = go; BlockAt[k] = at;
                }
            }
            catch (Exception e) { Log("ApplyBlocks failed: " + e); }
        }

        static bool _wasKinematic;
        static bool _blocksEnabled = true;

        /// <summary>While Steve drives the body is placed every frame: stop the game's physics from pushing it around (the cause of bouncing).</summary>
        public static void SetBodyKinematic(bool on)
        {
            try
            {
                var rb = TheForest.Utils.LocalPlayer.Rigidbody;
                if (rb == null) return;
                if (on) { _wasKinematic = rb.isKinematic; rb.isKinematic = true; }
                else rb.isKinematic = _wasKinematic;
            }
            catch (Exception e) { Log("SetBodyKinematic failed: " + e.Message); }
        }

        /// <summary>Minecraft's own physics handles the blocks while Steve drives, so the game's colliders for them must not push the body.</summary>
        public static void SetBlocksEnabled(bool on)
        {
            _blocksEnabled = on;
            foreach (var go in Blocks.Values)
            {
                if (go == null) continue;
                var c = go.GetComponent<BoxCollider>();
                if (c != null) c.enabled = on;
            }
        }

        public static void ClearBlocks()
        {
            foreach (var go in Blocks.Values) if (go != null) UnityEngine.Object.Destroy(go);
            Blocks.Clear(); BlockAt.Clear();
        }

        // ---- Steve walks: Minecraft's player moves, the game's player body follows ----
        static readonly string[] MoveActions = { "Horizontal", "Vertical", "Right", "Left", "Up", "Down", "Jump", "Run", "Crouch" };

        public static void SetMoveBlocked(bool blocked)
        {
            foreach (var name in MoveActions)
            {
                try
                {
                    var a = Sons.Input.InputSystem.GetInputActionFromName(name);
                    if (a == null) continue;
                    if (blocked) a.Disable(); else a.Enable();
                }
                catch (Exception) { }
            }
        }

        /// <summary>Where the game's player body is (its root), so the camera offset from it can be kept.</summary>
        public static bool TryGetBody(out Vector3 pos)
        {
            try { pos = TheForest.Utils.LocalPlayer.Transform.position; return true; }
            catch (Exception) { pos = Vector3.zero; return false; }
        }

        public static void MoveBody(Vector3 pos)
        {
            try
            {
                var rb = TheForest.Utils.LocalPlayer.Rigidbody;
                if (rb != null) { rb.position = pos; if (!rb.isKinematic) rb.velocity = Vector3.zero; }
                TheForest.Utils.LocalPlayer.Transform.position = pos;
                Physics.SyncTransforms();
            }
            catch (Exception e) { if (!_moveFailed) { _moveFailed = true; Log("MoveBody failed: " + e); } }
        }
        static bool _moveFailed;

        // ---- hide the game's own arms and held items while Minecraft is in charge ----
        static readonly List<Renderer> HiddenRenderers = new List<Renderer>();
        static readonly HashSet<int> HiddenIds = new HashSet<int>();

        public static void HideBody(bool hide)
        {
            try
            {
                if (!hide)
                {
                    foreach (var r in HiddenRenderers) if (r != null) r.enabled = true;
                    HiddenRenderers.Clear(); HiddenIds.Clear();
                    return;
                }
                var root = TheForest.Utils.LocalPlayer.GameObject;
                if (root == null) return;
                var all = root.GetComponentsInChildren<Renderer>(false);
                int n = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    var r = all[i];
                    if (r == null || !r.enabled) continue;
                    string tn = r.ToString(); // the wrapper's static type is always Renderer; Unity's text ("name (UnityEngine.ParticleSystemRenderer)") names the runtime class
                    if (tn.Contains("ParticleSystemRenderer") || tn.Contains("TrailRenderer") || tn.Contains("LineRenderer")) continue;
                    r.enabled = false; // always re-applied: the game may switch a renderer back on
                    if (HiddenIds.Add(r.GetInstanceID())) { HiddenRenderers.Add(r); n++; }
                }
                if (n > 0) Log("hid " + n + " player renderers");
            }
            catch (Exception e) { Log("HideBody failed: " + e.Message); }
        }

        /// <summary>Everything we switched off, restored (no healing): used when the world reloads, e.g. after dying.</summary>
        public static void ReleaseAll()
        {
            SetInputBlocked(false); SetLookBlocked(false); SetScreenBlocked(false); SetMoveBlocked(false); HideBody(false); SetBodyKinematic(false); SetBlocksEnabled(true);
            _explosionPrefab = null; // a new world has new objects: find the prefab again
        }

        /// <summary>F11: undo everything we switched off, and heal.</summary>
        public static void Unstick()
        {
            SetInputBlocked(false); SetLookBlocked(false); SetScreenBlocked(false); SetMoveBlocked(false); HideBody(false); SetBodyKinematic(false); SetBlocksEnabled(true);
            try { TheForest.Utils.LocalPlayer.Vitals.SetFullHealth(); } catch (Exception) { }
            Log("unstick: input restored, body shown, health restored");
        }

        // ---- look blocking while a Minecraft screen (inventory) is open ----
        static readonly string[] LookActions = { "MouseX", "MouseY", "LookRight", "LookLeft", "LookUp", "LookDown", "TogglePauseMenu" };

        public static void SetLookBlocked(bool blocked)
        {
            foreach (var name in LookActions)
            {
                try
                {
                    var a = Sons.Input.InputSystem.GetInputActionFromName(name);
                    if (a == null) continue;
                    if (blocked) a.Disable(); else a.Enable();
                }
                catch (Exception) { }
            }
        }

        // ---- trees ----
        static readonly Dictionary<int, int> ChopHits = new Dictionary<int, int>();
        const int HitsToFell = 4;

        public static void TryChop(Camera cam)
        {
            try
            {
                var o = cam.transform.position; var f = cam.transform.forward;
                // nearest hit that is not part of the player itself (its Grabber collider sits right in front of the camera) or one of our block colliders
                var hits = Physics.RaycastAll(o, f, 5f, ~(1 << 2), QueryTriggerInteraction.Collide);
                Transform self = null;
                try { self = TheForest.Utils.LocalPlayer.GameObject.transform; } catch (Exception) { }
                RaycastHit hit = default; float best = float.MaxValue;
                for (int i = 0; i < hits.Length; i++)
                {
                    var h = hits[i];
                    if (h.collider == null) continue;
                    if (h.collider.gameObject.name == "MCBlock") continue;
                    if (self != null && h.collider.transform.IsChildOf(self)) continue;
                    if (h.distance < best) { best = h.distance; hit = h; }
                }
                if (best == float.MaxValue) return;
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
