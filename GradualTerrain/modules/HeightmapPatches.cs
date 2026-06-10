using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GradualTerrain.modules {
    internal class HeightmapPatches {

        [HarmonyPatch(typeof(TerrainComp))]
        internal static class TerrainCompositionPatches {

            [HarmonyTranspiler]
            [HarmonyPatch(nameof(TerrainComp.ApplyToHeightmap))]
            static IEnumerable<CodeInstruction> ApplyToHeightmap(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldc_R4, 8f)
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(Transpilers.EmitDelegate(MinTerrainAdjustNoNeg))
                .ThrowIfNotMatch("Unable to patch min terrain adjustment.")
                .Advance(1)
                .MatchStartForward(
                    new CodeMatch(OpCodes.Ldc_R4, 8f)
                )
                .RemoveInstructions(1)
                .InsertAndAdvance(Transpilers.EmitDelegate(MaxTerrainAdjust))
                .ThrowIfNotMatch("Unable to patch max terrain adjustment.");

                return codeMatcher.Instructions();
            }

            [HarmonyTranspiler]
            [HarmonyPatch(nameof(TerrainComp.LevelTerrain))]
            static IEnumerable<CodeInstruction> LevelTerrain(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldc_R4, -8f),
                    new CodeMatch(OpCodes.Ldc_R4, 8f)
                )
                .RemoveInstructions(2)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(MinTerrainAdjust),
                    Transpilers.EmitDelegate(MaxTerrainAdjust)
                    )
                .ThrowIfNotMatch("Unable to patch terrain level height adjustments.");

                return codeMatcher.Instructions();
            }

            [HarmonyTranspiler]
            [HarmonyPatch(nameof(TerrainComp.RaiseTerrain))]
            static IEnumerable<CodeInstruction> RaiseTerrain(IEnumerable<CodeInstruction> instructions /*, ILGenerator generator*/) {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher.MatchStartForward(
                    new CodeMatch(OpCodes.Ldc_R4, -8f),
                    new CodeMatch(OpCodes.Ldc_R4, 8f)
                )
                .RemoveInstructions(2)
                .InsertAndAdvance(
                    Transpilers.EmitDelegate(MinTerrainAdjust),
                    Transpilers.EmitDelegate(MaxTerrainAdjust)
                    )
                .ThrowIfNotMatch("Unable to patch terrain level height adjustments.");

                return codeMatcher.Instructions();
            }

            // Populate the per-operation limits once, before the (transpiled) method body runs, so
            // the per-vertex clamp delegates below only read a static instead of doing a lookup.
            [HarmonyPrefix]
            [HarmonyPatch(nameof(TerrainComp.LevelTerrain))]
            static void LevelTerrainPrefix(TerrainComp __instance) {
                BiomeConfiguration.SetCurrentLimits(__instance.m_hmap);
            }

            [HarmonyPrefix]
            [HarmonyPatch(nameof(TerrainComp.RaiseTerrain))]
            static void RaiseTerrainPrefix(TerrainComp __instance) {
                BiomeConfiguration.SetCurrentLimits(__instance.m_hmap);
            }

            // ApplyToHeightmap produces the FINAL rendered height for every vertex and runs on each
            // tile independently. It must clamp to a tile-independent envelope, otherwise a vertex
            // shared across a seam clamps to two different biome limits and the tiles stop lining up.
            [HarmonyPrefix]
            [HarmonyPatch(nameof(TerrainComp.ApplyToHeightmap))]
            static void ApplyToHeightmapPrefix() {
                BiomeConfiguration.SetEnvelopeLimits();
            }

            internal static float MinTerrainAdjustNoNeg() {
                return Mathf.Abs(BiomeConfiguration.CurrentMin);
            }

            internal static float MinTerrainAdjust() {
                return BiomeConfiguration.CurrentMin;
            }

            internal static float MaxTerrainAdjust() {
                return BiomeConfiguration.CurrentMax;
            }

        }
    }
}
