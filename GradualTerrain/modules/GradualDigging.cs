using HarmonyLib;
using PlayFab.ClientModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Analytics;
using UnityEngine.SocialPlatforms;
using UnityEngine.UIElements;

namespace GradualTerrain.modules {
    internal static class GradualDigging {

        const int terrainArraySize = 4225;

        internal enum HmRelativePosition {
            PosY,
            NegY,
            PosX,
            NegX,
            Center,
            DiagPosXY,
            DiagNegXY,
            DiagNegXPosY,
            DiagPosXNegY,
        }

        internal class HeightPoint {
            public int refx { get; set; }
            public int refy { get; set; }
            public float height { get; set; }
            public float heightoffset { get; set; }

            public Vector2 GetPointVect2() {
                return new Vector2(refx, refy);
            }
        }

        [HarmonyPatch(typeof(TerrainComp))]
        internal static class CheckAndModifySurroundingHeightMap {
            //[HarmonyPatch(nameof(TerrainComp.RaiseTerrain))]
            //private static void Postfix(TerrainComp __instance, Vector3 worldPos, float radius, float delta) {

            //    Dictionary<HmRelativePosition, TerrainComp> neighborComps = GetNearbyTerrain(worldPos, ValConfig.AdjustmentRange.Value, __instance);
            //    int r = 1;
            //    while (r <= ValConfig.AdjustmentRange.Value) {
            //        SmoothNearbyTerrain(__instance, worldPos, r, neighborComps);
            //        r++;
            //    }

            //    float modified_radius = radius;
            //    if (ValConfig.AdjustmentRange.Value > modified_radius) {
            //        modified_radius = ValConfig.AdjustmentRange.Value * ValConfig.TerrainRadiusStitchingModifier.Value;
            //    }
            //    __instance.m_lastOpRadius = modified_radius;


            //    foreach (TerrainComp comp in neighborComps.Values) {
            //        // Ensure terrain regenerates
            //        //comp.m_lastOpPoint = worldPos;
            //        comp.m_lastOpRadius = modified_radius;
            //        comp.Save();
            //        comp.m_hmap.Poke(false);
            //    }
                
            //    if (ClutterSystem.instance) {
            //        ClutterSystem.instance.ResetGrass(worldPos, modified_radius);
            //    }
            //}

            internal static void SmoothNearbyTerrain(TerrainComp __instance, Vector3 center, float distance, Dictionary<HmRelativePosition,TerrainComp> nearbyComps) {
                int granularity = Mathf.RoundToInt(distance * ValConfig.CircularGranularity.Value); // number of vertices per ring default: 10
                __instance.m_hmap.WorldToVertex(center, out int cent_x, out int cent_y);
                Vector2 centerVertex = new Vector2(cent_x, cent_y);
                int rowLength = __instance.m_width + 1;
                List<int> completedIndexes = new List<int>();


                int center_index = cent_y * rowLength + cent_x;
                if (center_index < 0 || center_index >= terrainArraySize) {
                    // This is a different terraincomp that needs to be adjusted
                    Logger.LogWarning($"Got invalid index for centerpoint: vect3: {center} idx: {center_index} x: {cent_x} y: {cent_y} rowlen: {rowLength}");
                    return;
                }
                float centerHeight = __instance.m_hmap.GetHeight(cent_x, cent_y);
                float centerOffset = __instance.m_levelDelta[center_index];
                float centerTotalHeight = centerHeight + centerOffset;

                // Skip if the center is zero, we will always cause massive realignment with this
                //if (centerTotalHeight == 0) {
                //    Logger.LogDebug("Skipping due to center total height and offset being 0");
                //    return;
                //}

                Vector3 radii = new Vector3(center.x + distance, center.y, center.z);
                __instance.m_hmap.WorldToVertex(radii, out int radii_x, out int raddi_y);
                int circleRadii = Mathf.Abs(radii_x - cent_x);

                TerrainComp terrainMod = __instance;

                //float delta = (2 * Mathf.PI) / granularity;
                for (int i = 0; i < granularity; i++) {
                    //float t = delta * i;
                    //int x = Mathf.RoundToInt(cent_x + Mathf.Cos(t) * circleRadii);
                    //int y = Mathf.RoundToInt(cent_y + Mathf.Sin(t) * circleRadii);
                    //int index = (y * rowLength) + x;
                    terrainMod = __instance;


                    float angle = (float)i / granularity * Mathf.PI * 2;
                    int x = Mathf.RoundToInt(cent_x + Mathf.Cos(angle) * circleRadii);
                    int y = Mathf.RoundToInt(cent_y + Mathf.Sin(angle) * circleRadii);
                    int index = (y * rowLength) + x;

                    if (completedIndexes.Contains(index)) { continue; }
                    //Logger.LogDebug($"Checking Index: {index}");
                    // Skip invalid ranges


                    // Each of the adjacent hmaps here will need point modifications to the x and/or y
                    // Which will allow us to correctly re-calculate the index for an adjacent heightmap
                    if (y >= rowLength || x >= rowLength || x < 0 || y < 0) {
                        Logger.LogDebug($"Computed index outside of range {index}, y{y} x{x} row {rowLength} nearby: {string.Join(",", nearbyComps.Keys)}");
                        if (y < 0) {
                            // Up, forward
                            if (x > rowLength && nearbyComps.ContainsKey(HmRelativePosition.DiagPosXY)) {
                                terrainMod = nearbyComps[HmRelativePosition.DiagPosXY];
                                x = x - rowLength;
                                y = y + rowLength;
                                Logger.LogDebug($"Determined DiagPosXY Neighbor modified X{x} Y{y}");
                            }
                            // Up, back
                            else if (x < 0 && nearbyComps.ContainsKey(HmRelativePosition.DiagNegXPosY)) {
                                Logger.LogDebug("Determined DiagNegXPosY Neighbor");
                                terrainMod = nearbyComps[HmRelativePosition.DiagNegXPosY];
                                x = x + rowLength;
                                y = y + rowLength;
                                Logger.LogDebug($"Determined DiagNegXPosY Neighbor modified X{x} Y{y}");
                            } 
                            // Up
                            else if (x > 0 && x <= rowLength && nearbyComps.ContainsKey(HmRelativePosition.PosY)) {
                                terrainMod = nearbyComps[HmRelativePosition.PosY];
                                // 64 - (72 - 64) = 56
                                // Transform the y from overzied to within range, but at the bottom of the range instead of the top
                                // -1 -> 65 | 
                                y = y + rowLength;
                                Logger.LogDebug($"Determined PosY Neighbor modified X{x} Y{y}");
                            }
                        } else if (y >= rowLength) {
                            // Down forward
                            if (x > rowLength && nearbyComps.ContainsKey(HmRelativePosition.DiagPosXNegY)) {
                                terrainMod = nearbyComps[HmRelativePosition.DiagPosXNegY];
                                x = x - rowLength;
                                y = y - rowLength;
                                Logger.LogDebug($"Determined DiagPosXNegY Neighbor modified X{x} Y{y}");
                            }
                            // Down
                            if (x > 0 && x <= rowLength && nearbyComps.ContainsKey(HmRelativePosition.NegY)) {
                                terrainMod = nearbyComps[HmRelativePosition.NegY];
                                // transform the Y to a valid range, x is already valid?
                                y = y - rowLength;
                                Logger.LogDebug($"Determined NegY Neighbor modified X{x} Y{y}");
                            }
                            // Down, back
                            if (x < 0 && nearbyComps.ContainsKey(HmRelativePosition.DiagNegXY)) {
                                terrainMod = nearbyComps[HmRelativePosition.DiagNegXY];
                                x = x + rowLength;
                                y = y - rowLength;
                                Logger.LogDebug($"Determined DiagNegXY Neighbor modified X{x} Y{y}");
                            }
                        } else if (y > 0 && y < rowLength) {
                            // Forward
                            if (x > rowLength && nearbyComps.ContainsKey(HmRelativePosition.PosX)) {
                                terrainMod = nearbyComps[HmRelativePosition.PosX];
                                x = x - rowLength;
                                Logger.LogDebug($"Determined PosX Neighbor modified X{x} Y{y}");
                            }
                            //Backward
                            if (x < 0 && nearbyComps.ContainsKey(HmRelativePosition.NegX)) {
                                terrainMod = nearbyComps[HmRelativePosition.NegX];
                                x = x + rowLength;
                                Logger.LogDebug($"Determined NegX Neighbor modified X{x} Y{y}");
                            }
                        }
                        // if we are adjusting the X/Y, index must be modified too
                        index = (y * rowLength) + x;
                        //completedIndexes.Add(index);
                        //continue;
                    }


                    float pointHeight = terrainMod.m_hmap.GetHeight(x, y);
                    float pointOffset = terrainMod.m_levelDelta[index];
                    float pointTotalHeight = pointHeight + pointOffset;
                    float distanceToCenter = Vector2.Distance(centerVertex, new Vector2(x, y));
                    float allowedOffsetMine = (distanceToCenter * ValConfig.MaxAdjustmentMineSlope.Value);
                    float allowedOffsetHill = (distanceToCenter * ValConfig.MaxAdjustmentHillSlope.Value);

                    // Need to handle if the pointheight or total height is zero
                    // Need to address the center height being zero and resulting in the adjustment being massive in one direction or another
                    if (pointTotalHeight == 0) {
                        Logger.LogDebug($"Total height comparision was zero, is this the right h_map? x:{x} y:{y} {index}");
                        //completedIndexes.Add(index);
                        //continue;
                    }

                    // Logger.LogDebug($"Compare {pointTotalHeight}({pointOffset}) < ({centerTotalHeight} - {allowedOffsetMine})");
                    // May need to check for the negative too
                    if (pointTotalHeight > (centerTotalHeight + allowedOffsetMine)) {
                        // Calculate positive offset max and use that
                        float adjustment = Mathf.Abs(centerTotalHeight + allowedOffsetMine) - pointHeight;
                        // For for negative adjustments
                        terrainMod.m_levelDelta[index] = Mathf.Clamp(adjustment, ValConfig.MinTerrainHeightAdjustment.Value, ValConfig.MaxTerrainHeightAdjustment.Value);
                        terrainMod.m_modifiedHeight[index] = true;
                        Logger.LogDebug($"Adjusting: {pointTotalHeight}({pointOffset}) > ({centerTotalHeight}({centerOffset}) + {allowedOffsetMine}) setting adjustment: {adjustment} new total: {pointHeight + adjustment}");
                    } else if (pointTotalHeight < (centerTotalHeight - allowedOffsetHill)) {
                        float adjustment = Mathf.Abs(Mathf.Abs(centerTotalHeight - allowedOffsetHill) - pointHeight);
                        // For for negative adjustments
                        terrainMod.m_levelDelta[index] = Mathf.Clamp(adjustment, ValConfig.MinTerrainHeightAdjustment.Value, ValConfig.MaxTerrainHeightAdjustment.Value);
                        terrainMod.m_modifiedHeight[index] = true;
                        Logger.LogDebug($"Adjusting: {pointTotalHeight}({pointOffset}) < ({centerTotalHeight}({centerOffset}) + {allowedOffsetHill}) setting adjustment: {adjustment} new total: {pointHeight + adjustment}");
                    }
                    completedIndexes.Add(index);
                }
            }

            public static Dictionary<HmRelativePosition, TerrainComp> GetNearbyTerrain(Vector3 pos, float distance, TerrainComp center) {
                List<TerrainComp> nearbyComps = new List<TerrainComp>();
                foreach (TerrainComp s_instance in TerrainComp.s_instances) {
                    float area = s_instance.m_size / 1.5f;
                    Vector3 position = s_instance.transform.position;
                    float distanceToInstance = Vector3XZDistance(pos, position);
                    Logger.LogDebug($"Tcomp Nearby: center: {pos} nearby: {position} | {distanceToInstance} < {area} + {distance}");
                    if (distanceToInstance < area + distance) {
                        nearbyComps.Add(s_instance);
                    }
                }
                Logger.LogDebug($"Reviewed {TerrainComp.s_instances.Count} and found {nearbyComps.Count} nearby");
                Dictionary<HmRelativePosition, TerrainComp> neighborTerrainComps = new Dictionary<HmRelativePosition, TerrainComp>();
                if (nearbyComps.Count > 1) {
                    Logger.LogDebug($"Multiple terrain Compositions found nearby: {nearbyComps.Count}");
                    foreach (TerrainComp nearbyTcomp in nearbyComps) {
                        // Center is always the one that we start the operation from, typically will always have the most modifications needed on it
                        if (nearbyTcomp == center) {
                            neighborTerrainComps.Add(HmRelativePosition.Center, nearbyTcomp);
                            continue;
                        }
                        Vector3 relativePosition = center.transform.position - nearbyTcomp.transform.position;
                        Logger.LogDebug($"Nearby Terrain comp directional offset: {relativePosition}");
                        if (relativePosition.x > 0 && relativePosition.z == 0) {
                            neighborTerrainComps.Add(HmRelativePosition.PosX, nearbyTcomp);
                            Logger.LogDebug("PosX");
                            continue;
                        }
                        if (relativePosition.x < 0 && relativePosition.z == 0) {
                            neighborTerrainComps.Add(HmRelativePosition.NegX, nearbyTcomp);
                            Logger.LogDebug("NegX");
                            continue;
                        }
                        if (relativePosition.x == 0 && relativePosition.z > 0) {
                            neighborTerrainComps.Add(HmRelativePosition.PosY, nearbyTcomp);
                            Logger.LogDebug("PosY");
                            continue;
                        }
                        if (relativePosition.x == 0 && relativePosition.z < 0) {
                            neighborTerrainComps.Add(HmRelativePosition.NegY, nearbyTcomp);
                            Logger.LogDebug("NegY");
                            continue;
                        }
                        // Diagonals
                        if (relativePosition.x > 0 && relativePosition.z > 0) {
                            neighborTerrainComps.Add(HmRelativePosition.DiagPosXY, nearbyTcomp);
                            Logger.LogDebug("DiagPosXY");
                            continue;
                        }
                        if (relativePosition.x < 0 && relativePosition.z < 0) {
                            neighborTerrainComps.Add(HmRelativePosition.DiagNegXY, nearbyTcomp);
                            Logger.LogDebug("DiagNegXY");
                            continue;
                        }
                        if (relativePosition.x > 0 && relativePosition.z < 0) {
                            neighborTerrainComps.Add(HmRelativePosition.DiagPosXNegY, nearbyTcomp);
                            Logger.LogDebug("DiagPosXNegY");
                            continue;
                        }
                        if (relativePosition.x < 0 && relativePosition.z > 0) {
                            neighborTerrainComps.Add(HmRelativePosition.DiagNegXPosY, nearbyTcomp);
                            Logger.LogDebug("DiagNegXPosY");
                            continue;
                        }
                    }
                    if (!neighborTerrainComps.ContainsKey(HmRelativePosition.Center)) {
                        neighborTerrainComps.Add(HmRelativePosition.Center, center);
                        Logger.LogDebug("center");
                    }
                } else {
                    // Always ensure the center is available
                    neighborTerrainComps.Add(HmRelativePosition.Center, center);
                    Logger.LogDebug("center");
                }

                return neighborTerrainComps;
            }

            internal static float Vector3XZDistance(Vector3 center, Vector3 other) {
                float num = center.x - other.x;
                float num2 = center.z - other.z;
                return (float)Math.Sqrt(num * num + num2 * num2);
            }

        }
    }
}
