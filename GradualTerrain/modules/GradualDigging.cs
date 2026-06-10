using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace GradualTerrain.modules {
    internal static class GradualDigging {

        [HarmonyPatch(typeof(TerrainComp))]
        internal static class CheckAndModifySurroundingHeightMap {
            [HarmonyPatch(nameof(TerrainComp.RaiseTerrain))]
            private static void Postfix(TerrainComp __instance, Vector3 worldPos, float radius, float delta) {
                // Setting adjustment range to 0 effectively disables digging modifications.
                int range = ValConfig.AdjustmentRange.Value;
                if (range <= 0) { return; }


                // The dig is applied to every comp its radius overlaps, so this Postfix can fire on a
                // comp whose tile does NOT contain worldPos - worldPos sits just past its edge, which
                // gives a negative/out-of-range vertex (the "invalid index" warnings) and, worse, makes
                // each overlapping comp compute a different reference height for the same dig. Resolve
                // the centre from the comp that actually contains worldPos so every invocation agrees on
                // one consistent reference and the vertex index is always in range.
                TerrainComp centerComp = TerrainComp.FindTerrainCompiler(worldPos);
                if (centerComp == null || !centerComp.m_initialized) { return; }

                Heightmap centerHmap = centerComp.m_hmap;
                if (centerHmap == null || centerHmap.m_buildData == null) { return; }

                centerHmap.WorldToVertex(worldPos, out int cent_x, out int cent_y);
                int rowLength = centerHmap.m_width + 1;
                int center_index = cent_y * rowLength + cent_x;
                if (center_index < 0 || center_index >= centerHmap.m_buildData.m_baseHeights.Count) {
                    Logger.LogWarning($"Got invalid index for centerpoint: pos: {worldPos} idx: {center_index} x: {cent_x} y: {cent_y} rowlen: {rowLength}");
                    return;
                }

                // World-absolute height of the dig point: this avoids modification reference issues and can be used to correctly modulate how significant the terrain can deviate
                float centerTotalHeight = centerHmap.m_buildData.m_baseHeights[center_index] + centerHmap.transform.position.y + centerComp.m_levelDelta[center_index];

                HashSet<TerrainComp> modifiedComps = new HashSet<TerrainComp>();

                // Every loaded heightmap the operation can reach - including pristine neighbours that do not
                // own a TerrainComp yet (created on demand). This is what lets the change cross a zone seam
                // instead of stopping at the edge of the comp that was dug.
                // The whole operation uses the limit of the heightmap that was actually dug - the
                // same limit vanilla clamped the dig to (see HeightmapPatches prefix). Resolving the
                // limit per-tile (as the smoothing used to) makes adjacent tiles clamp the one crater
                // to different biome limits, which snaps the terrain to mismatched min/max where tiles
                // meet - most visibly at the corner where 4 heightmaps intersect.
                BiomeConfiguration.HeightLimits limits = BiomeConfiguration.GetHeightLimits(centerHmap);


                Logger.LogInfo($"Starting gradual terrain change from {worldPos} at height {centerTotalHeight} with limits ->  max:{limits.Max} min:{limits.Min}");

                List<Heightmap> reachable = new List<Heightmap>();
                Heightmap.FindHeightmap(worldPos, range, reachable);
                foreach (Heightmap hmap in reachable) {
                    if (hmap.IsDistantLod) { continue; }
                    SmoothHeightmap(hmap, worldPos, centerTotalHeight, range, limits, modifiedComps);
                }

                float modified_radius = Mathf.Max(radius, range);
                __instance.m_lastOpRadius = modified_radius;
                foreach (TerrainComp comp in modifiedComps) {
                    comp.m_lastOpRadius = modified_radius;
                    comp.Save();
                    comp.m_hmap.Poke(false);
                }

                if (ClutterSystem.instance) {
                    ClutterSystem.instance.ResetGrass(worldPos, modified_radius);
                }
            }

            // Walk every vertex of this heightmap within `range` of the operation centre and clamp it onto
            // the allowed slope. Heights are taken from the the reference height to avoid modifications
            // vertexs shared across a seam resolves to the same world height on both sides
            internal static void SmoothHeightmap(Heightmap hmap, Vector3 center, float centerTotalHeight, int range, BiomeConfiguration.HeightLimits limits, HashSet<TerrainComp> modifiedComps) {
                if (hmap.m_buildData == null) { return; }
                List<float> baseHeights = hmap.m_buildData.m_baseHeights;

                float scale = hmap.m_scale;
                int width = hmap.m_width;
                int rowLength = width + 1;
                int half = width / 2;
                Vector3 pos = hmap.transform.position;

                hmap.WorldToVertex(center, out int ccx, out int ccy);
                int rad = Mathf.CeilToInt(range / scale);
                // Clamp the iteration window to this heightmap's vertices which are in the modification area
                int x0 = Mathf.Max(0, ccx - rad);
                int x1 = Mathf.Min(width, ccx + rad);
                int y0 = Mathf.Max(0, ccy - rad);
                int y1 = Mathf.Min(width, ccy + rad);

                float mineSlope = ValConfig.MaxAdjustmentMineSlope.Value;
                float hillSlope = ValConfig.MaxAdjustmentHillSlope.Value;
                // Limit is resolved once for the whole operation from the dug heightmap (see Postfix)
                // so the crater clamps consistently across every tile it spans.
                float minAdj = limits.Min;
                float maxAdj = limits.Max;

                // Read the existing comp (if any) without creating one - only instantiate when we actually
                // need to write, so we don't litter untouched zones with empty comps.
                TerrainComp comp = TerrainComp.FindTerrainCompiler(pos);

                for (int y = y0; y <= y1; y++) {
                    for (int x = x0; x <= x1; x++) {
                        float vertexWorldX = pos.x + (x - half) * scale;
                        float vertexWorldZ = pos.z + (y - half) * scale;
                        float dx = center.x - vertexWorldX;
                        float dz = center.z - vertexWorldZ;
                        float distanceToCenter = Mathf.Sqrt(dx * dx + dz * dz);
                        if (distanceToCenter > range) { continue; } // disc, not square

                        int index = y * rowLength + x;
                        float baseHeight = baseHeights[index];
                        float currentDelta = (comp != null && comp.m_initialized) ? comp.m_levelDelta[index] : 0f;
                        float currentTotalHeight = baseHeight + pos.y + currentDelta;

                        // Allowed deviation grows linearly with distance
                        float allowedDown = distanceToCenter * mineSlope;
                        float allowedUp = distanceToCenter * hillSlope;

                        // This is for tamping existing entries down to allowed ranges
                        float targetHeight;
                        if (currentTotalHeight > centerTotalHeight + allowedDown) {
                            targetHeight = centerTotalHeight + allowedDown; // sticks up -> pull down onto slope
                        } else if (currentTotalHeight < centerTotalHeight - allowedUp) {
                            targetHeight = centerTotalHeight - allowedUp;   // sits low -> raise up onto slope
                        } else {
                            continue; // already within the allowed envelope
                        }

                        // Absolute level delta so the regenerated height (base + delta) equals targetHeight.
                        float newDelta = Mathf.Clamp((targetHeight - pos.y) - baseHeight, minAdj, maxAdj);

                        // If the clamp leaves the vertex exactly where it already is - e.g. it is
                        // already pinned at the +/- limit and the operation only wants to push it
                        // further past that limit - there is nothing to do. Skip it so we don't
                        // create a comp, re-flag the vertex, or trigger a needless save/regenerate.
                        //if (Mathf.Approximately(newDelta, currentDelta)) { continue; }

                        if (comp == null) {
                            comp = hmap.GetAndCreateTerrainCompiler();
                            if (comp == null || !comp.m_initialized) { return; }
                        }

                        comp.m_levelDelta[index] = newDelta;
                        comp.m_modifiedHeight[index] = true;
                        modifiedComps.Add(comp);
                    }
                }
            }
        }
    }
}
