using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SotfPassthrough
{
    /// <summary>Raw Windows key/mouse polling (works whatever input system the game uses) and "is the game focused".</summary>
    public static class RawInput
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")] static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll")] static extern short GetKeyState(int nVirtKey);

        /// <summary>The character a key produces with the current layout (0 for none).</summary>
        public static int CharFor(int vk, bool shift)
        {
            try
            {
                var st = new byte[256];
                if (shift) { st[0x10] = 0x80; st[0xA0] = 0x80; }
                if ((GetKeyState(0x14) & 1) != 0) st[0x14] = 1;
                var sb = new StringBuilder(4);
                int n = ToUnicode((uint)vk, MapVirtualKey((uint)vk, 0), st, sb, 4, 0);
                return n == 1 ? sb[0] : 0;
            }
            catch (Exception) { return 0; }
        }

        public static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        public static bool GameFocused()
        {
            uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return pid == (uint)Environment.ProcessId;
        }
    }
}
