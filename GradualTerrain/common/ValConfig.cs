using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GradualTerrain {
    internal class ValConfig {
        public static ConfigFile cfg;
        public static ConfigEntry<bool> EnableDebugMode;

        public static ConfigEntry<float> MaxTerrainHeightAdjustment;
        public static ConfigEntry<float> MinTerrainHeightAdjustment;

        public static ConfigEntry<int> AdjustmentRange;
        public static ConfigEntry<float> MaxAdjustmentMineSlope;
        public static ConfigEntry<float> MaxAdjustmentHillSlope;
        public static ConfigEntry<float> RingIncrements;
        public static ConfigEntry<float> OffsetFromCenter;
        public static ConfigEntry<float> CircularGranularity;
        public static ConfigEntry<float> OperationRadius;
        public static ConfigEntry<int> ChangesPerInterval;
        public static ConfigEntry<float> TerrainSmoothingModifier;
        public static ConfigEntry<float> TerrainSmoothingPower;
        public static ConfigEntry<bool> PaintTerrainDuringChange;
        public static ConfigEntry<bool> SmoothTerrainOnChange;
        public static ConfigEntry<bool> AdjustmentSquare;
        public static ConfigEntry<float> AdjustmentPower;
        public static ConfigEntry<bool> EnableBiomeSpecificHeightAdjustments;

        public static Dictionary<Heightmap.Biome, ConfigEntry<float>> BiomeBasedMinTerrainAdjust = new Dictionary<Heightmap.Biome, ConfigEntry<float>>();
        public static Dictionary<Heightmap.Biome, ConfigEntry<float>> BiomeBasedMaxTerrainAdjust = new Dictionary<Heightmap.Biome, ConfigEntry<float>>();

        public ValConfig(ConfigFile cf) {
            // ensure all the config values are created
            cfg = cf;
            cfg.SaveOnConfigSet = true;
            CreateConfigValues(cf);
            Logger.setDebugLogging(EnableDebugMode.Value);
            SetupMainFileWatcher();
        }

        private void CreateConfigValues(ConfigFile Config) {
            // Debugmode
            EnableDebugMode = Config.Bind("Client config", "EnableDebugMode", false,
                new ConfigDescription("Enables Debug logging.",
                null,
                new ConfigurationManagerAttributes { IsAdvanced = true }));
            EnableDebugMode.SettingChanged += Logger.enableDebugLogging;

            MaxTerrainHeightAdjustment = BindServerConfig("Terrain Height Adjustment", "MaxTerrainHeightAdjustment", 12f, "The height terrain can be raised compared to its original position (vanilla default 8).", false, 0, 300f);
            MinTerrainHeightAdjustment = BindServerConfig("Terrain Height Adjustment", "MinTerrainHeightAdjustment", -12f,"The depth terrain can be lowered to compared to its original position (vanilla default 8).", false, -300f, 0f);
            EnableBiomeSpecificHeightAdjustments = BindServerConfig("Gradual Digging", "EnableBiomeSpecificHeightAdjustments", false, "Uses biome-specific configurations for height map adjustments. This allows for things such as, leveling mountains but not turning plains into an ocean.", advanced: true);
            EnableBiomeSpecificHeightAdjustments.SettingChanged += (s, e) => modules.BiomeConfiguration.ClearCache();
            foreach(Heightmap.Biome biome in Enum.GetValues(typeof(Heightmap.Biome))) {
                // None and All are not real corner biomes - skip them so we don't bind nonsense
                // entries (e.g. "All-MinTerrainHeightAdjustment").
                if (biome == Heightmap.Biome.None || biome == Heightmap.Biome.All) { continue; }

                // Item1 is the (positive) max, Item2 the (negative) min. The min needs a negative
                // range or AcceptableValueRange clamps defaults like -50 up to 0.
                Tuple<float, float> biome_value_default = GetBiomeDefaultHeightAdjustments(biome);

                ConfigEntry<float> minEntry = BindServerConfig($"Terrain Height Adjustment Per Biome - {biome}", $"MinTerrainHeightAdjustment", biome_value_default.Item2, $"Minimum terrain height adjustment for {biome}.", advanced: true, valmin: -300f, valmax: 0f);
                ConfigEntry<float> maxEntry = BindServerConfig($"Terrain Height Adjustment Per Biome - {biome}", $"MaxTerrainHeightAdjustment", biome_value_default.Item1, $"Maximum terrain height adjustment for {biome}.", advanced: true, valmin: 0f, valmax: 300f);

                minEntry.SettingChanged += (s, e) => modules.BiomeConfiguration.ClearCache();
                maxEntry.SettingChanged += (s, e) => modules.BiomeConfiguration.ClearCache();

                BiomeBasedMinTerrainAdjust.Add(biome, minEntry);
                BiomeBasedMaxTerrainAdjust.Add(biome, maxEntry);
            }



            AdjustmentRange = BindServerConfig("Gradual Digging", "AdjustmentRange", 60, "The range that gradual terrain modifications will be applied", false, 0, 200);
            MaxAdjustmentMineSlope = BindServerConfig("Gradual Digging", "MaxAdjustmentMineSlope", 0.50f, "The force that smoothing is applied outward from the target operation.");
            MaxAdjustmentHillSlope = BindServerConfig("Gradual Digging", "MaxAdjustmentHillSlope", 0.75f, "The force that smoothing is applied outward from the target operation.");
        }

        private static Tuple<float, float> GetBiomeDefaultHeightAdjustments(Heightmap.Biome targetBiome) {
            Tuple<float, float> heightAdjustments = new Tuple<float, float>(0,0);
            switch(targetBiome) {
                case Heightmap.Biome.Mountain:
                case Heightmap.Biome.DeepNorth:
                    heightAdjustments = new Tuple<float, float>(6f, -50f);
                    break;

                case Heightmap.Biome.AshLands:
                case Heightmap.Biome.Meadows:
                    heightAdjustments = new Tuple<float, float>(8f, -8f);
                    break;

                case Heightmap.Biome.Plains:
                case Heightmap.Biome.BlackForest:
                    heightAdjustments = new Tuple<float, float>(10f, -10f);
                    break;

                case Heightmap.Biome.Mistlands:
                    heightAdjustments = new Tuple<float, float>(10f, -20f);
                    break;

                case Heightmap.Biome.None:
                case Heightmap.Biome.All:
                    heightAdjustments = new Tuple<float, float>(8f, -8f);
                    break;

                default:
                    heightAdjustments = new Tuple<float, float>(8f, -8f);
                    break;

            }


            return heightAdjustments;
        }

        internal static void SetupMainFileWatcher() {
            // Setup a file watcher to detect changes to the config file
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Path = Path.GetDirectoryName(cfg.ConfigFilePath);
            // Ignore changes to other files
            watcher.Filter = "MidnightsFX.GradualTerrain.cfg";
            watcher.Changed += OnConfigFileChanged;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private static void OnConfigFileChanged(object sender, FileSystemEventArgs e) {
            // We only want the config changes being allowed if this is a server (ie in game in a hosted world or dedicated ideally)
            if (ZNet.instance.IsServer() == false) {
                return;
            }
            // Handle the config file change event
            Logger.LogInfo("Configuration file has been changed, reloading settings.");
            cfg.Reload();
        }

        /// <summary>
        /// Helper to bind configs for float types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valmin"></param>
        /// <param name="valmax"></param>
        /// <returns></returns>
        public static ConfigEntry<float[]> BindServerConfig(string catagory, string key, float[] value, string description, bool advanced = false, float valmin = 0, float valmax = 150) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<float>(valmin, valmax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        ///  Helper to bind configs for bool types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="acceptableValues"></param>>
        /// <param name="advanced"></param>
        /// <returns></returns>
        public static ConfigEntry<bool> BindServerConfig(string catagory, string key, bool value, string description, AcceptableValueBase acceptableValues = null, bool advanced = false) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for int types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valmin"></param>
        /// <param name="valmax"></param>
        /// <returns></returns>
        public static ConfigEntry<int> BindServerConfig(string catagory, string key, int value, string description, bool advanced = false, int valmin = 0, int valmax = 150) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<int>(valmin, valmax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for float types
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <param name="valmin"></param>
        /// <param name="valmax"></param>
        /// <returns></returns>
        public static ConfigEntry<float> BindServerConfig(string catagory, string key, float value, string description, bool advanced = false, float valmin = 0, float valmax = 150) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(description,
                new AcceptableValueRange<float>(valmin, valmax),
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }

        /// <summary>
        /// Helper to bind configs for strings
        /// </summary>
        /// <param name="config_file"></param>
        /// <param name="catagory"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="description"></param>
        /// <param name="advanced"></param>
        /// <returns></returns>
        public static ConfigEntry<string> BindServerConfig(string catagory, string key, string value, string description, AcceptableValueList<string> acceptableValues = null, bool advanced = false) {
            return cfg.Bind(catagory, key, value,
                new ConfigDescription(
                    description,
                    acceptableValues,
                new ConfigurationManagerAttributes { IsAdminOnly = true, IsAdvanced = advanced })
                );
        }
    }
}
