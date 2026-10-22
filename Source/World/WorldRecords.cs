using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using System.Xml.Serialization;

namespace Inheritance.World
{
    public enum WorldGenerationStage { Empty, Planned, Generating, Ready }

    public sealed class NationSnapshot
    {
        public readonly string Id, GenerationKey, CultureKey, Name, CapitalCityId;
        public readonly bool TemporaryCapital;
        public readonly int CenterTile;
        public readonly long InitialBudget, AbstractIncomeTicks;
        public readonly string[] CityIds;
        internal NationSnapshot(NationPlan n, string[] cities, long income)
        {
            Id = n.entityId; GenerationKey = n.generationKey; CultureKey = n.culture; Name = n.name;
            CapitalCityId = n.capitalCityId; TemporaryCapital = n.temporaryCapital; CenterTile = n.centerTile;
            InitialBudget = n.initialBudget; AbstractIncomeTicks = income; CityIds = cities;
        }
    }
    public sealed class CitySnapshot
    {
        public readonly string Id, GenerationKey, Name, CultureKey, ControllerId, OwnerId;
        public readonly int Tile, Level;
        public readonly long LastIncomeTick;
        public readonly string[] CapitalFor;
        internal CitySnapshot(CityPlan c, Inheritance.Core.EntitySnapshot e, string[] capitalFor)
        {
            Id = c.entityId; GenerationKey = c.generationKey; Name = c.name; CultureKey = c.culture;
            ControllerId = e.ControllerId; OwnerId = e.OwnerId; Tile = c.tile; Level = c.level;
            LastIncomeTick = c.lastIncomeTick; CapitalFor = capitalFor;
        }
    }
    public sealed class RegionSnapshot
    {
        public readonly int Tile, NationIndex;
        public readonly float X, Y, Z;
        internal RegionSnapshot(TerrainRecord t)
        { Tile = t.tile; NationIndex = t.region; X = t.x; Y = t.y; Z = t.z; }
    }
    public sealed class IncomeSnapshot
    {
        public readonly string CityId, ControllerId, Key;
        public readonly long StartTick, EndTick, AmountTicks;
        internal IncomeSnapshot(IncomeRecord r)
        { CityId = r.cityId; ControllerId = r.controllerId; Key = r.key; StartTick = r.start; EndTick = r.end; AmountTicks = r.amount; }
    }
    public sealed class TerrainRecord : IExposable
    {
        public int tile, region, hilliness;
        public string biome;
        public float x, y, z;
        public TerrainRecord() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref tile, "tile", -1, true); Scribe_Values.Look(ref region, "region", -1, true);
            Scribe_Values.Look(ref hilliness, "hilliness", -1, true); Scribe_Values.Look(ref biome, "biome");
            Scribe_Values.Look(ref x, "x", float.NaN, true); Scribe_Values.Look(ref y, "y", float.NaN, true); Scribe_Values.Look(ref z, "z", float.NaN, true);
        }
    }
    public sealed class NationPlan : IExposable
    {
        public string generationKey, culture, name, createOperationId, entityId, capitalCityId;
        public int centerTile;
        public long initialBudget = 100;
        public bool temporaryCapital;
        public NationPlan() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref generationKey, "generationKey"); Scribe_Values.Look(ref culture, "culture"); Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref centerTile, "centerTile", -1, true); Scribe_Values.Look(ref initialBudget, "initialBudget", -1L, true);
            Scribe_Values.Look(ref createOperationId, "createOperationId"); Scribe_Values.Look(ref entityId, "entityId");
            Scribe_Values.Look(ref capitalCityId, "capitalCityId"); Scribe_Values.Look(ref temporaryCapital, "temporaryCapital", false, true);
        }
    }
    public sealed class CityPlan : IExposable
    {
        public string generationKey, culture, initialNationKey, name, createOperationId, entityId;
        public int tile, level;
        public long lastIncomeTick;
        public CityPlan() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref generationKey, "generationKey"); Scribe_Values.Look(ref culture, "culture"); Scribe_Values.Look(ref initialNationKey, "initialNationKey");
            Scribe_Values.Look(ref name, "name"); Scribe_Values.Look(ref tile, "tile", -1, true); Scribe_Values.Look(ref level, "level", -1, true);
            Scribe_Values.Look(ref createOperationId, "createOperationId"); Scribe_Values.Look(ref entityId, "entityId");
            Scribe_Values.Look(ref lastIncomeTick, "lastIncomeTick", -1L, true);
        }
    }
    public sealed class CaptureRecord : IExposable
    {
        public string cause, cityId, oldControllerId, newControllerId, operationId;
        public long tick, expectedRevision;
        public bool applied;
        public CaptureRecord() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref cause, "cause"); Scribe_Values.Look(ref cityId, "cityId");
            Scribe_Values.Look(ref oldControllerId, "oldControllerId"); Scribe_Values.Look(ref newControllerId, "newControllerId");
            Scribe_Values.Look(ref operationId, "operationId"); Scribe_Values.Look(ref tick, "tick", -1L, true);
            Scribe_Values.Look(ref expectedRevision, "expectedRevision", -1L, true); Scribe_Values.Look(ref applied, "applied", false, true);
        }
    }
    public sealed class IncomeRecord : IExposable
    {
        public string key, cityId, controllerId;
        public long start, end, amount;
        public IncomeRecord() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key"); Scribe_Values.Look(ref cityId, "cityId"); Scribe_Values.Look(ref controllerId, "controllerId");
            Scribe_Values.Look(ref start, "start", -1L, true); Scribe_Values.Look(ref end, "end", -1L, true); Scribe_Values.Look(ref amount, "amount", -1L, true);
        }
    }
    public sealed class WorldState : IExposable
    {
        public int schema = 1, planVersion = 1, totalSurfaceTiles;
        public string mode, seed, terrainFingerprint, planFingerprint;
        public WorldGenerationStage stage;
        public long startTick;
        [XmlArrayItem("li")]
        public List<TerrainRecord> terrain = new List<TerrainRecord>();
        [XmlArrayItem("li")]
        public List<NationPlan> nations = new List<NationPlan>();
        [XmlArrayItem("li")]
        public List<CityPlan> cities = new List<CityPlan>();
        [XmlArrayItem("li")]
        public List<CaptureRecord> captures = new List<CaptureRecord>();
        [XmlArrayItem("li")]
        public List<IncomeRecord> income = new List<IncomeRecord>();
        public int terrainCount = -1, nationCount = -1, cityCount = -1, captureCount = -1, incomeCount = -1;
        private bool uncheckedLoad;
        public WorldState() { }
        internal void MarkXmlLoaded() { uncheckedLoad = true; }
        internal void UpdateCounts() { terrainCount = terrain.Count; nationCount = nations.Count; cityCount = cities.Count; captureCount = captures.Count; incomeCount = income.Count; }
        internal void VerifyCounts()
        {
            if (terrain == null || nations == null || cities == null || captures == null || income == null)
                throw new InvalidOperationException("World saved table is missing");
            if (uncheckedLoad && (terrainCount != terrain.Count || nationCount != nations.Count || cityCount != cities.Count || captureCount != captures.Count || incomeCount != income.Count))
                throw new InvalidOperationException("World saved record count mismatch");
            uncheckedLoad = false;
        }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            { terrainCount = terrain.Count; nationCount = nations.Count; cityCount = cities.Count; captureCount = captures.Count; incomeCount = income.Count; }
            if (Scribe.mode == LoadSaveMode.LoadingVars) uncheckedLoad = true;
            Scribe_Values.Look(ref schema, "schema", -1, true); Scribe_Values.Look(ref planVersion, "planVersion", -1, true);
            Scribe_Values.Look(ref mode, "mode"); Scribe_Values.Look(ref seed, "seed");
            Scribe_Values.Look(ref totalSurfaceTiles, "totalSurfaceTiles", -1, true); Scribe_Values.Look(ref stage, "stage", (WorldGenerationStage)(-1), true);
            Scribe_Values.Look(ref startTick, "startTick", -1L, true);
            Scribe_Values.Look(ref terrainFingerprint, "terrainFingerprint"); Scribe_Values.Look(ref planFingerprint, "planFingerprint");
            Scribe_Values.Look(ref terrainCount, "terrainCount", -1, true); Scribe_Values.Look(ref nationCount, "nationCount", -1, true);
            Scribe_Values.Look(ref cityCount, "cityCount", -1, true); Scribe_Values.Look(ref captureCount, "captureCount", -1, true);
            Scribe_Values.Look(ref incomeCount, "incomeCount", -1, true);
            Scribe_Collections.Look(ref terrain, "terrain", LookMode.Deep); Scribe_Collections.Look(ref nations, "nations", LookMode.Deep);
            Scribe_Collections.Look(ref cities, "cities", LookMode.Deep); Scribe_Collections.Look(ref captures, "captures", LookMode.Deep);
            Scribe_Collections.Look(ref income, "income", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (terrain == null && terrainCount == 0) terrain = new List<TerrainRecord>();
                if (nations == null && nationCount == 0) nations = new List<NationPlan>();
                if (cities == null && cityCount == 0) cities = new List<CityPlan>();
                if (captures == null && captureCount == 0) captures = new List<CaptureRecord>();
                if (income == null && incomeCount == 0) income = new List<IncomeRecord>();
            }
        }
    }
}
