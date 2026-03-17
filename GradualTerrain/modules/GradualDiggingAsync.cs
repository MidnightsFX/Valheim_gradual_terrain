using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Analytics;
using UnityEngine.TerrainUtils;
using static InventoryGrid;
using static UnityEngine.EventSystems.EventTrigger;

namespace GradualTerrain.modules {
    internal static class GradualDiggingAsync {

        internal static bool InOperation = false;
        //internal static Queue QueuedTerrainModifications = new Queue();

        public class TerrainModificationDetail {
            public Vector3 position { get; set; }
            public float ringDistance { get; set; }
            public float allowedVariance { get; set; }
            public float targetDelta { get; set; }
        }


        //[HarmonyPatch(typeof(TerrainComp))]
        //internal static class ModifySurroundingTerrainAsync {
        //    [HarmonyPatch(nameof(TerrainComp.ApplyOperation))]
        //    private static void Postfix(TerrainOp modifier) {
        //        // Only run on raise/lowers
        //        if (InOperation || modifier.m_settings.m_raise == false) { return; } // This could be too slow and we don't want to allow the player steeper digging
        //        InOperation = true;
        //        Vector3 worldPos = modifier.transform.position;
        //        float radius = modifier.GetRadius();
        //        float delta = modifier.m_settings.m_raiseDelta;
        //        Heightmap.GetHeight(worldPos, out float centerHeight);
        //        bool raiseTerrain = false;
        //        if (delta > 0) { raiseTerrain = true; }
        //        Dictionary<float, List<TerrainModificationDetail>> SmoothingTargets = PlotGradualPoints(worldPos, raiseTerrain, centerHeight, ValConfig.AdjustmentRange.Value, radius + ValConfig.OffsetFromCenter.Value, ValConfig.RingIncrements.Value);

        //        Player.m_localPlayer.StartCoroutine(CheckAndApplyTerrainChanges(SmoothingTargets, worldPos, radius));
        //        //List<Heightmap> list = new List<Heightmap>();
        //        //Heightmap.FindHeightmap(worldPos, radius * ValConfig.AdjustmentRange.Value * 1.2f, list);
        //        //// This block could be ran async, potentially as a queued list of operations to allow running fewer updates
        //        //foreach (KeyValuePair<float, List<TerrainModificationDetail>> kvp in SmoothingTargets) {
        //        //    foreach (TerrainModificationDetail detail in kvp.Value) {
        //        //        foreach (Heightmap hmap in list) {
        //        //            TerrainComp tcomp = hmap.GetAndCreateTerrainCompiler();
        //        //            InvokeTerrainChanges(tcomp, detail.position, new TerrainOp.Settings() { 
        //        //                m_raise = true,
        //        //                m_raiseRadius = ValConfig.OperationRadius.Value,
        //        //                m_raiseDelta = detail.targetDelta,
        //        //                m_raisePower = 0.5f,
        //        //                m_paintCleared = false
        //        //            });
        //        //        }
        //        //    }
        //        //}

        //    }
        //}

        internal static void InvokeTerrainChanges(TerrainComp __instance, Vector3 pos, TerrainOp.Settings settings) {
            ZPackage zPackage = new ZPackage();
            zPackage.Write(pos);
            settings.Serialize(zPackage);
            __instance.m_nview.InvokeRPC("ApplyOperation", zPackage);
        }

        internal static IEnumerator CheckAndApplyTerrainChanges(Dictionary<float, List<TerrainModificationDetail>> SmoothingTargets, Vector3 center, float radius) {
            int operationCount = 0;
            List<Heightmap> list = new List<Heightmap>();
            Heightmap.FindHeightmap(center, radius * ValConfig.AdjustmentRange.Value * 1.2f, list);
            // This block could be ran async, potentially as a queued list of operations to allow running fewer updates
            foreach (KeyValuePair<float, List<TerrainModificationDetail>> kvp in SmoothingTargets) {
                foreach (TerrainModificationDetail detail in kvp.Value) {

                    if (operationCount == ValConfig.ChangesPerInterval.Value) {
                        operationCount = 0;
                        yield return new WaitForSeconds(1);
                    }
                    operationCount++;

                    foreach (Heightmap hmap in list) {
                        // Only start terrain changes for the Hmap that has the position
                        if (hmap.IsPointInside(detail.position) == false) { continue; }

                        TerrainComp tcomp = hmap.GetAndCreateTerrainCompiler();
                        InvokeTerrainChanges(tcomp, detail.position, new TerrainOp.Settings() {
                            m_square = ValConfig.AdjustmentSquare.Value,
                            m_raise = true,
                            m_raiseRadius = ValConfig.OperationRadius.Value,
                            m_raiseDelta = detail.targetDelta,
                            m_raisePower = ValConfig.AdjustmentPower.Value,
                            m_paintCleared = ValConfig.PaintTerrainDuringChange.Value,
                            m_paintRadius = ValConfig.OperationRadius.Value,
                            m_smooth = ValConfig.SmoothTerrainOnChange.Value,
                            m_smoothRadius = ValConfig.OperationRadius.Value * ValConfig.TerrainSmoothingModifier.Value,
                            m_smoothPower = ValConfig.TerrainSmoothingPower.Value
                            
                        });
                        // If we created the change, we already selected the right hmap, and can break out of this lower loop
                        break;
                    }
                }
            }
            InOperation = false;
            yield break;
        } 

        internal static Dictionary<float, List<TerrainModificationDetail>> PlotGradualPoints(Vector3 centerPoint, bool raiseTerrain, float centerHeight, float max_distance, float start, float increment = 1f) {
            Dictionary<float, List<TerrainModificationDetail>> estimateModificationSpots = new Dictionary<float, List<TerrainModificationDetail>>();

            for (float ring_distance = start; ring_distance < max_distance; ring_distance += increment) {
                float granularity = ring_distance * ValConfig.CircularGranularity.Value; // number of vertices per ring default: 10
                Vector3 radii = new Vector3(centerPoint.x + ring_distance, centerPoint.y, centerPoint.z);
                float circleRadii = Mathf.Abs(radii.x - centerPoint.x);
                estimateModificationSpots.Add(ring_distance, new List<TerrainModificationDetail>());

                for (int i = 0; i < granularity; i++) {
                    float angle = i / granularity * Mathf.PI * 2;
                    float x = centerPoint.x + Mathf.Cos(angle) * circleRadii;
                    float z = centerPoint.z + Mathf.Sin(angle) * circleRadii;
                    Vector3 targetPoint = new Vector3(x, 0, z);
                    targetPoint.y = ZoneSystem.instance.GetGroundHeight(targetPoint);
                    
                    float allowedOffset;
                    if (raiseTerrain) {
                        allowedOffset = ring_distance * ValConfig.MaxAdjustmentHillSlope.Value;
                    } else {
                        allowedOffset = ring_distance * ValConfig.MaxAdjustmentMineSlope.Value;
                    }
                    float heightDiff = targetPoint.y - centerPoint.y;
                    float delta = 0;
                    
                    if (raiseTerrain == false && heightDiff > allowedOffset) {
                        // lower, offset is too high
                        // take the centerpoint and add the max offset, as thats the max allowed.
                        delta = -Mathf.Abs(heightDiff - allowedOffset);
                        
                    } else if (heightDiff > allowedOffset) {
                        // raise but offset is too low
                        delta = Mathf.Abs(heightDiff + allowedOffset);
                    }
                    Logger.LogDebug($"ring-{ring_distance} point: {targetPoint} allowedOffset:{allowedOffset} center-y:{centerPoint.y} target-y:{targetPoint.y} delta:{delta}");
                    estimateModificationSpots[ring_distance].Add(new TerrainModificationDetail() { 
                        position = targetPoint,
                        allowedVariance = allowedOffset,
                        ringDistance = ring_distance,
                        targetDelta = delta
                    });
                }
            }


            return estimateModificationSpots;
        }
    }
}
