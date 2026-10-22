using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Inheritance.Core;
using RimWorld.Planet;
using Verse;

namespace Inheritance.World
{
    // Runtime service only. Persistent payload is owned by Core, not by a World-specific Scribe class.
    public sealed class WorldStateComponent
    {
        public const string Actor = "inheritance.world";
        private WorldState state = new WorldState { mode = "vanilla" };
        private CoreLedger ledger;
        private bool ready;
        private readonly RimWorld.Planet.World world;
        public RimWorld.Planet.World World { get { return world; } }
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(WorldState));
        public WorldStateComponent(RimWorld.Planet.World world) { this.world = world; }
        public bool IsReady { get { return ready; } }
        public WorldGenerationStage GenerationStage { get { return state.stage; } }
        public string PlanFingerprint { get { return state.planFingerprint; } }
        public string TerrainFingerprint { get { return state.terrainFingerprint; } }
        public long StartTick { get { return state.startTick; } }
        public IEnumerable<NationSnapshot> Nations { get { RequireReady(); return state.nations.Select(n => new NationSnapshot(n,state.cities.Where(c => ledger.GetEntity(c.entityId).ControllerId == n.entityId).Select(c => c.entityId).ToArray(),state.income.Where(r => r.controllerId == n.entityId).Aggregate(0L,(a,r) => checked(a+r.amount)))).ToArray(); } }
        public IEnumerable<CitySnapshot> Cities { get { RequireReady(); return state.cities.Select(c => new CitySnapshot(c,ledger.GetEntity(c.entityId),state.nations.Where(n => n.capitalCityId == c.entityId).Select(n => n.entityId).ToArray())).ToArray(); } }
        public IEnumerable<RegionSnapshot> Regions { get { RequireReady(); return state.terrain.Select(t => new RegionSnapshot(t)).ToArray(); } }
        public IEnumerable<IncomeSnapshot> Income { get { RequireReady(); return state.income.Select(r => new IncomeSnapshot(r)).ToArray(); } }
        private void RequireReady() { if (!ready) throw new InvalidOperationException("Six-nation world is not ready"); }
        public void Tick()
        {
            if (!ready || state.captures.Any(r => !r.applied)) return;
            var core = world.GetComponent<CoreWorldComponent>(); long tick = Find.TickManager.TicksGame;
            if (core.IsReady && tick % 60000 == 0 && state.cities.All(c => c.lastIncomeTick < tick)) SettleIncomeTo(tick);
        }
        public string SaveSnapshot(CoreWorldComponent core)
        {
            RequireReady();
            if (world != Find.World || core != world.GetComponent<CoreWorldComponent>()) throw new InvalidOperationException("World save adapter belongs to another world");
            state.UpdateCounts();
            using (var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            { Serializer.Serialize(writer,state); return writer.ToString(); }
        }
        public void Bootstrap(CoreWorldComponent core, CoreLedger service)
        {
            service.AssertWriteAccess(Actor); ledger = service;
            if (core.Header.WorldMode != "six-nations") throw new InvalidOperationException("World mode mismatch");
            if (core.IsLoadedWorld)
            {
                if (string.IsNullOrEmpty(core.WorldPayload)) throw new InvalidOperationException("Required World save payload is missing");
                using (var text = new StringReader(core.WorldPayload))
                using (var reader = XmlReader.Create(text,new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    state = (WorldState)Serializer.Deserialize(reader);
                if (state == null) throw new InvalidOperationException("World payload is null");
                state.MarkXmlLoaded();
            }
            if (!core.IsLoadedWorld && state.stage == WorldGenerationStage.Empty)
                state = WorldPlanner.Build(world.info.seedString,world.grid.Surface.TilesCount,WorldPlanner.ReadTerrain(world),Find.TickManager.TicksGame);
            ValidatePlan();
            bool alreadyReady = state.stage == WorldGenerationStage.Ready;
            if (!alreadyReady)
            {
                state.stage = WorldGenerationStage.Generating;
                foreach (var n in state.nations)
                    n.entityId = Materialize(n.createOperationId,n.entityId,n.generationKey,new EntitySpec("polity",n.name,n.culture,identityKey:n.generationKey),delegate(string id) { n.createOperationId = id; });
                foreach (var c in state.cities)
                {
                    var n = state.nations.Single(x => x.generationKey == c.initialNationKey);
                    c.entityId = Materialize(c.createOperationId,c.entityId,c.generationKey,new EntitySpec("city",c.name,c.culture,n.entityId,n.entityId,identityKey:c.generationKey),delegate(string id) { c.createOperationId = id; });
                }
                foreach (var n in state.nations) { n.capitalCityId = state.cities.Single(c => c.initialNationKey == n.generationKey && c.level == 4).entityId; n.temporaryCapital = false; }
                state.stage = WorldGenerationStage.Ready;
            }
            ValidateEntities();
            ValidateCaptures();
            foreach (var capture in state.captures.Where(x => !x.applied).ToArray()) CompleteCapture(capture);
            ValidateEconomy(); ValidateCapitals(); ready = true;
            Log.Message("[Inheritance-World] Ready; nations=6; cities=24; plan=" + state.planFingerprint);
        }
        private string Materialize(string operationId, string resultId, string cause, EntitySpec spec, Action<string> remember)
        {
            var prior = ledger.FindOperation(Actor,"create",cause);
            if (prior != null && operationId != null && operationId != prior.Id) throw new InvalidOperationException("Generation operation identity changed");
            string id = prior != null ? prior.Id : operationId ?? ledger.NewOperationId(Actor); remember(id);
            var prepared = ledger.PrepareCreate(id,cause,spec);
            if (resultId != null && resultId != prepared.ResultId) throw new InvalidOperationException("Generation result identity changed");
            return ledger.Commit(id).ResultId;
        }
        public string RecomputePlanFingerprint()
        {
            RequireReady();
            return WorldPlanner.Build(state.seed,world.grid.Surface.TilesCount,WorldPlanner.ReadTerrain(world),state.startTick).planFingerprint;
        }
        private void ValidatePlan()
        {
            state.VerifyCounts();
            if (state.schema != 1 || state.planVersion != 1 || state.mode != "six-nations" || string.IsNullOrEmpty(state.seed) ||
                state.seed != world.info.seedString || state.startTick < 0 || !Enum.IsDefined(typeof(WorldGenerationStage),state.stage) || state.stage == WorldGenerationStage.Empty ||
                state.totalSurfaceTiles != world.grid.Surface.TilesCount || state.nations.Count != 6 || state.cities.Count != 24)
                throw new InvalidOperationException("World plan header is incompatible");
            var actual = WorldPlanner.ReadTerrain(world);
            string actualFingerprint = WorldPlanner.TerrainFingerprint(state.totalSurfaceTiles,actual), savedFingerprint = WorldPlanner.TerrainFingerprint(state.totalSurfaceTiles,state.terrain), planFingerprint = WorldPlanner.PlanFingerprint(state);
            if (actualFingerprint != state.terrainFingerprint || savedFingerprint != state.terrainFingerprint || planFingerprint != state.planFingerprint)
            {
                Log.Message("[Inheritance-World] Fingerprint mismatch actual=" + actualFingerprint + " saved=" + savedFingerprint + " expected=" + state.terrainFingerprint + " plan=" + planFingerprint + " expectedPlan=" + state.planFingerprint + " tiles=" + actual.Count + "/" + state.terrain.Count);
                var difference = actual.Zip(state.terrain,(a,b) => new { a,b }).FirstOrDefault(x => x.a.tile != x.b.tile || x.a.biome != x.b.biome || x.a.hilliness != x.b.hilliness || x.a.x != x.b.x || x.a.y != x.b.y || x.a.z != x.b.z);
                if (difference != null) Log.Message("[Inheritance-World] First terrain difference actual=" + difference.a.tile + "," + difference.a.biome + "," + difference.a.hilliness + "," + difference.a.x.ToString("R") + "," + difference.a.y.ToString("R") + "," + difference.a.z.ToString("R") + " saved=" + difference.b.tile + "," + difference.b.biome + "," + difference.b.hilliness + "," + difference.b.x.ToString("R") + "," + difference.b.y.ToString("R") + "," + difference.b.z.ToString("R"));
                throw new InvalidOperationException("Terrain or plan fingerprint changed");
            }
            var tiles = new HashSet<int>(); var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 6; i++)
            {
                var n = state.nations[i];
                if (n == null || n.culture != WorldPlanner.Cultures[i] || n.name != WorldPlanner.NationNames[i] || n.initialBudget != 100 || !keys.Add(n.generationKey) ||
                    !state.terrain.Any(t => t.tile == n.centerTile && t.region == i)) throw new InvalidOperationException("Nation plan is invalid");
                var cities = state.cities.Where(c => c.initialNationKey == n.generationKey).OrderBy(c => c.level).ToArray();
                if (cities.Length != 4 || !cities.Select(c => c.level).SequenceEqual(new[] {1,2,3,4})) throw new InvalidOperationException("Nation lacks four city tiers");
                foreach (var c in cities)
                    if (!keys.Add(c.generationKey) || c.culture != n.culture || c.lastIncomeTick < state.startTick || !tiles.Add(c.tile) ||
                        !state.terrain.Any(t => t.tile == c.tile && t.region == i)) throw new InvalidOperationException("City plan is invalid");
            }
            if (state.terrain.Select(t => t.tile).Distinct().Count() != state.terrain.Count || state.terrain.Any(t => t.region < 0 || t.region >= 6))
                throw new InvalidOperationException("Region assignment is invalid");
        }
        private void ValidateEntities()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in state.nations)
            {
                var e = ledger.GetEntity(n.entityId); var op = ledger.GetOperation(n.createOperationId);
                if (!ids.Add(e.Id) || e.Kind != "polity" || e.Provider != Actor || e.IdentityKey != n.generationKey || e.CultureKey != n.culture || e.Name != n.name ||
                    op == null || op.Kind != "create" || op.CauseId != n.generationKey || op.ResultId != n.entityId || op.Stage != OperationStage.Committed)
                    throw new InvalidOperationException("Nation disagrees with Core generation journal");
            }
            foreach (var c in state.cities)
            {
                var n = state.nations.Single(x => x.generationKey == c.initialNationKey); var e = ledger.GetEntity(c.entityId); var op = ledger.GetOperation(c.createOperationId);
                if (!ids.Add(e.Id) || e.Kind != "city" || e.Status != EntityStatus.Active || e.Authority != SimulationAuthority.Abstract || e.Provider != Actor || e.IdentityKey != c.generationKey ||
                    e.CultureKey != c.culture || e.Name != c.name || e.OwnerId != n.entityId || !state.nations.Any(x => x.entityId == e.ControllerId) || op == null ||
                    op.Kind != "create" || op.CauseId != c.generationKey || op.ResultId != c.entityId || op.Stage != OperationStage.Committed)
                    throw new InvalidOperationException("City disagrees with Core generation journal");
            }
            if (ledger.Entities.Count(x => x.Provider == Actor && x.Status == EntityStatus.Active && (x.Kind == "city" || x.Kind == "polity")) != 30)
                throw new InvalidOperationException("Unexpected extra World cities or nations");
        }
        private void ValidateCaptures()
        {
            var causes = new HashSet<string>(StringComparer.Ordinal); var operationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in state.captures)
            {
                if (r == null || string.IsNullOrEmpty(r.cause) || !causes.Add(r.cause) || !operationIds.Add(r.operationId) || r.tick < state.startTick || r.expectedRevision < 0 ||
                    !state.cities.Any(c => c.entityId == r.cityId) || !state.nations.Any(n => n.entityId == r.oldControllerId) || !state.nations.Any(n => n.entityId == r.newControllerId) || r.oldControllerId == r.newControllerId)
                    throw new InvalidOperationException("Capture checkpoint is invalid");
                var op = ledger.GetOperation(r.operationId);
                var found = ledger.FindOperation(Actor,"controller",r.cause);
                if (op == null || found == null || found.Id != op.Id || op.ResultId != r.cityId || (r.applied && op.Stage != OperationStage.Committed)) throw new InvalidOperationException("Capture checkpoint journal is missing");
                var history = ledger.GetEntity(r.cityId).ControllerHistory.SingleOrDefault(h => h.OperationId == r.operationId);
                if (op.Stage == OperationStage.Committed && (history == null || history.OldControllerId != r.oldControllerId || history.NewControllerId != r.newControllerId || history.Tick != r.tick))
                    throw new InvalidOperationException("Capture checkpoint disagrees with controller history");
            }
            foreach (var c in state.cities)
                foreach (var h in ledger.GetEntity(c.entityId).ControllerHistory)
                    if (!state.captures.Any(r => r.cityId == c.entityId && r.operationId == h.OperationId)) throw new InvalidOperationException("World controller changed outside a capture checkpoint");
        }
        public string PrepareCapture(string cityId, string newControllerId, long tick, string cause)
        {
            RequireReady(); ledger.AssertWriteAccess(Actor);
            var existing = state.captures.SingleOrDefault(x => x.cause == cause);
            if (existing != null)
            {
                if (existing.cityId != cityId || existing.newControllerId != newControllerId || existing.tick != tick) throw new InvalidOperationException("Capture cause was reused for another request");
                return existing.operationId;
            }
            if (string.IsNullOrEmpty(cause) || !state.cities.Any(c => c.entityId == cityId) || !state.nations.Any(n => n.entityId == newControllerId) || state.captures.Any(x => !x.applied))
                throw new InvalidOperationException("Capture is invalid or another capture is pending");
            var city = ledger.GetEntity(cityId);
            if (city.ControllerId == newControllerId) throw new InvalidOperationException("City is already controlled by this nation");
            SettleIncomeTo(tick);
            var op = ledger.PrepareControllerChange(ledger.NewOperationId(Actor),cause,cityId,newControllerId,city.Revision,tick);
            state.captures.Add(new CaptureRecord { cause = cause, cityId = cityId, oldControllerId = city.ControllerId, newControllerId = newControllerId, tick = tick, expectedRevision = city.Revision, operationId = op.Id });
            return op.Id;
        }
        public void Capture(string cityId, string newControllerId, long tick, string cause)
        {
            string op = PrepareCapture(cityId,newControllerId,tick,cause);
            var r = state.captures.Single(x => x.operationId == op); CompleteCapture(r); ValidateCapitals(); ValidateEconomy();
        }
        private void CompleteCapture(CaptureRecord r)
        {
            if (r.applied) return;
            var op = ledger.GetOperation(r.operationId);
            if (op.Stage != OperationStage.Committed)
            {
                var same = ledger.PrepareControllerChange(r.operationId,r.cause,r.cityId,r.newControllerId,r.expectedRevision,r.tick);
                ledger.Commit(same.Id);
            }
            var change = ledger.GetEntity(r.cityId).ControllerHistory.SingleOrDefault(h => h.OperationId == r.operationId);
            if (change == null || change.OldControllerId != r.oldControllerId || change.NewControllerId != r.newControllerId || change.Tick != r.tick) throw new InvalidOperationException("Capture result disagrees with request");
            foreach (var n in state.nations)
            {
                if (n.capitalCityId != null && ledger.GetEntity(n.capitalCityId).ControllerId == n.entityId) continue;
                var replacement = state.cities.Where(c => ledger.GetEntity(c.entityId).ControllerId == n.entityId).OrderByDescending(c => c.level).ThenBy(c => c.generationKey,StringComparer.Ordinal).FirstOrDefault();
                n.capitalCityId = replacement == null ? null : replacement.entityId;
                n.temporaryCapital = true;
            }
            r.applied = true;
        }
        private void ValidateCapitals()
        {
            foreach (var n in state.nations)
            {
                var original = state.cities.Single(c => c.initialNationKey == n.generationKey && c.level == 4);
                if (!n.temporaryCapital && n.capitalCityId != original.entityId) throw new InvalidOperationException("Capital role lost its relocation flag");
                if ((n.capitalCityId == null && state.cities.Any(c => ledger.GetEntity(c.entityId).ControllerId == n.entityId)) ||
                    (n.capitalCityId != null && (!state.cities.Any(c => c.entityId == n.capitalCityId) || ledger.GetEntity(n.capitalCityId).ControllerId != n.entityId)))
                    throw new InvalidOperationException("Capital is not governed by its nation");
            }
        }
        private string InitialController(CityPlan city) { return state.nations.Single(n => n.generationKey == city.initialNationKey).entityId; }
        private List<IncomeRecord> Intervals(CityPlan city, long start, long end)
        {
            if (end < start || start < state.startTick) throw new InvalidOperationException("Income clock cannot move backwards");
            var result = new List<IncomeRecord>(); string controller = InitialController(city); long cursor = start;
            foreach (var change in ledger.GetEntity(city.entityId).ControllerHistory)
            {
                if (change.Tick <= start) { controller = change.NewControllerId; continue; }
                if (change.Tick >= end) break;
                AddIncome(result,city,controller,cursor,change.Tick); controller = change.NewControllerId; cursor = change.Tick;
            }
            AddIncome(result,city,controller,cursor,end); return result;
        }
        private static void AddIncome(List<IncomeRecord> list, CityPlan city, string controller, long start, long end)
        {
            if (end == start) return;
            list.Add(new IncomeRecord { cityId = city.entityId, controllerId = controller, start = start, end = end,
                amount = checked((end-start) * city.level * city.level), key = city.entityId + "|" + start + "|" + end + "|" + controller });
        }
        public void SettleIncomeTo(long tick)
        {
            RequireReady(); ledger.AssertWriteAccess(Actor);
            if (state.captures.Any(x => !x.applied)) throw new InvalidOperationException("Income is held during pending capture");
            var additions = new List<IncomeRecord>();
            foreach (var city in state.cities) additions.AddRange(Intervals(city,city.lastIncomeTick,tick));
            state.income.Concat(additions).Aggregate(0L,(total,r) => checked(total+r.amount));
            if (additions.Any(r => state.income.Any(x => x.key == r.key))) throw new InvalidOperationException("Income interval was already settled");
            state.income.AddRange(additions); foreach (var city in state.cities) city.lastIncomeTick = tick;
        }
        private void ValidateEconomy()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (state.income.Any(r => r == null || !keys.Add(r.key) || !state.cities.Any(c => c.entityId == r.cityId))) throw new InvalidOperationException("Income receipt table is invalid");
            foreach (var city in state.cities)
            {
                long cursor = state.startTick;
                foreach (var receipt in state.income.Where(r => r.cityId == city.entityId).OrderBy(r => r.start))
                {
                    var expected = Intervals(city,receipt.start,receipt.end);
                    if (receipt.start != cursor || receipt.end <= receipt.start || expected.Count != 1 || expected[0].key != receipt.key || expected[0].controllerId != receipt.controllerId || expected[0].amount != receipt.amount)
                        throw new InvalidOperationException("Income history contains gaps, overlaps or wrong recipients");
                    cursor = receipt.end;
                }
                if (city.lastIncomeTick != cursor) throw new InvalidOperationException("Income cursor disagrees with receipts");
            }
            state.income.Aggregate(0L,(total,r) => checked(total+r.amount));
        }
    }
}
