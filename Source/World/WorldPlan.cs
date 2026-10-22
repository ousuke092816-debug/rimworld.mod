using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.World
{
    internal static class WorldPlanner
    {
        internal static readonly string[] Cultures = { "starmound", "irontree", "forge", "whitecrown", "tidering", "skymark" };
        internal static readonly string[] NationNames = { "碑原評議会", "根海共同体", "炉路氏族連盟", "白冠諸侯盟約", "潮衡水務連邦", "遠標観測同盟" };
        internal static readonly string[] CapitalNames = { "第一碑庭", "万根の庭", "千炉環", "白冠の座", "分水の都", "灯群台" };
        internal static readonly string[] VillageNames = { "碑守村", "苗根村", "鉱炉村", "角旗村", "水取村", "遠見村" };
        internal static readonly string[] TownNames = { "石庫町", "樹液町", "鍛路町", "街道町", "配水町", "中継町" };
        internal static readonly string[] CityNames = { "修碑市", "種環市", "工廠市", "城塞市", "浄水市", "観測市" };
        internal static List<TerrainRecord> ReadTerrain(RimWorld.Planet.World world)
        {
            var result = new List<TerrainRecord>(); var surface = world.grid.Surface;
            for (int i = 0; i < surface.TilesCount; i++)
            {
                var tile = surface[i];
                if (tile.WaterCovered || tile.PrimaryBiome == null || tile.PrimaryBiome.impassable || !tile.PrimaryBiome.canBuildBase || tile.hilliness == Hilliness.Impassable) continue;
                Vector3 center = surface.GetTileCenter(i).normalized;
                result.Add(new TerrainRecord { tile = i, region = -1, biome = tile.PrimaryBiome.defName,
                    hilliness = (int)tile.hilliness, x = (float)Math.Round(center.x,5), y = (float)Math.Round(center.y,5), z = (float)Math.Round(center.z,5) });
            }
            return result;
        }
        internal static string Digest(string value)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        // XML round trips normalize negative zero; it is the same geometric coordinate.
        private static string FloatBits(float value) { return BitConverter.ToString(BitConverter.GetBytes(value == 0f ? 0f : value)); }
        internal static string TerrainFingerprint(int total, IEnumerable<TerrainRecord> terrain)
        {
            var b = new StringBuilder(); b.Append(total).Append(';');
            foreach (var t in terrain.OrderBy(x => x.tile))
                b.Append(t.tile).Append(':').Append(t.biome.Length).Append(':').Append(t.biome).Append(':').Append(t.hilliness).Append(':')
                    .Append(FloatBits(t.x)).Append(':').Append(FloatBits(t.y)).Append(':').Append(FloatBits(t.z)).Append(';');
            return Digest(b.ToString());
        }
        internal static string PlanFingerprint(WorldState state)
        {
            var b = new StringBuilder(); b.Append(state.planVersion).Append(';').Append(state.seed.Length).Append(':').Append(state.seed).Append(';').Append(state.terrainFingerprint).Append(';');
            foreach (var n in state.nations)
                b.Append(n.generationKey).Append(':').Append(n.culture).Append(':').Append(n.name).Append(':').Append(n.centerTile).Append(':').Append(n.initialBudget).Append(';');
            foreach (var c in state.cities)
                b.Append(c.generationKey).Append(':').Append(c.culture).Append(':').Append(c.initialNationKey).Append(':').Append(c.name).Append(':').Append(c.tile).Append(':').Append(c.level).Append(';');
            foreach (var t in state.terrain) b.Append(t.tile).Append(':').Append(t.region).Append(';');
            return Digest(b.ToString());
        }
        private static double Distance(TerrainRecord a, TerrainRecord b)
        { double x = (double)a.x - b.x, y = (double)a.y - b.y, z = (double)a.z - b.z; return x * x + y * y + z * z; }
        internal static WorldState Build(string seed, int total, List<TerrainRecord> terrain, long startTick)
        {
            if (string.IsNullOrEmpty(seed) || terrain.Count < 24) throw new InvalidOperationException("Six-nation world needs a seed and at least 24 buildable land tiles");
            terrain = terrain.OrderBy(t => t.tile).ToList();
            var state = new WorldState { mode = "six-nations", seed = seed, totalSurfaceTiles = total, startTick = startTick, stage = WorldGenerationStage.Planned, terrain = terrain };
            state.terrainFingerprint = TerrainFingerprint(total, terrain);
            // Stable seed digest, followed by farthest-point sampling on the actual sphere.
            string hash = Digest(seed + "|world-plan-1"); uint salt = uint.Parse(hash.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var centers = new List<TerrainRecord> { terrain[(int)(salt % (uint)terrain.Count)] };
            while (centers.Count < 6)
            {
                TerrainRecord best = null; double score = -1;
                foreach (var tile in terrain)
                {
                    if (centers.Any(x => x.tile == tile.tile)) continue;
                    double d = centers.Min(x => Distance(tile, x));
                    if (d > score) { best = tile; score = d; }
                }
                centers.Add(best);
            }
            foreach (var tile in terrain)
            {
                int best = 0; double score = Distance(tile, centers[0]);
                for (int i = 1; i < 6; i++) { double d = Distance(tile, centers[i]); if (d < score) { score = d; best = i; } }
                tile.region = best;
            }
            // Tiny islands can produce undersized Voronoi regions. Keep all 24 cities distinct.
            for (int i = 0; i < 6; i++)
                while (terrain.Count(t => t.region == i) < 4)
                {
                    var counts = terrain.GroupBy(t => t.region).ToDictionary(g => g.Key, g => g.Count());
                    var selected = terrain.Where(t => t.region != i && counts[t.region] > 4 && !centers.Any(c => c.tile == t.tile))
                        .OrderBy(t => Distance(t, centers[i])).ThenBy(t => t.tile).FirstOrDefault();
                    if (selected == null) throw new InvalidOperationException("Could not provide four land cities to every nation");
                    selected.region = i;
                }
            for (int i = 0; i < 6; i++)
            {
                var n = new NationPlan { culture = Cultures[i], name = NationNames[i], centerTile = centers[i].tile, generationKey = "p03:v1:nation:" + Cultures[i] };
                state.nations.Add(n);
                var sitesList = new List<TerrainRecord> { centers[i] };
                while (sitesList.Count < 4)
                    sitesList.Add(terrain.Where(t => t.region == i && sitesList.All(s => s.tile != t.tile))
                        .OrderByDescending(t => sitesList.Min(s => Distance(t,s))).ThenBy(t => t.tile).First());
                var sites = sitesList.ToArray();
                for (int level = 1; level <= 4; level++)
                {
                    string name = level == 4 ? CapitalNames[i] : level == 3 ? CityNames[i] : level == 2 ? TownNames[i] : VillageNames[i];
                    state.cities.Add(new CityPlan { generationKey = "p03:v1:city:" + Cultures[i] + ":l" + level, culture = Cultures[i], initialNationKey = n.generationKey,
                        name = name, tile = sites[4 - level].tile, level = level, lastIncomeTick = startTick });
                }
            }
            state.planFingerprint = PlanFingerprint(state); return state;
        }
    }
}
