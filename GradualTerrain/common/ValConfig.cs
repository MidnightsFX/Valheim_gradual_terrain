using BepInEx;
using BepInEx.Configuration;
using System.IO;

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

            MaxTerrainHeightAdjustment = BindServerConfig("Terrain Height Adjustment", "MaxTerrainHeightAdjustment", 12f, "The height terrain can be raised compared to its original position (vanilla default 8).", false, 0, 60f);
            MinTerrainHeightAdjustment = BindServerConfig("Terrain Height Adjustment", "MinTerrainHeightAdjustment", -12f,"The depth terrain can be lowered to compared to its original position (vanilla default 8).", false, -60, 0f);

            AdjustmentRange = BindServerConfig("Gradual Digging", "AdjustmentRange", 30, "The range that gradual terrain modifications will be applied", false, 0, 200);
            MaxAdjustmentMineSlope = BindServerConfig("Gradual Digging", "MaxAdjustmentMineSlope", 1f, "The force that smoothing is applied outward from the target operation.");
            MaxAdjustmentHillSlope = BindServerConfig("Gradual Digging", "MaxAdjustmentHillSlope", 0.5f, "The force that smoothing is applied outward from the target operation.");
            RingIncrements = BindServerConfig("Gradual Digging", "RingIncrements", 12f, "The distance between each ring of terrain changes", true, 1, 20);
            CircularGranularity = BindServerConfig("Gradual Digging", "CircularGranularity", 6f, "The granularity used when building the circle");
            OperationRadius = BindServerConfig("Gradual Digging", "OperationRadius", 1.5f, "The radius to apply circular operations", true);
            OffsetFromCenter = BindServerConfig("Gradual Digging", "OffsetFromCenter", 1f, "Modifier for how far out to start the height modifications", true);
            ChangesPerInterval = BindServerConfig("Gradual Digging", "ChangesPerInterval", 20, "The number of calculated points to operate on in a given second.");
            TerrainSmoothingModifier = BindServerConfig("Gradual Digging", "TerrainSmoothingModifier", 2f, "Multiplier on the operation radius that influences how far out smoothing occurs", true);
            TerrainSmoothingPower = BindServerConfig("Gradual Digging", "TerrainSmoothingPower", 3f, "The power of the smoothing effect", true);
            PaintTerrainDuringChange = BindServerConfig("Gradual Digging", "PaintTerrainDuringChange", false, "Wether or not to modify the terrain texture to that of dug dirt/stone");
        }

        internal static void SetupMainFileWatcher() {
            // Setup a file watcher to detect changes to the config file
            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Path = Path.GetDirectoryName(cfg.ConfigFilePath);
            // Ignore changes to other files
            watcher.Filter = "MidnightsFX.ImpactfulSkills.cfg";
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
