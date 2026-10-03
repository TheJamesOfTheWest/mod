using System;
using System.IO;

namespace SotfPassthrough
{
    /// <summary>Flight recorder: a timestamped debug file (BepInEx\sotf-debug.log) with state snapshots and events. Rotated at ~20 MB.</summary>
    public static class Dbg
    {
        static StreamWriter _w;
        static readonly object L = new object();

        public static void Init(string dir)
        {
            try
            {
                var path = Path.Combine(dir, "sotf-debug.log");
                if (File.Exists(path) && new FileInfo(path).Length > 20_000_000) File.Move(path, path + ".old", true);
                _w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
                Line("=== session start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            }
            catch (Exception) { }
        }

        public static void Line(string s)
        {
            lock (L) { try { _w?.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); } catch (Exception) { } }
        }

        public static void Flush()
        {
            lock (L) { try { _w?.Flush(); } catch (Exception) { } }
        }
    }
}
