using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SotfPassthrough
{
    /// <summary>
    /// Discovery aid: writes the game's type names and, for interesting types, their members, so we can find the real hooks
    /// (player, input, damage, enemies, trees, water). Press F8 in a world. Output: BepInEx\sotf-types.txt and sotf-members.txt.
    /// </summary>
    public static class TypeDump
    {
        static readonly string[] SkipPrefixes = { "System", "Unity", "Il2CppSystem", "Il2Cppmscorlib", "mscorlib", "BepInEx", "Il2CppInterop", "0Harmony", "Mono", "netstandard", "Microsoft", "Newtonsoft", "Cpp2IL", "AsmResolver", "AssetRipper", "Iced", "MonoMod", "SotfPassthrough", "Il2CppFMOD", "FMOD" };
        static readonly Regex Interesting = new Regex("Input|LocalPlayer|Health|Damage|Tree|Cannibal|Enemy|Mutant|Water|Swim|Weapon|Axe|Hit|Explo|Fell|Chop|PlayerState|Controller|Inventory|ItemHolder|Cursor|Block", RegexOptions.IgnoreCase);

        public static void Run(string dir)
        {
            var types = new StringBuilder();
            var members = new StringBuilder();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (SkipPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                Type[] all;
                try { all = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { all = e.Types.Where(t => t != null).ToArray(); }
                catch (Exception) { continue; }
                foreach (var t in all)
                {
                    types.Append(name).Append(": ").AppendLine(t.FullName);
                    if (!Interesting.IsMatch(t.Name)) continue;
                    members.Append("== ").Append(t.FullName).Append("  [").Append(name).AppendLine("]");
                    try
                    {
                        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        {
                            if (m.IsSpecialName && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")) continue;
                            if (m.Name.StartsWith("NativeMethodInfoPtr") || m.Name.StartsWith("Invoke_")) continue;
                            members.Append("  ").Append(m.IsStatic ? "static " : "").Append(m.ReturnType.Name).Append(' ').Append(m.Name).Append('(')
                                   .Append(string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))).AppendLine(")");
                        }
                    }
                    catch (Exception) { members.AppendLine("  (members unavailable)"); }
                }
            }
            File.WriteAllText(Path.Combine(dir, "sotf-types.txt"), types.ToString());
            File.WriteAllText(Path.Combine(dir, "sotf-members.txt"), members.ToString());
        }
    }
}
