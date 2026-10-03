using System.IO.MemoryMappedFiles;

namespace SotfPassthrough
{
    /// <summary>Writes the game's camera into "Local\\SotfHostState" for the ReShade add-on (layout in addon/compositor.cpp).</summary>
    public sealed class HostShm
    {
        readonly MemoryMappedFile _file;
        readonly MemoryMappedViewAccessor _view;
        long _seq;

        public HostShm()
        {
            _file = MemoryMappedFile.CreateOrOpen("Local\\SotfHostState", 256);
            _view = _file.CreateViewAccessor(0, 256);
            _view.Write(0, 0x53484653u); // "SFHS"
            _view.Write(4, 2u); // version 2: depth probes at offset 96
        }

        public void Write(bool active, float near, float far, float fov, float yaw, float pitch, float roll, double x, double y, double z, float depthScaleX = 0f, float depthScaleY = 0f, float[] probes = null)
        {
            _view.Write(8, ++_seq * 2 - 1);          // odd: being written
            _view.Write(16, active ? 1 : 0);
            _view.Write(20, near); _view.Write(24, far); _view.Write(28, fov);
            _view.Write(32, yaw); _view.Write(36, pitch); _view.Write(40, roll);
            _view.Write(48, x); _view.Write(56, y); _view.Write(64, z);
            _view.Write(72, depthScaleX); _view.Write(76, depthScaleY);
            // nine depth probes (u, v from the top-left, camera-space depth in metres, valid 0/1): where rays through the screen really hit
            for (int i = 0; i < 36; i++) _view.Write(96 + 4 * i, probes != null ? probes[i] : 0f);
            _view.Write(8, _seq * 2);                // even: complete
        }
    }
}
