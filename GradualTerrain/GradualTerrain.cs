using BepInEx;
using BepInEx.Logging;
using GradualTerrain.modules;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System.Reflection;

namespace GradualTerrain {
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.ClientMustHaveMod, VersionStrictness.Minor)]
    internal class GradualTerrain : BaseUnityPlugin {
        public const string PluginGUID = "MidnightsFX.GradualTerrain";
        public const string PluginName = "GradualTerrain";
        public const string PluginVersion = "0.0.1";

        internal static ManualLogSource Log;
        internal ValConfig cfg;

        public void Awake()
        {
            Log = this.Logger;
            cfg = new ValConfig(Config);

            Log.LogInfo("Breaking things down smoothly.");
            Assembly assembly = Assembly.GetExecutingAssembly();
            Harmony harmony = new(PluginGUID);
            harmony.PatchAll(assembly);
        }
    }
}