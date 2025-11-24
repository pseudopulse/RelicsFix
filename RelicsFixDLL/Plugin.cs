using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using BepInEx;
using HarmonyLib;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine.AddressableAssets;
using FRCSharp;
using System.Reflection.Emit;
using OpCodes = Mono.Cecil.Cil.OpCodes;
using System.Diagnostics;
using UnityEngine;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using RoR2.ExpansionManagement;
using UnityEngine.SceneManagement;

//Allows you to access private methods/fields/etc from the stubbed Assembly-CSharp that is included.

[module: UnverifiableCode]
#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace RelicsFixDLL
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class _RelicsFixMain : BaseUnityPlugin 
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "_0pseudopulse";
        public const string PluginName = "_0RelicsFixDLL";
        public const string PluginVersion = "1.0.0";
        public static BepInEx.Logging.ManualLogSource ModLogger;
        public static MethodInfo ItemDisplayRuleSet_Init;
        //
        private static DccsPool BasinDCCS;
        private static DccsPool SatelliteDCCS;
        private static DccsPool SatelliteInteractDCCS;
        private static DccsPool HavenDCCS;
        private static DccsPool HavenInteractDCCS;

        private void Awake() 
        {
            // set logger
            ModLogger = Logger;

            ItemDisplayRuleSet_Init = AccessTools.Method(typeof(FRItemIDRS), nameof(FRItemIDRS.ItemDisplayRuleSet_Init));
            ApplyHook(AccessTools.Method(typeof(RoR2.ItemDisplayRuleSet), nameof(ItemDisplayRuleSet.Init)), nameof(IDRSInit));
            ApplyHookIL(AccessTools.Method(typeof(VF2ContentPackProvider), nameof(VF2ContentPackProvider.Init)), (il) => {
                ILCursor c = new(il);

                c.TryGotoNext(MoveType.Before,
                    x => x.MatchLdnull(),
                    x => x.MatchLdftn(out _),
                    x => x.MatchNewobj(out _),
                    x => x.MatchCallOrCallvirt(out _),
                    x => x.MatchLdsfld(typeof(VF2ConfigManager), nameof(VF2ConfigManager.disableSagesShrine))
                );
                c.RemoveRange(4);
            });
            ApplyHook(AccessTools.Method(typeof(RoR2.ClassicStageInfo), nameof(RoR2.ClassicStageInfo.RebuildCards)), nameof(RebuildCards));
        }

        private static void RebuildCards(Action<ClassicStageInfo, DirectorCardCategorySelection, DirectorCardCategorySelection> orig, ClassicStageInfo self, DirectorCardCategorySelection p1, DirectorCardCategorySelection p2) {
            if (!BasinDCCS) {
                BuildMonsterDCCS();
            }

            string scene = SceneManager.GetActiveScene().name;
            switch (scene) {
                case "drybasin":
                    self.monsterDccsPool = BasinDCCS;
                    self.interactableDccsPool = Utils.Assets.DccsPool.dpGooLakeInteractables;
                    break;
                case "slumberingsatellite":
                    self.monsterDccsPool = SatelliteDCCS;
                    self.interactableDccsPool = SatelliteInteractDCCS;
                    break;
                case "forgottenhaven":
                    self.monsterDccsPool = HavenDCCS;
                    self.interactableDccsPool = HavenInteractDCCS;
                    break;
            }

            orig(self, p1, p2);
        }

        private static void BuildMonsterDCCS() {
            BasinDCCS = ScriptableObject.CreateInstance<DccsPool>();
            SetupDCCSEnemy(BasinDCCS, Utils.Assets.DirectorCardCategorySelection.dccsGooLakeMonstersDLC3, null, Utils.Assets.DirectorCardCategorySelection.dccsGooLakeMonstersDLC1, Utils.Assets.DirectorCardCategorySelection.dccsGooLakeMonsters);
            AddFamily(BasinDCCS, Utils.Assets.FamilyDirectorCardCategorySelection.dccsGolemFamilySandy, 2);
            AddFamily(BasinDCCS, Utils.Assets.FamilyDirectorCardCategorySelection.dccsWispFamily, 3);
            AddFamily(BasinDCCS, Utils.Assets.FamilyDirectorCardCategorySelection.dccsImpFamily, 1);

            SatelliteDCCS = ScriptableObject.CreateInstance<DccsPool>();
            SetupDCCSEnemy(SatelliteDCCS, 
                Utils.Assets.DirectorCardCategorySelection.dccsSkyMeadowMonstersDLC3,
                Utils.Assets.DirectorCardCategorySelection.dccsSkyMeadowMonstersDLC2,
                Utils.Assets.DirectorCardCategorySelection.dccsSkyMeadowMonstersDLC1,
                VF2ContentPackProvider.bundle.LoadAsset<DirectorCardCategorySelection>("dccsSSMonsters.asset")
            );
            AddFamily(SatelliteDCCS, Utils.Assets.FamilyDirectorCardCategorySelection.dccsWispFamily, 1);
            AddFamily(SatelliteDCCS, Utils.Assets.FamilyDirectorCardCategorySelection.dccsImpFamily, 1);

            SatelliteInteractDCCS = GameObject.Instantiate(Utils.Assets.DccsPool.dpSkyMeadowInteractables);
            SatelliteInteractDCCS.poolCategories[0].alwaysIncluded.AddToArray(new DccsPool.PoolEntry() {
                dccs = VF2ContentPackProvider.bundle.LoadAsset<DirectorCardCategorySelection>("dccsSSInteractible.asset"),
                weight = 1f
            });

            HavenDCCS = ScriptableObject.CreateInstance<DccsPool>();
            SetupDCCSEnemy(HavenDCCS, 
                Utils.Assets.DirectorCardCategorySelection.dccsSkyMeadowMonstersDLC3,
                Utils.Assets.DirectorCardCategorySelection.dccsSkyMeadowMonstersDLC2,
                Utils.Assets.DirectorCardCategorySelection.dccsSkyMeadowMonstersDLC1,
                VF2ContentPackProvider.bundle.LoadAsset<DirectorCardCategorySelection>("dccsFHMonsters.asset")
            );
            AddFamily(HavenDCCS, Utils.Assets.FamilyDirectorCardCategorySelection.dccsLunarFamily, 1);

            HavenInteractDCCS = GameObject.Instantiate(Utils.Assets.DccsPool.dpSkyMeadowInteractables);
            HavenInteractDCCS.poolCategories[0].alwaysIncluded.AddToArray(new DccsPool.PoolEntry() {
                dccs = VF2ContentPackProvider.bundle.LoadAsset<DirectorCardCategorySelection>("iccsVF2.asset"),
                weight = 1f
            });
        }

        private static void SetupDCCSEnemy(DccsPool pool, DirectorCardCategorySelection dlc3, DirectorCardCategorySelection dlc2, DirectorCardCategorySelection dlc1, DirectorCardCategorySelection none) {
            pool.poolCategories = new DccsPool.Category[] {
                new DccsPool.Category() {
                    name = "Standard",
                    categoryWeight = 0.98f,
                    includedIfNoConditionsMet = new DccsPool.PoolEntry[0],
                    includedIfConditionsMet = new DccsPool.ConditionalPoolEntry[0],
                    alwaysIncluded = new DccsPool.PoolEntry[] {
                        new DccsPool.PoolEntry() {
                            dccs = none,
                            weight = 1
                        }
                    }
                },
                new DccsPool.Category() {
                    name = "Family",
                    categoryWeight = 0.02f,
                    alwaysIncluded = new DccsPool.PoolEntry[0],
                    includedIfConditionsMet = new DccsPool.ConditionalPoolEntry[0],
                },
                new DccsPool.Category() {
                    name = "VoidInvasion",
                    categoryWeight = 0.02f,
                    includedIfConditionsMet = new DccsPool.ConditionalPoolEntry[] {
                        new DccsPool.ConditionalPoolEntry() {
                            dccs = Utils.Assets.FamilyDirectorCardCategorySelection.dccsVoidFamily,
                            weight = 1,
                            requiredExpansions = new ExpansionDef[] {
                                Utils.Assets.ExpansionDef.DLC1
                            }
                        }
                    },
                    alwaysIncluded = new DccsPool.PoolEntry[0],
                    includedIfNoConditionsMet = new DccsPool.PoolEntry[0]
                }
            };
            List<DccsPool.ConditionalPoolEntry> entries = new();
            DccsPool.Category standard = pool.poolCategories[0];

            if (dlc3) {
                entries.Add(new() {
                    dccs = dlc3,
                    weight = 1f,
                    requiredExpansions = new ExpansionDef[] {
                        Utils.Assets.ExpansionDef.DLC3
                    }
                });
            }

            if (dlc2) {
                entries.Add(new() {
                    dccs = dlc2,
                    weight = 1f,
                    requiredExpansions = new ExpansionDef[] {
                        Utils.Assets.ExpansionDef.DLC2
                    }
                });
            }

            if (dlc1) {
                entries.Add(new() {
                    dccs = dlc1,
                    weight = 1f,
                    requiredExpansions = new ExpansionDef[] {
                        Utils.Assets.ExpansionDef.DLC1
                    }
                });
            }

            standard.includedIfConditionsMet = entries.ToArray();
        }

        private static void AddFamily(DccsPool pool, DirectorCardCategorySelection dccs, float weight) {
            List<DccsPool.PoolEntry> entries = pool.poolCategories[1].alwaysIncluded.ToList();
            DccsPool.Category family = pool.poolCategories[1];
            entries.Add(new() {
                dccs = dccs,
                weight = weight
            });
            family.alwaysIncluded = entries.ToArray();
        }

        private static IEnumerator IDRSInit(Func<IEnumerator> orig) {
            Invoke(ItemDisplayRuleSet_Init);
            yield return orig();
        }

        private static void ApplyHook(MethodInfo from, string to) {
            new Hook(from, AccessTools.Method(typeof(_RelicsFixMain), to));
        }

        private static void ApplyHookIL(MethodInfo from, ILContext.Manipulator il) {
            new ILHook(from, il);
        }

        private static void Invoke(MethodInfo info, params object[] param) {
            info.Invoke(null, param);
        }

        private static void InvokeInstance(MethodInfo info, object self, params object[] param) {
            info.Invoke(self, param);
        }
    }
}