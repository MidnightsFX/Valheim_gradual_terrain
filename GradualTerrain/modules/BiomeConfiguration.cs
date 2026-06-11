using BepInEx.Configuration;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace GradualTerrain.modules {
    internal static class BiomeConfiguration {

        internal class HeightLimits {
            public float Min;
            public float Max;
        }

        // Per-heightmap cache. ConditionalWeakTable keys on reference identity and auto-evicts
        // entries once a heightmap GameObject is garbage collected (zones unload), so it never
        // leaks. Corner biomes are fixed for a heightmap's lifetime, so the only thing that can
        // invalidate an entry is a config change -> see ClearCache (wired up from ValConfig).
        private static ConditionalWeakTable<Heightmap, HeightLimits> cache = new ConditionalWeakTable<Heightmap, HeightLimits>();
        
        // default lookup values
        internal static float CurrentMin = -8f;
        internal static float CurrentMax = 8f;

        internal static HeightLimits GetHeightLimits(Heightmap hmap) {
            if (hmap == null || !ValConfig.EnableBiomeSpecificHeightAdjustments.Value) {
                // Read live so global config changes apply immediately without touching the cache.
                return new HeightLimits {
                    Min = ValConfig.MinTerrainHeightAdjustment.Value,
                    Max = ValConfig.MaxTerrainHeightAdjustment.Value,
                };
            }

            if (cache.TryGetValue(hmap, out HeightLimits cached)) { return cached; }

            HeightLimits limits = ComputeLimits(hmap);
            cache.Add(hmap, limits);
            return limits;
        }

        // Sets cache entry for this heightmap
        internal static void SetCurrentLimits(Heightmap hmap) {
            HeightLimits limits = GetHeightLimits(hmap);
            CurrentMin = limits.Min;
            CurrentMax = limits.Max;
        }

        // Widest limit across all biomes (plus the global) - a single tile-independent envelope.
        private static HeightLimits envelopeLimits;

        internal static void SetEnvelopeLimits() {
            HeightLimits limits = GetEnvelopeLimits();
            CurrentMin = limits.Min;
            CurrentMax = limits.Max;
        }

        private static HeightLimits GetEnvelopeLimits() {
            if (!ValConfig.EnableBiomeSpecificHeightAdjustments.Value) {
                // Global limits are already tile-independent, so no envelope is needed.
                return new HeightLimits {
                    Min = ValConfig.MinTerrainHeightAdjustment.Value,
                    Max = ValConfig.MaxTerrainHeightAdjustment.Value,
                };
            }

            if (envelopeLimits != null) { return envelopeLimits; }

            float min = ValConfig.MinTerrainHeightAdjustment.Value;
            float max = ValConfig.MaxTerrainHeightAdjustment.Value;
            foreach (ConfigEntry<float> entry in ValConfig.BiomeBasedMinTerrainAdjust.Values) { min = Mathf.Min(min, entry.Value); }
            foreach (ConfigEntry<float> entry in ValConfig.BiomeBasedMaxTerrainAdjust.Values) { max = Mathf.Max(max, entry.Value); }
            envelopeLimits = new HeightLimits { Min = min, Max = max };
            return envelopeLimits;
        }

        internal static void ClearCache() {
            cache = new ConditionalWeakTable<Heightmap, HeightLimits>();
            envelopeLimits = null;
        }

        // Average the configured min/max across the DISTINCT biomes found at the heightmap's four
        // corners. Biomes without a config entry (e.g. Biome.None at the world edge) are skipped;
        // if nothing usable is found we fall back to the global limits.
        private static HeightLimits ComputeLimits(Heightmap hmap) {
            Heightmap.Biome[] corners = hmap.m_cornerBiomes;
            float sumMin = 0f;
            float sumMax = 0f;
            int count = 0;

            for (int i = 0; i < corners.Length; i++) {
                Heightmap.Biome biome = corners[i];

                // Skip duplicate corner biomes so each distinct biome is weighted equally.
                bool alreadySeen = false;
                for (int j = 0; j < i; j++) {
                    if (corners[j] == biome) { alreadySeen = true; break; }
                }
                if (alreadySeen) { continue; }

                if (ValConfig.BiomeBasedMinTerrainAdjust.TryGetValue(biome, out ConfigEntry<float> minEntry)
                    && ValConfig.BiomeBasedMaxTerrainAdjust.TryGetValue(biome, out ConfigEntry<float> maxEntry)) {
                    sumMin += minEntry.Value;
                    sumMax += maxEntry.Value;
                    count++;
                }
            }

            if (count == 0) {
                return new HeightLimits {
                    Min = ValConfig.MinTerrainHeightAdjustment.Value,
                    Max = ValConfig.MaxTerrainHeightAdjustment.Value,
                };
            }

            return new HeightLimits {
                Min = sumMin / count,
                Max = sumMax / count,
            };
        }
    }
}
