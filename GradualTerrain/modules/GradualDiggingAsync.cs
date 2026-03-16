using HarmonyLib;
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
using static UnityEngine.EventSystems.EventTrigger;

namespace GradualTerrain.modules {
    internal static class GradualDiggingAsync {

        internal static bool InOperation = false;
        internal static Queue QueuedTerrainModifications = new Queue();
        internal static GameObject GTD = new GameObject(name: "GradualTerrainDeformer");

        public class TerrainModificationDetail {
            public Vector3 position { get; set; }
            public float ringDistance { get; set; }
            public float allowedVariance { get; set; }
            public float targetDelta { get; set; }
        }

        [HarmonyPatch(typeof(TerrainComp))]
        internal static class ModifySurroundingTerrainAsync {
            [HarmonyPatch(nameof(TerrainComp.ApplyOperation))]
            private static void Postfix(TerrainOp modifier) {
                // Only run on raise/lowers
                if (InOperation || modifier.m_settings.m_raise == false) { return; } // This could be too slow and we don't want to allow the player steeper digging
                Vector3 worldPos = modifier.transform.position;
                float radius = modifier.GetRadius();
                float delta = modifier.m_settings.m_raiseDelta;
                Heightmap.GetHeight(worldPos, out float centerHeight);
                bool raiseTerrain = false;
                if (delta > 0) { raiseTerrain = true; }
                Dictionary<float, List<TerrainModificationDetail>> SmoothingTargets = PlotGradualPoints(worldPos, raiseTerrain, centerHeight, ValConfig.AdjustmentRange.Value, radius, ValConfig.SmoothingPower.Value);

                List<Heightmap> list = new List<Heightmap>();
                Heightmap.FindHeightmap(worldPos, radius * 1.2f, list);
                // This block could be ran async, potentially as a queued list of operations to allow running fewer updates
                foreach (KeyValuePair<float, List<TerrainModificationDetail>> kvp in SmoothingTargets) {
                    foreach (TerrainModificationDetail detail in kvp.Value) {
                        GameObject instance = GameObject.Instantiate(GTD, detail.position, Quaternion.identity);
                        TerrainOp terop = instance.AddComponent<TerrainOp>();
                        terop = new TerrainOp() {
                            m_settings = new TerrainOp.Settings() { m_raise = true, m_raiseRadius = ValConfig.OperationRadius.Value, m_raiseDelta = detail.targetDelta, m_raisePower = 0.5f }
                        };
                        foreach (Heightmap item in list) {
                            item.GetAndCreateTerrainCompiler().ApplyOperation(
                                terop
                            );
                        }
                    }
                }

            }
        }

        internal static IEnumerator CheckAndApplyTerrainChanges() {
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
                    Logger.LogDebug($"ring-{ring_distance} point: {targetPoint}");
                    float allowedOffset;
                    if (raiseTerrain) {
                        allowedOffset = ring_distance * ValConfig.MaxAdjustmentHillSlope.Value;
                    } else {
                        allowedOffset = ring_distance * ValConfig.MaxAdjustmentMineSlope.Value;
                    }
                    float heightDiff = targetPoint.y - centerPoint.y;
                    float delta = 0;
                    // lower, but the offset is too high
                    if (raiseTerrain == false && heightDiff > allowedOffset) {
                        delta = targetPoint.y + allowedOffset;
                        // raise but offset is too low
                    } else if (heightDiff > allowedOffset) {
                        delta = targetPoint.y - allowedOffset;
                    }
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
