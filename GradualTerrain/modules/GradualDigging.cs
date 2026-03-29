using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GradualTerrain.modules {
    internal static class GradualDigging {

        const int terrainArraySize = 4225;

        internal enum RelativeDirection {
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

        internal class TerrainCompRange {
            public List<Vector3> points { get; set; } = new List<Vector3>();
            public RelativeDirection position { get; set; }
            public TerrainComp tcomp { get; set; }
        }

        internal class TerrainEdge {
            public float height { get; set; }
            public RelativeDirection direction { get; set; }
        }

        [HarmonyPatch(typeof(TerrainComp))]
        internal static class CheckAndModifySurroundingHeightMap {
            [HarmonyPatch(nameof(TerrainComp.RaiseTerrain))]
            private static void Postfix(TerrainComp __instance, Vector3 worldPos, float radius, float delta) {

                Dictionary<RelativeDirection, TerrainCompRange> neighborComps = GetNearbyTerrain(worldPos, ValConfig.AdjustmentRange.Value, __instance);
                int r = 1;
                while (r <= ValConfig.AdjustmentRange.Value) {
                    SmoothNearbyTerrain(__instance, worldPos, r, neighborComps);
                    r++;
                }

                float modified_radius = radius;
                if (ValConfig.AdjustmentRange.Value > modified_radius) {
                    modified_radius = ValConfig.AdjustmentRange.Value;
                }
                __instance.m_lastOpRadius = modified_radius;


                foreach (KeyValuePair<RelativeDirection, TerrainCompRange> comp in neighborComps) {
                    // Ensure terrain regenerates
                    //comp.m_lastOpPoint = worldPos;
                    comp.Value.tcomp.m_lastOpRadius = modified_radius;
                    comp.Value.tcomp.Save();
                    comp.Value.tcomp.m_hmap.Poke(false);
                }

                if (ClutterSystem.instance) {
                    ClutterSystem.instance.ResetGrass(worldPos, modified_radius);
                }
            }



            internal static void SmoothNearbyTerrain(TerrainComp __instance, Vector3 center, int distance, Dictionary<RelativeDirection, TerrainCompRange> nearbyComps) {
                int granularity = Mathf.RoundToInt(distance * ValConfig.CircularGranularity.Value); // number of vertices per ring default: 10
                __instance.m_hmap.WorldToVertex(center, out int cent_x, out int cent_y);
                Vector2 centerVertex = new Vector2(cent_x, cent_y);
                int rowLength = __instance.m_width + 1;
                List<int> completedIndexes = new List<int>();

                Dictionary<RelativeDirection, float> terrainEdges = new Dictionary<RelativeDirection, float>();


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
                if (centerTotalHeight == 0) {
                    Logger.LogDebug("Skipping due to center total height being 0.");
                    return;
                }

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

                    RelativeDirection direction = RelativeDirection.Center;
                    
                    // The relative direction of overflow here does not correlate to the actual alignment of hmaps
                    // which is why we test to see if the modified value falls within all of the hmaps, unless we already have a determined mapping
                    if (y >= rowLength || x >= rowLength || x < 0 || y < 0) {
                        // Ignore invalid indexes for now
                        int orig_x = x;
                        int orig_y = y;
                        Logger.LogDebug($"Computed index outside of range {index}, y{y} x{x} row {rowLength} nearby: {string.Join(",", nearbyComps.Keys.Count)}");
                        if (y < 0) {
                            // Up, forward
                            if (x >= rowLength) {
                                direction = RelativeDirection.DiagPosXY;
                                x = x - rowLength;
                                y = y + rowLength;
                                Logger.LogDebug($"Determined DiagPosXY Neighbor modified X{x} Y{y}");
                            }
                            // Up, back
                            else if (x < 0) {
                                direction = RelativeDirection.DiagNegXPosY;
                                x = x + rowLength;
                                y = y + rowLength;
                                Logger.LogDebug($"Determined DiagNegXPosY Neighbor modified X{x} Y{y}");
                            } 
                            // Up
                            else if (x > 0 && x < rowLength) {
                                direction = RelativeDirection.PosY;
                                // 64 - (72 - 64) = 56
                                // Transform the y from overzied to within range, but at the bottom of the range instead of the top
                                // -1 -> 65 | 
                                y = y + rowLength;
                                Logger.LogDebug($"Determined PosY Neighbor modified X{x} Y{y}");
                            }
                        } else if (y >= rowLength) {
                            // Down forward
                            if (x >= rowLength) {
                                direction = RelativeDirection.DiagPosXNegY;
                                x = x - rowLength;
                                y = y - rowLength;
                                Logger.LogDebug($"Determined DiagPosXNegY Neighbor modified X{x} Y{y}");
                            }
                            // Down
                            if (x > 0 && x <= rowLength) {
                                direction = RelativeDirection.NegY;
                                // transform the Y to a valid range, x is already valid?
                                y = y - rowLength;
                                Logger.LogDebug($"Determined NegY Neighbor modified X{x} Y{y}");
                            }
                            // Down, back
                            if (x < 0) {
                                direction = RelativeDirection.DiagNegXY;
                                x = x + rowLength;
                                y = y - rowLength;
                                Logger.LogDebug($"Determined DiagNegXY Neighbor modified X{x} Y{y}");
                            }
                        } else if (y > 0 && y < rowLength) {
                            // Forward
                            if (x >= rowLength) {
                                direction = RelativeDirection.PosX;
                                x = x - rowLength;
                                Logger.LogDebug($"Determined PosX Neighbor modified X{x} Y{y}");
                            }
                            //Backward
                            if (x < 0) {
                                direction = RelativeDirection.NegX;
                                x = x + rowLength;
                                Logger.LogDebug($"Determined NegX Neighbor modified X{x} Y{y}");
                            }
                        }
                        terrainMod = SelectTerrainComp(nearbyComps, direction, orig_x, orig_y);
                        // if we are adjusting the X/Y, index must be modified too
                        index = (y * rowLength) + x;
                        // We need to map the direction that is incoming to the actual tcomp that occupies that space
                        
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
                    if (pointTotalHeight == 0 || pointHeight == 0) {
                        Logger.LogDebug($"Total height comparision was zero, is this the right h_map? x:{x} y:{y} {index}");
                        foreach(var nbhmp in nearbyComps) {
                            Logger.LogDebug($" Nearby TComp {nbhmp.Key} - {nbhmp.Value.position} - {nbhmp.Value.points}");
                        }
                        //completedIndexes.Add(index);
                        //continue;
                    }

                    float requiredHeight = DetermineEdgeRequiredHeight(x,y,rowLength, distance, pointHeight, direction, terrainEdges);
                    if (requiredHeight != 0) {
                        float adjustment = requiredHeight - pointHeight;
                        Logger.LogDebug($"Entry is an edge, forcing height normalization along edge {direction} - ph-{pointHeight} rqh-{requiredHeight}");
                        terrainMod.m_levelDelta[index] = adjustment;
                        terrainMod.m_modifiedHeight[index] = true;
                        completedIndexes.Add(index);
                        continue;
                    }

                    // Logger.LogDebug($"Compare {pointTotalHeight}({pointOffset}) < ({centerTotalHeight} - {allowedOffsetMine})");
                    // May need to check for the negative too
                    if (pointTotalHeight > (centerTotalHeight + allowedOffsetMine)) {
                        // Calculate positive offset max and use that
                        float adjustment = pointOffset - (pointTotalHeight - (centerTotalHeight + allowedOffsetHill));
                        // For for negative adjustments
                        terrainMod.m_levelDelta[index] = Mathf.Clamp(adjustment, ValConfig.MinTerrainHeightAdjustment.Value, ValConfig.MaxTerrainHeightAdjustment.Value);
                        terrainMod.m_modifiedHeight[index] = true;
                        Logger.LogDebug($"Adjusting Mine-: {pointTotalHeight}({pointOffset}) > ({centerTotalHeight}({centerOffset}) + {allowedOffsetMine}) setting adjustment: {adjustment} new total: {pointHeight + adjustment}");
                    }
                    if (pointTotalHeight < (centerTotalHeight - allowedOffsetHill)) {
                        float adjustment = pointOffset - (pointTotalHeight - (centerTotalHeight - allowedOffsetHill));
                        // For for negative adjustments
                        terrainMod.m_levelDelta[index] = Mathf.Clamp(adjustment, ValConfig.MinTerrainHeightAdjustment.Value, ValConfig.MaxTerrainHeightAdjustment.Value);
                        terrainMod.m_modifiedHeight[index] = true;
                        Logger.LogDebug($"Adjusting Hill+: {pointTotalHeight}({pointOffset}) < ({centerTotalHeight}({centerOffset}) + {allowedOffsetHill}) setting adjustment: {adjustment} new total: {pointHeight + adjustment}");
                    }
                    completedIndexes.Add(index);
                }
            }

            public static Dictionary<RelativeDirection, TerrainCompRange> GetNearbyTerrain(Vector3 pos, float distance, TerrainComp center) {
                Dictionary<RelativeDirection, TerrainCompRange> neighborTerrainComps = new Dictionary<RelativeDirection, TerrainCompRange>();
                neighborTerrainComps.Add(RelativeDirection.Center, new TerrainCompRange() { position = RelativeDirection.Center, tcomp = center });
                // Calculate a ring at the max distance with a handful of points around it 6-7 points per quadrant
                // Maximum distance of the modification,
                List<TerrainComp> nearbyComps = new List<TerrainComp>();
                foreach (TerrainComp s_instance in TerrainComp.s_instances) {
                    float area = s_instance.m_size + 2; // small buffer to ensure we actually get the things which are close enough to be hit
                    Vector3 position = s_instance.transform.position;
                    float distanceToInstance = Vector3XZDistance(pos, position);
                    
                    if (distanceToInstance < area + distance) {
                        Logger.LogDebug($"Tcomp Nearby: center: {pos} nearby: {position} | {distanceToInstance} < {area} + {distance}");
                        nearbyComps.Add(s_instance);
                    }
                }

                Vector3 radii = new Vector3(pos.x + distance, pos.y, pos.z);
                float circleRadii = Mathf.Abs(radii.x - pos.x);
                float granularity = 1;

                for (int i = 0; i < granularity; i++) {
                    float angle = i / granularity * Mathf.PI * 2;
                    float x = pos.x + Mathf.Cos(angle) * circleRadii;
                    float z = pos.z + Mathf.Sin(angle) * circleRadii;

                    Vector3 testpoint = VertexToWorld(center, x, z);
                    Logger.LogDebug($"Determined testpoint {testpoint}");
                    bool determined = false;
                    foreach (TerrainComp tcomp in nearbyComps) {
                        if (IsPointInsideTerrainComp(tcomp, testpoint)) {
                            RelativeDirection position = DetermineRelativePosition(x, z, pos);
                            if (neighborTerrainComps.ContainsKey(position)) {
                                neighborTerrainComps[position].points.Add(testpoint);
                            } else {
                                neighborTerrainComps.Add(position, new TerrainCompRange() { points = new List<Vector3>() { testpoint }, position = position, tcomp = tcomp });
                            }
                            // If we found the tcomp things are inside, then skip the others
                            determined = true;
                            break;
                        }
                    }
                    if (determined == false) {
                        Logger.LogWarning($"The position: {testpoint} was not in any of the available hmaps.");
                    }
                }
                Logger.LogDebug($"Found nearby TerrainComps: {string.Join(" ,", neighborTerrainComps.Keys)}");
                return neighborTerrainComps;
            }

            internal static float DetermineEdgeRequiredHeight(int x, int y, int rowLength, int distance, float pointHeight, RelativeDirection direction, Dictionary<RelativeDirection, float> terrainEdge) {
                float requiredHeight = 0;
                // Top
                if (x >= 0 && x <= rowLength && y == 0) {
                    if (terrainEdge.ContainsKey(direction)) {
                        requiredHeight = terrainEdge[direction];
                    } else {
                        terrainEdge.Add(direction, pointHeight);
                        requiredHeight = pointHeight;
                    }
                }

                // Bottom
                if (x > 0 && x < rowLength && y == rowLength) {
                    if (terrainEdge.ContainsKey(direction)) {
                        requiredHeight = terrainEdge[direction];
                    } else {
                        terrainEdge.Add(direction, pointHeight);
                        requiredHeight = pointHeight;
                    }
                }

                // Right
                if (x == rowLength && y >= 0 && y <= rowLength) {
                    if (terrainEdge.ContainsKey(direction)) {
                        requiredHeight = terrainEdge[direction];
                    } else {
                        terrainEdge.Add(direction, pointHeight);
                        requiredHeight = pointHeight;
                    }
                }

                // Left
                if (x == rowLength && y >= 0 && y <= rowLength) {
                    if (terrainEdge.ContainsKey(direction)) {
                        requiredHeight = terrainEdge[direction];
                    } else {
                        terrainEdge.Add(direction, pointHeight);
                        requiredHeight = pointHeight;
                    }
                }
                return requiredHeight;
            }

            internal static TerrainComp SelectTerrainComp(Dictionary<RelativeDirection, TerrainCompRange> available, RelativeDirection position, int x, int y) {
                // if the nearby contains the specified one, use it
                if (available.ContainsKey(position)) {
                    return available[position].tcomp;
                }
                // Shortcut when only two TComps are even within range, basically its always the second one
                if (available.Count == 2) {
                    foreach (RelativeDirection tk in available.Keys) {
                        if (tk != RelativeDirection.Center) {
                            return available[tk].tcomp;
                        }
                    }
                }

                // Warning, this is the fallback case and it is expensive
                Vector3 testPosition = VertexToWorld(available.First().Value.tcomp, x, y) + Player.m_localPlayer.transform.position;
                Logger.LogDebug($"Testing for {testPosition} in TerrainComps. Player pos: {Player.m_localPlayer.transform.position}");
                foreach (KeyValuePair<RelativeDirection, TerrainCompRange> tcomp in available) {
                    //if (tcomp.Value.position == RelativeDirection.Center) { continue; }
                    if (IsPointInsideTerrainComp(tcomp.Value.tcomp, testPosition)) {
                        Logger.LogDebug("Validated location is inside controlled Hmap");
                        return tcomp.Value.tcomp;
                    }
                }
                Logger.LogWarning($"Could not find {position} in the available TerrainComps");
                return available.First().Value.tcomp;
            }

            internal static RelativeDirection DetermineRelativePosition(float x, float y, Vector3 center) {
                RelativeDirection result = RelativeDirection.Center;
                // Calculate the differences
                float dx = x - center.x;
                float dy = y - center.y;

                if (dy > 0) {
                    if (dx > 0) {
                        result = RelativeDirection.DiagPosXY;
                    } else if (dx < 0) {
                        result = RelativeDirection.DiagNegXPosY;
                    } else {
                        result = RelativeDirection.PosY;
                    }
                } else if (dy < 0) {
                    if (dx > 0) {
                        result = RelativeDirection.DiagPosXNegY;
                    } else if (dx < 0) {
                        result = RelativeDirection.DiagNegXY;
                    } else {
                        result = RelativeDirection.NegY;
                    }
                } else {
                    if (dx > 0) {
                        result = RelativeDirection.PosX;
                    } else {
                        result = RelativeDirection.NegX;
                    }
                }

                Logger.LogDebug($"Checking x{x}-xd{dx} y{y}-dy{dy} determined: {result}");
                return result;
            }

            public static bool IsPointInsideTerrainComp(TerrainComp tcomp, Vector3 point) {
                float xtco = tcomp.transform.position.x;
                float ztco = tcomp.transform.position.z;
                int variance = tcomp.m_hmap.m_width;
                // X/Z need to be within the bounds of the controlled Hmap space, eg +/- 32 in any direction from the placed Hmap
                if (point.x >= xtco - variance && point.x <= xtco + variance && point.z >= ztco - variance && point.z <= ztco + variance) {
                    return true;
                }
                return false;
            }

            public static Vector3 VertexToWorld(TerrainComp tcomp, float x, float y) {
                int num = tcomp.m_hmap.m_width / 2;
                float vecx = (x - num - 0.5f) * tcomp.m_hmap.m_scale;
                float vecz = (y - num - 0.5f) * tcomp.m_hmap.m_scale;
                return new Vector3(vecx, 0, vecz);
            }

            public static Vector3 VertexToWorld(TerrainComp tcomp, int x, int y) {
                int num = tcomp.m_hmap.m_width / 2;
                float vecx = (x - num - 0.5f) * tcomp.m_hmap.m_scale;
                float vecz = (y - num - 0.5f) * tcomp.m_hmap.m_scale;
                return new Vector3(vecx, 0, vecz);
            }

            internal static float Vector3XZDistance(Vector3 center, Vector3 other) {
                float num = center.x - other.x;
                float num2 = center.z - other.z;
                return (float)Math.Sqrt(num * num + num2 * num2);
            }

        }
    }
}
