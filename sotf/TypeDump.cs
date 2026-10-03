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


        static readonly string[] DefaultRequest =
        {
            "Sons.Input.SonsInputMapping+DefaultActions", "Sons.Input.SonsInputMapping+ItemHotkeyActions", "Sons.Input.SonsInputMapping+IDefaultActions",
            "Sons.Input.InputSystem+Action", "Sons.Input.InputSystem+ActionId", "Sons.Input.InputActionMapState",
            "Sons.Ai.Vail.VailActor", "Sons.Ai.Vail.VailController", "Sons.Ai.Vail.VailControllerBase", "Sons.Ai.Vail.ActorHealthSettings", "Sons.Ai.Vail.DamageReceivedStimuli",
            "Sons.Ai.Vail.StimuliTypes.ExplosionEvent", "Sons.Ai.Vail.VailActorManager",
            "Sons.Weapon.IImpactReceiver", "Sons.Weapon.IImpactData", "Sons.Weapon.DamageController", "Sons.Weapon.DamageNode", "Sons.Weapon.PhysicalImpactReceiver", "Sons.Weapon.IImpactSender",
            "Sons.StatSystem.HealthStat", "ExplosionImpactData", "MeleeImpactData", "ProjectileImpactData", "Explode",
            "Sons.Gameplay.TreeCutting.TreeCutManager", "Sons.Gameplay.TreeCutting.TreeCutEventReceiver", "Sons.Gameplay.TreeCutting.FallingTreeSpawner", "Sons.Gameplay.TreeCutting.FallingTreeDamage",
            "Sons.Gameplay.TreeCutting.TreeCutGrid", "treeHitTrigger", "Sons.Gameplay.MeleeWeapon", "Sons.Weapon.MeleeWeaponController",
            "Vitals", "TheForest.Utils.LocalPlayer", "playerHitReactions", "IKnockDownReceiver", "PlayerAnimatorControl", "PlayerAnimatorControl+KnockdownType"
        };

        /// <summary>F9: full member dump (methods, properties, fields, enum values) of the types listed in BepInEx\dump-request.txt (one full name per line), else a built-in list.</summary>
        public static void RunRequest(string dir)
        {
            var req = File.Exists(Path.Combine(dir, "dump-request.txt")) ? File.ReadAllLines(Path.Combine(dir, "dump-request.txt")).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray() : DefaultRequest;
            var sb = new StringBuilder();
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var name in req)
            {
                Type found = null;
                foreach (var a in asms) { try { found = a.GetType(name); } catch (Exception) { } if (found != null) break; }
                if (found == null) { sb.Append("== ").Append(name).AppendLine("  NOT FOUND"); continue; }
                sb.Append("== ").Append(found.FullName).Append("  : ").Append(found.BaseType?.FullName).AppendLine();
                try
                {
                    if (found.IsEnum) { sb.Append("  enum: ").AppendLine(string.Join(", ", Enum.GetNames(found))); continue; }
                    var all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                    foreach (var f in found.GetFields(all))
                        if (!f.Name.StartsWith("NativeFieldInfoPtr")) sb.Append("  field ").Append(f.IsStatic ? "static " : "").Append(f.FieldType.Name).Append(' ').AppendLine(f.Name);
                    foreach (var m in found.GetMethods(all))
                    {
                        if (m.Name.StartsWith("NativeMethodInfoPtr") || m.Name.StartsWith("Invoke_") || m.Name.Contains("BackingField")) continue;
                        sb.Append("  ").Append(m.IsStatic ? "static " : "").Append(m.ReturnType.Name).Append(' ').Append(m.Name).Append('(')
                          .Append(string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))).AppendLine(")");
                    }
                }
                catch (Exception e) { sb.Append("  (error: ").Append(e.GetType().Name).AppendLine(")"); }
            }
            File.WriteAllText(Path.Combine(dir, "sotf-request.txt"), sb.ToString());
        }

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
