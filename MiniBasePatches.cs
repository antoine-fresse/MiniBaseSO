/*
	MIT License

	Copyright (c) 2020 Steven Brelsford (Versepelles)

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in all
	copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
	SOFTWARE.
*/

using System;
using HarmonyLib;
using ProcGen;
using ProcGenGame;
using PeterHan.PLib.Options;
using System.Linq;
using static MiniBase.MiniBaseConfig;
using static MiniBase.MiniBaseUtils;
using UnityEngine;
using System.IO;
using PeterHan.PLib.Core;
using System.Collections.Generic;
using static Klei.ClusterLayoutSave;
using Klei;
using YamlDotNet.RepresentationModel;
using Klei.AI;
using Database;
using TUNING;

namespace MiniBase
{
    public class MiniBasePatches : KMod.UserMod2
    {
        public static string ModPath;
        public static List<WorldPlacement> DefaultWorldPlacements = null;
        public static List<SpaceMapPOIPlacement> DefaultPOIPlacements = null;

        public override void OnLoad(Harmony harmony)
        {
            PUtil.InitLibrary(false);
            MiniBaseOptions.Reload();
            new POptions().RegisterOptions(this, typeof(MiniBaseOptions));
            ModPath = mod.ContentPath;
            base.OnLoad(harmony);

            Log($"MiniBase loaded.", true);
        }

        // Reload mod options at asteroid select screen, before world gen happens
        [HarmonyPatch(typeof(ColonyDestinationSelectScreen), "LaunchClicked")]
        public static class ColonyDestinationSelectScreen_LaunchClicked_Patch
        {
            public static void Prefix()
            {
                Log("ColonyDestinationSelectScreen_LaunchClicked_Patch Prefix");
                MiniBaseOptions.Reload();
            }
        }

        [HarmonyPatch(typeof(GameplaySeasons), "Expansion1Seasons")]
        public static class GameplaySeasons_Expansion1Seasons_Patch
        {
            public static void Postfix(ref GameplaySeasons __instance)
            {
                // Custom meteor showers
                var MixedMinibaseShower = Db.Get().GameplayEvents.Add((GameplayEvent)new MeteorShowerEvent("ClusterMinibaseShower", 150f, 4.5f, METEORS.BOMBARDMENT_OFF.NONE, METEORS.BOMBARDMENT_ON.UNLIMITED, ClusterMapMeteorShowerConfig.GetFullID("HeavyDust"))
                    .AddMeteor(CopperCometConfig.ID, 1f)
                    .AddMeteor(IronCometConfig.ID, 1f)
                    .AddMeteor(GoldCometConfig.ID, 1f)
                    .AddMeteor(UraniumCometConfig.ID, 1f)
                    .AddMeteor(RockCometConfig.ID, 1f));

                var FullereneMinibaseShower = Db.Get().GameplayEvents.Add((GameplayEvent)new MeteorShowerEvent("FullereneMinibaseShower", 150f, 4.5f, METEORS.BOMBARDMENT_OFF.NONE, METEORS.BOMBARDMENT_ON.UNLIMITED, ClusterMapMeteorShowerConfig.GetFullID("HeavyDust"))
                    .AddMeteor(FullereneCometConfig.ID, 1f)
                    .AddMeteor(RockCometConfig.ID, 1f));

                // Vanilla meteor event cannot be used with spaced out starmap, need to redefine them
                var VanillaMeteorShowerGoldEvent = Db.Get().GameplayEvents.Add(new MeteorShowerEvent("MiniBaseVanillaMeteorShowerGoldEvent", 3000f, 0.4f, clusterMapMeteorShowerID: ClusterMapMeteorShowerConfig.GetFullID("Iron"), secondsBombardmentOn: new MathUtil.MinMax(50f, 100f), secondsBombardmentOff: new MathUtil.MinMax(800f, 1200f)).AddMeteor(GoldCometConfig.ID, 2f).AddMeteor(RockCometConfig.ID, 0.5f).AddMeteor(DustCometConfig.ID, 5f));
                var VanillaMeteorShowerCopperEvent = Db.Get().GameplayEvents.Add(new MeteorShowerEvent("MiniBaseVanillaMeteorShowerCopperEvent", 4200f, 5.5f, clusterMapMeteorShowerID: ClusterMapMeteorShowerConfig.GetFullID("Copper"), secondsBombardmentOn: new MathUtil.MinMax(100f, 400f), secondsBombardmentOff: new MathUtil.MinMax(300f, 1200f)).AddMeteor(CopperCometConfig.ID, 1f).AddMeteor(RockCometConfig.ID, 1f));
                var VanillaMeteorShowerIronEvent = Db.Get().GameplayEvents.Add(new MeteorShowerEvent("MiniBaseVanillaMeteorShowerIronEvent", 6000f, 1.25f, clusterMapMeteorShowerID: ClusterMapMeteorShowerConfig.GetFullID("Gold"), secondsBombardmentOn: new MathUtil.MinMax(100f, 400f), secondsBombardmentOff: new MathUtil.MinMax(300f, 1200f)).AddMeteor(IronCometConfig.ID, 1f).AddMeteor(RockCometConfig.ID, 2f).AddMeteor(DustCometConfig.ID, 5f));

                __instance.Add(new MeteorShowerSeason("FullereneMinibaseShower", GameplaySeason.Type.World, "EXPANSION1_ID", 20f, false, startActive: true, clusterTravelDuration: 6000f)
                    .AddEvent(FullereneMinibaseShower));
                __instance.Add(new MeteorShowerSeason("MixedMinibaseShower", GameplaySeason.Type.World, "EXPANSION1_ID", 20f, false, startActive: true, clusterTravelDuration: 6000f)
                    .AddEvent(MixedMinibaseShower));
                __instance.Add(new MeteorShowerSeason("VanillaMinibaseShower", GameplaySeason.Type.World, "EXPANSION1_ID", 20f, false, startActive: true, clusterTravelDuration: 6000f)
                    .AddEvent(VanillaMeteorShowerIronEvent)
                    .AddEvent(VanillaMeteorShowerGoldEvent)
                    .AddEvent(VanillaMeteorShowerCopperEvent));
            }
        }

        [HarmonyPatch(typeof(ColonyDestinationSelectScreen), "OnSpawn")]
        public static class ColonyDestinationSelectScreen_OnSpawn_Patch
        {
            public static void Prefix()
            {
                Log("ColonyDestinationSelectScreen_OnSpawn_Patch Prefix");
                MiniBaseOptions.Reload();

                var minibase_world = SettingsCache.worlds.worldCache["worlds/MiniBase"];
                minibase_world.seasons.Clear();
                
                switch (MiniBaseOptions.Instance.SpaceRads)
                {
                    case MiniBaseOptions.Intensity.VERY_VERY_LOW: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.VERY_VERY_LOW); break;
                    case MiniBaseOptions.Intensity.VERY_LOW: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.VERY_LOW); break;
                    case MiniBaseOptions.Intensity.LOW: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.LOW); break;
                    case MiniBaseOptions.Intensity.MED_LOW: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.MED_LOW); break;
                    case MiniBaseOptions.Intensity.MED: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.MED); break;
                    case MiniBaseOptions.Intensity.MED_HIGH: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.MED_HIGH); break;
                    case MiniBaseOptions.Intensity.HIGH: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.HIGH); break;
                    case MiniBaseOptions.Intensity.VERY_HIGH: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.VERY_HIGH); break;
                    case MiniBaseOptions.Intensity.VERY_VERY_HIGH: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.VERY_VERY_HIGH); break;
                    case MiniBaseOptions.Intensity.NONE: minibase_world.fixedTraits.Add(TUNING.FIXEDTRAITS.COSMICRADIATION.NAME.NONE); break;
                }

                if (MiniBaseOptions.Instance.MeteorShower == MiniBaseOptions.MeteorShowerType.Classic)
                    minibase_world.seasons.Add("VanillaMinibaseShower");
                else if (MiniBaseOptions.Instance.MeteorShower == MiniBaseOptions.MeteorShowerType.SpacedOut)
                    minibase_world.seasons.Add("ClassicStyleStartMeteorShowers");
                else if (MiniBaseOptions.Instance.MeteorShower == MiniBaseOptions.MeteorShowerType.Radioactive)
                    minibase_world.seasons.Add("MiniRadioactiveOceanMeteorShowers");
                else if (MiniBaseOptions.Instance.MeteorShower == MiniBaseOptions.MeteorShowerType.Fullerene)
                    minibase_world.seasons.Add("FullereneMinibaseShower");
                else if (MiniBaseOptions.Instance.MeteorShower == MiniBaseOptions.MeteorShowerType.Mixed)
                    minibase_world.seasons.Add("MixedMinibaseShower");

                Dictionary<string, ClusterLayout> clusterCache = SettingsCache.clusterLayouts.clusterCache;
                var minibase_layout = clusterCache["clusters/MiniBase"];

                if (DefaultWorldPlacements == null)
                    DefaultWorldPlacements = minibase_layout.worldPlacements;

                if (DefaultPOIPlacements == null)
                    DefaultPOIPlacements = minibase_layout.poiPlacements;

                minibase_layout.worldPlacements = new List<WorldPlacement>();
                minibase_layout.poiPlacements = new List<SpaceMapPOIPlacement>(DefaultPOIPlacements);

                minibase_layout.startWorldIndex = 0;

                foreach (var world in DefaultWorldPlacements)
                {
                    if (world.world == "worlds/MiniBase")
                    {
                        minibase_layout.worldPlacements.Add(world);
                    }
                    else if (world.world == "worlds/BabyOilyMoonlet" && MiniBaseOptions.Instance.OilMoonlet)
                    {
                        world.allowedRings = new MinMaxI(MiniBaseOptions.Instance.OilMoonletDisance, MiniBaseOptions.Instance.OilMoonletDisance);
                        minibase_layout.worldPlacements.Add(world);
                    }
                    else if (world.world == "worlds/BabyMarshyMoonlet" && MiniBaseOptions.Instance.ResinMoonlet)
                    {
                        world.allowedRings = new MinMaxI(MiniBaseOptions.Instance.ResinMoonletDisance, MiniBaseOptions.Instance.ResinMoonletDisance);
                        minibase_layout.worldPlacements.Add(world);
                    }
                    else if (world.world == "worlds/BabyNiobiumMoonlet" && MiniBaseOptions.Instance.NiobiumMoonlet)
                    {
                        world.allowedRings = new MinMaxI(MiniBaseOptions.Instance.NiobiumMoonletDisance, MiniBaseOptions.Instance.NiobiumMoonletDisance);
                        minibase_layout.worldPlacements.Add(world);
                    }
                }
                void AddPOI(string name, int distance)
                {
                    FileHandle f = new FileHandle();
                    var poi = YamlIO.Parse<SpaceMapPOIPlacement>($"pois:\n  - {name}\nnumToSpawn: 1\navoidClumping: true\nallowedRings:\n  min: {distance}\n  max: {distance}", f);
                    minibase_layout.poiPlacements.Insert(0, poi);
                };

                if (MiniBaseOptions.Instance.ResinPOI)
                {
                    AddPOI("HarvestableSpacePOI_ResinAsteroidField", MiniBaseOptions.Instance.ResinPOIDistance);
                }
                if (MiniBaseOptions.Instance.NiobiumPOI)
                {
                    AddPOI("HarvestableSpacePOI_NiobiumAsteroidField", MiniBaseOptions.Instance.NiobiumPOIDistance);
                }

                foreach(var poi_placement in minibase_layout.poiPlacements)
                {
                    if(poi_placement.pois.Count == 1 && poi_placement.pois[0] == "HarvestableSpacePOI_GildedAsteroidField")
                    {
                        poi_placement.allowedRings = new MinMaxI(MiniBaseOptions.Instance.GildedAsteroidDistance, MiniBaseOptions.Instance.GildedAsteroidDistance);
                        break;
                    }
                }
            }
        }

        // Reload mod options when game is reloaded from save
        [HarmonyPatch(typeof(Game), "OnPrefabInit")]
        public static class Game_OnPrefabInit_Patch
        {
            public static void Prefix()
            {
                Log("Game_OnPrefabInit_Patch Prefix");
                MiniBaseOptions.Reload();
            }
        }

        // Reveal map on startup
        [HarmonyPatch(typeof(MinionSelectScreen), "OnProceed")]
        public static class MinionSelectScreen_OnProceed_Patch
        {
            public static void Postfix()
            {
                if (!IsMiniBaseStartPlanetoid())
                    return;
                Log("MinionSelectScreen_OnProceed_Patch Postfix");
                int radius = (int)(Math.Max(Grid.WidthInCells, Grid.HeightInCells) * 1.5f);
                GridVisibility.Reveal(0, 0, radius, radius - 1);
            }
        }

        [HarmonyPatch(typeof(ClusterPOIManager), "RegisterTemporalTear")]

        public static class ClusterPOIManager_RegisterTemporalTear_Patch
        {
            public static void Postfix(TemporalTear temporalTear)
            {
                if (!IsMiniBaseCluster())
                    return;

                Log("ClusterPOIManager_RegisterTemporalTear_Patch Postfix");
                if (!temporalTear.IsOpen()) temporalTear.Open();
            }
        }
        #region CarePackages

        // Immigration Speed
        [HarmonyPatch(typeof(Game), "OnSpawn")]
        public static class Game_OnSpawn_Patch
        {
            public static void Postfix()
            {
                Log("Game_OnSpawn_Patch Postfix");
                if (IsMiniBaseCluster())
                {
                    var immigration = Immigration.Instance;
                    const float SecondsPerDay = 600f;
                    float frequency = MiniBaseOptions.Instance.FastImmigration ? 10f : (MiniBaseOptions.Instance.CarePackageFrequency * SecondsPerDay);
                    immigration.spawnInterval = new float[] { frequency, frequency };
                    immigration.timeBeforeSpawn = Math.Min(frequency, immigration.timeBeforeSpawn);
                }
            }
        }

        // Add care package drops
        [HarmonyPatch(typeof(Immigration), "ConfigureCarePackages")]
        public static class Immigration_ConfigureCarePackages_Patch
        {
            public static void Postfix(ref CarePackageInfo[] ___carePackages)
            {
                if (!IsMiniBaseCluster())
                    return;
                Log("Immigration_ConfigureCarePackages_Patch Postfix");
                // Add new care packages
                var packageList = ___carePackages.ToList();
                void AddElement(SimHashes element, float amount, int cycle = -1)
                {
                    AddItem(ElementLoader.FindElementByHash(element).tag.ToString(), amount, cycle);
                }
                void AddItem(string name, float amount, int cycle = -1)
                {
                    packageList.Add(new CarePackageInfo(name, amount, cycle < 0 ? IsMiniBaseCluster : (Func<bool>)(() => CycleCondition(cycle) && IsMiniBaseCluster())));
                }

                // Minerals
                AddElement(SimHashes.Granite, 2000f);
                AddElement(SimHashes.IgneousRock, 2000f);
                AddElement(SimHashes.Obsidian, 2000f, 24);
                AddElement(SimHashes.Salt, 2000f);
                AddElement(SimHashes.BleachStone, 2000f, 12);
                AddElement(SimHashes.Fossil, 1000f, 24);
                // Metals
                AddElement(SimHashes.IronOre, 1000f);
                AddElement(SimHashes.FoolsGold, 1000f, 12);
                AddElement(SimHashes.Wolframite, 500f, 24);
                AddElement(SimHashes.Lead, 1000f, 36);
                AddElement(SimHashes.AluminumOre, 500f, 24);
                AddElement(SimHashes.UraniumOre, 400f, 36);
                // Liquids
                AddElement(SimHashes.DirtyWater, 2000f, 12);
                AddElement(SimHashes.CrudeOil, 1000f, 24);
                AddElement(SimHashes.Petroleum, 1000f, 48);
                // Gases
                AddElement(SimHashes.ChlorineGas, 50f);
                AddElement(SimHashes.Methane, 50f, 24);
                // Plants
                AddItem("BasicSingleHarvestPlantSeed", 4f);             // Mealwood
                AddItem("SeaLettuceSeed", 3f);                          // Waterweed
                AddItem("SaltPlantSeed", 3f);                           // Dasha Saltvine
                AddItem("BulbPlantSeed", 3f);                           // Buddy Bud
                AddItem("ColdWheatSeed", 8f);                           // Sleet Wheat      TODO: solve invisible sleetwheat / nosh bean
                AddItem("BeanPlantSeed", 5f);                           // Nosh Bean
                AddItem("EvilFlowerSeed", 1f, 36);                      // Sporechid
                AddItem("WormPlantSeed", 3f);                           // Grubfruit Plant
                AddItem("SwampHarvestPlantSeed", 3f);                   // Bog Bucket Plant
                AddItem("CritterTrapPlantSeed", 1f, 36);                // Satturn Critter Trap
                                                                        // Critters
                AddItem("PacuEgg", 3f);                                 // Pacu
                AddItem("BeeBaby", 1f, 36);                             // Beetiny
                ___carePackages = packageList.ToArray();
            }

            private static bool CycleCondition(int cycle) { return GameClock.Instance.GetCycle() >= cycle; }
        }


        // Remove the need to discovers items for them to be available in the printing pod
        [HarmonyPatch(typeof(Immigration), "DiscoveredCondition")]
        public static class Immigration_DiscoveredCondition_Patch
        {
            public static void Postfix(ref bool __result)
            {
                if (IsMiniBaseCluster())
                    __result = true;
            }
        }
        #endregion

        #region WorldGen

        // Add minibase asteroid cluster
        [HarmonyPatch(typeof(Db), "Initialize")]
        public class Db_Initialize_Patch
        {
            public static void Prefix()
            {
                Log("Db_Initialize_Patch Prefix");
                Strings.Add($"STRINGS.WORLDS.{ClusterName.ToUpperInvariant()}.NAME", ClusterName);
                Strings.Add($"STRINGS.WORLDS.{ClusterName.ToUpperInvariant()}.DESCRIPTION", ClusterDescription);
                Strings.Add($"STRINGS.UI.SPACEDESTINATIONS.HARVESTABLE_POI.NIOBIUMASTEROIDFIELD.NAME", "Niobium asteroid field");
                Strings.Add($"STRINGS.UI.SPACEDESTINATIONS.HARVESTABLE_POI.NIOBIUMASTEROIDFIELD.DESC", "An asteroid field containing traces of niobium");
                Strings.Add($"STRINGS.UI.SPACEDESTINATIONS.HARVESTABLE_POI.RESINASTEROIDFIELD.NAME", "Resin asteroid field");
                Strings.Add($"STRINGS.UI.SPACEDESTINATIONS.HARVESTABLE_POI.RESINASTEROIDFIELD.DESC", "An asteroid field with plenty of liquid resin");

                string spritePath = System.IO.Path.Combine(ModPath, ClusterIconName) + ".png";
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                ImageConversion.LoadImage(texture, File.ReadAllBytes(spritePath));
                Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 512f, 512f), Vector2.zero);
                Assets.Sprites.Add(ClusterIconName, sprite);
            }
        }

        // Change minibase world size
        [HarmonyPatch(typeof(Worlds), "UpdateWorldCache")]
        public static class Worlds_UpdateWorldCache_Patch
        {
            public static void Postfix(Worlds __instance)
            {
                Log("Worlds_UpdateWorldCache_Patch Postfix");
                var world = __instance.worldCache["worlds/" + ClusterMainWorld];
                Traverse.Create(world).Property("worldsize").SetValue(new Vector2I(WORLD_WIDTH, WORLD_HEIGHT));

                var world_tree = __instance.worldCache["worlds/BabyMarshyMoonlet"];
                Traverse.Create(world_tree).Property("worldsize").SetValue(new Vector2I(WORLD_WIDTH, WORLD_HEIGHT));

                var world_magma = __instance.worldCache["worlds/BabyNiobiumMoonlet"];
                Traverse.Create(world_magma).Property("worldsize").SetValue(new Vector2I(WORLD_WIDTH, WORLD_HEIGHT));

                var world_radioactive = __instance.worldCache["worlds/BabyOilyMoonlet"];
                Traverse.Create(world_radioactive).Property("worldsize").SetValue(new Vector2I(WORLD_WIDTH, WORLD_HEIGHT));
            }
        }

        // Bypass and rewrite world generation
        [HarmonyPatch(typeof(WorldGen), "RenderOffline")]
        public static class WorldGen_RenderOffline_Patch
        {
            public static bool Prefix(WorldGen __instance, ref bool __result, bool doSettle, ref Sim.Cell[] cells, ref Sim.DiseaseCell[] dc, int baseId, ref List<WorldTrait> placedStoryTraits, bool isStartingWorld)
            {
                Log("WorldGen_RenderOffline_Patch Prefix");
                // Skip the original method if on minibase world
                return !IsMiniBasePlanetoid(__instance);
            }

            public static void Postfix(WorldGen __instance, ref bool __result, bool doSettle, ref Sim.Cell[] cells, ref Sim.DiseaseCell[] dc, int baseId, ref List<WorldTrait> placedStoryTraits, bool isStartingWorld)
            {
                if (!IsMiniBasePlanetoid(__instance))
                    return;
                Log("WorldGen_RenderOffline_Patch Postfix");
                __result = MiniBaseWorldGen.CreateWorld(__instance, ref cells, ref dc, baseId, ref placedStoryTraits);
            }
        }

        [HarmonyPatch(typeof(EntityConfigManager), "RegisterEntities")]
        public static class EntityConfigManager_RegisterEntities_Patch
        {
            public static void Postfix(IMultiEntityConfig config)
            {
                if (config is HarvestablePOIConfig)
                {
                    Log("EntityConfigManager_RegisterEntities_Patch Postfix for HarvestablePOIConfig");
                    void AddPrefab(HarvestablePOIConfig.HarvestablePOIParams poi_config)
                    {
                        var prefab = HarvestablePOIConfig.CreateHarvestablePOI(poi_config.id, poi_config.anim, (string)Strings.Get(poi_config.nameStringKey), poi_config.descStringKey, poi_config.poiType.idHash, poi_config.poiType.canProvideArtifacts);
                        KPrefabID component = prefab.GetComponent<KPrefabID>();
                        component.prefabInitFn += new KPrefabID.PrefabFn(config.OnPrefabInit);
                        component.prefabSpawnFn += new KPrefabID.PrefabFn(config.OnSpawn);
                        Assets.AddPrefab(component);
                    }
                    AddPrefab(new HarvestablePOIConfig.HarvestablePOIParams("metallic_asteroid_field", new HarvestablePOIConfigurator.HarvestablePOIType("NiobiumAsteroidField", new Dictionary<SimHashes, float>()
                    {
                        {
                            SimHashes.Obsidian,
                            5.0f
                        },
                        {
                            SimHashes.MoltenTungsten,
                            3.0f
                        },
                        {
                            SimHashes.Niobium,
                            0.03f
                        }
                    })));
                    AddPrefab(new HarvestablePOIConfig.HarvestablePOIParams("gilded_asteroid_field", new HarvestablePOIConfigurator.HarvestablePOIType("ResinAsteroidField", new Dictionary<SimHashes, float>()
                    {
                        {
                            SimHashes.Fossil,
                            0.2f
                        },
                        {
                            SimHashes.CrudeOil,
                            0.4f
                        },
                        {
                            SimHashes.Resin,
                            0.4f
                        }
                    }, 56250, 56250, 30000, 30000)));
                }
            }
        }
        #endregion
    }
}
