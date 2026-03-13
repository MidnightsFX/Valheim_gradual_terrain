using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GradualTerrain.modules {
    internal class HeightmapChanges {

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

            internal static float MinTerrainAdjustNoNeg() {
                return Mathf.Abs(ValConfig.MinTerrainHeightAdjustment.Value);
            }

            internal static float MinTerrainAdjust() {
                return ValConfig.MinTerrainHeightAdjustment.Value;
            }

            internal static float MaxTerrainAdjust() {
                return ValConfig.MaxTerrainHeightAdjustment.Value;
            }

        }
    }
}
