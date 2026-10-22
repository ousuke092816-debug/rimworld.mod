using System;
using System.Collections.Generic;
using Verse;

namespace Inheritance.Core
{
    public enum SimulationAuthority { Abstract, Physical, Pending }
    public enum EntityStatus { Allocated, Active, Retired }
    public enum OperationStage { Prepared, Applying, Committed, Aborted, NeedsReconciliation }
    public enum ReservationStatus { Active, Released, Consumed }

    public sealed class EntitySpec
    {
        public readonly string Kind, Name, CultureKey, ControllerId, OwnerId, CommunityId, AffiliationId, CustodyId, ItemKey, IdentityKey;
        public readonly long Quantity;
        public readonly SimulationAuthority Authority;
        public readonly Thing Binding;
        public EntitySpec(string kind, string name = null, string cultureKey = null, string controllerId = null,
            string ownerId = null, string communityId = null, string affiliationId = null, string custodyId = null,
            SimulationAuthority authority = SimulationAuthority.Abstract, long quantity = 0, string itemKey = null,
            string identityKey = null, Thing binding = null)
        {
            Kind = kind; Name = name; CultureKey = cultureKey; ControllerId = controllerId; OwnerId = ownerId;
            CommunityId = communityId; AffiliationId = affiliationId; CustodyId = custodyId; Authority = authority;
            Quantity = quantity; ItemKey = itemKey; IdentityKey = identityKey; Binding = binding;
        }
    }

    // Snapshots are detached; modules cannot edit the saved tables through this API.
    public sealed class EntitySnapshot
    {
        private readonly EntityRecord record;
        internal EntitySnapshot(EntityRecord source) { record = source.Copy(); }
        public string Id { get { return record.id; } }
        public string Kind { get { return record.kind; } }
        public string Provider { get { return record.provider; } }
        public string Name { get { return record.name; } }
        public string CultureKey { get { return record.culture; } }
        public string ControllerId { get { return record.controller; } }
        public string OwnerId { get { return record.owner; } }
        public string CommunityId { get { return record.community; } }
        public string AffiliationId { get { return record.affiliation; } }
        public string CustodyId { get { return record.custody; } }
        public string ItemKey { get { return record.item; } }
        public string IdentityKey { get { return record.identity; } }
        public string LineageId { get { return record.lineage; } }
        public long Quantity { get { return record.quantity; } }
        public long Revision { get { return record.revision; } }
        public EntityStatus Status { get { return record.status; } }
        public SimulationAuthority Authority { get { return record.authority; } }
        public string BoundThingLoadId { get { return record.binding == null ? null : record.binding.GetUniqueLoadID(); } }
        public int ControllerChangeCount { get { return record.controllerHistory.Count; } }
        public IEnumerable<ControllerChangeSnapshot> ControllerHistory { get { return record.controllerHistory.ConvertAll(x => new ControllerChangeSnapshot(x)).ToArray(); } }
    }

    public sealed class ControllerChangeSnapshot
    {
        public readonly string OldControllerId, NewControllerId, OperationId;
        public readonly long Tick;
        internal ControllerChangeSnapshot(ControllerChange c)
        { OldControllerId = c.oldController; NewControllerId = c.newController; OperationId = c.operation; Tick = c.tick; }
    }

    public sealed class OperationSnapshot
    {
        public readonly string Id, Kind, CauseId, ResultId, Actor, SubjectId, DestinationId;
        public readonly long Quantity, ExpectedRevision;
        public readonly OperationStage Stage;
        public readonly int RecoveryCount;
        internal OperationSnapshot(OperationRecord r)
        { Id = r.id; Kind = r.kind; CauseId = r.cause; ResultId = r.result; Actor = r.actor; SubjectId = r.subject; DestinationId = r.destination;
            Quantity = r.quantity; ExpectedRevision = r.expectedRevision; Stage = r.stage; RecoveryCount = r.recoveryCount; }
    }
    public sealed class ReservationSnapshot
    {
        public readonly string Id, LotId, CustodyId;
        public readonly long Quantity;
        public readonly ReservationStatus Status;
        internal ReservationSnapshot(ReservationRecord r) { Id = r.id; LotId = r.lot; CustodyId = r.custody; Quantity = r.quantity; Status = r.status; }
    }

    internal sealed class RevisionStamp : IExposable
    {
        internal string id;
        internal long revision;
        internal string fingerprint;
        public RevisionStamp() { }
        internal RevisionStamp(string id, long revision, string fingerprint) { this.id = id; this.revision = revision; this.fingerprint = fingerprint; }
        internal RevisionStamp Copy() { return new RevisionStamp(id, revision, fingerprint); }
        public void ExposeData() { Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref revision, "revision", -1L, true); Scribe_Values.Look(ref fingerprint, "fingerprint"); }
    }
    internal sealed class ControllerChange : IExposable
    {
        internal string oldController, newController, operation;
        internal long tick;
        public ControllerChange() { }
        internal ControllerChange Copy() { return (ControllerChange)MemberwiseClone(); }
        public void ExposeData()
        {
            Scribe_Values.Look(ref oldController, "oldController"); Scribe_Values.Look(ref newController, "newController");
            Scribe_Values.Look(ref operation, "operation"); Scribe_Values.Look(ref tick, "tick", 0L, true);
        }
    }
    internal sealed class EntityRecord : IExposable
    {
        internal string id, kind, provider, name, culture, controller, owner, community, affiliation, custody, item, identity, lineage;
        internal long quantity, revision;
        internal EntityStatus status;
        internal SimulationAuthority authority;
        internal Thing binding;
        internal List<ControllerChange> controllerHistory = new List<ControllerChange>();
        public EntityRecord() { }
        internal EntityRecord Copy()
        {
            var r = (EntityRecord)MemberwiseClone();
            r.controllerHistory = controllerHistory.ConvertAll(x => x.Copy()); return r;
        }
        internal void ApplySpec(EntitySpec s)
        {
            name = s.Name; culture = s.CultureKey; controller = s.ControllerId; owner = s.OwnerId;
            community = s.CommunityId; affiliation = s.AffiliationId; custody = s.CustodyId;
            authority = s.Authority; quantity = s.Quantity; item = s.ItemKey; identity = s.IdentityKey; binding = s.Binding;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref provider, "provider");
            Scribe_Values.Look(ref name, "name"); Scribe_Values.Look(ref culture, "culture"); Scribe_Values.Look(ref controller, "controller");
            Scribe_Values.Look(ref owner, "owner"); Scribe_Values.Look(ref community, "community"); Scribe_Values.Look(ref affiliation, "affiliation");
            Scribe_Values.Look(ref custody, "custody"); Scribe_Values.Look(ref item, "item"); Scribe_Values.Look(ref identity, "identity");
            Scribe_Values.Look(ref lineage, "lineage"); Scribe_Values.Look(ref quantity, "quantity", -1L, true);
            Scribe_Values.Look(ref revision, "revision", -1L, true); Scribe_Values.Look(ref status, "status", (EntityStatus)(-1), true);
            Scribe_Values.Look(ref authority, "authority", (SimulationAuthority)(-1), true);
            Scribe_References.Look(ref binding, "binding");
            Scribe_Collections.Look(ref controllerHistory, "controllerHistory", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && controllerHistory == null) controllerHistory = new List<ControllerChange>();
        }
    }
    internal sealed class ReservationRecord : IExposable
    {
        internal string id, lot, custody, createdBy, closedBy;
        internal long quantity;
        internal ReservationStatus status;
        public ReservationRecord() { }
        internal ReservationRecord Copy() { return (ReservationRecord)MemberwiseClone(); }
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref lot, "lot"); Scribe_Values.Look(ref custody, "custody");
            Scribe_Values.Look(ref createdBy, "createdBy"); Scribe_Values.Look(ref closedBy, "closedBy");
            Scribe_Values.Look(ref quantity, "quantity", -1L, true); Scribe_Values.Look(ref status, "status", (ReservationStatus)(-1), true);
        }
    }
    internal sealed class OperationRecord : IExposable
    {
        internal string id, actor, cause, kind, subject, destination, result, request, digest;
        internal long quantity, expectedRevision, tick;
        internal OperationStage stage;
        internal int recoveryCount;
        internal EntityRecord specification;
        internal List<RevisionStamp> before = new List<RevisionStamp>();
        public OperationRecord() { }
        internal OperationRecord Copy()
        {
            var r = (OperationRecord)MemberwiseClone();
            r.before = before.ConvertAll(x => x.Copy()); r.specification = specification == null ? null : specification.Copy(); return r;
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref actor, "actor"); Scribe_Values.Look(ref cause, "cause");
            Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref subject, "subject"); Scribe_Values.Look(ref destination, "destination");
            Scribe_Values.Look(ref result, "result"); Scribe_Values.Look(ref request, "request"); Scribe_Values.Look(ref digest, "digest");
            Scribe_Values.Look(ref quantity, "quantity", 0L, true); Scribe_Values.Look(ref expectedRevision, "expectedRevision", 0L, true);
            Scribe_Values.Look(ref tick, "tick", -1L, true); Scribe_Values.Look(ref stage, "stage", (OperationStage)(-1), true);
            Scribe_Values.Look(ref recoveryCount, "recoveryCount", -1, true);
            Scribe_Deep.Look(ref specification, "specification"); Scribe_Collections.Look(ref before, "before", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && before == null) before = new List<RevisionStamp>();
        }
    }
    internal sealed class LedgerState : IExposable
    {
        internal int formatVersion = 1;
        internal List<EntityRecord> entities = new List<EntityRecord>();
        internal List<ReservationRecord> reservations = new List<ReservationRecord>();
        internal List<OperationRecord> operations = new List<OperationRecord>();
        private int entityCount, reservationCount, operationCount;
        internal bool uncheckedLoad;
        public LedgerState() { }
        internal LedgerState Copy()
        {
            return new LedgerState { formatVersion = formatVersion, entities = entities.ConvertAll(x => x.Copy()),
                reservations = reservations.ConvertAll(x => x.Copy()), operations = operations.ConvertAll(x => x.Copy()) };
        }
        internal void VerifyCounts()
        {
            if (uncheckedLoad && (entityCount != entities.Count || reservationCount != reservations.Count || operationCount != operations.Count))
                throw new InvalidOperationException("Ledger record count mismatch");
            uncheckedLoad = false;
        }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) { entityCount = entities.Count; reservationCount = reservations.Count; operationCount = operations.Count; }
            if (Scribe.mode == LoadSaveMode.LoadingVars) uncheckedLoad = true;
            Scribe_Values.Look(ref formatVersion, "formatVersion", -1, true);
            Scribe_Values.Look(ref entityCount, "entityCount", -1, true); Scribe_Values.Look(ref reservationCount, "reservationCount", -1, true);
            Scribe_Values.Look(ref operationCount, "operationCount", -1, true);
            Scribe_Collections.Look(ref entities, "entities", LookMode.Deep); Scribe_Collections.Look(ref reservations, "reservations", LookMode.Deep);
            Scribe_Collections.Look(ref operations, "operations", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (entities == null) entities = new List<EntityRecord>(); if (reservations == null) reservations = new List<ReservationRecord>();
                if (operations == null) operations = new List<OperationRecord>();
            }
        }
    }
}
