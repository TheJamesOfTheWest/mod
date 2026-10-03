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
        readonly bool[] _prevText = new bool[256];
        readonly float[] _repeatAt = new float[256];
        bool _walk;                   // F6: Steve drives, the game's body follows Minecraft's player
        Vector3 _camOffset;           // camera minus body root, measured when walking starts
        Vector3 _mcFeet; float _mcEye = 1.62f; bool _haveMcPos;
        float _nextGuard, _nextHide; float _lastSurface = float.NaN;
        // ground heights (MC y at each column centre) and the 8x8 regions whose triangles must be (re)sent
        readonly Dictionary<long, float> _heights = new Dictionary<long, float>();
        readonly HashSet<long> _dirty = new HashSet<long>();
        readonly Dictionary<long, float> _sentAt = new Dictionary<long, float>();
        bool _mcScreen;               // a Minecraft screen (inventory) is open: the virtual cursor drives it
        float _cx, _cy;               // virtual cursor, in Minecraft-window pixels
        float _lastCx = -1, _lastCy = -1;
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
            if (_ws.JustConnected) { _ws.JustConnected = false; _sampled.Clear(); _retryAt.Clear(); _heights.Clear(); _dirty.Clear(); _sentAt.Clear(); _haveOffset = false; _ws.Send("{\"t\":\"clear\"}"); _ws.Send("{\"t\":\"view\",\"w\":" + Screen.width + ",\"h\":" + Screen.height + "}"); _ws.Send("{\"t\":\"blocksync\",\"r\":48}"); Hooks.ClearBlocks(); }
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
            float dsx = 0f, dsy = 0f;
            try
            {
                // an upscaler draws the scene smaller than the screen: tell the effect so it reads the depth buffer in the right place
                float sx = (float)cam.scaledPixelWidth / Math.Max(1, cam.pixelWidth), sy = (float)cam.scaledPixelHeight / Math.Max(1, cam.pixelHeight);
                if (sx > 0.2f && sx < 0.99f && sy > 0.2f && sy < 0.99f) { dsx = sx; dsy = sy; }
            }
            catch (Exception) { }
            _shm.Write(true, cam.nearClipPlane, cam.farClipPlane, cam.fieldOfView, yaw, pitch, 0f, -pos.x, pos.y + _yOffset, pos.z, dsx, dsy);

            SampleGround(feet);
            if (_walk && _haveMcPos)
            {
                GuardGround();
                Hooks.MoveBody(_mcFeet + Vector3.up * _mcEye - _camOffset);
            }
            if (_mcMode && Time.unscaledTime > _nextHide) { _nextHide = Time.unscaledTime + 2f; Hooks.HideBody(true); }
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
            Edge(0x76, d => { if (d) { _mcMode = !_mcMode; Plugin.Instance.Log.LogInfo("Minecraft mode " + (_mcMode ? "ON" : "OFF")); Hooks.SetInputBlocked(_mcMode); if (!_mcMode) Hooks.HideBody(false); _nextReassert = Time.unscaledTime + 2f; if (!_mcMode) ReleaseKeys(); } }); // F7
            Edge(0x77, d => { if (d) { try { TypeDump.Run(BepInEx.Paths.BepInExRootPath); Plugin.Instance.Log.LogInfo("Wrote sotf-types.txt and sotf-members.txt to " + BepInEx.Paths.BepInExRootPath); } catch (Exception e) { Plugin.Instance.Log.LogError(e.ToString()); } } }); // F8
            Edge(0x78, d => { if (d) { try { TypeDump.RunRequest(BepInEx.Paths.BepInExRootPath); Plugin.Instance.Log.LogInfo("Wrote sotf-request.txt"); } catch (Exception e) { Plugin.Instance.Log.LogError(e.ToString()); } } }); // F9
            Edge(0x79, d => { if (d) { var c = Camera.main; Hooks.SpawnExplosion(c.transform.position + c.transform.forward * 8f); } }); // F10: test explosion 8 m ahead
            Edge(0x75, d => { if (d) ToggleWalk(); }); // F6: Steve drives
            Edge(0x7A, d => { if (d) { _mcMode = false; _walk = false; _mcScreen = false; Hooks.Unstick(); } }); // F11: undo everything
            if (!_mcMode) return;
            if (_walk && !_mcScreen)
            {
                Edge(0x57, d => Key("forward", d)); Edge(0x53, d => Key("back", d)); Edge(0x41, d => Key("left", d)); Edge(0x44, d => Key("right", d));
                Edge(0x20, d => Key("jump", d)); Edge(0xA0, d => Key("sneak", d)); Edge(0xA2, d => Key("sprint", d));
            }
            if (Time.unscaledTime > _nextReassert) { _nextReassert = Time.unscaledTime + 2f; Hooks.SetInputBlocked(true); }
            Edge(0x45, d => { if (d && !_mcScreen) { Key("inventory", true); Key("inventory", false); } }); // E opens the Minecraft inventory (once open, keys go to the screen, so E can close it or type into a search box)
            Edge(0x1B, d => { if (d && _mcScreen) Key("escape", true); });                                                                              // Esc closes a Minecraft screen
            if (_mcScreen)
            {
                try
                {
                    var delta = UnityEngine.InputSystem.Mouse.current.delta.ReadValue();
                    _cx = Mathf.Clamp(_cx + delta.x * 1.5f, 0f, Screen.width - 1f);
                    _cy = Mathf.Clamp(_cy - delta.y * 1.5f, 0f, Screen.height - 1f);
                    if (_cx != _lastCx || _cy != _lastCy) { _lastCx = _cx; _lastCy = _cy; _ws.Send("{\"t\":\"mouse\",\"x\":" + _cx.ToString("R", C) + ",\"y\":" + _cy.ToString("R", C) + "}"); }
                }
                catch (Exception) { }
                ForwardScreenKeys();
                try
                {
                    float sc = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y / 120f;
                    if (sc > 0.01f) _ws.Send("{\"t\":\"mscroll\",\"d\":1}");
                    else if (sc < -0.01f) _ws.Send("{\"t\":\"mscroll\",\"d\":-1}");
                }
                catch (Exception) { }
                Edge(0x01, d => _ws.Send("{\"t\":\"click\",\"b\":0,\"down\":" + (d ? "true" : "false") + "}"));
                Edge(0x02, d => _ws.Send("{\"t\":\"click\",\"b\":1,\"down\":" + (d ? "true" : "false") + "}"));
                return;
            }
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
                if (!root.TryGetProperty("t", out var t)) return;
                switch (t.GetString())
                {
                    case "explosion":
                    {
                        var p = root.GetProperty("pos");
                        float mx = (float)p[0].GetDouble(), my = (float)p[1].GetDouble(), mz = (float)p[2].GetDouble();
                        float r = root.TryGetProperty("r", out var rr) ? (float)rr.GetDouble() : 0f;
                        Hooks.SpawnExplosion(new Vector3(-mx, my - _yOffset, mz), r);
                        break;
                    }
                    case "blocks":
                    {
                        var set = new List<int>(); var clear = new List<int>();
                        foreach (var v in root.GetProperty("set").EnumerateArray()) set.Add(v.GetInt32());
                        foreach (var v in root.GetProperty("clear").EnumerateArray()) clear.Add(v.GetInt32());
                        Hooks.ApplyBlocks(set, clear, _yOffset);
                        break;
                    }
                    case "mcpos":
                    {
                        if (!root.TryGetProperty("walk", out var w) || !w.GetBoolean()) break;
                        var p = root.GetProperty("pos");
                        float mx = (float)p[0].GetDouble(), my = (float)p[1].GetDouble(), mz = (float)p[2].GetDouble();
                        _mcFeet = new Vector3(-mx, my - _yOffset, mz);
                        if (root.TryGetProperty("eye", out var e)) _mcEye = (float)e.GetDouble();
                        _haveMcPos = true;
                        break;
                    }
                    case "screen":
                    {
                        bool open = root.GetProperty("open").GetBoolean();
                        if (open != _mcScreen)
                        {
                            _mcScreen = open;
                            _cx = Screen.width * 0.5f; _cy = Screen.height * 0.5f; _lastCx = _lastCy = -1;
                            Hooks.SetLookBlocked(open);
                            Plugin.Instance.Log.LogInfo("Minecraft screen " + (open ? "open" : "closed") + " (cursor starts at " + _cx + "," + _cy + ", window " + Screen.width + "x" + Screen.height + ", game focused: " + RawInput.GameFocused() + ")");
                        }
                        break;
                    }
                }
            }
            catch (Exception e) { Plugin.Instance.Log.LogInfo("bad message: " + e.Message); }
        }

        /// <summary>
        /// Steve must never be inside the barrier ground (he would drop through it into the void): if Minecraft's feet are below the
        /// surface Minecraft has under them, put him back on it. The surface is the same rounding the ground columns use.
        /// </summary>
        void GuardGround()
        {
            if (Time.unscaledTime < _nextGuard) return;
            _nextGuard = Time.unscaledTime + 0.1f;
            float mcFeetY = _mcFeet.y + _yOffset;
            if (Ground(_mcFeet + Vector3.up * 1.5f, out float g)) _lastSurface = g + _yOffset;
            if (float.IsNaN(_lastSurface)) return;
            if (mcFeetY < _lastSurface - 0.6f)
                _ws.Send("{\"t\":\"setpos\",\"p\":[" + (-_mcFeet.x).ToString("R", C) + "," + (_lastSurface + 0.01f).ToString("R", C) + "," + _mcFeet.z.ToString("R", C) + "]}");
        }

        static readonly int[] ScreenKeys = BuildScreenKeys();

        static int[] BuildScreenKeys()
        {
            var l = new List<int>();
            for (int v = 0x30; v <= 0x39; v++) l.Add(v);
            for (int v = 0x41; v <= 0x5A; v++) l.Add(v);
            l.AddRange(new[] { 0x20, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE, 0x08, 0x0D, 0x09, 0x2E, 0x25, 0x26, 0x27, 0x28, 0x24, 0x23 });
            return l.ToArray();
        }

        /// <summary>SDL scancode (what Minecraft 26 uses) for a Windows virtual key; 0 if none.</summary>
        static int Scan(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return 4 + (vk - 0x41);
            if (vk >= 0x31 && vk <= 0x39) return 30 + (vk - 0x31);
            switch (vk)
            {
                case 0x30: return 39; case 0x0D: return 40; case 0x08: return 42; case 0x09: return 43; case 0x20: return 44;
                case 0xBD: return 45; case 0xBB: return 46; case 0xDB: return 47; case 0xDD: return 48; case 0xDC: return 49;
                case 0xBA: return 51; case 0xDE: return 52; case 0xC0: return 53; case 0xBC: return 54; case 0xBE: return 55; case 0xBF: return 56;
                case 0x2E: return 76; case 0x24: return 74; case 0x23: return 77;
                case 0x27: return 79; case 0x25: return 80; case 0x28: return 81; case 0x26: return 82;
            }
            return 0;
        }

        /// <summary>While a Minecraft screen is open every typing key goes to it as a key press plus (when it makes a character) typed text, with key repeat.</summary>
        void ForwardScreenKeys()
        {
            bool shift = RawInput.Down(0x10), ctrl = RawInput.Down(0x11), alt = RawInput.Down(0x12);
            int mods = (shift ? 3 : 0) | (ctrl ? 192 : 0) | (alt ? 768 : 0);
            float now = Time.unscaledTime;
            foreach (int vk in ScreenKeys)
            {
                bool d = RawInput.Down(vk);
                bool fire = false;
                if (d && !_prevText[vk]) { fire = true; _repeatAt[vk] = now + 0.4f; }
                else if (d && now > _repeatAt[vk]) { fire = true; _repeatAt[vk] = now + 0.04f; }
                _prevText[vk] = d;
                if (!fire) continue;
                int sc = Scan(vk);
                if (sc != 0) _ws.Send("{\"t\":\"skey\",\"sc\":" + sc + ",\"mods\":" + mods + "}");
                if (!ctrl && !alt)
                {
                    int cp = RawInput.CharFor(vk, shift);
                    if (cp >= 32) _ws.Send("{\"t\":\"char\",\"c\":" + cp + "}");
                }
            }
        }

        void ToggleWalk()
        {
            _walk = !_walk;
            if (_walk)
            {
                var cam = Camera.main;
                if (cam == null || !Hooks.TryGetBody(out var body)) { _walk = false; Plugin.Instance.Log.LogInfo("walk: could not find the player body"); return; }
                _camOffset = cam.transform.position - body;
                _haveMcPos = false; _lastSurface = float.NaN;
                if (!_mcMode) { _mcMode = true; Hooks.SetInputBlocked(true); }
                Hooks.SetMoveBlocked(true); Hooks.SetBodyKinematic(true); Hooks.SetBlocksEnabled(false);
                _ws.Send("{\"t\":\"walk\",\"on\":true}");
            }
            else
            {
                Hooks.SetMoveBlocked(false); Hooks.SetBodyKinematic(false); Hooks.SetBlocksEnabled(true);
                _ws.Send("{\"t\":\"walk\",\"on\":false}");
                foreach (var k in new[] { "forward", "back", "left", "right", "jump", "sneak", "sprint" }) Key(k, false);
            }
            Plugin.Instance.Log.LogInfo("Steve drives: " + (_walk ? "ON" : "OFF"));
        }

        void Key(string k, bool down) { _ws.Send("{\"t\":\"key\",\"k\":\"" + k + "\",\"down\":" + (down ? "true" : "false") + "}"); }

        void ReleaseKeys() { Key("attack", false); Key("use", false); Key("pick", false); }


        static bool Ground(Vector3 from, out float groundY)
        {
            // The nearest hit that is not one of our own Minecraft-block colliders (those must stay on the default layer so the game's
            // controller treats them as ground). Water, triggers and the player's own collider may need excluding: TODO from the dump.
            var hits = Physics.RaycastAll(from, Vector3.down, 80f, ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; groundY = 0;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.gameObject.name == "MCBlock") continue;
                if (h.distance < best) { best = h.distance; groundY = h.point.y; }
            }
            return best < float.MaxValue;
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
                int top = (int)Math.Floor(g + _yOffset) - 1; // the invisible ground blocks stay at or below the terrain: the player walks on the triangles above them
                _heights[HKey(mx, mz)] = g + _yOffset;
                for (int qx = mx - 1; qx <= mx; qx++) for (int qz = mz - 1; qz <= mz; qz++) _dirty.Add(HKey(FloorDiv(qx, 8), FloorDiv(qz, 8)));
                if (sb.Length > 0) sb.Append(',');
                sb.Append(mx).Append(',').Append(mz).Append(',').Append(top - GroundDepth + 1).Append(',').Append(top);
            }
            if (sb.Length > 0) _ws.Send("{\"t\":\"ground\",\"c\":[" + sb + "]}");
            SendTriangles();
        }

        static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

        static long HKey(int x, int z) => ((long)x << 32) | (uint)z;

        /// <summary>Triangulates the ground between neighbouring column centres, one 8x8 region at a time, and sends it as a binary frame.</summary>
        void SendTriangles()
        {
            if (_dirty.Count == 0) return;
            int sent = 0;
            var done = new List<long>();
            foreach (var rk in _dirty)
            {
                if (sent >= 4) break;
                if (_sentAt.TryGetValue(rk, out float last) && Time.unscaledTime - last < 0.5f) continue;
                int rx = (int)(rk >> 32), rz = (int)(rk & 0xFFFFFFFF);
                var ms = new System.IO.MemoryStream();
                var w = new System.IO.BinaryWriter(ms);
                w.Write(rx); w.Write(rz); w.Write(0);
                int count = 0;
                for (int qx = rx * 8; qx < rx * 8 + 8; qx++)
                    for (int qz = rz * 8; qz < rz * 8 + 8; qz++)
                    {
                        if (!_heights.TryGetValue(HKey(qx, qz), out float h00) || !_heights.TryGetValue(HKey(qx + 1, qz), out float h10)
                            || !_heights.TryGetValue(HKey(qx, qz + 1), out float h01) || !_heights.TryGetValue(HKey(qx + 1, qz + 1), out float h11)) continue;
                        float x0 = qx + 0.5f, x1 = qx + 1.5f, z0 = qz + 0.5f, z1 = qz + 1.5f;
                        WriteTri(w, x0, h00, z0, x0, h01, z1, x1, h10, z0); WriteTri(w, x1, h10, z0, x0, h01, z1, x1, h11, z1);
                        count += 2;
                    }
                var bytes = ms.ToArray();
                BitConverter.GetBytes(count).CopyTo(bytes, 8);
                _ws.SendBinary(bytes);
                _sentAt[rk] = Time.unscaledTime;
                done.Add(rk); sent++;
            }
            foreach (var rk in done) _dirty.Remove(rk);
        }

        static void WriteTri(System.IO.BinaryWriter w, float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz)
        {
            w.Write(ax); w.Write(ay); w.Write(az); w.Write(bx); w.Write(by); w.Write(bz); w.Write(cx); w.Write(cy); w.Write(cz);
            w.Write(8); // TRI_TERRAIN
        }
    }
}
