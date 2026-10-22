using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Core
{
    public enum RationSupplyIntentStage { Prepared, Cancelled }

    // These are historical observations, never a reserved inventory or delivery receipt.
    public sealed class RationSupplyIntentSnapshot
    {
        public readonly string Id,Actor,Cause,ScopeId,EntityId,NativeId,CityId,StoreTransferId,CaravanId;
        public readonly string SourceNativeId,SourceEntityId,SourceKind,FoodNativeId,DefName,StuffDef;
        public readonly string PrepareOperationId,CancelCause,CancelOperationId,CancelReceiptId;
        public readonly int Tile,WholeCount,HitPoints,PreparedTick;
        public readonly float UnitNutrition,DailyDemand;
        public readonly RationSupplyIntentStage Stage;
        public readonly OperationStage PrepareProofStage;
        public readonly OperationStage? CancelProofStage;
        internal RationSupplyIntentSnapshot(RationSupplyIntentRecord r,OperationSnapshot prepare,OperationSnapshot cancel)
        {
            Id=r.id; Actor=r.actor; Cause=r.cause; ScopeId=r.scope; EntityId=r.entity; NativeId=r.native; CityId=r.city;
            StoreTransferId=r.store; CaravanId=r.caravan; SourceNativeId=r.source; SourceEntityId=r.sourceEntity; SourceKind=r.sourceKind;
            FoodNativeId=r.food; DefName=r.def; StuffDef=r.stuff; PrepareOperationId=r.prepareOp; CancelCause=r.cancelCause;
            CancelOperationId=r.cancelOp; CancelReceiptId=r.cancelReceipt; Tile=r.tile; WholeCount=r.count; HitPoints=r.hp;
            PreparedTick=r.tick; UnitNutrition=r.nutrition; DailyDemand=r.demand; Stage=r.stage;
            PrepareProofStage=prepare.Stage; CancelProofStage=cancel == null ? (OperationStage?)null : cancel.Stage;
        }
    }
    public sealed class RationSupplyIntentStatusSnapshot
    {
        public readonly RationSupplyIntentSnapshot Intent;
        public readonly string Problem;
        public readonly bool IsCurrent,CanCancel,CanResumeProof;
        internal RationSupplyIntentStatusSnapshot(RationSupplyIntentSnapshot intent,string problem)
        {
            Intent=intent; Problem=problem; IsCurrent=problem == "None";
            CanCancel=intent.Stage == RationSupplyIntentStage.Prepared && intent.PrepareProofStage == OperationStage.Committed && intent.CancelCause == null;
            CanResumeProof=intent.PrepareProofStage == OperationStage.Prepared || intent.PrepareProofStage == OperationStage.Applying ||
                intent.CancelProofStage == OperationStage.Prepared || intent.CancelProofStage == OperationStage.Applying;
        }
    }

    internal sealed class RationSupplyIntentRecord : IExposable
    {
        internal string id,actor,cause,scope,entity,native,city,store,caravan,source,sourceEntity,sourceKind,food,def,stuff,digest;
        internal string prepareOp,cancelCause,cancelOp,cancelReceipt;
        internal int tile=-1,count=-1,hp=-1,tick=-1;
        internal float nutrition=-1f,demand=-1f;
        internal RationSupplyIntentStage stage;
        internal bool invalidXml;
        public RationSupplyIntentRecord() { }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                invalidXml=RationSupplyIntents.InvalidXml(new[] {"id","actor","cause","scope","entity","native","city","store","caravan","source","sourceEntity","sourceKind","food","def","stuff","digest",
                    "prepareOp","cancelCause","cancelOp","cancelReceipt","tile","count","hp","tick","nutrition","demand","stage"});
            Scribe_Values.Look(ref id,"id"); Scribe_Values.Look(ref actor,"actor"); Scribe_Values.Look(ref cause,"cause");
            Scribe_Values.Look(ref scope,"scope"); Scribe_Values.Look(ref entity,"entity"); Scribe_Values.Look(ref native,"native");
            Scribe_Values.Look(ref city,"city"); Scribe_Values.Look(ref store,"store"); Scribe_Values.Look(ref caravan,"caravan");
            Scribe_Values.Look(ref source,"source"); Scribe_Values.Look(ref sourceEntity,"sourceEntity"); Scribe_Values.Look(ref sourceKind,"sourceKind");
            Scribe_Values.Look(ref food,"food"); Scribe_Values.Look(ref def,"def"); Scribe_Values.Look(ref stuff,"stuff"); Scribe_Values.Look(ref digest,"digest");
            Scribe_Values.Look(ref prepareOp,"prepareOp"); Scribe_Values.Look(ref cancelCause,"cancelCause");
            Scribe_Values.Look(ref cancelOp,"cancelOp"); Scribe_Values.Look(ref cancelReceipt,"cancelReceipt");
            Scribe_Values.Look(ref tile,"tile",-1,true); Scribe_Values.Look(ref count,"count",-1,true);
            Scribe_Values.Look(ref hp,"hp",-1,true); Scribe_Values.Look(ref tick,"tick",-1,true);
            Scribe_Values.Look(ref nutrition,"nutrition",-1f,true); Scribe_Values.Look(ref demand,"demand",-1f,true);
            Scribe_Values.Look(ref stage,"stage",(RationSupplyIntentStage)(-1),true);
        }
        internal string Hash()
        {
            return RationSupplyIntents.Hash(new[] {"ration-supply-intent-v1",scope,actor,cause,entity,native,city,store,caravan,
                tile.ToString(CultureInfo.InvariantCulture),source,sourceEntity,sourceKind,"PawnInventory",food,def,stuff,
                hp.ToString(CultureInfo.InvariantCulture),count.ToString(CultureInfo.InvariantCulture),tick.ToString(CultureInfo.InvariantCulture),
                nutrition.ToString("R",CultureInfo.InvariantCulture),demand.ToString("R",CultureInfo.InvariantCulture)});
        }
        internal string CancelHash()
        { return RationSupplyIntents.Hash(new[] {"ration-supply-cancel-v1",scope,actor,cancelCause,id,digest}); }
    }

    // Core8 metadata only. The journal never owns or references a native Thing for Scribe.
    public sealed class RationSupplyIntents : IExposable
    {
        internal const string PreparePrefix="supply-intent/",CancelPrefix="supply-cancel/";
        private readonly CoreWorldComponent owner;
        private List<RationSupplyIntentRecord> records=new List<RationSupplyIntentRecord>();
        private int format=1,recordCount;
        private bool writing,invalidXml;
        internal bool IsWriting { get { return writing; } }
        public RationSupplyIntents(CoreWorldComponent owner) { this.owner=owner; }
        private CoreWorldComponent Core
        {
            get
            {
                if (owner == null || Find.World == null || Find.World.GetComponent<CoreWorldComponent>() != owner || !owner.IsReady || owner.Header.SchemaVersion < 8)
                    throw new InvalidOperationException("Supply intent belongs to an unavailable Core8 world");
                return owner;
            }
        }
        internal static string Hash(IEnumerable<string> fields)
        {
            var text=new StringBuilder();
            foreach (string field in fields) text.Append(field == null ? "-1:" : field.Length.ToString(CultureInfo.InvariantCulture)+":"+field);
            using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","").ToLowerInvariant();
        }
        internal static bool InvalidXml(IEnumerable<string> names)
        {
            var allowed=new HashSet<string>(names,StringComparer.Ordinal); var seen=new HashSet<string>(StringComparer.Ordinal);
            var node=Scribe.loader.curXmlParent;
            return node == null || node.ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element)
                .Any(n => !allowed.Contains(n.Name) || !seen.Add(n.Name));
        }
        private static bool Positive(float value) { return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Token(string value,int max)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= max && value == value.Trim() && !value.Any(char.IsControl); }
        private static bool NativeId(string value,string prefix)
        { return value != null && value.StartsWith(prefix,StringComparison.Ordinal) && value.Length > prefix.Length && value.Length <= 256 && !value.Any(char.IsControl); }
        private CoreLedger Gate(string actor)
        {
            var core=Core; var ledger=core.Ledger; ledger.AssertWriteAccess(actor);
            if (writing || core.RationMutation) throw new InvalidOperationException("Supply intent is unavailable during another write");
            return ledger;
        }
        private RationSupplyIntentRecord FindRecord(string id,string actor)
        {
            var record=records.SingleOrDefault(r => r.id == id);
            if (record == null || record.actor != actor) throw new InvalidOperationException("Supply intent is unavailable to this actor");
            return record;
        }
        public IEnumerable<RationSupplyIntentSnapshot> Snapshots
        {
            get { return ReadSnapshots(Core.Ledger); }
        }
        // The Core owner gates this for its own bootstrap actor and candidate ledger.
        // Native/current-status APIs continue to require a ready active Core8 world.
        internal RationSupplyIntentSnapshot[] ReadSnapshots(CoreLedger candidate)
        { return records.Select(r => Snapshot(candidate,r)).ToArray(); }
        private RationSupplyIntentSnapshot Snapshot(CoreLedger ledger,RationSupplyIntentRecord r)
        {
            CheckSeals(ledger,r,false,false);
            CheckHistory(owner,ledger,owner.Header,r);
            return new RationSupplyIntentSnapshot(r,ledger.FindOperation(r.actor,"create",PreparePrefix+r.cause),
                r.cancelCause == null ? null : ledger.FindOperation(r.actor,"create",CancelPrefix+r.cancelCause));
        }
        private bool CauseUsed(string actor,string cause)
        { return records.Any(r => r.actor == actor && (r.cause == cause || r.cancelCause == cause)); }
        private void CheckOverlap(string entity,string recipient,string source,string food)
        {
            if (records.Any(r => r.stage == RationSupplyIntentStage.Prepared && (r.entity == entity || r.source == source || r.food == food ||
                r.native == source || r.source == recipient)))
                throw new InvalidOperationException("An uncancelled supply intent already names this original individual or food");
        }
        private static bool SourceEligible(Pawn pawn,Caravan caravan)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead && !pawn.Spawned && pawn.Faction == Faction.OfPlayer && !pawn.Downed && !pawn.Drafted && !pawn.InMentalState &&
                (pawn.RaceProps.Animal || pawn.IsColonist && !pawn.DevelopmentalStage.Baby()) && (pawn.carryTracker == null || pawn.carryTracker.CarriedThing == null) &&
                pawn.holdingOwner == caravan.GetDirectlyHeldThings() && caravan.PawnsListForReading.Contains(pawn) && Find.WorldPawns.Contains(pawn) &&
                Find.WorldObjects.Caravans.Count(c => c.PawnsListForReading.Contains(pawn)) == 1;
        }
        private static string SourceBinding(CoreLedger ledger,string actor,Pawn source)
        {
            var rows=ledger.Entities.Where(e => e.BoundThingLoadId == source.GetUniqueLoadID()).ToArray();
            if (rows.Length > 1) throw new InvalidOperationException("Source individual has repeated permanent bindings");
            if (rows.Length == 0) return null; // Do not register an individual for an intent.
            var binding=rows[0];
            if (binding.Provider != actor || binding.Status != EntityStatus.Active || binding.Authority != SimulationAuthority.Physical || binding.Kind != (source.RaceProps.Animal ? "animal" : "person"))
                throw new InvalidOperationException("Source individual belongs to another provider or invalid binding");
            return binding.Id;
        }
        private static void CheckCaravan(Caravan caravan,PawnCustodyResidentSnapshot resident)
        {
            if (caravan == null || caravan.Destroyed || !caravan.Spawned || !Find.WorldObjects.Contains(caravan) || !caravan.IsPlayerControlled || caravan.Faction != Faction.OfPlayer ||
                caravan.GetUniqueLoadID() != resident.CaravanId || !caravan.Tile.Valid || caravan.Tile != new PlanetTile(resident.Tile,0) || caravan.pather == null || caravan.pather.Moving)
                throw new InvalidOperationException("Supply intent requires the stopped original caravan at the saved surface site");
        }
        public RationSupplyIntentSnapshot Prepare(string actor,string cause,string entity,string city,Caravan caravan,Pawn sourcePawn,Thing food,int wholeCount)
        {
            var ledger=Gate(actor);
            if (!Token(cause,128) || caravan == null || sourcePawn == null || food == null || wholeCount <= 0)
                throw new InvalidOperationException("Supply intent requires a stable cause and an explicit whole-stack request");
            string source=sourcePawn.GetUniqueLoadID(),foodId=food.GetUniqueLoadID(),anchor=caravan.GetUniqueLoadID();
            var previous=records.SingleOrDefault(item => item.actor == actor && item.cause == cause);
            if (previous != null)
            {
                if (previous.entity != entity || previous.city != city || previous.caravan != anchor || previous.source != source || previous.food != foodId || previous.count != wholeCount)
                    throw new InvalidOperationException("Supply cause was reused for another immutable request");
                return Snapshot(ledger,previous); // No freshness check, allocation, commit or metadata update.
            }
            if (CauseUsed(actor,cause) || ledger.FindOperation(actor,"create",PreparePrefix+cause) != null)
                throw new InvalidOperationException("Supply cause is already used or has an orphan proof");
            var core=Core;
            var resident=core.PawnCustody.Residents.SingleOrDefault(item => item.EntityId == entity && item.CityId == city);
            if (resident == null || resident.CaravanId == null) throw new InvalidOperationException("Supply recipient has no original delegated caravan store");
            var target=ledger.GetEntity(entity); var cityRow=ledger.GetEntity(city);
            if (target.Provider != actor || cityRow.Provider != actor || cityRow.Kind != "city" || cityRow.Status != EntityStatus.Active)
                throw new InvalidOperationException("Supply target and city must belong to the actor");
            if (ledger.HasPendingReference(city)) throw new InvalidOperationException("Supply city is locked before intent allocation");
            CheckCaravan(caravan,resident);
            if (!SourceEligible(sourcePawn,caravan) || food.stackCount != wholeCount || food.HitPoints <= 0)
                throw new InvalidOperationException("Supply source must be the original caravan member and entire unchanged native stack");
            string sourceEntity=SourceBinding(ledger,actor,sourcePawn);
            if (core.PawnCustody.Transfers.Any(t => t.Stage != PawnCustodyStage.Committed && (t.EntityId == entity || t.NativeId == source)) ||
                core.Rations.HasPending(entity) || sourceEntity != null && core.Rations.HasPending(sourceEntity))
                throw new InvalidOperationException("Supply originals have a pending custody or ration operation");
            var quote=core.Rations.QuoteSupplyCandidate(actor,entity,city,sourcePawn,food);
            if (!quote.CanPreview || quote.Recipient == null || !Positive(quote.UnitNutrition) || !Positive(quote.DailyDemand))
                throw new InvalidOperationException("Supply request has no eligible native food quote");
            core.AssertNoActiveSupplyTransfer(city,entity,resident.NativeId,foodId);
            core.AssertNoActiveSupplyTransfer(nativeId:source);
            CheckOverlap(entity,resident.NativeId,source,foodId);
            var r=new RationSupplyIntentRecord {scope=core.Header.SaveScopeId,actor=actor,cause=cause,entity=entity,native=resident.NativeId,city=city,store=resident.StoreTransferId,
                caravan=anchor,tile=resident.Tile,source=source,sourceEntity=sourceEntity,sourceKind=sourcePawn.RaceProps.Animal ? "animal" : "person",food=foodId,
                def=food.def.defName,stuff=food.Stuff == null ? null : food.Stuff.defName,hp=food.HitPoints,count=wholeCount,tick=Find.TickManager.TicksGame,
                nutrition=quote.UnitNutrition,demand=quote.DailyDemand,stage=RationSupplyIntentStage.Prepared};
            r.digest=r.Hash();
            writing=true;
            try
            {
                // The NewOperationId -> PrepareCreate failure window is not rollbackable.
                // All normal refusal conditions above precede allocation; see IntegrationNotes.
                string op=ledger.NewOperationId(actor);
                var prepared=ledger.PrepareCreate(op,PreparePrefix+cause,new EntitySpec("lease",identityKey:r.digest,custodyId:city));
                r.id=prepared.ResultId; r.prepareOp=prepared.Id; records.Add(r); SynchronizeCounts();
                ledger.Commit(op); // A failure leaves the same saveable Prepared proof + journal.
                return Snapshot(ledger,r);
            }
            finally { writing=false; }
        }
        public RationSupplyIntentSnapshot Cancel(string actor,string cancelCause,string id)
        {
            var ledger=Gate(actor); var r=FindRecord(id,actor); CheckSeals(ledger,r,false,false); CheckHistory(owner,ledger,owner.Header,r);
            if (!Token(cancelCause,128)) throw new InvalidOperationException("Supply cancellation requires a stable cause");
            if (r.cancelCause != null)
            {
                if (r.cancelCause != cancelCause) throw new InvalidOperationException("Supply intent already has a different cancellation cause");
                return Snapshot(ledger,r); // Checkpoint replay does not commit; ResumeProof is explicit.
            }
            var first=ledger.FindOperation(actor,"create",PreparePrefix+r.cause);
            owner.AssertNoActiveSupplyTransfer(intentId:r.id); if (first.Stage != OperationStage.Committed) throw new InvalidOperationException("Supply first proof must be explicitly resumed before cancellation");
            if (CauseUsed(actor,cancelCause) || ledger.FindOperation(actor,"create",CancelPrefix+cancelCause) != null)
                throw new InvalidOperationException("Supply cancellation cause is already used or has an orphan proof");
            // Never inspect current recipient/source/food/caravan or stamp the city.
            string identity=Hash(new[] {"ration-supply-cancel-v1",r.scope,actor,cancelCause,r.id,r.digest});
            writing=true;
            try
            {
                string op=ledger.NewOperationId(actor);
                var prepared=ledger.PrepareCreate(op,CancelPrefix+cancelCause,new EntitySpec("lease",identityKey:identity));
                r.cancelCause=cancelCause; r.cancelOp=prepared.Id; r.cancelReceipt=prepared.ResultId;
                ledger.Commit(op); // The fixed cancel refs survive a commit failure.
                r.stage=RationSupplyIntentStage.Cancelled;
                return Snapshot(ledger,r);
            }
            finally { writing=false; }
        }
        public RationSupplyIntentSnapshot ResumeProof(string actor,string id)
        {
            var ledger=Gate(actor); var r=FindRecord(id,actor); CheckSeals(ledger,r,true,true); CheckHistory(owner,ledger,owner.Header,r);
            writing=true;
            try
            {
                var first=ledger.FindOperation(actor,"create",PreparePrefix+r.cause);
                ResumeExisting(ledger,first);
                if (r.cancelCause != null)
                {
                    owner.AssertNoActiveSupplyTransfer(intentId:r.id);
                    ResumeExisting(ledger,ledger.FindOperation(actor,"create",CancelPrefix+r.cancelCause));
                    r.stage=RationSupplyIntentStage.Cancelled;
                }
                return Snapshot(ledger,r);
            }
            finally { writing=false; }
        }
        private static void ResumeExisting(CoreLedger ledger,OperationSnapshot operation)
        {
            if (operation.Stage == OperationStage.Committed) return;
            if (operation.Stage != OperationStage.Prepared && operation.Stage != OperationStage.Applying)
                throw new InvalidOperationException("Supply proof needs Core precondition reconciliation before explicit resume");
            ledger.Commit(operation.Id); // No NewOperationId and no native action.
        }
        private static OperationSnapshot CheckLease(CoreLedger ledger,string actor,string cause,string expectedIdentity,string custody)
        {
            var op=ledger.FindOperation(actor,"create",cause);
            if (op == null) throw new InvalidOperationException("Supply journal has no corresponding sealed Core proof");
            var saved=ledger.SavedState.operations.Single(o => o.id == op.Id); var spec=saved.specification; var result=ledger.GetEntity(op.ResultId);
            bool committed=op.Stage == OperationStage.Committed;
            bool pending=op.Stage == OperationStage.Prepared || op.Stage == OperationStage.Applying || op.Stage == OperationStage.NeedsReconciliation;
            if ((!committed && !pending) || spec == null || spec.kind != "lease" || spec.identity != expectedIdentity || spec.custody != custody ||
                spec.authority != SimulationAuthority.Abstract || spec.quantity != 0 || spec.name != null || spec.culture != null || spec.controller != null ||
                spec.owner != null || spec.community != null || spec.affiliation != null || spec.item != null || spec.binding != null || spec.lineage != null ||
                saved.subject != null || saved.destination != null || saved.quantity != 0 || saved.expectedRevision != 0 || saved.tick != 0 ||
                result.Provider != actor || result.Kind != "lease" || result.Authority != SimulationAuthority.Abstract || result.Quantity != 0 || result.BoundThingLoadId != null ||
                result.Name != null || result.CultureKey != null || result.ControllerId != null || result.OwnerId != null || result.CommunityId != null || result.AffiliationId != null ||
                result.ItemKey != null || result.LineageId != null || result.ControllerChangeCount != 0 ||
                (committed ? result.Status != EntityStatus.Active || result.CustodyId != custody || result.IdentityKey != expectedIdentity || result.Revision != 1 :
                    result.Status != EntityStatus.Allocated || result.Revision != 0 || result.CustodyId != null || result.IdentityKey != null))
                throw new InvalidOperationException("Supply proof disagrees with its immutable lease specification or result");
            return op;
        }
        private static void CheckSeals(CoreLedger ledger,RationSupplyIntentRecord r,bool repair,bool completeStage)
        {
            if (r == null || r.invalidXml || !Token(r.actor,128) || !Token(r.cause,128) || string.IsNullOrEmpty(r.scope) || string.IsNullOrEmpty(r.entity) ||
                !NativeId(r.native,"Thing_") || string.IsNullOrEmpty(r.city) || string.IsNullOrEmpty(r.store) || !NativeId(r.caravan,"WorldObject_") ||
                !NativeId(r.source,"Thing_") || !NativeId(r.food,"Thing_") || r.source == r.native || r.food == r.source || r.food == r.native ||
                (r.sourceKind != "person" && r.sourceKind != "animal") || !Token(r.def,256) || r.stuff != null && !Token(r.stuff,256) ||
                r.tile < 0 || r.count <= 0 || r.hp <= 0 || r.tick < 0 || !Positive(r.nutrition) || !Positive(r.demand) ||
                !Enum.IsDefined(typeof(RationSupplyIntentStage),r.stage) || r.digest != r.Hash())
                throw new InvalidOperationException("Supply immutable manifest is incomplete or invalid");
            var first=CheckLease(ledger,r.actor,PreparePrefix+r.cause,r.digest,r.city);
            if (r.id != null && r.id != first.ResultId || r.prepareOp != null && r.prepareOp != first.Id || !repair && (r.id == null || r.prepareOp == null))
                throw new InvalidOperationException("Supply first proof references are missing or contradictory");
            if (repair) { r.id=first.ResultId; r.prepareOp=first.Id; }
            if (r.cancelCause == null)
            {
                if (r.cancelOp != null || r.cancelReceipt != null || r.stage != RationSupplyIntentStage.Prepared)
                    throw new InvalidOperationException("Supply cancellation metadata exists without its cause");
                return;
            }
            if (!Token(r.cancelCause,128) || r.cancelCause == r.cause || first.Stage != OperationStage.Committed)
                throw new InvalidOperationException("Supply cancellation has no committed first proof");
            var cancel=CheckLease(ledger,r.actor,CancelPrefix+r.cancelCause,r.CancelHash(),null);
            if (r.cancelOp != null && r.cancelOp != cancel.Id || r.cancelReceipt != null && r.cancelReceipt != cancel.ResultId ||
                !repair && (r.cancelOp == null || r.cancelReceipt == null))
                throw new InvalidOperationException("Supply cancellation proof references are missing or contradictory");
            if (repair) { r.cancelOp=cancel.Id; r.cancelReceipt=cancel.ResultId; }
            if (r.stage == RationSupplyIntentStage.Cancelled && cancel.Stage != OperationStage.Committed)
                throw new InvalidOperationException("Cancelled supply intent has an unfinished cancellation proof");
            if (cancel.Stage == OperationStage.Committed && r.stage != RationSupplyIntentStage.Cancelled)
            {
                if (!completeStage) throw new InvalidOperationException("Supply stage requires explicit restoration of its committed cancel proof");
                r.stage=RationSupplyIntentStage.Cancelled;
            }
        }
        private static void CheckHistory(CoreWorldComponent owner,CoreLedger ledger,SaveHeader header,RationSupplyIntentRecord r)
        {
            if (r.scope != header.SaveScopeId) throw new InvalidOperationException("Supply intent belongs to another save scope");
            header.ParseId(r.id,"lease"); header.ParseId(r.prepareOp,"operation"); header.ParseId(r.city,"city"); header.ParseId(r.store,"lease");
            var entity=ledger.GetEntity(r.entity);
            header.ParseId(r.entity,entity.Kind);
            if (entity.Provider != r.actor || (entity.Kind != "person" && entity.Kind != "animal")) throw new InvalidOperationException("Supply recipient permanent identity belongs to another provider");
            if (r.sourceEntity != null)
            {
                header.ParseId(r.sourceEntity,r.sourceKind); var source=ledger.GetEntity(r.sourceEntity);
                if (source.Provider != r.actor || source.Kind != r.sourceKind) throw new InvalidOperationException("Supply source historical identity belongs to another provider");
            }
            var store=owner.CustodyForValidation.Transfers.SingleOrDefault(t => t.Id == r.store);
            if (store == null || store.Stage != PawnCustodyStage.Committed || store.IsRelease || store.Actor != r.actor || store.EntityId != r.entity ||
                store.NativeId != r.native || store.CityId != r.city || store.CaravanId != r.caravan || store.Tile != r.tile || store.MapId != -1)
                throw new InvalidOperationException("Supply intent lost its original historical committed caravan store");
            if (r.cancelCause != null) { header.ParseId(r.cancelOp,"operation"); header.ParseId(r.cancelReceipt,"lease"); }
            // Current resident, food, caravan and source existence are intentionally absent.
        }
        internal void Restore(CoreLedger ledger,SaveHeader header)
        {
            if (writing || records == null) throw new InvalidOperationException("Supply journal cannot restore while unavailable");
            foreach (var r in records) { CheckSeals(ledger,r,true,true); CheckHistory(owner,ledger,header,r); }
            Validate(ledger,header); // This boundary repairs only existing proof refs/stage, never commits or allocates.
        }
        internal void Validate(CoreLedger ledger,SaveHeader header)
        {
            if (invalidXml || writing || format != 1 || records == null || recordCount != records.Count || header.SchemaVersion < 8)
                throw new InvalidOperationException("Core8 supply journal is missing, incomplete or has an unsupported shape");
            var ids=new HashSet<string>(StringComparer.Ordinal); var causes=new HashSet<string>(StringComparer.Ordinal);
            var targets=new HashSet<string>(StringComparer.Ordinal); var persons=new HashSet<string>(StringComparer.Ordinal); var foods=new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in records)
            {
                CheckSeals(ledger,r,false,false); CheckHistory(owner,ledger,header,r);
                if (!ids.Add(r.id) || !causes.Add(Hash(new[] {r.actor,r.cause})) || r.cancelCause != null &&
                    (!ids.Add(r.cancelReceipt) || !causes.Add(Hash(new[] {r.actor,r.cancelCause}))))
                    throw new InvalidOperationException("Supply journal has duplicate proof IDs or actor/cause identities");
                if (r.stage == RationSupplyIntentStage.Prepared && (!targets.Add(r.entity) || !persons.Add(r.native) || !persons.Add(r.source) || !foods.Add(r.food)))
                    throw new InvalidOperationException("Uncancelled supply intents overlap on original individuals or food");
            }
            foreach (var op in ledger.SavedState.operations.Where(o => o.cause.StartsWith(PreparePrefix,StringComparison.Ordinal)))
                if (op.kind != "create" || !records.Any(r => r.actor == op.actor && r.prepareOp == op.id && r.id == op.result && PreparePrefix+r.cause == op.cause))
                    throw new InvalidOperationException("Orphan Core supply intent proof has no corresponding journal");
            foreach (var op in ledger.SavedState.operations.Where(o => o.cause.StartsWith(CancelPrefix,StringComparison.Ordinal)))
                if (op.kind != "create" || !records.Any(r => r.actor == op.actor && r.cancelOp == op.id && r.cancelReceipt == op.result && r.cancelCause != null && CancelPrefix+r.cancelCause == op.cause))
                    throw new InvalidOperationException("Orphan Core supply cancel proof has no corresponding journal");
        }
        private static Pawn ResolveNative(CoreWorldComponent core,string native)
        {
            var found=core.GetDirectlyHeldThings().OfType<Pawn>().Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading))
                .Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns)).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().Where(p => p.GetUniqueLoadID() == native).ToArray();
            return found.Length == 1 ? found[0] : null;
        }
        public RationSupplyIntentStatusSnapshot Inspect(string actor,string id)
        {
            var ledger=Gate(actor); var r=FindRecord(id,actor); var snapshot=Snapshot(ledger,r);
            return new RationSupplyIntentStatusSnapshot(snapshot,CurrentProblem(ledger,r,snapshot));
        }
        private string CurrentProblem(CoreLedger ledger,RationSupplyIntentRecord r,RationSupplyIntentSnapshot snapshot)
        {
            if (r.stage == RationSupplyIntentStage.Cancelled) return "Cancelled";
            if (snapshot.PrepareProofStage != OperationStage.Committed) return snapshot.PrepareProofStage == OperationStage.NeedsReconciliation ? "ProofNeedsReconciliation" : "IntentProofPending";
            if (r.cancelCause != null) return snapshot.CancelProofStage == OperationStage.NeedsReconciliation ? "ProofNeedsReconciliation" : "CancelProofPending";
            var core=Core;
            var resident=core.PawnCustody.Residents.SingleOrDefault(t => t.EntityId == r.entity);
            if (resident == null) return "RecipientReleased";
            if (resident.StoreTransferId != r.store || resident.CityId != r.city || resident.NativeId != r.native || resident.CaravanId != r.caravan) return "RecipientResidenceChanged";
            if (ledger.HasPendingReference(r.city)) return "CityLocked";
            var caravan=Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == r.caravan);
            if (caravan == null || caravan.Destroyed || !caravan.Spawned || !Find.WorldObjects.Contains(caravan) || caravan.Faction != Faction.OfPlayer || !caravan.IsPlayerControlled) return "CaravanUnavailable";
            if (caravan.pather == null || caravan.pather.Moving) return "CaravanMoving";
            if (caravan.Tile != new PlanetTile(r.tile,0)) return "CaravanAway";
            var source=ResolveNative(core,r.source);
            if (source == null || source.Destroyed || source.Dead) return "SourcePawnUnavailable";
            if (!SourceEligible(source,caravan)) return "SourceMovedOrIneligible";
            try { if (SourceBinding(ledger,r.actor,source) != r.sourceEntity) return "SourceBindingChanged"; }
            catch (InvalidOperationException) { return "SourceBindingChanged"; }
            if (core.PawnCustody.Transfers.Any(t => t.Stage != PawnCustodyStage.Committed && (t.EntityId == r.entity || t.NativeId == r.source))) return "OriginalPendingCustody";
            if (core.Rations.HasPending(r.entity) || r.sourceEntity != null && core.Rations.HasPending(r.sourceEntity)) return "OriginalPendingRations";
            if (source.inventory == null || source.inventory.innerContainer == null) return "SourceInventoryUnavailable";
            var foods=source.inventory.innerContainer.Where(t => t.GetUniqueLoadID() == r.food).ToArray();
            if (foods.Length != 1 || foods[0].Destroyed || foods[0].holdingOwner != source.inventory.innerContainer) return "SourceUnavailable";
            var food=foods[0];
            if (food.def.defName != r.def || (food.Stuff == null ? null : food.Stuff.defName) != r.stuff || food.HitPoints != r.hp || food.stackCount != r.count || food.Spawned) return "SourceChanged";
            var quote=core.Rations.QuoteSupplyCandidate(r.actor,r.entity,r.city,source,food);
            if (quote.RecipientProblem != "None") return quote.RecipientProblem;
            if (quote.FoodProblem != "None") return quote.FoodProblem;
            if (quote.UnitNutrition != r.nutrition) return "SourceNutritionChanged";
            if (quote.DailyDemand != r.demand) return "RecipientDemandChanged";
            return "None"; // Current metadata matches; still no reservation or transfer permission.
        }
        internal void SynchronizeCounts() { if (records == null) throw new InvalidOperationException("Supply records are missing"); recordCount=records.Count; }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (writing) throw new InvalidOperationException("Cannot save inside supply proof creation");
                SynchronizeCounts();
            }
            if (Scribe.mode == LoadSaveMode.LoadingVars) invalidXml=InvalidXml(new[] {"format","recordCount","records"});
            Scribe_Values.Look(ref format,"format",-1,true); Scribe_Values.Look(ref recordCount,"recordCount",-1,true);
            Scribe_Collections.Look(ref records,"records",LookMode.Deep);
            // Never replace missing schema8 data with constructor defaults.
        }
    }
}
