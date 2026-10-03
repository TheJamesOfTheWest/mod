using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace SotfPassthrough
{
    /// <summary>
    /// Every frame: send Sons of the Forest's camera to Minecraft; stream the ground around the player as "ground" columns.
    ///
    /// Coordinates (UNVERIFIED, check with the axis test in README): 1 Unity metre = 1 block. Unity is left-handed (+Z forward, +X right),
    /// Minecraft is right-handed with yaw 0 = +Z and yaw growing toward -X. So MC = (-x, y + yOffset, z), yaw = atan2(fx, fz),
    /// pitch = -asin(fy) (positive = down).
    /// </summary>
    public class PassthroughBehaviour : MonoBehaviour
    {
        public PassthroughBehaviour(IntPtr ptr) : base(ptr) { }

        const int GroundRadius = 40, ProbesPerFrame = 160, GroundDepth = 6;
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        WsClient _ws;
        HostShm _shm;
        bool _mcMode;                 // F7: mouse buttons, wheel and 1-9 go to Minecraft
        readonly bool[] _prev = new bool[256];
        float _lastScroll;
        float _nextReassert;
        float _nextLog;
        float _yOffset; bool _haveOffset;
        readonly HashSet<long> _sampled = new HashSet<long>();
        // columns whose raycast found nothing (water, holes, not streamed in): retry later so they don't starve the nearer-first probe budget
        readonly Dictionary<long, float> _retryAt = new Dictionary<long, float>();
        List<(int dx, int dz)> _spiral;

        void Awake() { _ws = new WsClient("ws://127.0.0.1:25599"); _shm = new HostShm(); }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (Time.unscaledTime > _nextLog)
            {
                _nextLog = Time.unscaledTime + 5f;
                Plugin.Instance.Log.LogInfo("status: camera=" + (cam == null ? "none" : cam.name + " pos=" + cam.transform.position) + " ws=" + _ws.Connected + " offset=" + (_haveOffset ? _yOffset.ToString("0.00") : "unset") + " groundColumnsSent=" + _sampled.Count);
            }
            if (cam == null || !_ws.Connected) { _shm.Write(false, 0.3f, 1000f, 60f, 0, 0, 0, 0, 0, 0); return; }
            if (_ws.JustConnected) { _ws.JustConnected = false; _sampled.Clear(); _retryAt.Clear(); _haveOffset = false; _ws.Send("{\"t\":\"clear\"}"); _ws.Send("{\"t\":\"view\",\"w\":" + Screen.width + ",\"h\":" + Screen.height + "}"); }
            while (_ws.TryReceive(out string incoming)) HandleMessage(incoming);

            var t = cam.transform;
            Vector3 pos = t.position, fwd = t.forward;
            // Placeholder: SotF's player position isn't known yet (find the LocalPlayer feet via the IL2CPP dump). Camera minus eye height for now.
            Vector3 feet = pos - new Vector3(0, 1.6f, 0);

            if (!_haveOffset)
            {
                if (Ground(feet + Vector3.up * 1.5f, out float g)) { _yOffset = (float)Math.Round(g) - g; _haveOffset = true; }
                else return;
            }

            float yaw = (float)(Math.Atan2(fwd.x, fwd.z) * 180.0 / Math.PI);
            float pitch = (float)(-Math.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * 180.0 / Math.PI);
            var sb = new StringBuilder(256);
            sb.Append("{\"t\":\"cam\",\"f\":").Append(Time.frameCount);
            sb.Append(",\"p\":[").Append((-pos.x).ToString("R", C)).Append(',').Append((pos.y + _yOffset).ToString("R", C)).Append(',').Append(pos.z.ToString("R", C));
            sb.Append("],\"r\":[").Append(yaw.ToString("R", C)).Append(',').Append(pitch.ToString("R", C)).Append(",0]");
            sb.Append(",\"fov\":").Append(cam.fieldOfView.ToString("R", C)); // vertical FOV in Unity
            sb.Append(",\"fp\":true");
            sb.Append(",\"pl\":[").Append((-feet.x).ToString("R", C)).Append(',').Append((feet.y + _yOffset).ToString("R", C)).Append(',').Append(feet.z.ToString("R", C)).Append("]}");
            _ws.Send(sb.ToString());
            _shm.Write(true, cam.nearClipPlane, cam.farClipPlane, cam.fieldOfView, yaw, pitch, 0f, -pos.x, pos.y + _yOffset, pos.z);

            SampleGround(feet);
            PollInput();
        }

        void Edge(int vk, Action<bool> onChange)
        {
            bool d = RawInput.Down(vk);
            if (d != _prev[vk]) { _prev[vk] = d; onChange(d); }
        }

        void PollInput()
        {
            if (!RawInput.GameFocused()) return;
            Edge(0x76, d => { if (d) { _mcMode = !_mcMode; Plugin.Instance.Log.LogInfo("Minecraft mode " + (_mcMode ? "ON" : "OFF")); Hooks.SetInputBlocked(_mcMode); _nextReassert = Time.unscaledTime + 2f; if (!_mcMode) ReleaseKeys(); } }); // F7
            Edge(0x77, d => { if (d) { try { TypeDump.Run(BepInEx.Paths.BepInExRootPath); Plugin.Instance.Log.LogInfo("Wrote sotf-types.txt and sotf-members.txt to " + BepInEx.Paths.BepInExRootPath); } catch (Exception e) { Plugin.Instance.Log.LogError(e.ToString()); } } }); // F8
            Edge(0x78, d => { if (d) { try { TypeDump.RunRequest(BepInEx.Paths.BepInExRootPath); Plugin.Instance.Log.LogInfo("Wrote sotf-request.txt"); } catch (Exception e) { Plugin.Instance.Log.LogError(e.ToString()); } } }); // F9
            Edge(0x79, d => { if (d) { var c = Camera.main; Hooks.SpawnExplosion(c.transform.position + c.transform.forward * 8f); } }); // F10: test explosion 8 m ahead
            if (!_mcMode) return;
            if (Time.unscaledTime > _nextReassert) { _nextReassert = Time.unscaledTime + 2f; Hooks.SetInputBlocked(true); }
            Edge(0x01, d => { Key("attack", d); if (d) Hooks.TryChop(Camera.main); });
            Edge(0x02, d => Key("use", d));
            Edge(0x04, d => Key("pick", d));
            for (int n = 0; n < 9; n++) { int vk = 0x31 + n; int slot = n; Edge(vk, d => { if (d) _ws.Send("{\"t\":\"slot\",\"n\":" + slot + "}"); }); }
            try
            {
                float sc = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y / 120f; // the game uses the new Input System
                if (sc > 0.01f) _ws.Send("{\"t\":\"scroll\",\"d\":-1}");
                else if (sc < -0.01f) _ws.Send("{\"t\":\"scroll\",\"d\":1}");
            }
            catch (Exception) { }
        }

        /// <summary>Messages from Minecraft: {"t":"explosion","pos":[x,y,z],"r":radius} (Minecraft coordinates).</summary>
        void HandleMessage(string msg)
        {
            try
            {
                using var doc = JsonDocument.Parse(msg);
                var root = doc.RootElement;
                if (!root.TryGetProperty("t", out var t) || t.GetString() != "explosion") return;
                var p = root.GetProperty("pos");
                float mx = (float)p[0].GetDouble(), my = (float)p[1].GetDouble(), mz = (float)p[2].GetDouble();
                Hooks.SpawnExplosion(new Vector3(-mx, my - _yOffset, mz));
            }
            catch (Exception e) { Plugin.Instance.Log.LogInfo("bad message: " + e.Message); }
        }

        void Key(string k, bool down) { _ws.Send("{\"t\":\"key\",\"k\":\"" + k + "\",\"down\":" + (down ? "true" : "false") + "}"); }

        void ReleaseKeys() { Key("attack", false); Key("use", false); Key("pick", false); }


        static bool Ground(Vector3 from, out float groundY)
        {
            // Layer mask: everything except "Ignore Raycast" (layer 2). Water, triggers and the player's own collider may need excluding: TODO from the dump.
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 80f, ~(1 << 2), QueryTriggerInteraction.Ignore)) { groundY = hit.point.y; return true; }
            groundY = 0; return false;
        }

        void SampleGround(Vector3 feet)
        {
            if (_spiral == null)
            {
                _spiral = new List<(int, int)>();
                for (int dx = -GroundRadius; dx <= GroundRadius; dx++)
                    for (int dz = -GroundRadius; dz <= GroundRadius; dz++)
                        if (dx * dx + dz * dz <= GroundRadius * GroundRadius) _spiral.Add((dx, dz));
                _spiral.Sort((a, b) => (a.Item1 * a.Item1 + a.Item2 * a.Item2).CompareTo(b.Item1 * b.Item1 + b.Item2 * b.Item2));
            }
            // MC column (mx, mz) <-> Unity x = -(mx + 0.5), z = mz + 0.5
            int px = (int)Math.Floor(-feet.x), pz = (int)Math.Floor(feet.z);
            var sb = new StringBuilder(); int probes = 0;
            foreach (var (dx, dz) in _spiral)
            {
                int mx = px + dx, mz = pz + dz; long key = ((long)mx << 32) ^ (uint)mz;
                if (_sampled.Contains(key)) continue;
                if (_retryAt.TryGetValue(key, out float at) && Time.unscaledTime < at) continue;
                if (++probes > ProbesPerFrame) break;
                if (!Ground(new Vector3(-(mx + 0.5f), feet.y + 1.5f, mz + 0.5f), out float g)) { _retryAt[key] = Time.unscaledTime + 3f; continue; }
                _retryAt.Remove(key);
                _sampled.Add(key);
                int top = (int)Math.Floor(g + _yOffset + 0.5f) - 1;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(mx).Append(',').Append(mz).Append(',').Append(top - GroundDepth + 1).Append(',').Append(top);
            }
            if (sb.Length > 0) _ws.Send("{\"t\":\"ground\",\"c\":[" + sb + "]}");
        }
    }
}
