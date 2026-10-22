using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RimWorld;
using Verse;

namespace Inheritance.Core
{
    public enum RationDebitStage { Prepared, Applied, Committed }
    public sealed class RationQuote
    {
        public readonly float DailyDemand, AvailableNutrition;
        public readonly bool CanPay;
        internal RationQuote(float demand,double available) { DailyDemand=demand; AvailableNutrition=(float)Math.Min(float.MaxValue,available); CanPay=available >= demand; }
    }
    public sealed class RationDebitLineSnapshot
    {
        public readonly string SourceNativeId, SpentNativeId, DefName, StuffDef;
        public readonly int HitPoints, BeforeCount, UsedCount, AfterCount;
        public readonly float UnitNutrition;
        internal RationDebitLineSnapshot(RationDebitLine line)
        { SourceNativeId=line.source; SpentNativeId=line.spent; DefName=line.def; StuffDef=line.stuff; HitPoints=line.hp; BeforeCount=line.before; UsedCount=line.used; AfterCount=line.before-line.used; UnitNutrition=line.nutrition; }
    }
    public sealed class RationDebitSnapshot
    {
        public readonly string Id, Actor, Cause, EntityId, NativeId, CityId, StoreTransferId, AppliedReceiptId;
        public readonly int Day;
        public readonly float Demand, PaidNutrition;
        public readonly RationDebitStage Stage;
        public readonly RationDebitLineSnapshot[] Lines;
        internal RationDebitSnapshot(RationDebitRecord record)
        {
            Id=record.id; Actor=record.actor; Cause=record.cause; EntityId=record.entity; NativeId=record.native;
            CityId=record.city; StoreTransferId=record.store; AppliedReceiptId=record.appliedReceipt; Day=record.day; Demand=record.demand;
            PaidNutrition=(float)record.lines.Sum(line => (double)line.nutrition*line.used);
            Stage=record.stage; Lines=record.lines.Select(line => new RationDebitLineSnapshot(line)).ToArray();
        }
    }
    internal sealed class RationDebitLine : IExposable
    {
        internal string source,spent,def,stuff;
        internal int hp,before,used;
        internal float nutrition;
        public RationDebitLine() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref source,"source"); Scribe_Values.Look(ref spent,"spent");
            Scribe_Values.Look(ref def,"def"); Scribe_Values.Look(ref stuff,"stuff");
            Scribe_Values.Look(ref hp,"hp",-1,true); Scribe_Values.Look(ref before,"before",-1,true);
            Scribe_Values.Look(ref used,"used",-1,true); Scribe_Values.Look(ref nutrition,"nutrition",-1f,true);
        }
    }
    internal sealed class RationDebitRecord : IExposable
    {
        internal string id,actor,cause,entity,native,city,store,digest,appliedReceipt;
        internal int day=-1,lineCount;
        internal float demand;
        internal RationDebitStage stage;
        internal List<RationDebitLine> lines=new List<RationDebitLine>();
        public RationDebitRecord() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref id,"id"); Scribe_Values.Look(ref actor,"actor"); Scribe_Values.Look(ref cause,"cause");
            Scribe_Values.Look(ref entity,"entity"); Scribe_Values.Look(ref native,"native"); Scribe_Values.Look(ref city,"city");
            Scribe_Values.Look(ref store,"store"); Scribe_Values.Look(ref digest,"digest");
            Scribe_Values.Look(ref appliedReceipt,"appliedReceipt");
            Scribe_Values.Look(ref day,"day",-1,true); Scribe_Values.Look(ref demand,"demand",-1f,true);
            Scribe_Values.Look(ref stage,"stage",(RationDebitStage)(-1),true);
            if (Scribe.mode == LoadSaveMode.Saving) lineCount=lines.Count;
            Scribe_Collections.Look(ref lines,"lines",LookMode.Deep); Scribe_Values.Look(ref lineCount,"lineCount",-1,true);
        }
        internal string Hash()
        {
            var fields=new List<string> { "ration-debit-v1",actor,cause,entity,native,city,store,
                day.ToString(CultureInfo.InvariantCulture),demand.ToString("R",CultureInfo.InvariantCulture),
                lines.Count.ToString(CultureInfo.InvariantCulture) };
            foreach (var line in lines)
                fields.AddRange(new[] {line.source,line.def,line.stuff,line.hp.ToString(CultureInfo.InvariantCulture),
                    line.before.ToString(CultureInfo.InvariantCulture),line.used.ToString(CultureInfo.InvariantCulture),
                    line.nutrition.ToString("R",CultureInfo.InvariantCulture)});
            var text=new StringBuilder();
            foreach (string field in fields)
                text.Append(field == null ? "-1:" : field.Length.ToString(CultureInfo.InvariantCulture)+":"+field);
            using (var sha=SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","").ToLowerInvariant();
        }
    }

    // A current-day maintenance debit, not ingestion or a native needs simulation.
    // Debited originals/pieces remain inaccessible witnesses; no discarded food is regenerated.
    public sealed class RationDebits : IExposable, IThingHolder
    {
        private readonly CoreWorldComponent owner;
        private ThingOwner<Thing> spent;
        private List<RationDebitRecord> debits=new List<RationDebitRecord>();
        private int debitCount,spentCount;
        public RationDebits(CoreWorldComponent owner)
        { this.owner=owner; spent=new ThingOwner<Thing>(this); }
        internal ThingOwner Container { get { return spent; } }
        public IThingHolder ParentHolder { get { return owner; } }
        public ThingOwner GetDirectlyHeldThings() { return spent; }
        public void GetChildHolders(List<IThingHolder> children)
        { ThingOwnerUtility.AppendThingHoldersFromThings(children,spent); }
        public IEnumerable<RationDebitSnapshot> Snapshots
        { get { return debits.Select(record => new RationDebitSnapshot(record)).ToArray(); } }
        public bool HasPending(string entity)
        { return debits.Any(record => record.entity == entity && record.stage != RationDebitStage.Committed); }
        private CoreWorldComponent Core
        {
            get
            {
                if (owner == null || Find.World == null || Find.World.GetComponent<CoreWorldComponent>() != owner || !owner.IsReady)
                    throw new InvalidOperationException("Ration debit belongs to an unavailable world");
                return owner;
            }
        }
        private static bool Positive(float value)
        { return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value); }
        private static Pawn BoundPawn(CoreLedger ledger,string entity,string actor)
        {
            var e=ledger.GetEntity(entity);
            var pawn=ledger.SavedState.entities.Single(record => record.id == entity).binding as Pawn;
            if (e.Provider != actor || e.Status != EntityStatus.Active || e.Authority != SimulationAuthority.Physical ||
                (e.Kind != "person" && e.Kind != "animal") || pawn == null || pawn.Destroyed || pawn.Dead ||
                e.BoundThingLoadId != pawn.GetUniqueLoadID() || pawn.Faction != Faction.OfPlayer ||
                !(pawn.RaceProps.Animal || pawn.IsColonist && !pawn.DevelopmentalStage.Baby()))
                throw new InvalidOperationException("Ration debit requires an original player individual owned by its provider");
            return pawn;
        }
        private Pawn CheckResident(CoreLedger ledger,RationDebitRecord record,PawnCustody custody)
        {
            var pawn=BoundPawn(ledger,record.entity,record.actor);
            var resident=custody.Residents.SingleOrDefault(item => item.EntityId == record.entity);
            var store=custody.Transfers.SingleOrDefault(item => item.Id == record.store);
            if (resident == null || resident.StoreTransferId != record.store || resident.CityId != record.city ||
                resident.NativeId != record.native || pawn.GetUniqueLoadID() != record.native || store == null ||
                store.Actor != record.actor || store.IsRelease || store.Stage != PawnCustodyStage.Committed ||
                store.CaravanId == null || store.EntityId != record.entity || store.CityId != record.city ||
                store.NativeId != record.native || pawn.Spawned || pawn.holdingOwner != custody.Container ||
                !custody.Container.Contains(pawn) || Find.WorldPawns.Contains(pawn) ||
                Find.WorldObjects.Caravans.Any(caravan => caravan.PawnsListForReading.Contains(pawn)) ||
                custody.Transfers.Any(item => item.EntityId == record.entity && item.Stage != PawnCustodyStage.Committed) ||
                pawn.inventory == null || pawn.inventory.innerContainer == null)
                throw new InvalidOperationException("Ration debit requires the exclusive original delegated resident with no custody transfer");
            return pawn;
        }
        private static bool PlainFood(Pawn pawn,Thing food)
        {
            return food != null && !(food is Corpse) && !food.Destroyed && !food.Spawned && food.stackCount > 0 &&
                food.def.category == ThingCategory.Item && food.def.ingestible != null && food.def.IsNutritionGivingIngestible &&
                !food.def.IsDrug && !food.def.IsCorpse && food.def.ingestible.drugCategory == DrugCategory.None &&
                (food.def.ingestible.outcomeDoers == null || food.def.ingestible.outcomeDoers.Count == 0) &&
                food.IngestibleNow && FoodUtility.WillEat(pawn,food,null,true,false);
        }
        private static bool Metadata(Thing food,RationDebitLine line)
        {
            return food != null && !food.Destroyed && !food.Spawned && food.def.defName == line.def &&
                (food.Stuff == null ? null : food.Stuff.defName) == line.stuff && food.HitPoints == line.hp;
        }
        private static Thing InventorySource(Pawn pawn,RationDebitLine line)
        { return pawn.inventory.innerContainer.SingleOrDefault(food => food.GetUniqueLoadID() == line.source); }
        private RationDebitRecord FindDebit(string id)
        {
            var record=debits.SingleOrDefault(item => item.id == id);
            if (record == null) throw new InvalidOperationException("Unknown ration debit");
            return record;
        }
        private static float DailyDemand(Pawn pawn)
        {
            if (pawn.needs == null || pawn.needs.food == null || pawn.genes != null &&
                pawn.genes.GenesListForReading.Any(gene => !gene.Overridden && gene is Gene_Resource))
                throw new InvalidOperationException("Ration debit does not simulate absent food needs or special gene resources");
            float demand=pawn.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed,true)*60000f;
            if (!Positive(demand)) throw new InvalidOperationException("Ration debit has no finite positive daily demand");
            return demand;
        }
        public RationSupplyCandidateQuote QuoteSupplyCandidate(string actor,string entity,string city,Pawn sourcePawn,Thing food)
        {
            var core=Core;
            core.Ledger.AssertWriteAccess(actor); // Existing world/main-thread gate; allocates nothing.
            if (core.RationMutation)
                return new RationSupplyCandidateQuote(null,-1f,-1f,"RationMutation","NotQuoted");
            var resident=core.PawnCustody.Residents.SingleOrDefault(r => r.EntityId == entity && r.CityId == city);
            if (resident == null)
                return new RationSupplyCandidateQuote(null,-1f,-1f,"ResidentUnavailable","NotQuoted");
            if (core.PawnCustody.Transfers.Any(t => t.EntityId == entity && t.Stage != PawnCustodyStage.Committed))
                return new RationSupplyCandidateQuote(null,-1f,-1f,"RecipientPendingCustody","NotQuoted");
            Pawn pawn;
            try
            {
                pawn=CheckResident(core.Ledger,new RationDebitRecord {
                    actor=actor,entity=entity,city=city,native=resident.NativeId,store=resident.StoreTransferId
                },core.PawnCustody);
            }
            catch (InvalidOperationException)
            { return new RationSupplyCandidateQuote(null,-1f,-1f,"ResidentUnavailable","NotQuoted"); }
            float demand;
            try { demand=DailyDemand(pawn); } // Includes the existing special Gene_Resource exclusion.
            catch (InvalidOperationException)
            { return new RationSupplyCandidateQuote(pawn,-1f,-1f,"UnsupportedRecipient","NotQuoted"); }
            string recipientProblem=HasPending(entity) ? "RecipientPendingRations" : "None";
            if (sourcePawn == null || sourcePawn.Destroyed || sourcePawn.Dead || sourcePawn.inventory == null ||
                sourcePawn.inventory.innerContainer == null || food == null ||
                food.holdingOwner != sourcePawn.inventory.innerContainer || !sourcePawn.inventory.innerContainer.Contains(food))
                return new RationSupplyCandidateQuote(pawn,demand,-1f,recipientProblem,"SourceInventoryMismatch");
            if (!PlainFood(pawn,food))
                return new RationSupplyCandidateQuote(pawn,demand,-1f,recipientProblem,"UnsupportedFood");
            float nutrition=FoodUtility.NutritionForEater(pawn,food);
            if (!Positive(nutrition))
                return new RationSupplyCandidateQuote(pawn,demand,-1f,recipientProblem,"InvalidNutrition");
            return new RationSupplyCandidateQuote(pawn,demand,nutrition,recipientProblem,"None");
        }

        public RationQuote Quote(string actor,string entity,string city)
        {
            var core=Core; core.Ledger.AssertWriteAccess(actor);
            var resident=core.PawnCustody.Residents.SingleOrDefault(r => r.EntityId == entity && r.CityId == city);
            if (resident == null) throw new InvalidOperationException("Ration quote requires an existing delegated resident");
            var pawn=CheckResident(core.Ledger,new RationDebitRecord {actor=actor,entity=entity,city=city,native=resident.NativeId,store=resident.StoreTransferId},core.PawnCustody);
            float demand=DailyDemand(pawn); double available=0d;
            foreach (Thing food in pawn.inventory.innerContainer)
            {
                if (!PlainFood(pawn,food) || food.holdingOwner != pawn.inventory.innerContainer) continue;
                float nutrition=FoodUtility.NutritionForEater(pawn,food);
                if (Positive(nutrition)) available+=(double)nutrition*food.stackCount;
            }
            return new RationQuote(demand,available);
        }
        public RationDebitSnapshot Prepare(string actor,string cause,string entity,string city,int day)
        {
            var core=Core; var ledger=core.Ledger; ledger.AssertWriteAccess(actor);
            if (core.RationMutation || string.IsNullOrEmpty(cause)) throw new InvalidOperationException("Ration debit cannot prepare during another mutation or without a cause");
            var previous=debits.SingleOrDefault(item => item.actor == actor && item.cause == cause);
            if (previous != null)
            {
                if (previous.entity != entity || previous.city != city || previous.day != day)
                    throw new InvalidOperationException("Ration debit cause was reused for another request");
                CheckCertificate(ledger,previous); return new RationDebitSnapshot(previous);
            }
            core.AssertNoActiveSupplyTransfer(city,entity); if (day < 0 || day != Find.TickManager.TicksGame/60000 || HasPending(entity) ||
                debits.Any(item => item.actor == actor && item.entity == entity && item.day == day))
                throw new InvalidOperationException("A ration debit must prepay the current global day exactly once per individual");
            var resident=core.PawnCustody.Residents.SingleOrDefault(item => item.EntityId == entity);
            if (resident == null) throw new InvalidOperationException("Ration debit requires an existing delegated resident");
            var record=new RationDebitRecord { actor=actor,cause=cause,entity=entity,city=city,native=resident.NativeId,
                store=resident.StoreTransferId,day=day };
            core.AssertNoActiveSupplyTransfer(record.city,record.entity,record.native); var pawn=CheckResident(ledger,record,core.PawnCustody);
            record.demand=DailyDemand(pawn);
            double remaining=record.demand;
            foreach (Thing food in pawn.inventory.innerContainer.OrderBy(item => item.GetUniqueLoadID(),StringComparer.Ordinal))
            {
                if (!PlainFood(pawn,food) || food.holdingOwner != pawn.inventory.innerContainer) continue;
                float nutrition=FoodUtility.NutritionForEater(pawn,food);
                if (!Positive(nutrition)) continue;
                double units=Math.Ceiling(remaining/nutrition);
                int used=units >= food.stackCount ? food.stackCount : Math.Max(1,(int)units);
                record.lines.Add(new RationDebitLine { source=food.GetUniqueLoadID(),def=food.def.defName,
                    stuff=food.Stuff == null ? null : food.Stuff.defName,hp=food.HitPoints,
                    before=food.stackCount,used=used,nutrition=nutrition });
                remaining-=(double)nutrition*used;
                if (remaining <= 0d) break;
            }
            if (remaining > 0d || record.lines.Count == 0)
                throw new InvalidOperationException("The original personal inventory does not contain enough permitted ration nutrition");
            foreach (var line in record.lines) core.AssertNoActiveSupplyTransfer(foodId:line.source);
            record.lineCount=record.lines.Count; record.digest=record.Hash();
            if (ledger.FindOperation(actor,"create","ration-debit/"+cause) != null)
                throw new InvalidOperationException("Orphan ration debit exists; refusing another allocation");
            if (ledger.HasPendingReference(city)) throw new InvalidOperationException("Ration city is locked by a pending Core operation");
            string operation=ledger.NewOperationId(actor);
            var prepared=ledger.PrepareCreate(operation,"ration-debit/"+cause,new EntitySpec("lease",identityKey:record.digest,custodyId:city));
            ledger.Commit(operation); record.id=prepared.ResultId; debits.Add(record);
            return new RationDebitSnapshot(record);
        }
        private static void CheckBefore(Pawn pawn,RationDebitRecord record)
        {
            foreach (var line in record.lines)
            {
                var source=InventorySource(pawn,line);
                if (!Metadata(source,line) || source.stackCount != line.before || source.holdingOwner != pawn.inventory.innerContainer ||
                    line.spent != null || !PlainFood(pawn,source) || FoodUtility.NutritionForEater(pawn,source) != line.nutrition)
                    throw new InvalidOperationException("Prepared ration original inventory changed");
            }
        }
        private void CheckWitnesses(RationDebitRecord record,HashSet<string> seen)
        {
            foreach (var line in record.lines)
            {
                if (string.IsNullOrEmpty(line.spent) || seen != null && !seen.Add(line.spent))
                    throw new InvalidOperationException("Ration debit witness identity is missing or repeated");
                var witness=spent.SingleOrDefault(food => food.GetUniqueLoadID() == line.spent);
                if (!Metadata(witness,line) || witness.stackCount != line.used || witness.holdingOwner != spent ||
                    (line.before == line.used ? line.spent != line.source : line.spent == line.source))
                    throw new InvalidOperationException("Ration debit has no exact exclusive spent witness");
            }
        }
        private void CheckAfter(Pawn pawn,RationDebitRecord record)
        {
            CheckWitnesses(record,null);
            foreach (var line in record.lines)
            {
                var source=InventorySource(pawn,line);
                int after=line.before-line.used;
                if (after == 0 ? source != null : !Metadata(source,line) || source.stackCount != after || source.holdingOwner != pawn.inventory.innerContainer)
                    throw new InvalidOperationException("Applied ration inventory disagrees with its fixed debit");
            }
        }
        public RationDebitSnapshot Apply(string id)
        {
            var core=Core; var record=FindDebit(id); var ledger=core.Ledger;
            ledger.AssertWriteAccess(record.actor); CheckCertificate(ledger,record);
            if (record.stage == RationDebitStage.Committed) return new RationDebitSnapshot(record);
            if (core.RationMutation) throw new InvalidOperationException("Another ration mutation is already running");
            core.AssertNoActiveSupplyTransfer(record.city,record.entity,record.native); var pawn=CheckResident(ledger,record,core.PawnCustody);
            if (record.stage == RationDebitStage.Applied)
            { CheckAfter(pawn,record); EnsureAppliedReceipt(ledger,record); return new RationDebitSnapshot(record); }
            if (ledger.HasPendingReference(record.city)) throw new InvalidOperationException("Ration city is locked before native food mutation");
            CheckBefore(pawn,record);
            var originals=record.lines.Select(line => InventorySource(pawn,line)).ToArray();
            var pieces=new Thing[record.lines.Count]; core.RationMutation=true;
            try
            {
                for (int index=0;index<record.lines.Count;index++)
                {
                    var line=record.lines[index]; var source=originals[index];
                    if (line.used == line.before)
                    {
                        pieces[index]=source;
                        if (!pawn.inventory.innerContainer.Remove(source)) throw new InvalidOperationException("Cannot remove the original ration stack");
                    }
                    else pieces[index]=source.SplitOff(line.used);
                    var piece=pieces[index];
                    if (piece == null || piece.Destroyed || piece.holdingOwner != null || !Metadata(piece,line) || piece.stackCount != line.used ||
                        (line.used == line.before ? piece != source : piece == source) || !spent.TryAdd(piece,false))
                        throw new InvalidOperationException("Native ration split did not produce an exclusive spent witness");
                    line.spent=piece.GetUniqueLoadID();
                }
                CheckAfter(pawn,record); record.stage=RationDebitStage.Applied;
            }
            catch
            {
                // Restore only these original stacks and these exact native pieces.
                // Native stacking retires a split piece; it never creates replacement food.
                for (int index=record.lines.Count-1;index>=0;index--)
                {
                    var piece=pieces[index]; var source=originals[index]; var line=record.lines[index];
                    if (piece == null) continue;
                    if (piece.holdingOwner == spent && !spent.Remove(piece)) throw new InvalidOperationException("Cannot roll back the exact spent ration witness");
                    if (line.used == line.before)
                    {
                        if (piece != source || piece.Destroyed || piece.holdingOwner != null && piece.holdingOwner != pawn.inventory.innerContainer ||
                            piece.holdingOwner == null && !pawn.inventory.innerContainer.TryAdd(piece,false))
                            throw new InvalidOperationException("Original whole ration stack cannot return to its inventory");
                    }
                    else
                    {
                        if (source.Destroyed || source.holdingOwner != pawn.inventory.innerContainer || piece.Destroyed || piece.holdingOwner != null ||
                            source.stackCount+piece.stackCount != line.before || !source.TryAbsorbStack(piece,false) || source.stackCount != line.before)
                            throw new InvalidOperationException("Original split ration stack cannot be restored without replacement");
                    }
                    line.spent=null;
                }
                CheckBefore(pawn,record); throw;
            }
            finally { core.RationMutation=false; }
            // Native ownership is now fully applied. A failure to allocate the proof
            // leaves a saveable Applied checkpoint, never a rolled-back certified debit.
            EnsureAppliedReceipt(ledger,record);
            return new RationDebitSnapshot(record);
        }
        public RationDebitSnapshot Commit(string id)
        {
            var core=Core; var record=FindDebit(id); var ledger=core.Ledger;
            ledger.AssertWriteAccess(record.actor); CheckCertificate(ledger,record);
            if (record.stage == RationDebitStage.Committed) return new RationDebitSnapshot(record);
            core.AssertNoActiveSupplyTransfer(record.city,record.entity,record.native); if (core.RationMutation || record.stage != RationDebitStage.Applied)
                throw new InvalidOperationException("Ration debit has not applied its exact native debit");
            core.AssertNoActiveSupplyTransfer(record.city,record.entity,record.native); var pawn=CheckResident(ledger,record,core.PawnCustody); CheckAfter(pawn,record);
            EnsureAppliedReceipt(ledger,record);
            record.stage=RationDebitStage.Committed; return new RationDebitSnapshot(record);
        }
        private static string AppliedIdentity(RationDebitRecord record)
        {
            var text=new StringBuilder("ration-applied-v1:");
            foreach (string part in new[] {record.id,record.digest}.Concat(record.lines.Select(line => line.spent)))
                text.Append(part == null ? "-1:" : part.Length.ToString(CultureInfo.InvariantCulture)+":"+part);
            using (var sha=SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","").ToLowerInvariant();
        }
        private static void CheckAppliedReceipt(CoreLedger ledger,RationDebitRecord record)
        {
            var operation=ledger.FindOperation(record.actor,"create","ration-applied/"+record.cause);
            if (record.stage == RationDebitStage.Prepared)
            {
                if (record.appliedReceipt != null || operation != null)
                    throw new InvalidOperationException("Prepared ration debit already has a native applied certificate");
                return;
            }
            if (operation == null)
            {
                if (record.appliedReceipt != null || record.stage == RationDebitStage.Committed)
                    throw new InvalidOperationException("Ration debit native applied certificate is missing");
                return; // Exact native witnesses remain an explicitly resumable Applied checkpoint.
            }
            var saved=ledger.SavedState.operations.Single(item => item.id == operation.Id);
            var specification=saved.specification;
            var lease=ledger.GetEntity(operation.ResultId);
            bool committed=operation.Stage == OperationStage.Committed;
            bool pending=operation.Stage == OperationStage.Prepared || operation.Stage == OperationStage.Applying ||
                operation.Stage == OperationStage.NeedsReconciliation;
            // A pending Core create has a deliberately empty allocated result. Its
            // immutable request, not that result's un-applied fields, seals the witnesses.
            bool specificationMatches=specification != null && specification.kind == "lease" &&
                specification.identity == AppliedIdentity(record) && specification.custody == record.city &&
                specification.authority == SimulationAuthority.Abstract && specification.quantity == 0 &&
                specification.name == null && specification.culture == null && specification.controller == null &&
                specification.owner == null && specification.community == null && specification.affiliation == null &&
                specification.item == null && specification.binding == null && saved.subject == null && saved.destination == null &&
                saved.quantity == 0 && saved.expectedRevision == 0 && saved.tick == 0;
            bool resultMatches=lease.Provider == record.actor && lease.Kind == "lease" &&
                lease.Authority == SimulationAuthority.Abstract && lease.Quantity == 0 && lease.BoundThingLoadId == null &&
                lease.Name == null && lease.CultureKey == null && lease.ControllerId == null && lease.OwnerId == null &&
                lease.CommunityId == null && lease.AffiliationId == null && lease.ItemKey == null && lease.LineageId == null &&
                (committed ? lease.Status == EntityStatus.Active && lease.CustodyId == record.city && lease.IdentityKey == AppliedIdentity(record) :
                    lease.Status == EntityStatus.Allocated && lease.Revision == 0 && lease.CustodyId == null && lease.IdentityKey == null);
            if ((!committed && !pending) || record.stage == RationDebitStage.Committed && !committed ||
                !specificationMatches || !resultMatches || record.appliedReceipt != null && record.appliedReceipt != operation.ResultId)
                throw new InvalidOperationException("Ration debit native witnesses disagree with their immutable applied certificate");
            // Recover only the reference to the existing Core request/result. Pending
            // creates remain pending until an explicit resume; loading allocates no IDs
            // and does not repeat the native food debit.
            record.appliedReceipt=operation.ResultId;
        }
        private void EnsureAppliedReceipt(CoreLedger ledger,RationDebitRecord record)
        {
            CheckAppliedReceipt(ledger,record);
            var existing=ledger.FindOperation(record.actor,"create","ration-applied/"+record.cause);
            if (existing != null && existing.Stage == OperationStage.Committed) return;
            owner.AssertNoActiveSupplyTransfer(record.city,record.entity,record.native); if (record.stage != RationDebitStage.Applied)
                throw new InvalidOperationException("Only an applied ration debit can certify its spent native witnesses");
            if (existing != null && existing.Stage != OperationStage.Prepared && existing.Stage != OperationStage.Applying)
                throw new InvalidOperationException("Ration applied certificate requires Core precondition reconciliation before it can resume");
            if (owner.RationMutation) throw new InvalidOperationException("Another native ration mutation is running");
            owner.RationMutation=true;
            try
            {
                if (existing != null)
                {
                    // The checked request already owns the witness manifest and result
                    // ID. Resume that transaction, without selecting/splitting food again.
                    ledger.Commit(existing.Id); record.appliedReceipt=existing.ResultId;
                }
                else
                {
                    if (ledger.HasPendingReference(record.city)) throw new InvalidOperationException("Ration city is locked before applied proof allocation");
                    string operation=ledger.NewOperationId(record.actor);
                    var prepared=ledger.PrepareCreate(operation,"ration-applied/"+record.cause,
                        new EntitySpec("lease",identityKey:AppliedIdentity(record),custodyId:record.city));
                    ledger.Commit(operation); record.appliedReceipt=prepared.ResultId;
                }
            }
            finally { owner.RationMutation=false; }
        }
        private static void CheckCertificate(CoreLedger ledger,RationDebitRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.actor) || string.IsNullOrEmpty(record.cause) || string.IsNullOrEmpty(record.store) ||
                record.day < 0 || !Positive(record.demand) || record.lines == null || record.lines.Count == 0 ||
                record.lineCount != record.lines.Count || !Enum.IsDefined(typeof(RationDebitStage),record.stage))
                throw new InvalidOperationException("Ration debit journal is incomplete or invalid");
            var sources=new HashSet<string>(StringComparer.Ordinal); double nutrition=0d;
            foreach (var line in record.lines)
            {
                if (line == null || string.IsNullOrEmpty(line.source) || !sources.Add(line.source) || string.IsNullOrEmpty(line.def) ||
                    line.hp <= 0 || line.before <= 0 || line.used <= 0 || line.used > line.before || !Positive(line.nutrition))
                    throw new InvalidOperationException("Invalid ration debit line");
                nutrition+=(double)line.nutrition*line.used;
            }
            if (nutrition < record.demand || double.IsInfinity(nutrition)) throw new InvalidOperationException("Ration debit does not cover its fixed nutrition demand");
            var lease=ledger.GetEntity(record.id); var operation=ledger.FindOperation(record.actor,"create","ration-debit/"+record.cause);
            var entity=ledger.GetEntity(record.entity); var city=ledger.GetEntity(record.city);
            if (record.digest != record.Hash() || lease.Provider != record.actor || lease.Kind != "lease" || lease.Status != EntityStatus.Active ||
                lease.CustodyId != record.city || lease.IdentityKey != record.digest || operation == null || operation.Stage != OperationStage.Committed ||
                operation.ResultId != record.id || entity.Provider != record.actor || entity.BoundThingLoadId != record.native ||
                entity.Authority != SimulationAuthority.Physical || (entity.Kind != "person" && entity.Kind != "animal") ||
                city.Provider != record.actor || city.Kind != "city" || city.Status != EntityStatus.Active)
                throw new InvalidOperationException("Ration debit disagrees with its immutable Core certificate");
            CheckAppliedReceipt(ledger,record);
        }
        internal void Validate(CoreLedger ledger,SaveHeader header)
        {
            if (spent == null || debits == null || debitCount != debits.Count || spentCount != spent.Count ||
                header.SchemaVersion < 7 && (debits.Count != 0 || spent.Count != 0))
                throw new InvalidOperationException("Ration debit tables are missing, incomplete or from an unsupported schema");
            var ids=new HashSet<string>(StringComparer.Ordinal); var causes=new HashSet<string>(StringComparer.Ordinal);
            var periods=new HashSet<string>(StringComparer.Ordinal); var pending=new HashSet<string>(StringComparer.Ordinal);
            var witnesses=new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in debits)
            {
                CheckCertificate(ledger,record);
                if (!ids.Add(record.id) || !causes.Add(record.actor+"/"+record.cause) ||
                    !periods.Add(record.actor+"/"+record.entity+"/"+record.day.ToString(CultureInfo.InvariantCulture)))
                    throw new InvalidOperationException("Duplicate ration debit journal or daily period");
                var store=owner.CustodyForValidation.Transfers.SingleOrDefault(item => item.Id == record.store);
                if (store == null || store.IsRelease || store.Stage != PawnCustodyStage.Committed || store.CaravanId == null ||
                    store.Actor != record.actor || store.EntityId != record.entity || store.NativeId != record.native || store.CityId != record.city)
                    throw new InvalidOperationException("Ration debit lost its original committed delegated store");
                if (record.stage == RationDebitStage.Prepared)
                {
                    if (!pending.Add(record.entity)) throw new InvalidOperationException("Overlapping ration debits");
                    CheckBefore(CheckResident(ledger,record,owner.CustodyForValidation),record);
                }
                else
                {
                    CheckWitnesses(record,witnesses);
                    if (record.stage == RationDebitStage.Applied)
                    {
                        if (!pending.Add(record.entity)) throw new InvalidOperationException("Overlapping ration debits");
                        CheckAfter(CheckResident(ledger,record,owner.CustodyForValidation),record);
                    }
                }
            }
            if (witnesses.Count != spent.Count || spent.Any(food => food == null || !witnesses.Contains(food.GetUniqueLoadID())))
                throw new InvalidOperationException("Unaccounted spent ration witness");
            foreach (var operation in ledger.SavedState.operations.Where(item => item.kind == "create" && item.cause.StartsWith("ration-debit/",StringComparison.Ordinal)))
                if (!debits.Any(record => record.actor == operation.actor && record.id == operation.result && "ration-debit/"+record.cause == operation.cause))
                    throw new InvalidOperationException("Missing ration debit journal for a Core intent");
            foreach (var operation in ledger.SavedState.operations.Where(item => item.kind == "create" && item.cause.StartsWith("ration-applied/",StringComparison.Ordinal)))
                if (!debits.Any(record => record.actor == operation.actor && record.appliedReceipt == operation.result &&
                    record.stage != RationDebitStage.Prepared && "ration-applied/"+record.cause == operation.cause))
                    throw new InvalidOperationException("Missing ration debit journal for a Core applied certificate");
        }
        internal void SynchronizeCounts() { debitCount=debits.Count; spentCount=spent.Count; }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (owner.RationMutation) throw new InvalidOperationException("Cannot save halfway through native ration mutation");
                SynchronizeCounts();
            }
            Scribe_Deep.Look(ref spent,"spent",this);
            Scribe_Collections.Look(ref debits,"debits",LookMode.Deep);
            Scribe_Values.Look(ref debitCount,"debitCount",-1,true); Scribe_Values.Look(ref spentCount,"spentCount",-1,true);
        }
    }
}
