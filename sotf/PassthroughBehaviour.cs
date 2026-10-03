using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
        float _yOffset; bool _haveOffset;
        readonly HashSet<long> _sampled = new HashSet<long>();
        List<(int dx, int dz)> _spiral;

        void Awake() { _ws = new WsClient("ws://127.0.0.1:25599"); }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || !_ws.Connected) return;
            if (_ws.JustConnected) { _ws.JustConnected = false; _sampled.Clear(); _haveOffset = false; _ws.Send("{\"t\":\"clear\"}"); }
            while (_ws.TryReceive(out _)) { } // TODO: handle "explosion" etc.

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

            SampleGround(feet);
        }

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
                if (++probes > ProbesPerFrame) break;
                if (!Ground(new Vector3(-(mx + 0.5f), feet.y + 1.5f, mz + 0.5f), out float g)) continue;
                _sampled.Add(key);
                int top = (int)Math.Floor(g + _yOffset + 0.5f) - 1;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(mx).Append(',').Append(mz).Append(',').Append(top - GroundDepth + 1).Append(',').Append(top);
            }
            if (sb.Length > 0) _ws.Send("{\"t\":\"ground\",\"c\":[" + sb + "]}");
        }
    }
}
