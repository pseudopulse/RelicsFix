using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using RoR2;
using UnityEngine;
using System.Collections;
using MonoMod.Utils;
using MonoMod.Cil;
using System;
using BepInEx;
using System.IO;
using System.Reflection;
using Path = System.IO.Path;
using MonoMod.RuntimeDetour;
using HarmonyLib;

[module: System.Security.UnverifiableCode]
#pragma warning disable CS0618
[assembly: System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 

namespace RelicsFix
{
    public static class RelicsFix
    {
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];
        private static ModuleDefinition FR;

        public static void Patch(AssemblyDefinition assemblyDef)
        {
            
        }
        
        public static void Finish() {
            string filePathBackup = Directory.EnumerateFiles(BepInEx.Paths.PluginPath, "FHCSharp.dll.bak", SearchOption.AllDirectories).FirstOrDefault();
            string filePath = Directory.EnumerateFiles(BepInEx.Paths.PluginPath, "FHCSharp.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (filePath != null) {
                if (filePathBackup == null) {
                    filePathBackup = filePath + ".bak";
                    if (File.Exists(filePathBackup)) {
                        File.Delete(filePathBackup);
                    }
                    File.Move(filePath, filePathBackup);
                }
            }
            else {
                filePath = filePathBackup;
            }
            AssemblyDefinition assemblyDef = AssemblyDefinition.ReadAssembly(filePathBackup);
            FR = assemblyDef.MainModule;

            // Fix FRCSharp.FRItemIDRS.ItemDisplayRuleSet_Init wrong return type issue.
            TypeDefinition FRItemsIDRS = FR.GetType("FRCSharp.FRItemIDRS");
            MethodDefinition ItemDisplayRuleSet_Init = FRItemsIDRS.Methods.First(x => x.Name == "ItemDisplayRuleSet_Init");
            ItemDisplayRuleSet_Init.Parameters.Clear(); // couldnt get an empty enumerator delegate to pass so we just strip all params
            ILProcessor proc = ItemDisplayRuleSet_Init.Body.GetILProcessor();
            int index = proc.Body.Instructions.IndexOf(proc.Body.Instructions.First(x => x.MatchLdarg(0)));
            proc.Replace(proc.Body.Instructions.ElementAt(index), Instruction.Create(OpCodes.Nop));
            proc.Replace(proc.Body.Instructions.ElementAt(index + 1), Instruction.Create(OpCodes.Nop));

            // Fix CostTypeDef
            TypeDefinition BCI = FR.GetType("FRCSharp.BatteryContainerInteraction");
            MethodDefinition OnInteractionBegin = BCI.Methods.First(x => x.Name == "OnInteractionBegin");
            proc = OnInteractionBegin.Body.GetILProcessor();
            index = proc.Body.Instructions.IndexOf(Match(proc,
                x => x.MatchLdcI4(5),
                x => x.MatchCallOrCallvirt(out _),
                x => x.MatchLdcI4(2),
                x => x.MatchLdarg(1),
                x => x.MatchLdarg(0)
            ));
            NopRange(proc, index, 2);
            index = proc.Body.Instructions.IndexOf(Match(proc,
                x => x.MatchLdcI4(2),
                x => x.MatchLdarg(1),
                x => x.MatchLdarg(0),
                x => x.MatchCallOrCallvirt(out _),
                x => x.MatchLdarg(0),
                x => x.MatchLdfld(out _),
                x => x.MatchLdcI4(-1),
                x => x.MatchCallOrCallvirt(out _)
            ));
            index += 7;
            NopRange(proc, index, 3);
            index += 2;
            EmitIndex(proc, index, Instruction.Create(OpCodes.Call, FR.ImportReference(AccessTools.Method(typeof(RelicsFix), nameof(RelicsFix.Hell)))));
            OnInteractionBegin.RecalculateILOffsets();

            // hard crashes idfk
            // spawn pools broken only coil golems spawn


            // Print(proc);

            assemblyDef.Write(Path.Combine(Path.GetDirectoryName(filePath), "FHCSharp.dll"));
            assemblyDef.Dispose();
        }

        private static void ReplaceIndex(ILProcessor proc, int index, Instruction instruction) {
            Instruction target = proc.Body.Instructions.ElementAt(index);
            proc.Replace(target, instruction);
        }

        private static void EmitIndex(ILProcessor proc, int index, Instruction instruction) {
            Instruction target = proc.Body.Instructions.ElementAt(index);
            proc.InsertAfter(target, instruction);
        }

        private static void NopRange(ILProcessor proc, int start, int amount) {
            for (int i = 0; i < amount; i++) {
                Instruction target = proc.Body.Instructions.ElementAt(start);
                Instruction nop = Instruction.Create(OpCodes.Nop);
                proc.Replace(target, nop);
                start++;
            }
        }

        private static void Print(ILProcessor proc) {
            foreach (var inst in proc.Body.Instructions) {
                Logger.Error(inst.ToString());
            }
        }

        private static List<ItemIndex>.Enumerator Hell(int cost, Interactor interactor, GameObject obj, Xoroshiro128Plus rng, ItemIndex index) {
            CostTypeDef.PayCostContext context = new();
            context.activator = interactor;
            context.activatorBody = interactor.GetComponent<CharacterBody>();
            context.activatorMaster = context.activatorBody?.master;
            context.activatorInventory = context.activatorMaster?.inventory;
            context.cost = cost;
            context.costTypeDef = CostTypeCatalog.GetCostTypeDef(CostTypeIndex.GreenItem);
            context.rng = rng;
            context.purchasedObject = obj;
            context.avoidedItemIndex = (ItemIndex)index;
            CostTypeDef.PayCostResults results = new();
            context.costTypeDef.PayCost(context, results);
            List<ItemIndex> indexes = new();
            foreach (var stack in results.itemStacksTaken)
            {
                for (int i = 0; i < stack.stackValues.totalStacks; i++)
                {
                    indexes.Add(stack.itemIndex);
                }
            }
            return indexes.GetEnumerator();
        }

        private static Instruction Match(ILProcessor proc, params Func<Instruction, bool>[] matches) {
            return proc.Body.Instructions.First((x) => {
                int index = proc.Body.Instructions.IndexOf(x);

                for (int i = 0; i < matches.Length; i++) {
                    Instruction instruction = proc.Body.Instructions.ElementAt(index);
                    if (!matches[i](instruction)) {
                        return false;
                    }
                    index++;
                }

                return true;
            });
        }

        internal static class Logger
        {
            private static readonly ManualLogSource logSource = BepInEx.Logging.Logger.CreateLogSource("RelicsFix");

            public static void Info(object data) => logSource.LogInfo(data);

            public static void Error(object data) => logSource.LogError(data);

            public static void Warn(object data) => logSource.LogWarning(data);

            public static void Fatal(object data) => logSource.LogFatal(data);

            public static void Message(object data) => logSource.LogMessage(data);

            public static void Debug(object data) => logSource.LogDebug(data);
        }
    }
}