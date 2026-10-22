using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Verse;

namespace Inheritance.Core
{
    // T0 only: active responsibility, with no native transfer or delivery stage.
    public enum RationSupplyTransferStage { Armed, Cancelled }

    public sealed class RationSupplyTransferSnapshot
    {
        public readonly string Id,Actor,Cause,ScopeId,IntentId,IntentDigest;
        public readonly string EntityId,NativeId,CityId,StoreTransferId,CaravanId,SourceNativeId,SourceEntityId,SourceKind,FoodNativeId,DefName,StuffDef;
        public readonly string ArmOperationId,DisarmCause,DisarmOperationId,DisarmReceiptId;
        public readonly int Tile,WholeCount,HitPoints,ArmedTick;
        public readonly float UnitNutrition,DailyDemand;
        public readonly RationSupplyTransferStage Stage;
        public readonly OperationStage ArmProofStage;
        public readonly OperationStage? DisarmProofStage;
        internal RationSupplyTransferSnapshot(RationSupplyTransferRecord r,OperationSnapshot arm,OperationSnapshot disarm)
        {
            Id=r.id; Actor=r.actor; Cause=r.cause; ScopeId=r.scope; IntentId=r.intent; IntentDigest=r.intentDigest;
            EntityId=r.entity; NativeId=r.native; CityId=r.city; StoreTransferId=r.store; CaravanId=r.caravan;
            SourceNativeId=r.source; SourceEntityId=r.sourceEntity; SourceKind=r.sourceKind; FoodNativeId=r.food; DefName=r.def; StuffDef=r.stuff;
            ArmOperationId=r.armOp; DisarmCause=r.disarmCause; DisarmOperationId=r.disarmOp; DisarmReceiptId=r.disarmReceipt;
            Tile=r.tile; WholeCount=r.count; HitPoints=r.hp; ArmedTick=r.tick; UnitNutrition=r.nutrition; DailyDemand=r.demand;
            Stage=r.stage; ArmProofStage=arm.Stage; DisarmProofStage=disarm == null ? (OperationStage?)null : disarm.Stage;
        }
    }
    internal sealed class RationSupplyTransferRecord : IExposable
    {
        internal string id,actor,cause,scope,intent,intentDigest,entity,native,city,store,caravan,source,sourceEntity,sourceKind,food,def,stuff,digest;
        internal string armOp,disarmCause,disarmOp,disarmReceipt;
        internal int tile=-1,count=-1,hp=-1,tick=-1;
        internal float nutrition=-1f,demand=-1f;
        internal RationSupplyTransferStage stage;
        internal bool invalidXml;
        public RationSupplyTransferRecord() { }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                invalidXml=RationSupplyIntents.InvalidXml(new[] {"id","actor","cause","scope","intent","intentDigest","entity","native","city","store","caravan",
                    "source","sourceEntity","sourceKind","food","def","stuff","digest","armOp","disarmCause","disarmOp","disarmReceipt","tile","count","hp","tick","nutrition","demand","stage"});
            Scribe_Values.Look(ref id,"id"); Scribe_Values.Look(ref actor,"actor"); Scribe_Values.Look(ref cause,"cause"); Scribe_Values.Look(ref scope,"scope");
            Scribe_Values.Look(ref intent,"intent"); Scribe_Values.Look(ref intentDigest,"intentDigest"); Scribe_Values.Look(ref entity,"entity"); Scribe_Values.Look(ref native,"native");
            Scribe_Values.Look(ref city,"city"); Scribe_Values.Look(ref store,"store"); Scribe_Values.Look(ref caravan,"caravan");
            Scribe_Values.Look(ref source,"source"); Scribe_Values.Look(ref sourceEntity,"sourceEntity"); Scribe_Values.Look(ref sourceKind,"sourceKind");
            Scribe_Values.Look(ref food,"food"); Scribe_Values.Look(ref def,"def"); Scribe_Values.Look(ref stuff,"stuff"); Scribe_Values.Look(ref digest,"digest");
            Scribe_Values.Look(ref armOp,"armOp"); Scribe_Values.Look(ref disarmCause,"disarmCause"); Scribe_Values.Look(ref disarmOp,"disarmOp"); Scribe_Values.Look(ref disarmReceipt,"disarmReceipt");
            Scribe_Values.Look(ref tile,"tile",-1,true); Scribe_Values.Look(ref count,"count",-1,true); Scribe_Values.Look(ref hp,"hp",-1,true); Scribe_Values.Look(ref tick,"tick",-1,true);
            Scribe_Values.Look(ref nutrition,"nutrition",-1f,true); Scribe_Values.Look(ref demand,"demand",-1f,true);
            Scribe_Values.Look(ref stage,"stage",(RationSupplyTransferStage)(-1),true);
        }
        internal string Hash()
        {
            return RationSupplyIntents.Hash(new[] {"ration-supply-transfer-arm-v1",scope,actor,cause,intent,intentDigest,entity,native,city,store,caravan,
                tile.ToString(CultureInfo.InvariantCulture),source,sourceEntity,sourceKind,"PawnInventory",food,def,stuff,hp.ToString(CultureInfo.InvariantCulture),
                count.ToString(CultureInfo.InvariantCulture),tick.ToString(CultureInfo.InvariantCulture),nutrition.ToString("R",CultureInfo.InvariantCulture),demand.ToString("R",CultureInfo.InvariantCulture)});
        }
        internal string DisarmHash()
        { return RationSupplyIntents.Hash(new[] {"ration-supply-transfer-disarm-v1",scope,actor,disarmCause,id,digest,intent,intentDigest}); }
    }

    // No IThingHolder and no Thing/Pawn field/reference: T0 metadata only.
    public sealed class RationSupplyTransfers : IExposable
    {
        internal const string ArmPrefix="supply-transfer-arm/",DisarmPrefix="supply-transfer-disarm/";
        private readonly CoreWorldComponent owner;
        private List<RationSupplyTransferRecord> records=new List<RationSupplyTransferRecord>();
        private int format=1,recordCount;
        private bool writing,invalidXml;
        internal bool IsWriting { get { return writing; } }
        public RationSupplyTransfers(CoreWorldComponent owner) { this.owner=owner; }
        private CoreWorldComponent Core
        {
            get
            {
                if (owner == null || Find.World == null || Find.World.GetComponent<CoreWorldComponent>() != owner || !owner.IsReady || owner.Header.SchemaVersion != 9)
                    throw new InvalidOperationException("Supply transfer responsibility belongs to an unavailable Core9 world");
                return owner;
            }
        }
        private static bool Token(string value,int max)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= max && value == value.Trim() && !value.Any(char.IsControl); }
        private static bool Positive(float value) { return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value); }
        private CoreLedger Gate(string actor)
        {
            var core=Core; var ledger=core.Ledger; ledger.AssertWriteAccess(actor);
            if (writing || core.RationMutation || core.SupplyIntentsForValidation.IsWriting)
                throw new InvalidOperationException("Supply transfer responsibility is unavailable during another metadata or ration write");
            return ledger;
        }
        private RationSupplyTransferRecord FindRecord(string id,string actor)
        {
            var r=records.SingleOrDefault(record => record.id == id);
            if (r == null || r.actor != actor) throw new InvalidOperationException("Supply transfer responsibility is unavailable to this actor");
            return r;
        }
        public IEnumerable<RationSupplyTransferSnapshot> Snapshots { get { return ReadSnapshots(Core.Ledger); } }
        // Owner provides the bootstrap actor gate and validated candidate ledger.
        internal RationSupplyTransferSnapshot[] ReadSnapshots(CoreLedger candidate)
        { return records.Select(r => Snapshot(candidate,r)).ToArray(); }
        private RationSupplyTransferSnapshot Snapshot(CoreLedger ledger,RationSupplyTransferRecord r)
        {
            CheckSeals(ledger,r,false,false); CheckHistory(ledger,owner.Header,r);
            return new RationSupplyTransferSnapshot(r,ledger.FindOperation(r.actor,"create",ArmPrefix+r.cause),
                r.disarmCause == null ? null : ledger.FindOperation(r.actor,"create",DisarmPrefix+r.disarmCause));
        }
        // Pure raw-journal lock queries, all actors, with no load repair/freshness lookup.
        // A missing/malformed table throws rather than pretending no responsibility exists.
        private bool Active(string key,string except,Func<RationSupplyTransferRecord,string,bool> matches)
        {
            if (records == null || invalidXml || format != 1 || recordCount != records.Count ||
                records.Any(r => r == null || r.invalidXml || !Enum.IsDefined(typeof(RationSupplyTransferStage),r.stage)))
                throw new InvalidOperationException("Supply transfer responsibility table is unavailable");
            if (key == null) return false;
            return records.Any(r => r != null && r.stage == RationSupplyTransferStage.Armed && r.id != except && matches(r,key));
        }
        public bool HasActiveForIntent(string key,string exceptTransferId=null) { return Active(key,exceptTransferId,(r,k) => r.intent == k); }
        public bool HasActiveForCity(string key,string exceptTransferId=null) { return Active(key,exceptTransferId,(r,k) => r.city == k); }
        public bool HasActiveForEntity(string key,string exceptTransferId=null) { return Active(key,exceptTransferId,(r,k) => r.entity == k || r.sourceEntity == k); }
        public bool HasActiveForNative(string key,string exceptTransferId=null) { return Active(key,exceptTransferId,(r,k) => r.native == k || r.source == k || r.food == k); }
        public bool HasActiveForFood(string key,string exceptTransferId=null) { return Active(key,exceptTransferId,(r,k) => r.food == k); }
        // The CoreLedger guard may exempt only an exact persisted proof, never a
        // caller-supplied transfer ID or a matching cause prefix. This query is pure.
        internal string LockExemptionForProof(CoreLedger candidate,string operationId)
        {
            if (operationId == null) return null;
            if (candidate == null || records == null || invalidXml || format != 1 || recordCount != records.Count)
                throw new InvalidOperationException("Supply transfer responsibility table is unavailable for proof exemption");
            var record=records.SingleOrDefault(r => r != null && (r.armOp == operationId || r.disarmOp == operationId));
            if (record == null) return null;
            CheckSeals(candidate,record,false,false); CheckHistory(candidate,owner.Header,record);
            var arm=candidate.FindOperation(record.actor,"create",ArmPrefix+record.cause);
            var disarm=record.disarmCause == null ? null : candidate.FindOperation(record.actor,"create",DisarmPrefix+record.disarmCause);
            if (arm != null && arm.Id == operationId && arm.Id == record.armOp && arm.ResultId == record.id ||
                disarm != null && disarm.Id == operationId && disarm.Id == record.disarmOp && disarm.ResultId == record.disarmReceipt)
                return record.id;
            throw new InvalidOperationException("Supply transfer proof exemption disagrees with its exact persisted proof");
        }
        private bool CauseUsed(string actor,string cause) { return records.Any(r => r.actor == actor && (r.cause == cause || r.disarmCause == cause)); }
        private void CheckConflicts(RationSupplyIntentSnapshot intent)
        {
            if (HasActiveForIntent(intent.Id) || HasActiveForCity(intent.CityId) || HasActiveForEntity(intent.EntityId) ||
                HasActiveForEntity(intent.SourceEntityId) || HasActiveForNative(intent.NativeId) || HasActiveForNative(intent.SourceNativeId) || HasActiveForNative(intent.FoodNativeId))
                throw new InvalidOperationException("Supply transfer originals or city already have active transfer responsibility");
        }
        public RationSupplyTransferSnapshot Begin(string actor,string cause,string intentId)
        {
            var ledger=Gate(actor);
            if (!Token(cause,128)) throw new InvalidOperationException("Supply transfer responsibility requires a stable arm cause");
            var previous=records.SingleOrDefault(r => r.actor == actor && r.cause == cause);
            if (previous != null)
            {
                if (previous.intent != intentId) throw new InvalidOperationException("Supply arm cause was reused for a different historical intent");
                return Snapshot(ledger,previous); // No native inspection, new IDs or implicit proof resume.
            }
            if (CauseUsed(actor,cause) || ledger.FindOperation(actor,"create",ArmPrefix+cause) != null)
                throw new InvalidOperationException("Supply arm cause is already used or has an orphan proof");
            var core=Core;
            var status=core.SupplyIntents.Inspect(actor,intentId); var intent=status.Intent;
            if (intent.Stage != RationSupplyIntentStage.Prepared || intent.PrepareProofStage != OperationStage.Committed || intent.CancelCause != null || !status.IsCurrent)
                throw new InvalidOperationException("Supply transfer requires a committed uncancelled current original intent");
            CheckConflicts(intent);
            if (ledger.HasPendingCityReference(intent.CityId)) throw new InvalidOperationException("Supply city is locked before transfer responsibility allocation");
            if (Find.TickManager.TicksGame < 0) throw new InvalidOperationException("Supply transfer arm clock is invalid before allocation");
            string intentDigest=ledger.GetEntity(intent.Id).IdentityKey;
            var record=new RationSupplyTransferRecord {scope=core.Header.SaveScopeId,actor=actor,cause=cause,intent=intent.Id,intentDigest=intentDigest,
                entity=intent.EntityId,native=intent.NativeId,city=intent.CityId,store=intent.StoreTransferId,caravan=intent.CaravanId,source=intent.SourceNativeId,
                sourceEntity=intent.SourceEntityId,sourceKind=intent.SourceKind,food=intent.FoodNativeId,def=intent.DefName,stuff=intent.StuffDef,tile=intent.Tile,
                count=intent.WholeCount,hp=intent.HitPoints,nutrition=intent.UnitNutrition,demand=intent.DailyDemand,tick=Find.TickManager.TicksGame,stage=RationSupplyTransferStage.Armed};
            record.digest=record.Hash(); CheckHistory(ledger,core.Header,record);
            writing=true;
            try
            {
                // No allocator rollback is claimed between these two Core calls.
                string operation=ledger.NewOperationId(actor);
                var armed=ledger.PrepareCreate(operation,ArmPrefix+cause,new EntitySpec("lease",identityKey:record.digest,custodyId:record.city));
                record.id=armed.ResultId; record.armOp=armed.Id; records.Add(record); SynchronizeCounts();
                ledger.Commit(operation); // Corresponding Armed journal already exists if Commit fails.
                return Snapshot(ledger,record);
            }
            finally { writing=false; }
        }
        public RationSupplyTransferSnapshot CancelBeforeMove(string actor,string cancelCause,string id)
        {
            var ledger=Gate(actor); var record=FindRecord(id,actor); CheckSeals(ledger,record,false,false); CheckHistory(ledger,owner.Header,record);
            if (!Token(cancelCause,128)) throw new InvalidOperationException("Supply disarm requires a stable cause");
            if (record.disarmCause != null)
            {
                if (record.disarmCause != cancelCause) throw new InvalidOperationException("Supply transfer already has a different disarm cause");
                return Snapshot(ledger,record); // A pending disarm remains Armed until explicit ResumeProof.
            }
            if (ledger.FindOperation(actor,"create",ArmPrefix+record.cause).Stage != OperationStage.Committed)
                throw new InvalidOperationException("Supply arm proof must be explicitly resumed before disarm");
            if (CauseUsed(actor,cancelCause) || ledger.FindOperation(actor,"create",DisarmPrefix+cancelCause) != null)
                throw new InvalidOperationException("Supply disarm cause is already used or has an orphan proof");
            string identity=RationSupplyIntents.Hash(new[] {"ration-supply-transfer-disarm-v1",record.scope,actor,cancelCause,record.id,record.digest,record.intent,record.intentDigest});
            writing=true;
            try
            {
                string operation=ledger.NewOperationId(actor);
                // Reference-free: stale source/recipient and another city lock do not prevent cancellation.
                var disarm=ledger.PrepareCreate(operation,DisarmPrefix+cancelCause,new EntitySpec("lease",identityKey:identity));
                record.disarmCause=cancelCause; record.disarmOp=disarm.Id; record.disarmReceipt=disarm.ResultId;
                ledger.Commit(operation); record.stage=RationSupplyTransferStage.Cancelled;
                return Snapshot(ledger,record);
            }
            finally { writing=false; }
        }
        public RationSupplyTransferSnapshot ResumeProof(string actor,string id)
        {
            var ledger=Gate(actor); var r=FindRecord(id,actor); CheckSeals(ledger,r,true,true); CheckHistory(ledger,owner.Header,r);
            writing=true;
            try
            {
                ResumeExisting(ledger,ledger.FindOperation(actor,"create",ArmPrefix+r.cause));
                if (r.disarmCause != null)
                {
                    ResumeExisting(ledger,ledger.FindOperation(actor,"create",DisarmPrefix+r.disarmCause));
                    r.stage=RationSupplyTransferStage.Cancelled;
                }
                return Snapshot(ledger,r);
            }
            finally { writing=false; }
        }
        private static void ResumeExisting(CoreLedger ledger,OperationSnapshot op)
        {
            if (op.Stage == OperationStage.Committed) return;
            if (op.Stage != OperationStage.Prepared && op.Stage != OperationStage.Applying)
                throw new InvalidOperationException("Supply transfer proof needs Core precondition reconciliation before resume");
            ledger.Commit(op.Id); // Exact existing proof, no IDs and no native action.
        }
        private static OperationSnapshot CheckLease(CoreLedger ledger,string actor,string cause,string identity,string custody)
        {
            var op=ledger.FindOperation(actor,"create",cause);
            if (op == null) throw new InvalidOperationException("Supply transfer journal has no corresponding immutable Core proof");
            var saved=ledger.SavedState.operations.Single(item => item.id == op.Id); var spec=saved.specification; var lease=ledger.GetEntity(op.ResultId);
            bool committed=op.Stage == OperationStage.Committed;
            bool pending=op.Stage == OperationStage.Prepared || op.Stage == OperationStage.Applying || op.Stage == OperationStage.NeedsReconciliation;
            if ((!committed && !pending) || spec == null || spec.kind != "lease" || spec.identity != identity || spec.custody != custody ||
                spec.authority != SimulationAuthority.Abstract || spec.quantity != 0 || spec.name != null || spec.culture != null || spec.controller != null || spec.owner != null ||
                spec.community != null || spec.affiliation != null || spec.item != null || spec.binding != null || spec.lineage != null ||
                saved.subject != null || saved.destination != null || saved.quantity != 0 || saved.expectedRevision != 0 || saved.tick != 0 ||
                lease.Provider != actor || lease.Kind != "lease" || lease.Authority != SimulationAuthority.Abstract || lease.Quantity != 0 || lease.BoundThingLoadId != null ||
                lease.Name != null || lease.CultureKey != null || lease.ControllerId != null || lease.OwnerId != null || lease.CommunityId != null || lease.AffiliationId != null ||
                lease.ItemKey != null || lease.LineageId != null || lease.ControllerChangeCount != 0 ||
                (committed ? lease.Status != EntityStatus.Active || lease.CustodyId != custody || lease.IdentityKey != identity || lease.Revision != 1 :
                    lease.Status != EntityStatus.Allocated || lease.CustodyId != null || lease.IdentityKey != null || lease.Revision != 0))
                throw new InvalidOperationException("Supply transfer proof disagrees with its immutable request or result");
            return op;
        }
        private static void CheckSeals(CoreLedger ledger,RationSupplyTransferRecord r,bool repair,bool complete)
        {
            if (r == null || r.invalidXml || !Token(r.actor,128) || !Token(r.cause,128) || !Token(r.scope,64) || !Token(r.intent,256) ||
                r.intentDigest == null || r.intentDigest.Length != 64 || !Token(r.entity,256) || !Token(r.native,256) || !Token(r.city,256) || !Token(r.store,256) ||
                !Token(r.caravan,256) || !Token(r.source,256) || !Token(r.food,256) || r.source == r.native || r.food == r.source || r.food == r.native ||
                (r.sourceKind != "person" && r.sourceKind != "animal") || !Token(r.def,256) || r.stuff != null && !Token(r.stuff,256) ||
                r.tile < 0 || r.count <= 0 || r.hp <= 0 || r.tick < 0 || !Positive(r.nutrition) || !Positive(r.demand) ||
                !Enum.IsDefined(typeof(RationSupplyTransferStage),r.stage) || r.digest != r.Hash())
                throw new InvalidOperationException("Supply transfer fixed manifest is incomplete or invalid");
            var arm=CheckLease(ledger,r.actor,ArmPrefix+r.cause,r.digest,r.city);
            if (r.id != null && r.id != arm.ResultId || r.armOp != null && r.armOp != arm.Id || !repair && (r.id == null || r.armOp == null))
                throw new InvalidOperationException("Supply transfer arm proof refs are missing or contradictory");
            if (repair) { r.id=arm.ResultId; r.armOp=arm.Id; }
            if (r.disarmCause == null)
            {
                if (r.disarmOp != null || r.disarmReceipt != null || r.stage != RationSupplyTransferStage.Armed)
                    throw new InvalidOperationException("Supply transfer disarm metadata exists without its cause");
                return;
            }
            if (!Token(r.disarmCause,128) || r.disarmCause == r.cause || arm.Stage != OperationStage.Committed)
                throw new InvalidOperationException("Supply disarm requires its own cause and a committed arm proof");
            var disarm=CheckLease(ledger,r.actor,DisarmPrefix+r.disarmCause,r.DisarmHash(),null);
            if (r.disarmOp != null && r.disarmOp != disarm.Id || r.disarmReceipt != null && r.disarmReceipt != disarm.ResultId || !repair && (r.disarmOp == null || r.disarmReceipt == null))
                throw new InvalidOperationException("Supply transfer disarm proof refs are missing or contradictory");
            if (repair) { r.disarmOp=disarm.Id; r.disarmReceipt=disarm.ResultId; }
            if (r.stage == RationSupplyTransferStage.Cancelled && disarm.Stage != OperationStage.Committed)
                throw new InvalidOperationException("Cancelled transfer responsibility still has an unfinished disarm proof");
            if (disarm.Stage == OperationStage.Committed && r.stage != RationSupplyTransferStage.Cancelled)
            {
                if (!complete) throw new InvalidOperationException("Supply transfer stage needs explicit restoration of its committed disarm proof");
                r.stage=RationSupplyTransferStage.Cancelled;
            }
        }
        private void CheckHistory(CoreLedger ledger,SaveHeader header,RationSupplyTransferRecord r)
        {
            if (header.SchemaVersion != 9 || r.scope != header.SaveScopeId) throw new InvalidOperationException("Supply transfer responsibility belongs to another schema or scope");
            var intents=owner.SupplyIntentsForValidation.ReadSnapshots(ledger);
            var intent=intents.SingleOrDefault(item => item.Id == r.intent);
            if (intent == null || intent.Actor != r.actor || intent.ScopeId != r.scope || intent.PrepareProofStage != OperationStage.Committed ||
                ledger.GetEntity(intent.Id).IdentityKey != r.intentDigest || intent.EntityId != r.entity || intent.NativeId != r.native || intent.CityId != r.city ||
                intent.StoreTransferId != r.store || intent.CaravanId != r.caravan || intent.SourceNativeId != r.source || intent.SourceEntityId != r.sourceEntity ||
                intent.SourceKind != r.sourceKind || intent.FoodNativeId != r.food || intent.DefName != r.def || intent.StuffDef != r.stuff || intent.Tile != r.tile ||
                intent.WholeCount != r.count || intent.HitPoints != r.hp || intent.UnitNutrition != r.nutrition || intent.DailyDemand != r.demand ||
                r.stage == RationSupplyTransferStage.Armed && (intent.Stage != RationSupplyIntentStage.Prepared || intent.CancelCause != null))
                throw new InvalidOperationException("Supply transfer disagrees with its original committed historical intent");
            // All referenced Core identities must be in this allocator/scope. Intent's
            // historical validator already checks its immutable committed store proof.
            header.ParseId(r.intent,"lease");
            if (r.id != null) header.ParseId(r.id,"lease");
            if (r.armOp != null) header.ParseId(r.armOp,"operation");
            if (r.disarmOp != null) header.ParseId(r.disarmOp,"operation");
            if (r.disarmReceipt != null) header.ParseId(r.disarmReceipt,"lease");
            // Deliberately no current Pawn/food/holder/resident/caravan lookup here.
        }
        internal void Restore(CoreLedger ledger,SaveHeader header)
        {
            if (writing || records == null) throw new InvalidOperationException("Supply transfer journal cannot restore while unavailable");
            foreach (var r in records) { CheckSeals(ledger,r,true,true); CheckHistory(ledger,header,r); }
            Validate(ledger,header); // Explicit existing refs/stage repair only; no Commit or native move.
        }
        internal void Validate(CoreLedger ledger,SaveHeader header)
        {
            if (writing || invalidXml || format != 1 || records == null || recordCount != records.Count || header.SchemaVersion != 9)
                throw new InvalidOperationException("Mandatory Core9 supply transfer table is missing or invalid");
            var ids=new HashSet<string>(StringComparer.Ordinal); var causes=new HashSet<string>(StringComparer.Ordinal);
            var intents=new HashSet<string>(StringComparer.Ordinal); var cities=new HashSet<string>(StringComparer.Ordinal);
            var entities=new HashSet<string>(StringComparer.Ordinal); var native=new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in records)
            {
                CheckSeals(ledger,r,false,false); CheckHistory(ledger,header,r);
                if (!ids.Add(r.id) || !ids.Add(r.armOp) || !causes.Add(RationSupplyIntents.Hash(new[] {r.actor,r.cause})) || r.disarmCause != null &&
                    (!ids.Add(r.disarmReceipt) || !ids.Add(r.disarmOp) || !causes.Add(RationSupplyIntents.Hash(new[] {r.actor,r.disarmCause}))))
                    throw new InvalidOperationException("Supply transfer journal duplicates its proof IDs or actor/cause");
                // Multiple Cancelled histories for one intent are legal. Only active
                // responsibility excludes every actor and source/recipient role.
                if (r.stage == RationSupplyTransferStage.Armed && (!intents.Add(r.intent) || !cities.Add(r.city) || !entities.Add(r.entity) ||
                    r.sourceEntity != null && !entities.Add(r.sourceEntity) || !native.Add(r.native) || !native.Add(r.source) || !native.Add(r.food)))
                    throw new InvalidOperationException("Armed transfer responsibilities overlap on original assets or city");
            }
            foreach (var op in ledger.SavedState.operations.Where(item => item.cause.StartsWith(ArmPrefix,StringComparison.Ordinal)))
                if (op.kind != "create" || !records.Any(r => r.actor == op.actor && r.armOp == op.id && r.id == op.result && ArmPrefix+r.cause == op.cause))
                    throw new InvalidOperationException("Core arm proof is orphaned from its transfer responsibility journal");
            foreach (var op in ledger.SavedState.operations.Where(item => item.cause.StartsWith(DisarmPrefix,StringComparison.Ordinal)))
                if (op.kind != "create" || !records.Any(r => r.actor == op.actor && r.disarmOp == op.id && r.disarmReceipt == op.result && r.disarmCause != null && DisarmPrefix+r.disarmCause == op.cause))
                    throw new InvalidOperationException("Core disarm proof is orphaned from its transfer responsibility journal");
        }
        internal void SynchronizeCounts() { if (records == null) throw new InvalidOperationException("Supply transfer records are missing"); recordCount=records.Count; }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (writing) throw new InvalidOperationException("Cannot save inside supply transfer proof creation");
                SynchronizeCounts();
            }
            if (Scribe.mode == LoadSaveMode.LoadingVars) invalidXml=RationSupplyIntents.InvalidXml(new[] {"format","recordCount","records"});
            Scribe_Values.Look(ref format,"format",-1,true); Scribe_Values.Look(ref recordCount,"recordCount",-1,true);
            Scribe_Collections.Look(ref records,"records",LookMode.Deep);
            // Missing Core9 data stays missing; migration must explicitly seed legacy emptiness.
        }
    }
}
