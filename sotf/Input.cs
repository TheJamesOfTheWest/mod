using System;
using System.Runtime.InteropServices;

namespace SotfPassthrough
{
    /// <summary>Raw Windows key/mouse polling (works whatever input system the game uses) and "is the game focused".</summary>
    public static class RawInput
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        public static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        public static bool GameFocused()
        {
            uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return pid == (uint)Environment.ProcessId;
        }
    }
}
