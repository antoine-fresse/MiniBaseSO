using System;
using UnityEngine;
using ProcGenGame;
using Klei.CustomSettings;
using static MiniBase.MiniBaseConfig;

namespace MiniBase
{
    class MiniBaseUtils
    {
        public static bool IsMiniBaseCluster()
        {
            return CustomGameSettings.Instance.GetCurrentQualitySetting(CustomGameSettingConfigs.ClusterLayout).id == ("clusters/" + ClusterName);
        }

        public static bool IsMiniBaseStartPlanetoid()
        {
	    var clusterLayout = SaveLoader.Instance.ClusterLayout;
            foreach (WorldGen world in clusterLayout.worlds) {
                if (!world.isStartingWorld) {
                    continue;
                }

                return IsMiniBasePlanetoid(world);
            }

            return false;
        }

        public static bool IsMiniBasePlanetoid(WorldGen gen)
        {
            bool is_main = gen.Settings.world.filePath == "worlds/MiniBase";
            bool is_second = gen.Settings.world.filePath == "worlds/BabyOilyMoonlet";
            bool is_tree = gen.Settings.world.filePath == "worlds/BabyMarshyMoonlet";
            bool is_niobium = gen.Settings.world.filePath == "worlds/BabyNiobiumMoonlet";

	    return (is_main || is_second || is_tree || is_niobium);
        }

        public static void Log(string msg, bool force = false)
        {
            if (force || MiniBaseOptions.Instance.DebugMode)
                Console.WriteLine("<MiniBase> " + msg);
        }

        #region Debug
        
        public static void TestNoiseMaps()
        {
            int NumTests = 20;
            System.Random random = new System.Random();
            for (int i = 0; i < NumTests; i++)
                PrintNoiseMapBuckets(MiniBaseWorldGen.GenerateNoiseMap(random, MiniBaseWorldGen.Width(), MiniBaseWorldGen.Height()), 20);
        }

        // Debug util method to visualize noisemap
        public static void PrintNoiseMapBuckets(float[,] map, int numBuckets)
        {
            float[] buckets = new float[numBuckets];
            float total = 0f;
            int outliersLow = 0;
            int outliersHigh = 0;
            for (int i = 0; i < map.GetLength(0); i++)
                for (int j = 0; j < map.GetLength(1); j++)
                {
                    float f = Mathf.Clamp(map[i, j], 0f, 1f);
                    if (f <= 0)
                        outliersLow++;
                    else if (f >= 1)
                        outliersHigh++;
                    int index = Math.Max(0, Math.Min(numBuckets - 1, (int) (f * numBuckets))); // Clamp
                    buckets[index]++;
                    total += f;
                }
            string div = "\n----------------------------------------------------------------------------------------\n";
            string str = div + $"NoiseMap   average: {total / map.Length}   low: {outliersLow}   high: {outliersHigh}\n";
            for (int i = 0; i < numBuckets; i++)
                str += (int) Mathf.RoundToInt(buckets[i] * 100f / map.Length) + (i == numBuckets / 2 ? "*" : "") + " ";
            str += div;
            Log(str);
        }

        #endregion
    }
}
