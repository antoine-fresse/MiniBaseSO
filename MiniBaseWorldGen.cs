﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Klei;
using ProcGen;
using ProcGenGame;
using STRINGS;
using UnityEngine;
using Delaunay.Geo;
using VoronoiTree;
using TemplateClasses;
using static MiniBase.MiniBaseConfig;
using static MiniBase.MiniBaseUtils;
using static ProcGenGame.TemplateSpawning;
using MiniBase.Profiles;
using static MiniBase.MiniBaseOptions;
using static STRINGS.UI.CLUSTERMAP;
using static WorldGenSpawner;

namespace MiniBase
{
    public class MiniBaseWorldGen
    {
        private static System.Random random;

        private class MoonletData
        {
            public MiniBaseBiomeProfile biome;
            public MiniBaseBiomeProfile core_biome;
            public Vector2I size;
            public bool has_core;
            public int extra_top_margin = 0;
            public enum Moonlet
            {
                Start,
                Second,
                Tree,
                Niobium
            };
            public Moonlet type = Moonlet.Start;
        };

        private static MoonletData moonlet = new MoonletData();

        // Rewrite of WorldGen.RenderOffline
        public static bool CreateWorld(WorldGen gen, ref Sim.Cell[] cells, ref Sim.DiseaseCell[] dc, int baseId, ref List<WorldTrait> placedStoryTraits)
        {
            Log("Creating world");

            MiniBaseOptions options = MiniBaseOptions.Instance;

            moonlet.biome = options.GetBiome();
            moonlet.core_biome = options.GetCoreBiome();
            moonlet.size = options.GetBaseSize();
            moonlet.has_core = options.HasCore();

            bool is_main = gen.Settings.world.filePath == "worlds/MiniBase";
            if (is_main) moonlet.type = MoonletData.Moonlet.Start;
            bool is_second = gen.Settings.world.filePath == "worlds/BabyOilyMoonlet";
            if (is_second) moonlet.type = MoonletData.Moonlet.Second;
            bool is_tree = gen.Settings.world.filePath == "worlds/BabyMarshyMoonlet";
            if (is_tree) moonlet.type = MoonletData.Moonlet.Tree;
            bool is_niobium = gen.Settings.world.filePath == "worlds/BabyNiobiumMoonlet";
            if (is_niobium) moonlet.type = MoonletData.Moonlet.Niobium;
               
            if (!is_main)
            {
                moonlet.size.x = (2 * moonlet.size.x) / 3;
                moonlet.size.y = 60;
                moonlet.extra_top_margin = 8;
                moonlet.has_core = false;
                if (is_niobium)
                {
                    moonlet.biome = MiniBaseBiomeProfiles.NiobiumMoonletProfile;
                }
                if (is_tree)
                {
                    moonlet.biome = MiniBaseBiomeProfiles.TreeMoonletProfile;
                }
                if(is_second)
                {
                    moonlet.biome = MiniBaseBiomeProfiles.OilMoonletProfile;
                    moonlet.core_biome = MiniBaseCoreBiomeProfiles.MagmaCoreProfile;
                    moonlet.has_core = true;
                }
            }

            Log("World name : " + gen.Settings.world.name);
            Log("World file : " + gen.Settings.world.filePath);

            // Convenience variables, including private fields/properties
            var worldGen = Traverse.Create(gen);
            var data = gen.data;
            var myRandom = worldGen.Field("myRandom").GetValue<SeededRandom>();
            var updateProgressFn = worldGen.Field("successCallbackFn").GetValue<WorldGen.OfflineCallbackFunction>();
            var errorCallback = worldGen.Field("errorCallback").GetValue<Action<OfflineWorldGen.ErrorInfo>>();
            var running = worldGen.Field("running");
            running.SetValue(true);
            random = new System.Random(data.globalTerrainSeed);

            // Initialize noise maps
            Log("Initializing noise maps");
            updateProgressFn(UI.WORLDGEN.GENERATENOISE.key, 0f, WorldGenProgressStages.Stages.NoiseMapBuilder);
            var noiseMap = GenerateNoiseMap(random, WORLD_WIDTH, WORLD_HEIGHT);
            updateProgressFn(UI.WORLDGEN.GENERATENOISE.key, 0.9f, WorldGenProgressStages.Stages.NoiseMapBuilder);

            // Set biomes
            Log("Setting biomes");
            SetBiomes(data.overworldCells);

            // Rewrite of WorldGen.RenderToMap function, which calls the default terrain and border generation, places features, and spawns flora and fauna
            cells = new Sim.Cell[Grid.CellCount];
            var bgTemp = new float[Grid.CellCount];
            dc = new Sim.DiseaseCell[Grid.CellCount];

            // Initialize terrain
            Log("Clearing terrain");
            updateProgressFn(UI.WORLDGEN.CLEARINGLEVEL.key, 0f, WorldGenProgressStages.Stages.ClearingLevel);
            ClearTerrain(cells, bgTemp, dc);
            updateProgressFn(UI.WORLDGEN.CLEARINGLEVEL.key, 1f, WorldGenProgressStages.Stages.ClearingLevel);

            // Draw custom terrain
            Log("Drawing terrain");
            updateProgressFn(UI.WORLDGEN.PROCESSING.key, 0f, WorldGenProgressStages.Stages.Processing);
            ISet<Vector2I> biomeCells, coreCells, borderCells;
            biomeCells = DrawCustomTerrain(data, cells, bgTemp, dc, noiseMap, out coreCells);
            updateProgressFn(UI.WORLDGEN.PROCESSING.key, 0.9f, WorldGenProgressStages.Stages.Processing);

            // Printing pod
            Log("Generating starting template");

            var templateSpawnTargets = new List<TemplateSpawner>();
            var reservedCells = new HashSet<Vector2I>();

            if (gen.isStartingWorld)
            {
                var startPos = Vec(Left() + (Width() / 2) - 1, Bottom() + (Height() / 2) + 2);
                data.gameSpawnData.baseStartPos = startPos;
                TemplateContainer startingBaseTemplate = TemplateCache.GetTemplate(gen.Settings.world.startingBaseTemplate);
                Log("Adding starting items");
                startingBaseTemplate.pickupables.Clear(); // Remove stray hatch
                var itemPos = new Vector2I(3, 1);
                foreach (var entry in moonlet.biome.startingItems) // Add custom defined starting items
                    startingBaseTemplate.pickupables.Add(new Prefab(entry.Key, Prefab.Type.Pickupable, itemPos.x, itemPos.y, (SimHashes)0, _units: entry.Value));
                foreach (Cell cell in startingBaseTemplate.cells)
                {
                    if (cell.element == SimHashes.SandStone || cell.element == SimHashes.Algae)
                        cell.element = moonlet.biome.defaultMaterial;
                    reservedCells.Add(new Vector2I(cell.location_x, cell.location_y) + startPos);
                }
                Log("Looking for starting Terrain Cell");
                TerrainCell terrainCell = data.overworldCells.Find((Predicate<TerrainCell>)(tc => tc.node.tags.Contains(WorldGenTags.StartLocation)));
                if (terrainCell == null)
                {
                    Log("Null Terrain Cell");
                }
                Log("Done Looking for starting Terrain Cell");
                startingBaseTemplate.cells.RemoveAll((c) => (c.location_x == -8) || (c.location_x == 9)); // Trim the starting base area
                templateSpawnTargets.Add(new TemplateSpawner(startPos,startingBaseTemplate.GetTemplateBounds(), startingBaseTemplate, terrainCell));
            
                // Geysers
                Log("Placing features");
                int GeyserMinX = Left() + CORNER_SIZE + 2;
                int GeyserMaxX = Right() - CORNER_SIZE - 4;
                int GeyserMinY = Bottom() + CORNER_SIZE + 2;
                int GeyserMaxY = Top() - CORNER_SIZE - 4;
                Element coverElement = moonlet.biome.DefaultElement();
                PlaceGeyser(data, cells, options.FeatureWest, Vec(Left() + 2, random.Next(GeyserMinY, GeyserMaxY + 1)), coverElement);
                PlaceGeyser(data, cells, options.FeatureEast, Vec(Right() - 4, random.Next(GeyserMinY, GeyserMaxY + 1)), coverElement);
                if (moonlet.has_core)
                    coverElement = moonlet.core_biome.DefaultElement();

                var bottom_geyser_pos = Vec(random.Next(GeyserMinX, GeyserMaxX + 1), Bottom());
                PlaceGeyser(data, cells, options.FeatureSouth, bottom_geyser_pos, coverElement);

                // Change geysers to be made of abyssalite so they don't melt in magma
                var geyserPrefabs = Assets.GetPrefabsWithComponent<Geyser>();
                geyserPrefabs.Add(Assets.GetPrefab(GameTags.OilWell));
                foreach (var prefab in geyserPrefabs)
                    prefab.GetComponent<PrimaryElement>().SetElement(SimHashes.Katairite);

                if(moonlet.has_core && moonlet.core_biome == MiniBaseCoreBiomeProfiles.RadioactiveCoreProfile)
                {
                    // Add a single viable beeta in the radioactive core
                    if(bottom_geyser_pos.x > Left() + Width() / 2)
                        bottom_geyser_pos.x -= 12;
                    else
                        bottom_geyser_pos.x += 12;

                    bottom_geyser_pos.y += 1;
                    coverElement = ElementLoader.FindElementByHash(SimHashes.Wolframite);

                    cells[Grid.XYToCell(bottom_geyser_pos.x, bottom_geyser_pos.y)].SetValues(coverElement, GetPhysicsData(coverElement, 1f, moonlet.biome.defaultTemperature), ElementLoader.elements);
                    cells[Grid.XYToCell(bottom_geyser_pos.x+1, bottom_geyser_pos.y)].SetValues(coverElement, GetPhysicsData(coverElement, 1f, moonlet.biome.defaultTemperature), ElementLoader.elements);

                    coverElement = ElementLoader.FindElementByHash(SimHashes.CarbonDioxide);
                    for (int xx = 0; xx<2; xx++)
                        for (int yy = 0; yy < 3; yy++)
                            cells[Grid.XYToCell(bottom_geyser_pos.x + xx, bottom_geyser_pos.y + 1 + yy)].SetValues(coverElement, GetPhysicsData(coverElement, 1f, moonlet.biome.defaultTemperature), ElementLoader.elements);

                    data.gameSpawnData.pickupables.Add(new Prefab("BeeHive", Prefab.Type.Pickupable, bottom_geyser_pos.x, bottom_geyser_pos.y+1, (SimHashes)0));
                }

            }
            else if (is_tree)
            {
                var startPos = Vec(Left() + (2*Width() / 3) - 1, Bottom() + (Height() / 2) + 2);
                TemplateContainer template = TemplateCache.GetTemplate("expansion1::poi/sap_tree_room");
                TerrainCell terrainCell = data.overworldCells.Find((Predicate<TerrainCell>)(tc => tc.node.tags.Contains(WorldGenTags.AtSurface)));
                if (terrainCell == null)
                {
                    Log("Null Terrain Cell");
                }
                templateSpawnTargets.Add(new TemplateSpawner(startPos, template.GetTemplateBounds(), template, terrainCell));
            }
            else if (is_second)
            {
                int GeyserMinX = Left() + CORNER_SIZE + 2;
                int GeyserMaxX = Right() - CORNER_SIZE - 4;
                int GeyserMinY = Bottom() + CORNER_SIZE + 2;
                int GeyserMaxY = Top() - Height() / 2 - CORNER_SIZE - 4;

                Element coverElement = moonlet.biome.DefaultElement();
                PlaceGeyser(data, cells, MiniBaseOptions.FeatureType.OilReservoir, Vec(Left() + 2, random.Next(GeyserMinY, GeyserMaxY + 1)), coverElement, false, false);
                PlaceGeyser(data, cells, MiniBaseOptions.FeatureType.OilReservoir, Vec(Right() - 4, random.Next(GeyserMinY, GeyserMaxY + 1)), coverElement, false, false);
                if (moonlet.has_core)
                    coverElement = moonlet.core_biome.DefaultElement();
                PlaceGeyser(data, cells, MiniBaseOptions.FeatureType.Volcano, Vec(random.Next(GeyserMinX, GeyserMaxX + 1), Bottom()), coverElement);
            }
            else if (is_niobium)
            {
                var startPos = Vec(Left() + (Width() / 2) - 1, Bottom() + (Height() / 4) + 2);
                Element coverElement = ElementLoader.FindElementByHash(SimHashes.Niobium);
                PlaceGeyser(data, cells, MiniBaseOptions.FeatureType.Niobium, startPos, coverElement);


                // Replace niobium near the surface with obsidian
                coverElement = ElementLoader.FindElementByHash(SimHashes.Obsidian);
                for (int x = Left(); x <= Right(); x++)
                {
                    for(int y = Top(); y >= (Top() - (3*Height()/4)); y--)
                    {
                        if (ElementLoader.elements[cells[Grid.XYToCell(x, y)].elementIdx].id == SimHashes.Niobium)
                        {
                            cells[Grid.XYToCell(x, y)].SetValues(coverElement, GetPhysicsData(coverElement, 1f, moonlet.biome.defaultTemperature), ElementLoader.elements);
                        }
                    }
                }
            }
            // Draw borders
            Log("Drawing borders");
            updateProgressFn(UI.WORLDGEN.DRAWWORLDBORDER.key, 0f, WorldGenProgressStages.Stages.DrawWorldBorder);
            borderCells = DrawCustomWorldBorders(cells);
            biomeCells.ExceptWith(borderCells);
            coreCells.ExceptWith(borderCells);
            updateProgressFn(UI.WORLDGEN.DRAWWORLDBORDER.key, 1f, WorldGenProgressStages.Stages.DrawWorldBorder);

            // Settle simulation
            // This writes the cells to the world, then performs a couple of game frames of simulation, then saves the game
            Log("Settling sim");
            running.SetValue(WorldGenSimUtil.DoSettleSim(gen.Settings, ref cells, ref bgTemp, ref dc, updateProgressFn, data, templateSpawnTargets, errorCallback, baseId));

            // Place templates, pretty much just the printing pod
            Log("Placing templates");
            var claimedCells = new Dictionary<int, int>();
            foreach (var templateSpawner in templateSpawnTargets)
                data.gameSpawnData.AddTemplate(templateSpawner.container, templateSpawner.position, ref claimedCells);

            // Add plants, critters, and items
            Log("Adding critters, etc");
            updateProgressFn(UI.WORLDGEN.PLACINGCREATURES.key, 0f, WorldGenProgressStages.Stages.PlacingCreatures);
            PlaceSpawnables(cells, data.gameSpawnData.pickupables, moonlet.biome, biomeCells, reservedCells);
            updateProgressFn(UI.WORLDGEN.PLACINGCREATURES.key, 50f, WorldGenProgressStages.Stages.PlacingCreatures);
            if (moonlet.has_core)
                PlaceSpawnables(cells, data.gameSpawnData.pickupables, moonlet.core_biome, coreCells, reservedCells);
            updateProgressFn(UI.WORLDGEN.PLACINGCREATURES.key, 100f, WorldGenProgressStages.Stages.PlacingCreatures);

            // Finish and save
            Log("Saving world");
            gen.SaveWorldGen();
            updateProgressFn(UI.WORLDGEN.COMPLETE.key, 1f, WorldGenProgressStages.Stages.Complete);
            running.SetValue(false);
            return true;
        }

        // Set biome background for all cells
        // Overworld cells are large polygons that divide the map into zones (biomes)
        // To change a biome, create an appropriate overworld cell and add it to Data.overworldCells
        private static void SetBiomes(List<TerrainCell> overworldCells)
        {
            overworldCells.Clear();
            //var options = MiniBaseOptions.Instance;
            string SpaceBiome = "subworlds/space/Space";
            string backgroundBiome = moonlet.biome.backgroundSubworld;
            string sideBiome = SpaceBiome;

            TagSet tags;
            List<Vector2> vertices;
            Polygon bounds;
            uint cellId = 0;
            void CreateOverworldCell(string type, Polygon bs, TagSet ts)
            {
                ProcGen.Map.Cell cell = new ProcGen.Map.Cell(); // biome
                cell.SetType(type);
                foreach (Tag tag in ts)
                    cell.tags.Add(tag);
                Diagram.Site site = new Diagram.Site();
                site.id = cellId++;
                site.poly = bs; // bounds of the overworld cell
                site.position = site.poly.Centroid();
                overworldCells.Add(new TerrainCellLogged(cell, site, new Dictionary<Tag, int>()));
            };

            // Vertices of the liveable area (octogon)
            Vector2I BottomLeftSE = BottomLeft() + Vec(CORNER_SIZE, 0),
                BottomLeftNW = BottomLeft() + Vec(0, CORNER_SIZE),
                TopLeftSW = TopLeft() - Vec(0, CORNER_SIZE) - Vec(0, 1),
                TopLeftNE = TopLeft() + Vec(CORNER_SIZE, 0),
                TopRightNW = TopRight() - Vec(CORNER_SIZE, 0) - Vec(1, 0),
                TopRightSE = TopRight() - Vec(0, CORNER_SIZE) - Vec(0, 1),
                BottomRightNE = BottomRight() + Vec(0, CORNER_SIZE),
                BottomRightSW = BottomRight() - Vec(CORNER_SIZE, 0);

            // Liveable cell
            tags = new TagSet();

            if (moonlet.type ==MoonletData.Moonlet.Start)
            {
                tags.Add(WorldGenTags.AtStart);
                tags.Add(WorldGenTags.StartWorld);
                tags.Add(WorldGenTags.StartLocation);
            }
           /* else
            {
                tags.Add(WorldGenTags.AtSurface);
            }*/
            vertices = new List<Vector2>()
            {
                BottomLeftSE,
                BottomLeftNW,
                TopLeftSW,
                TopLeftNE,
                TopRightNW,
                TopRightSE,
                BottomRightNE,
                BottomRightSW
            };
            bounds = new Polygon(vertices);
            CreateOverworldCell(backgroundBiome, bounds, tags);

            // Top cell
            tags = new TagSet();
            bounds = new Polygon(new Rect(0f, Top(), WORLD_WIDTH, WORLD_HEIGHT - Top()));
            CreateOverworldCell(SpaceBiome, bounds, tags);

            // Bottom cell
            tags = new TagSet();
            bounds = new Polygon(new Rect(0f, 0, WORLD_WIDTH, Bottom()));
            CreateOverworldCell(SpaceBiome, bounds, tags);

            // Left side cell
            tags = new TagSet();
            vertices = new List<Vector2>()
            {
                Vec(0, Bottom()),
                Vec(0, Top()),
                TopLeftNE,
                TopLeftSW,
                BottomLeftNW,
                BottomLeftSE,
            };
            bounds = new Polygon(vertices);
            CreateOverworldCell(sideBiome, bounds, tags);

            // Right side cell
            tags = new TagSet();
            vertices = new List<Vector2>()
            {
                BottomRightSW,
                BottomRightNE,
                TopRightSE,
                TopRightNW,
                Vec(WORLD_WIDTH, Top()),
                Vec(WORLD_WIDTH, Bottom()),
            };
            bounds = new Polygon(vertices);
            CreateOverworldCell(sideBiome, bounds, tags);
        }

        // From WorldGen.RenderToMap
        private static void ClearTerrain(Sim.Cell[] cells, float[] bgTemp, Sim.DiseaseCell[] dc)
        {
            for (int index = 0; index < cells.Length; ++index)
            {
                cells[index].SetValues(ElementLoader.FindElementByHash(SimHashes.Vacuum), ElementLoader.elements);
                bgTemp[index] = -1f;
                dc[index] = new Sim.DiseaseCell();
                dc[index].diseaseIdx = byte.MaxValue;
            }
        }

        // Rewrite of WorldGen.ProcessByTerrainCell
        private static ISet<Vector2I> DrawCustomTerrain(Data data, Sim.Cell[] cells, float[] bgTemp, Sim.DiseaseCell[] dc, float[,] noiseMap, out ISet<Vector2I> coreCells)
        {
            var options = MiniBaseOptions.Instance;
            var biomeCells = new HashSet<Vector2I>();
            var sideCells = new HashSet<Vector2I>();
            var claimedCells = new HashSet<int>();
            coreCells = new HashSet<Vector2I>();
            for (int index = 0; index < data.terrainCells.Count; ++index)
                data.terrainCells[index].InitializeCells(claimedCells);
            if (MiniBaseOptions.Instance.SkipLiveableArea)
                return biomeCells;

            // Using a smooth noisemap, map the noise values to elements via the element band profile
            void SetTerrain(MiniBaseBiomeProfile biome, ISet<Vector2I> positions)
            {
                var DiseaseDb = Db.Get().Diseases;
                var DiseaseDict = new Dictionary<DiseaseID, byte>()
                {
                    { DiseaseID.NONE, byte.MaxValue},
                    { DiseaseID.SLIMELUNG, DiseaseDb.GetIndex(DiseaseDb.SlimeGerms.id)},
                    { DiseaseID.FOOD_POISONING, DiseaseDb.GetIndex(DiseaseDb.FoodGerms.id)},
                };

                foreach (var pos in positions)
                {
                    float e = noiseMap[pos.x, pos.y];
                    BandInfo bandInfo = biome.GetBand(e);
                    Element element = bandInfo.GetElement();
                    Sim.PhysicsData elementData = biome.GetPhysicsData(bandInfo);
                    int cell = Grid.PosToCell(pos);
                    cells[cell].SetValues(element, elementData, ElementLoader.elements);
                    dc[cell] = new Sim.DiseaseCell()
                    {
                        diseaseIdx = Db.Get().Diseases.GetIndex(DiseaseDict[bandInfo.disease]),
                        elementCount = random.Next(10000, 1000000),
                    };
                }
            }

            // Main biome
            int relativeLeft = Left();
            int relativeRight = Right();
            for (int x = relativeLeft; x < relativeRight; x++)
                for (int y = Bottom(); y < Top(); y++)
                {
                    var pos = Vec(x, y);
                    int extra = (int)(noiseMap[pos.x, pos.y] * 8f);
                    if (moonlet.type != MoonletData.Moonlet.Start && y > Bottom() + extra + (2 * Height() / 3)) continue; 

                    if (InLiveableArea(pos))
                        biomeCells.Add(pos);
                    else
                        sideCells.Add(pos);
                }
            SetTerrain(moonlet.biome, biomeCells);
            SetTerrain(moonlet.biome, sideCells);

            // Core area
            if (moonlet.has_core)
            {
                int coreHeight = CORE_MIN + Height() / 10;
                int[] heights = GetHorizontalWalk(WORLD_WIDTH, coreHeight, coreHeight + CORE_DEVIATION);
                ISet<Vector2I> abyssaliteCells = new HashSet<Vector2I>();
                for (int x = relativeLeft; x < relativeRight; x++)
                {
                    // Create abyssalite border of size CORE_BORDER
                    for (int j = 0; j < CORE_BORDER; j++)
                        abyssaliteCells.Add(Vec(x, Bottom() + heights[x] + j));

                    // Ensure border thickness at high slopes
                    if (x > relativeLeft && x < relativeRight - 1)
                        if ((heights[x - 1] - heights[x] > 1) || (heights[x + 1] - heights[x] > 1))
                        {
                            Vector2I top = Vec(x, Bottom() + heights[x] + CORE_BORDER - 1);
                            abyssaliteCells.Add(top + Vec(-1, 0));
                            abyssaliteCells.Add(top + Vec(1, 0));
                            abyssaliteCells.Add(top + Vec(0, 1));
                        }

                    // Mark core biome cells
                    for (int y = Bottom(); y < Bottom() + heights[x]; y++)
                        coreCells.Add(Vec(x, y));
                }
                coreCells.ExceptWith(abyssaliteCells);
                SetTerrain(moonlet.core_biome, coreCells);
                foreach (Vector2I abyssaliteCell in abyssaliteCells)
                    cells[Grid.PosToCell(abyssaliteCell)].SetValues(WorldGen.katairiteElement, ElementLoader.elements);
                biomeCells.ExceptWith(coreCells);
                biomeCells.ExceptWith(abyssaliteCells);
            }
            return biomeCells;
        }

        // Rewrite of WorldGen.DrawWorldBorder
        private static ISet<Vector2I> DrawCustomWorldBorders(Sim.Cell[] cells)
        {
            ISet<Vector2I> borderCells = new HashSet<Vector2I>();

            void AddBorderCell(int x, int y, Element e)
            {
                int cell = Grid.XYToCell(x, y);
                if (Grid.IsValidCell(cell))
                {
                    borderCells.Add(Vec(x, y));
                    cells[cell].SetValues(e, ElementLoader.elements);
                }
            }
           
            Element borderMat = WorldGen.unobtaniumElement;
            for (int x = 0; x < WORLD_WIDTH; x++)
            {
                // Top border
                for (int y = Top(false); y < Top(true); y++)
                {
                    if(moonlet.type == MoonletData.Moonlet.Start || x < (Left() + CORNER_SIZE) || x > (Right() - CORNER_SIZE))
                        AddBorderCell(x, y, borderMat);
                }
                
                // Bottom border
                for (int y = Bottom(true); y < Bottom(false); y++)
                    AddBorderCell(x, y, borderMat);
            }

            for (int y = Bottom(true); y < Top(true); y++)
            {
                // Left border
                for (int x = Left(true); x < Left(false); x++)
                    AddBorderCell(x, y, borderMat);

                // Right border
                for (int x = Right(false); x < Right(true); x++)
                    AddBorderCell(x, y, borderMat);
            }

            // Corner structures
            int leftCenterX = (Left(true) + Left(false)) / 2;
            int rightCenterX = (Right(false) + Right(true)) / 2;
            int adjustedCornerSize = CORNER_SIZE + (int)Math.Ceiling(BORDER_SIZE / 2f);
            for (int i = 0; i < adjustedCornerSize; i++)
                for (int j = adjustedCornerSize; j > i; j--)
                {
                    int bottomY = Bottom() + adjustedCornerSize - j;
                    int topY = Top() - adjustedCornerSize + j - 1;
                    if (j - i <= DIAGONAL_BORDER_SIZE)
                        borderMat = WorldGen.unobtaniumElement;
                    else
                        borderMat = ElementLoader.FindElementByHash(SimHashes.Glass);
                    AddBorderCell(leftCenterX + i, bottomY, borderMat);
                    AddBorderCell(leftCenterX - i, bottomY, borderMat);
                    AddBorderCell(rightCenterX + i, bottomY, borderMat);
                    AddBorderCell(rightCenterX - i, bottomY, borderMat);
                   // if (moonlet.is_starting_asteroid)
                    {
                        AddBorderCell(leftCenterX + i, topY, borderMat);
                        AddBorderCell(leftCenterX - i, topY, borderMat);
                        AddBorderCell(rightCenterX + i, topY, borderMat);
                        AddBorderCell(rightCenterX - i, topY, borderMat);
                    }
                }

            // Space access
            if (moonlet.type == MoonletData.Moonlet.Start)
            {
                if (MiniBaseOptions.Instance.SpaceAccess == MiniBaseOptions.AccessType.Classic)
                {
                    borderMat = WorldGen.katairiteElement;

                    for (int y = Top(); y < Top(true); y++)
                    {
                        //Left cutout
                        for (int x = Left() + CORNER_SIZE; x < Math.Min(Left() + CORNER_SIZE + SPACE_ACCESS_SIZE, Right() - CORNER_SIZE); x++)
                            AddBorderCell(x, y, borderMat);
                        //Right cutout
                        for (int x = Math.Max(Right() - CORNER_SIZE - SPACE_ACCESS_SIZE, Left() + CORNER_SIZE); x < Right() - CORNER_SIZE; x++)
                            AddBorderCell(x, y, borderMat);
                        if (MiniBaseOptions.Instance.SpaceTunnelAccess == MiniBaseOptions.SpaceTunnelAccessType.LeftOnly)
                        {
                            //far left cutout
                            for (int x = CORNER_SIZE; x < CORNER_SIZE + SPACE_ACCESS_SIZE; x++)
                                AddBorderCell(x, y, borderMat);
                        }
                    }
                    if (MiniBaseOptions.Instance.TunnelAccess == MiniBaseOptions.TunnelAccessType.BothSides ||
                        MiniBaseOptions.Instance.TunnelAccess == MiniBaseOptions.TunnelAccessType.LeftOnly ||
                        MiniBaseOptions.Instance.TunnelAccess == MiniBaseOptions.TunnelAccessType.RightOnly)
                    {
                        //Space Tunnels
                        for (int y = Bottom(false) + CORNER_SIZE; y < Bottom(false) + 5 + CORNER_SIZE; y++)
                        {
                            if (MiniBaseOptions.Instance.TunnelAccess == MiniBaseOptions.TunnelAccessType.LeftOnly ||
                                MiniBaseOptions.Instance.TunnelAccess == MiniBaseOptions.TunnelAccessType.BothSides)
                            {
                                //Far left tunnel
                                for (int x = Left(true); x < Left(false); x++)
                                    AddBorderCell(x, y, borderMat);
                            }
                            if (MiniBaseOptions.Instance.TunnelAccess == MiniBaseOptions.TunnelAccessType.RightOnly ||
                                MiniBaseOptions.Instance.TunnelAccess == MiniBaseOptions.TunnelAccessType.BothSides)
                            {
                                //Far Right tunnel
                                for (int x = Right(false); x < Right(true); x++)
                                    AddBorderCell(x, y, borderMat);
                            }
                        }
                    }

                }
                else if (MiniBaseOptions.Instance.SpaceAccess == MiniBaseOptions.AccessType.Full)
                {
                    borderMat = WorldGen.katairiteElement;
                    for (int y = Top(); y < Top(true); y++)
                        for (int x = Left() + CORNER_SIZE; x < Right() - CORNER_SIZE; x++)
                            AddBorderCell(x, y, borderMat);
                }
            }
            return borderCells;
        }

        // Create and place a feature (geysers, volcanoes, vents, etc) given a feature type
        private static void PlaceGeyser(Data data, Sim.Cell[] cells, MiniBaseOptions.FeatureType type, Vector2I pos, Element coverMaterial, bool neutronium = true, bool cover = true)
        {
            string featureName;
            switch (type)
            {
                case MiniBaseOptions.FeatureType.None:
                    featureName = null;
                    break;
                case MiniBaseOptions.FeatureType.RandomAny:
                    FeatureType feature_type;
                    do
                    {
                        feature_type = ChooseRandom(GeyserDictionary.Keys.ToArray());
                    } while (feature_type == FeatureType.Niobium);
                    featureName = GeyserDictionary[feature_type];
                    break;
                case MiniBaseOptions.FeatureType.RandomWater:
                    featureName = GeyserDictionary[ChooseRandom(RandomWaterFeatures)];
                    break;
                case MiniBaseOptions.FeatureType.RandomUseful:
                    featureName = GeyserDictionary[ChooseRandom(RandomUsefulFeatures)];
                    break;
                case MiniBaseOptions.FeatureType.RandomVolcano:
                    featureName = GeyserDictionary[ChooseRandom(RandomVolcanoFeatures)];
                    break;
                case MiniBaseOptions.FeatureType.RandomMagmaVolcano:
                    featureName = GeyserDictionary[ChooseRandom(RandomMagmaVolcanoFeatures)];
                    break;
                case MiniBaseOptions.FeatureType.RandomMetalVolcano:
                    featureName = GeyserDictionary[ChooseRandom(RandomMetalVolcanoFeatures)];
                    break;
                default:
                    if (GeyserDictionary.ContainsKey(type))
                        featureName = GeyserDictionary[type];
                    else
                        featureName = null;
                    break;
            }
            if (featureName == null)
                return;
            Prefab feature = new Prefab(featureName, Prefab.Type.Other, pos.x, pos.y, SimHashes.Katairite);
            data.gameSpawnData.otherEntities.Add(feature);

            // Base
            if (neutronium)
            {
                for (int x = pos.x - 1; x < pos.x + 3; x++)
                    cells[Grid.XYToCell(x, pos.y - 1)].SetValues(WorldGen.unobtaniumElement, ElementLoader.elements);
            }
            // Cover feature
            if (cover)
            {
                for (int x = pos.x; x < pos.x + 2; x++)
                    for (int y = pos.y; y < pos.y + 3; y++)
                        if (!ElementLoader.elements[cells[Grid.XYToCell(x, y)].elementIdx].IsSolid)
                            cells[Grid.XYToCell(x, y)].SetValues(coverMaterial, GetPhysicsData(coverMaterial), ElementLoader.elements);
            }
        }

        private static SpawnPoints GetSpawnPoints(Sim.Cell[] cells, IEnumerable<Vector2I> biomeCells)
        {
            var spawnPoints = new SpawnPoints()
            {
                onFloor = new HashSet<Vector2I>(),
                onCeil = new HashSet<Vector2I>(),
                inGround = new HashSet<Vector2I>(),
                inAir = new HashSet<Vector2I>(),
                inLiquid = new HashSet<Vector2I>(),
            };

            foreach (Vector2I pos in biomeCells)
            {
                int cell = Grid.PosToCell(pos);
                Element element = ElementLoader.elements[cells[cell].elementIdx];
                if (element.IsSolid && element.id != SimHashes.Katairite && element.id != SimHashes.Unobtanium && cells[cell].temperature < 373f)
                    spawnPoints.inGround.Add(pos);
                else if (element.IsGas)
                {
                    Element elementBelow = ElementLoader.elements[cells[Grid.CellBelow(cell)].elementIdx];
                    if (elementBelow.IsSolid)
                        spawnPoints.onFloor.Add(pos);
                    else
                        spawnPoints.inAir.Add(pos);
                }
                else if (element.IsLiquid)
                    spawnPoints.inLiquid.Add(pos);
            }

            return spawnPoints;
        }

        public struct SpawnPoints
        {
            public ISet<Vector2I> onFloor;
            public ISet<Vector2I> onCeil;
            public ISet<Vector2I> inGround;
            public ISet<Vector2I> inAir;
            public ISet<Vector2I> inLiquid;
        }

        private static void PlaceSpawnables(Sim.Cell[] cells, List<Prefab> spawnList, MiniBaseBiomeProfile biome, ISet<Vector2I> biomeCells, ISet<Vector2I> reservedCells)
        {
            var spawnStruct = GetSpawnPoints(cells, biomeCells.Except(reservedCells));
            PlaceSpawnables(spawnList, biome.spawnablesOnFloor, spawnStruct.onFloor, Prefab.Type.Pickupable);
            PlaceSpawnables(spawnList, biome.spawnablesOnCeil, spawnStruct.onCeil, Prefab.Type.Pickupable);
            PlaceSpawnables(spawnList, biome.spawnablesInGround, spawnStruct.inGround, Prefab.Type.Pickupable);
            PlaceSpawnables(spawnList, biome.spawnablesInLiquid, spawnStruct.inLiquid, Prefab.Type.Pickupable);
            PlaceSpawnables(spawnList, biome.spawnablesInAir, spawnStruct.inAir, Prefab.Type.Pickupable);
        }

        // Add spawnables to the GameSpawnData list
        private static void PlaceSpawnables(List<Prefab> spawnList, Dictionary<string, float> spawnables, ISet<Vector2I> spawnPoints, Prefab.Type prefabType)
        {
            if (spawnables == null || spawnables.Count == 0 || spawnPoints.Count == 0)
                return;
            foreach (KeyValuePair<string, float> spawnable in spawnables)
            {
                int numSpawnables = (int)Math.Ceiling(spawnable.Value * spawnPoints.Count);
                for (int i = 0; i < numSpawnables && spawnPoints.Count > 0; i++)
                {
                    var pos = spawnPoints.ElementAt(random.Next(0, spawnPoints.Count));
                    spawnPoints.Remove(pos);
                    spawnList.Add(new Prefab(spawnable.Key, prefabType, pos.x, pos.y, (SimHashes)0));
                }
            }
        }

        #region Util

        // The following utility methods all refer to the main liveable area
        // E.g., Width() returns the width of the liveable area, not the whole map
        public static int SideMargin() { return (WORLD_WIDTH - moonlet.size.x - 2 * BORDER_SIZE) / 2; }
        public static int Left(bool withBorders = false) { return SideMargin() + (withBorders ? 0 : BORDER_SIZE); }
        public static int Right(bool withBorders = false) { return Left(withBorders) + moonlet.size.x + (withBorders ? BORDER_SIZE * 2 : 0); }
        public static int Top(bool withBorders = false) { return WORLD_HEIGHT - TOP_MARGIN - moonlet.extra_top_margin - (withBorders ? 0 : BORDER_SIZE) + 1; }
        public static int Bottom(bool withBorders = false) { return Top(withBorders) - moonlet.size.y - (withBorders ? BORDER_SIZE * 2 : 0); }
        public static int Width(bool withBorders = false) { return Right(withBorders) - Left(withBorders); }
        public static int Height(bool withBorders = false) { return Top(withBorders) - Bottom(withBorders); }
        public static Vector2I TopLeft(bool withBorders = false) { return Vec(Left(withBorders), Top(withBorders)); }
        public static Vector2I TopRight(bool withBorders = false) { return Vec(Right(withBorders), Top(withBorders)); }
        public static Vector2I BottomLeft(bool withBorders = false) { return Vec(Left(withBorders), Bottom(withBorders)); }
        public static Vector2I BottomRight(bool withBorders = false) { return Vec(Right(withBorders), Bottom(withBorders)); }
        public static Vector2I Vec(int a, int b) { return new Vector2I(a, b); }
        public static bool InLiveableArea(Vector2I pos) { return pos.x >= Left() && pos.x < Right() && pos.y >= Bottom() && pos.y < Top(); }

        public static Sim.PhysicsData GetPhysicsData(Element element, float modifier = 1f, float temperature = -1f)
        {
            Sim.PhysicsData defaultData = element.defaultValues;
            return new Sim.PhysicsData()
            {
                mass = defaultData.mass * modifier * (element.IsSolid ? MiniBaseOptions.Instance.GetResourceModifier() : 1f),
                temperature = temperature == -1 ? defaultData.temperature : temperature,
                pressure = defaultData.pressure
            };
        }
        public static T ChooseRandom<T>(T[] tArray) { return tArray[random.Next(0, tArray.Length)]; }

        // Returns a coherent noisemap normalized between [0.0, 1.0]
        // NOTE: currently, the range is actually [yCorrection, 1.0] roughly centered around 0.5
        public static float[,] GenerateNoiseMap(System.Random r, int width, int height)
        {
            Octave oct1 = new Octave(1f, 10f);
            Octave oct2 = new Octave(oct1.amp / 2, oct1.freq * 2);
            Octave oct3 = new Octave(oct2.amp / 2, oct2.freq * 2);
            float maxAmp = oct1.amp + oct2.amp + oct3.amp;
            float absolutePeriod = 100f;
            float xStretch = 2.5f;
            float zStretch = 1.6f;
            Vector2f offset = new Vector2f((float)r.NextDouble(), (float)r.NextDouble());
            float[,] noiseMap = new float[width, height];

            float total = 0f;
            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    Vector2f pos = new Vector2f(i / absolutePeriod + offset.x, j / absolutePeriod + offset.y);      // Find current x,y position for the noise function
                    double e =                                                                                      // Generate a value in [0, maxAmp] with average maxAmp / 2
                        oct1.amp * Mathf.PerlinNoise(oct1.freq * pos.x / xStretch, oct1.freq * pos.y) +
                        oct2.amp * Mathf.PerlinNoise(oct2.freq * pos.x / xStretch, oct2.freq * pos.y) +
                        oct3.amp * Mathf.PerlinNoise(oct3.freq * pos.x / xStretch, oct3.freq * pos.y);

                    e = e / maxAmp;                                                                                 // Normalize to [0, 1]
                    float f = Mathf.Clamp((float)e, 0f, 1f);
                    total += f;

                    noiseMap[i, j] = f;
                }
            }

            // Center the distribution at 0.5 and stretch it to fill out [0, 1]
            float average = total / noiseMap.Length;
            for (int i = 0; i < width; i++)
                for (int j = 0; j < height; j++)
                {
                    float f = noiseMap[i, j];
                    f -= average;
                    f *= zStretch;
                    f += 0.5f;
                    noiseMap[i, j] = Mathf.Clamp(f, 0f, 1f);
                }

            return noiseMap;
        }

        public struct Octave
        {
            public float amp;
            public float freq;
            public Octave(float amplitude, float frequency)
            {
                amp = amplitude;
                freq = frequency;
            }
        }

        // Return a description of a vertical movement over a horizontal axis
        public static int[] GetHorizontalWalk(int width, int min, int max)
        {
            double WalkChance = 0.7;
            double DoubleWalkChance = 0.25;
            int[] walk = new int[width];
            int height = random.Next(min, max + 1);
            for (int i = 0; i < walk.Length; i++)
            {
                if (random.NextDouble() < WalkChance)
                {
                    int direction;
                    if (height >= max)
                        direction = -1;
                    else if (height <= min)
                        direction = 1;
                    else
                        direction = random.NextDouble() < 0.5 ? 1 : -1;
                    if (random.NextDouble() < DoubleWalkChance)
                        direction *= 2;
                    height = Mathf.Clamp(height + direction, min, max);
                }
                walk[i] = height;
            }
            return walk;
        }

        #endregion
    }
}