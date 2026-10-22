using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Verse;

namespace Inheritance.Core
{
    public sealed class CoreLedger
    {
        private readonly SaveHeader header;
        private readonly ModuleCatalogue modules;
        private LedgerState state;
        private bool writing;
        private static readonly HashSet<string> Kinds = new HashSet<string>(new[]
        { "community", "polity", "city", "district", "person", "animal", "lot", "unit", "shipment", "reservation", "lease", "operation" }, StringComparer.Ordinal);
        internal LedgerState SavedState { get { if (writing) throw new InvalidOperationException("Cannot save during ledger commit"); return state; } }
        public int EntityCount { get { return state.entities.Count; } }
        public int OperationCount { get { return state.operations.Count; } }
        public void AssertWriteAccess(string actorModule) { Gate(actorModule); modules.Require(actorModule); }
        internal CoreLedger(SaveHeader header, LedgerState state, ModuleCatalogue modules)
        { this.header = header; this.state = state; this.modules = modules; }
        private void SupplyScope(OperationRecord operation,bool allowOwnProof=true)
        {
            var component=Verse.Find.World.GetComponent<CoreWorldComponent>();
            var journal=component.SupplyTransfersForValidation;
            if (journal == null) throw new InvalidOperationException("Supply transfer responsibility is missing");
            string except=allowOwnProof ? journal.LockExemptionForProof(this,operation.id) : null;
            Action<string> checkKey=delegate(string key) {
                if (key == null) return;
                if (journal.HasActiveForCity(key,except) || journal.HasActiveForEntity(key,except))
                    throw new InvalidOperationException("Core asset is locked by an active supply transfer preparation");
                var entity=state.entities.SingleOrDefault(e => e.id == key);
                if (entity != null)
                {
                    if (journal.HasActiveForCity(entity.custody,except)) throw new InvalidOperationException("Core custody is locked by an active supply transfer preparation");
                    string native=entity.binding == null ? null : entity.binding.GetUniqueLoadID();
                    if (journal.HasActiveForNative(native,except) || journal.HasActiveForFood(native,except))
                        throw new InvalidOperationException("Core native binding is locked by an active supply transfer preparation");
                }
            };
            checkKey(operation.subject); checkKey(operation.destination);
            // Reservation entities do not themselves carry custody. Resolve their
            // recorded source before planning can allocate a cargo result or proof.
            if (operation.kind == "depart" || operation.kind == "release")
            {
                var reservation=state.reservations.SingleOrDefault(r => r.id == operation.subject);
                if (reservation != null) { checkKey(reservation.custody); checkKey(reservation.lot); }
            }
            foreach (var stamp in operation.before) checkKey(stamp.id);
            var spec=operation.specification;
            if (spec != null)
            {
                foreach (string key in new[] {spec.controller,spec.owner,spec.community,spec.affiliation,spec.custody}) checkKey(key);
                string native=spec.binding == null ? null : spec.binding.GetUniqueLoadID();
                if (journal.HasActiveForNative(native,except) || journal.HasActiveForFood(native,except))
                    throw new InvalidOperationException("New native binding is locked by an active supply transfer preparation");
            }
        }
        private void Gate(string actor)
        {
            var world = Verse.Find.World;
            var component = world == null ? null : world.GetComponent<CoreWorldComponent>();
            if (component == null || !component.AllowsWrite(this,actor))
                throw new InvalidOperationException("Ledger does not belong to the active, validated world");
            if (!header.IsCompatible || Scribe.mode != LoadSaveMode.Inactive || writing)
                throw new InvalidOperationException("Ledger writing is not available during load, save or commit");
            if (CoreMod.MainThreadId == 0 || Thread.CurrentThread.ManagedThreadId != CoreMod.MainThreadId)
                throw new InvalidOperationException("Ledger writes must run on the game thread");
        }
        public EntitySnapshot GetEntity(string id) { return new EntitySnapshot(Find(state, id)); }
        public IEnumerable<EntitySnapshot> Entities { get { return state.entities.Select(x => new EntitySnapshot(x)).ToArray(); } }
        public IEnumerable<OperationSnapshot> Operations { get { return state.operations.Select(x => new OperationSnapshot(x)).ToArray(); } }
        public bool HasPendingReference(string id)
        { Find(state,id); return state.operations.Any(op => (op.stage == OperationStage.Prepared || op.stage == OperationStage.Applying || op.stage == OperationStage.NeedsReconciliation) && op.before.Any(stamp => stamp.id == id)); }
        public bool HasPendingCityReference(string cityId)
        {
            Find(state,cityId);
            // A pending operation may stamp a lot or resident whose custody is the city,
            // rather than stamping the city itself. Inspect before allocating transfer IDs.
            return state.operations.Where(op => op.stage == OperationStage.Prepared || op.stage == OperationStage.Applying || op.stage == OperationStage.NeedsReconciliation)
                .Any(op => op.subject == cityId || op.destination == cityId ||
                    op.specification != null && op.specification.custody == cityId ||
                    op.before.Any(stamp => stamp.id == cityId || Find(state,stamp.id).custody == cityId));
        }
        public OperationSnapshot GetOperation(string id)
        { var op = state.operations.FirstOrDefault(x => x.id == id); return op == null ? null : new OperationSnapshot(op); }
        public OperationSnapshot FindOperation(string actor, string kind, string cause)
        { var op = state.operations.SingleOrDefault(x => x.actor == actor && x.kind == kind && x.cause == cause); return op == null ? null : new OperationSnapshot(op); }
        public ReservationSnapshot GetReservation(string id)
        { var r = state.reservations.SingleOrDefault(x => x.id == id); if (r == null) throw new InvalidOperationException("Unknown reservation"); return new ReservationSnapshot(r); }
        public long Reserved(string lotId)
        { var e = Active(state,lotId); Abstract(e); if (e.kind != "lot") throw new InvalidOperationException("Expected abstract lot"); return state.reservations.Where(x => x.lot == lotId && x.status == ReservationStatus.Active).Aggregate(0L, (a, b) => checked(a + b.quantity)); }
        public long Available(string lotId) { var e = Active(state, lotId); return checked(e.quantity - Reserved(lotId)); }
        public long Inventory(string custodyId, string itemKey)
        { return state.entities.Where(x => x.status == EntityStatus.Active && x.kind == "lot" && x.authority == SimulationAuthority.Abstract && x.custody == custodyId && x.item == itemKey).Aggregate(0L, (a,b) => checked(a + b.quantity)); }
        public long TotalAbstractQuantity(string itemKey)
        { return state.entities.Where(x => x.status == EntityStatus.Active && x.kind == "lot" && x.authority == SimulationAuthority.Abstract && x.item == itemKey).Aggregate(0L, (a,b) => checked(a + b.quantity)); }

        public string NewOperationId(string actorModule)
        {
            Gate(actorModule); modules.Require(actorModule);
            var work = state.Copy(); string id = Allocate(work, "operation", actorModule);
            Validate(header, work, modules); state = work; return id;
        }
        private string Allocate(LedgerState work, string kind, string provider)
        {
            if (!Kinds.Contains(kind)) throw new InvalidOperationException("Unknown entity kind: " + kind);
            modules.Require(provider);
            string id = header.Issue(kind);
            work.entities.Add(new EntityRecord { id = id, kind = kind, provider = provider, status = EntityStatus.Allocated });
            return id;
        }
        private static EntityRecord Find(LedgerState s, string id)
        { var e = s.entities.FirstOrDefault(x => x.id == id); if (e == null) throw new InvalidOperationException("Unknown entity: " + id); return e; }
        private static EntityRecord Active(LedgerState s, string id)
        { var e = Find(s,id); if (e.status != EntityStatus.Active) throw new InvalidOperationException("Entity is not active: " + id); return e; }
        private static void Abstract(EntityRecord e)
        { if (e.authority != SimulationAuthority.Abstract || e.binding != null) throw new InvalidOperationException("Physical asset transfers require a verified adapter: " + e.id); }
        private static void Custodian(LedgerState s, string id)
        { var e = Active(s,id); if (e.kind != "city" && e.kind != "district" && e.kind != "shipment") throw new InvalidOperationException("Invalid custody: " + id); }
        private static string Canonical(params object[] fields)
        {
            var b = new StringBuilder();
            foreach (object f in fields)
            {
                if (f == null) b.Append("-1:");
                else { string t = Convert.ToString(f, CultureInfo.InvariantCulture); b.Append(t.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(t); }
            }
            return b.ToString();
        }
        private static string Digest(string text)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
        private static string Request(OperationRecord op)
        {
            var s = op.specification;
            return Canonical(op.actor, op.cause, op.kind, op.subject, op.destination, op.quantity, op.expectedRevision, op.tick,
                s == null ? null : Canonical(s.kind,s.name,s.culture,s.controller,s.owner,s.community,s.affiliation,s.custody,
                    (int)s.authority,s.quantity,s.item,s.identity,s.binding == null ? null : s.binding.GetUniqueLoadID()));
        }
        private OperationSnapshot Prepare(OperationRecord request, Action<LedgerState,OperationRecord> plan)
        {
            var actor = Find(state, request.id);
            Gate(actor.provider);
            if (actor.kind != "operation") throw new InvalidOperationException("Expected an operation ID");
            request.actor = actor.provider; modules.Require(request.actor);
            if (string.IsNullOrEmpty(request.cause)) throw new InvalidOperationException("Every operation requires a cause ID");
            request.request = Request(request); request.digest = Digest(request.request);
            var existing = state.operations.FirstOrDefault(x => x.id == request.id);
            if (existing != null)
            {
                if (existing.request != request.request || existing.digest != request.digest)
                    throw new InvalidOperationException("Operation ID was reused for a different request");
                return new OperationSnapshot(existing);
            }
            if (state.operations.Any(x => x.actor == request.actor && x.kind == request.kind && x.cause == request.cause))
                throw new InvalidOperationException("Cause ID already belongs to another operation");
            var work = state.Copy();
            SupplyScope(request); plan(work, request);
            foreach (var stamp in request.before)
                if (state.operations.Any(x => (x.stage == OperationStage.Prepared || x.stage == OperationStage.Applying || x.stage == OperationStage.NeedsReconciliation) && x.before.Any(y => y.id == stamp.id)))
                    throw new InvalidOperationException("Asset is locked by an unfinished operation: " + stamp.id);
            request.stage = OperationStage.Prepared;
            work.operations.Add(request.Copy()); Find(work, request.id).status = EntityStatus.Active;
            Validate(header, work, modules); state = work;
            return new OperationSnapshot(request);
        }
        private static string Fingerprint(LedgerState s, EntityRecord e)
        {
            var r = s.reservations.FirstOrDefault(x => x.id == e.id);
            return Digest(Canonical(e.id,e.kind,e.provider,e.name,e.culture,e.controller,e.owner,e.community,e.affiliation,e.custody,
                e.item,e.identity,e.lineage,e.quantity,e.revision,(int)e.status,(int)e.authority,
                e.binding == null ? null : e.binding.GetUniqueLoadID(),
                Canonical(e.controllerHistory.Select(x => Canonical(x.oldController,x.newController,x.operation,x.tick)).Cast<object>().ToArray()),
                r == null ? null : Canonical(r.lot,r.custody,r.quantity,(int)r.status,r.createdBy,r.closedBy)));
        }
        private static void Stamp(LedgerState work, OperationRecord op, EntityRecord e)
        { if (!op.before.Any(x => x.id == e.id)) op.before.Add(new RevisionStamp(e.id,e.revision,Fingerprint(work,e))); }
        public OperationSnapshot PrepareCreate(string operationId, string causeId, EntitySpec specification)
        {
            if (specification == null) throw new ArgumentNullException("specification");
            var spec = new EntityRecord { kind = specification.Kind }; spec.ApplySpec(specification);
            return Prepare(new OperationRecord { id = operationId, kind = "create", cause = causeId, specification = spec }, delegate(LedgerState work, OperationRecord op)
            {
                ValidateSpec(work, spec);
                foreach (string r in References(spec)) Stamp(work,op,Active(work,r));
                op.result = Allocate(work,spec.kind,op.actor);
                Stamp(work,op,Find(work,op.result));
            });
        }
        public OperationSnapshot PrepareReservation(string operationId, string causeId, string lotId, long amount, long expectedRevision)
        {
            return Prepare(new OperationRecord { id = operationId, kind = "reserve", cause = causeId, subject = lotId, quantity = amount, expectedRevision = expectedRevision }, delegate(LedgerState work, OperationRecord op)
            {
                var e = Active(work,lotId); Abstract(e);
                if (e.kind != "lot" || amount <= 0 || expectedRevision != e.revision || amount > Available(lotId)) throw new InvalidOperationException("Invalid or overdrawn reservation");
                Stamp(work,op,e); op.result = Allocate(work,"reservation",op.actor); Stamp(work,op,Find(work,op.result));
            });
        }
        public OperationSnapshot PrepareRelease(string operationId, string causeId, string reservationId)
        {
            return Prepare(new OperationRecord { id = operationId, kind = "release", cause = causeId, subject = reservationId }, delegate(LedgerState work,OperationRecord op)
            {
                var r = work.reservations.FirstOrDefault(x => x.id == reservationId);
                if (r == null || r.status != ReservationStatus.Active) throw new InvalidOperationException("Reservation is not active");
                Stamp(work,op, Active(work,r.lot)); Stamp(work,op,Active(work,r.id)); op.result = r.id;
            });
        }
        public OperationSnapshot PrepareDeparture(string operationId, string causeId, string reservationId, string destinationId)
        {
            return Prepare(new OperationRecord { id = operationId, kind = "depart", cause = causeId, subject = reservationId, destination = destinationId }, delegate(LedgerState work,OperationRecord op)
            {
                var r = work.reservations.FirstOrDefault(x => x.id == reservationId);
                if (r == null || r.status != ReservationStatus.Active) throw new InvalidOperationException("Reservation is not active");
                var source = Active(work,r.lot); Abstract(source); Custodian(work,destinationId);
                if (source.custody != r.custody || source.quantity < r.quantity || source.custody == destinationId) throw new InvalidOperationException("Reservation custody or quantity changed");
                Stamp(work,op,source); Stamp(work,op,Active(work,r.id)); Stamp(work,op,Active(work,destinationId));
                op.result = Allocate(work,"lot",source.provider); Stamp(work,op,Find(work,op.result));
            });
        }
        public OperationSnapshot PrepareMove(string operationId, string causeId, string entityId, string destinationId, long expectedRevision)
        {
            return Prepare(new OperationRecord { id = operationId, kind = "move", cause = causeId, subject = entityId, destination = destinationId, expectedRevision = expectedRevision }, delegate(LedgerState work,OperationRecord op)
            {
                var e = Active(work,entityId); Abstract(e); Custodian(work,destinationId);
                if (e.kind != "lot" && e.kind != "person" && e.kind != "animal" && e.kind != "unit") throw new InvalidOperationException("Entity kind cannot be moved");
                if (expectedRevision != e.revision || e.custody == destinationId || (e.kind == "lot" && Reserved(e.id) != 0)) throw new InvalidOperationException("Stale or reserved asset cannot move");
                Stamp(work,op,e); Stamp(work,op,Active(work,destinationId)); op.result = e.id;
            });
        }
        public OperationSnapshot PrepareControllerChange(string operationId, string causeId, string cityId, string controllerId, long expectedRevision, long tick)
        {
            return Prepare(new OperationRecord { id = operationId, kind = "controller", cause = causeId, subject = cityId, destination = controllerId, expectedRevision = expectedRevision, tick = tick }, delegate(LedgerState work,OperationRecord op)
            {
                var e = Active(work,cityId); var controller = Active(work,controllerId);
                if (e.kind != "city" || (controller.kind != "polity" && controller.kind != "community") || expectedRevision != e.revision || tick < 0)
                    throw new InvalidOperationException("Invalid controller change");
                if (e.controllerHistory.Count != 0 && e.controllerHistory.Last().tick > tick) throw new InvalidOperationException("Controller history cannot move backwards");
                Stamp(work,op,e); Stamp(work,op,controller); op.result = e.id;
            });
        }
        public OperationSnapshot BeginApplying(string operationId)
        {
            Gate(Find(state,operationId).provider); var work = state.Copy(); var op = work.operations.First(x => x.id == operationId);
            SupplyScope(op); if (op.stage == OperationStage.Prepared) op.stage = OperationStage.Applying;
            else if (op.stage != OperationStage.Applying && op.stage != OperationStage.Committed) throw new InvalidOperationException("Operation cannot begin applying");
            state = work; return new OperationSnapshot(op);
        }
        public OperationSnapshot Abort(string operationId)
        {
            Gate(Find(state,operationId).provider); var work = state.Copy(); var op = work.operations.First(x => x.id == operationId);
            if (op.stage != OperationStage.Prepared && op.stage != OperationStage.Applying) throw new InvalidOperationException("Only side-effect-free pending operations can abort");
            if (Verse.Find.World.GetComponent<CoreWorldComponent>().SupplyTransfersForValidation.LockExemptionForProof(this,op.id) != null)
                throw new InvalidOperationException("Supply transfer proof must be explicitly resumed or disarmed, not aborted");
            SupplyScope(op,false); op.stage = OperationStage.Aborted; state = work; return new OperationSnapshot(op);
        }
        public OperationSnapshot Commit(string operationId)
        {
            Gate(Find(state,operationId).provider); var original = state.operations.First(x => x.id == operationId);
            if (original.stage == OperationStage.Committed) return new OperationSnapshot(original);
            SupplyScope(original); if (original.stage != OperationStage.Prepared && original.stage != OperationStage.Applying) throw new InvalidOperationException("Operation is not committable");
            var work = state.Copy(); var op = work.operations.First(x => x.id == operationId);
            foreach (var stamp in op.before) if (Find(work,stamp.id).revision != stamp.revision || Fingerprint(work,Find(work,stamp.id)) != stamp.fingerprint) throw new InvalidOperationException("Operation precondition changed");
            writing = true;
            try
            {
                Apply(work,op); op.stage = OperationStage.Committed;
                foreach (var stamp in op.before) Find(work,stamp.id).revision = checked(stamp.revision + 1);
                Validate(header,work,modules); state = work;
                return new OperationSnapshot(op);
            }
            finally { writing = false; }
        }
        private static void Apply(LedgerState work, OperationRecord op)
        {
            if (op.kind == "create")
            {
                var e = Find(work,op.result); var s = op.specification;
                e.ApplySpec(new EntitySpec(s.kind,s.name,s.culture,s.controller,s.owner,s.community,s.affiliation,s.custody,s.authority,s.quantity,s.item,s.identity,s.binding));
                e.status = EntityStatus.Active;
            }
            else if (op.kind == "reserve")
            {
                var source = Active(work,op.subject);
                Find(work,op.result).status = EntityStatus.Active;
                work.reservations.Add(new ReservationRecord { id = op.result, lot = source.id, custody = source.custody, quantity = op.quantity, createdBy = op.id });
            }
            else if (op.kind == "release")
            {
                var r = work.reservations.First(x => x.id == op.subject); r.status = ReservationStatus.Released; r.closedBy = op.id;
                Find(work,r.id).status = EntityStatus.Retired;
            }
            else if (op.kind == "depart")
            {
                var r = work.reservations.First(x => x.id == op.subject); var source = Active(work,r.lot);
                var child = source.Copy(); child.id = op.result; child.quantity = r.quantity; child.revision = 0; child.custody = op.destination; child.lineage = source.id;
                child.controllerHistory.Clear();
                work.entities[work.entities.FindIndex(x => x.id == op.result)] = child;
                source.quantity = checked(source.quantity - r.quantity); r.status = ReservationStatus.Consumed; r.closedBy = op.id;
                Find(work,r.id).status = EntityStatus.Retired;
            }
            else if (op.kind == "move") { Active(work,op.subject).custody = op.destination; }
            else if (op.kind == "controller")
            {
                var e = Active(work,op.subject);
                e.controllerHistory.Add(new ControllerChange { oldController = e.controller, newController = op.destination, operation = op.id, tick = op.tick });
                e.controller = op.destination;
            }
            else throw new InvalidOperationException("Unknown operation kind: " + op.kind);
        }
        internal void Recover()
        {
            var work = state.Copy();
            foreach (var op in work.operations.Where(x => x.stage == OperationStage.Applying || x.stage == OperationStage.Prepared))
            {
                bool matches = op.before.All(x => Find(work,x.id).revision == x.revision && Fingerprint(work,Find(work,x.id)) == x.fingerprint);
                if (!matches) op.stage = OperationStage.NeedsReconciliation;
                else if (op.stage == OperationStage.Applying) { op.stage = OperationStage.Prepared; op.recoveryCount = checked(op.recoveryCount + 1); }
            }
            Validate(header,work,modules); state = work;
        }
        private static IEnumerable<string> References(EntityRecord e)
        { return new[] { e.controller,e.owner,e.community,e.affiliation,e.custody }.Where(x => !string.IsNullOrEmpty(x)); }
        private static void ValidateSpec(LedgerState work, EntityRecord e)
        {
            if (!Kinds.Contains(e.kind) || e.kind == "operation" || e.kind == "reservation") throw new InvalidOperationException("Invalid entity specification kind");
            foreach (string r in References(e)) Active(work,r);
            if (e.custody != null) Custodian(work,e.custody);
            if (e.controller != null) { var p = Active(work,e.controller); if (p.kind != "polity" && p.kind != "community") throw new InvalidOperationException("Controller must be a political actor"); }
            if (e.community != null && Active(work,e.community).kind != "community") throw new InvalidOperationException("Community kind mismatch");
            if (e.affiliation != null) { var p = Active(work,e.affiliation); if (p.kind != "polity" && p.kind != "community") throw new InvalidOperationException("Affiliation must be a political actor"); }
            if (!Enum.IsDefined(typeof(SimulationAuthority),e.authority) || e.authority == SimulationAuthority.Pending) throw new InvalidOperationException("Unverified representation authority");
            if (e.authority == SimulationAuthority.Abstract && e.binding != null) throw new InvalidOperationException("Abstract entity cannot own a physical binding");
            if (e.authority == SimulationAuthority.Physical)
            {
                if ((e.kind != "person" && e.kind != "animal" && e.kind != "lot") || e.binding == null || e.binding.Destroyed)
                    throw new InvalidOperationException("Physical asset binding is unresolved");
                if ((e.kind == "person" || e.kind == "animal") && !(e.binding is Pawn)) throw new InvalidOperationException("Individual binding is not a pawn");
                if (e.quantity != 0) throw new InvalidOperationException("Physical quantities belong to the game, not to the abstract ledger");
            }
            if (e.quantity < 0 || (e.kind != "lot" && e.quantity != 0)) throw new InvalidOperationException("Invalid entity quantity");
            if (e.kind == "lot")
            {
                if (e.owner == null || e.custody == null || string.IsNullOrEmpty(e.item)) throw new InvalidOperationException("Lot ownership, custody and item are required");
                var parts = e.item.Split(':');
                var def = parts.Length == 2 ? DefDatabase<ThingDef>.GetNamedSilentFail(parts[1]) : null;
                if (def == null || !ModsConfig.IsActive(parts[0]) || def.modContentPack == null || def.modContentPack.PackageId.ToLowerInvariant() != parts[0])
                    throw new InvalidOperationException("Lot item provider or definition is missing");
                if (e.authority == SimulationAuthority.Physical && e.binding.def != def) throw new InvalidOperationException("Bound item definition mismatch");
            }
        }
        internal static void Validate(SaveHeader h, LedgerState s, ModuleCatalogue m)
        {
            h.ValidateIdentity(); s.VerifyCounts();
            if (s.formatVersion != 1) throw new InvalidOperationException("Unsupported ledger format");
            var ids = new HashSet<string>(StringComparer.Ordinal); var sequences = new HashSet<long>(); long max = 0;
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in s.entities)
            {
                if (e == null || !ids.Add(e.id)) throw new InvalidOperationException("Duplicate or null entity ID");
                long seq = h.ParseId(e.id,e.kind); if (!sequences.Add(seq)) throw new InvalidOperationException("Sequence reused across entity kinds"); max = Math.Max(max,seq);
                m.Require(e.provider);
                if (!Kinds.Contains(e.kind) || e.revision < 0 || e.quantity < 0 || (e.kind != "lot" && e.quantity != 0) ||
                    !Enum.IsDefined(typeof(EntityStatus),e.status) || !Enum.IsDefined(typeof(SimulationAuthority),e.authority) || e.authority == SimulationAuthority.Pending)
                    throw new InvalidOperationException("Invalid entity state");
                if (e.status == EntityStatus.Active && e.kind != "operation" && e.kind != "reservation") ValidateSpec(s,e);
                if (e.binding != null && !bindings.Add(e.binding.GetUniqueLoadID())) throw new InvalidOperationException("Physical object has more than one registry binding");
            }
            if (h.NextSequence <= max) throw new InvalidOperationException("ID allocator has moved backwards");
            var reservations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in s.reservations)
            {
                if (r == null || !reservations.Add(r.id) || Find(s,r.id).kind != "reservation" || r.quantity <= 0 || !Enum.IsDefined(typeof(ReservationStatus),r.status))
                    throw new InvalidOperationException("Invalid reservation record");
                var source = Active(s,r.lot); Abstract(source);
                if (source.kind != "lot") throw new InvalidOperationException("Reservation does not reference a lot");
                if (r.status == ReservationStatus.Active && (source.custody != r.custody || Find(s,r.id).status != EntityStatus.Active)) throw new InvalidOperationException("Reservation location or registry disagrees");
            }
            foreach (var lot in s.entities.Where(x => x.kind == "lot" && x.status == EntityStatus.Active && x.authority == SimulationAuthority.Abstract))
            {
                long reserved = s.reservations.Where(x => x.lot == lot.id && x.status == ReservationStatus.Active).Aggregate(0L,(a,b) => checked(a+b.quantity));
                if (reserved > lot.quantity) throw new InvalidOperationException("Reservations exceed stock");
            }
            var operationIds = new HashSet<string>(StringComparer.Ordinal); var causes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var op in s.operations)
            {
                if (op == null || !operationIds.Add(op.id) || Find(s,op.id).kind != "operation" || Find(s,op.id).provider != op.actor ||
                    !Enum.IsDefined(typeof(OperationStage),op.stage) || op.recoveryCount < 0 || string.IsNullOrEmpty(op.cause)) throw new InvalidOperationException("Invalid operation journal");
                m.Require(op.actor);
                if (!causes.Add(Canonical(op.actor,op.kind,op.cause))) throw new InvalidOperationException("Duplicate business cause in journal");
                if (Request(op) != op.request || Digest(op.request) != op.digest) throw new InvalidOperationException("Operation request digest mismatch");
                if (!new[] {"create","reserve","release","depart","move","controller"}.Contains(op.kind)) throw new InvalidOperationException("Unknown saved operation");
                Find(s,op.result);
                var touched = new HashSet<string>(StringComparer.Ordinal);
                foreach (var stamp in op.before) if (stamp == null || stamp.revision < 0 || string.IsNullOrEmpty(stamp.fingerprint) || stamp.fingerprint.Length != 64 || !touched.Add(stamp.id) || Find(s,stamp.id) == null) throw new InvalidOperationException("Invalid operation precondition");
                if (op.subject != null) Find(s,op.subject); if (op.destination != null) Find(s,op.destination);
                ValidateOperationShape(s,op,touched);
            }
            foreach (var e in s.entities.Where(x => x.status != EntityStatus.Allocated))
            {
                if (e.kind == "operation" && (!operationIds.Contains(e.id) || e.status != EntityStatus.Active)) throw new InvalidOperationException("Orphan operation registry entry");
                if (e.kind == "reservation" && !reservations.Contains(e.id)) throw new InvalidOperationException("Orphan reservation registry entry");
            }
            foreach (var r in s.reservations)
            {
                var created = s.operations.SingleOrDefault(x => x.id == r.createdBy);
                if (created == null || created.kind != "reserve" || created.stage != OperationStage.Committed || created.result != r.id || created.subject != r.lot || created.quantity != r.quantity)
                    throw new InvalidOperationException("Reservation creation journal mismatch");
                if (r.status == ReservationStatus.Active && r.closedBy != null) throw new InvalidOperationException("Active reservation is closed");
                if (r.status != ReservationStatus.Active)
                {
                    var closed = s.operations.SingleOrDefault(x => x.id == r.closedBy);
                    if (closed == null || closed.stage != OperationStage.Committed || closed.subject != r.id ||
                        closed.kind != (r.status == ReservationStatus.Consumed ? "depart" : "release") || Find(s,r.id).status != EntityStatus.Retired)
                        throw new InvalidOperationException("Reservation closure journal mismatch");
                }
            }
            foreach (var city in s.entities.Where(x => x.kind == "city" && x.status == EntityStatus.Active))
            {
                var created = s.operations.SingleOrDefault(x => x.kind == "create" && x.result == city.id && x.stage == OperationStage.Committed);
                if (created == null) throw new InvalidOperationException("City creation journal is missing");
                string controller = created.specification.controller; long previousTick = -1;
                var changes = s.operations.Where(x => x.kind == "controller" && x.subject == city.id && x.stage == OperationStage.Committed).ToArray();
                if (changes.Length != city.controllerHistory.Count) throw new InvalidOperationException("Controller history is incomplete");
                var historyIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var change in city.controllerHistory)
                {
                    if (change == null) throw new InvalidOperationException("Null controller history record");
                    var op = changes.SingleOrDefault(x => x.id == change.operation);
                    if (change == null || !historyIds.Add(change.operation) || op == null || change.oldController != controller ||
                        change.newController != op.destination || change.tick != op.tick || change.tick < previousTick)
                        throw new InvalidOperationException("Controller history disagrees with journal");
                    controller = change.newController; previousTick = change.tick;
                }
                if (controller != city.controller) throw new InvalidOperationException("Current controller disagrees with history");
            }
            var locked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var op in s.operations.Where(x => x.stage == OperationStage.Prepared || x.stage == OperationStage.Applying || x.stage == OperationStage.NeedsReconciliation))
                foreach (var stamp in op.before) if (!locked.Add(stamp.id)) throw new InvalidOperationException("Overlapping pending operation locks");
            m.ValidateSaved(h.ModuleStamps, s.entities.Select(x => x.provider).Concat(s.operations.Select(x => x.actor)).Concat(
                h.WorldMode == "six-nations" ? new[] { "inheritance.world" } : new string[0]),h.SchemaVersion);
        }
        private static void ValidateOperationShape(LedgerState s, OperationRecord op, HashSet<string> touched)
        {
            var expected = new HashSet<string>(StringComparer.Ordinal);
            var result = Find(s,op.result);
            if (op.kind == "create")
            {
                if (op.specification == null || result.kind != op.specification.kind || result.provider != op.actor) throw new InvalidOperationException("Create result mismatch");
                foreach (string r in References(op.specification)) { Find(s,r); expected.Add(r); }
                expected.Add(op.result);
            }
            else
            {
                if (op.specification != null) throw new InvalidOperationException("Unexpected operation specification");
                var subject = Find(s,op.subject); expected.Add(op.subject);
                if (op.kind == "reserve")
                { if (subject.kind != "lot" || result.kind != "reservation" || op.quantity <= 0) throw new InvalidOperationException("Reserve result mismatch"); expected.Add(op.result); }
                else if (op.kind == "release" || op.kind == "depart")
                {
                    var r = s.reservations.SingleOrDefault(x => x.id == op.subject);
                    if (r == null) throw new InvalidOperationException("Missing reservation for operation"); expected.Add(r.lot);
                    if (op.kind == "release" && op.result != r.id) throw new InvalidOperationException("Release result mismatch");
                    if (op.kind == "depart") { Custodian(s,op.destination); expected.Add(op.destination); expected.Add(op.result); if (result.kind != "lot" || result.provider != Find(s,r.lot).provider) throw new InvalidOperationException("Departure result mismatch"); }
                }
                else
                {
                    expected.Add(op.destination);
                    if (op.result != op.subject) throw new InvalidOperationException("Operation result must retain identity");
                    if (op.kind == "move") { Custodian(s,op.destination); if (!new[] {"lot","person","animal","unit"}.Contains(subject.kind)) throw new InvalidOperationException("Move subject mismatch"); }
                    if (op.kind == "controller") { var p = Find(s,op.destination); if (subject.kind != "city" || (p.kind != "polity" && p.kind != "community") || op.tick < 0) throw new InvalidOperationException("Controller subject mismatch"); }
                }
            }
            if (!expected.SetEquals(touched)) throw new InvalidOperationException("Operation precondition set is incomplete");
        }
    }
}
