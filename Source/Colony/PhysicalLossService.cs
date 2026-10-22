using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Inheritance.Core;
using RimWorld;
using Verse;

namespace Inheritance.Colony
{
    public sealed class PhysicalLossItem
    {
        public readonly string Label;
        public readonly int Expected, Remaining;
        internal PhysicalLossItem(string label,int expected,int remaining) { Label=label; Expected=expected; Remaining=remaining; }
    }
    public sealed class PhysicalLossPreview
    {
        internal readonly string Scope, Shipment, Previous, Observation;
        internal readonly int[] Counts;
        private readonly PhysicalLossItem[] items;
        public readonly string TripCause, ActionCause;
        public IEnumerable<PhysicalLossItem> Items { get { return items.ToArray(); } }
        public long Lost { get { return items.Sum(i => (long)i.Expected-i.Remaining); } }
        internal PhysicalLossPreview(string scope,PhysicalTransportRecord r,string observation,int[] counts,PhysicalLossItem[] items)
        { Scope=scope; Shipment=r.shipmentId; Previous=r.losses.Count == 0 ? null : r.losses.Last().receipt; Observation=observation; Counts=counts; this.items=items; TripCause=r.cause; ActionCause=Guid.NewGuid().ToString("N"); }
    }
    public sealed partial class ColonyService
    {
        private static int ExpectedCargo(PhysicalTransportRecord r,int i)
        { var last=r.losses.LastOrDefault(l => l.receipt != null); return last == null ? r.cargo[i].count : last.remaining[i]; }
        private static bool CargoNumber(PhysicalCargoRecord cargo,out int number)
        {
            number=0; string prefix="Thing_"+cargo.defName;
            return cargo.cargoNativeId != null && cargo.cargoNativeId.StartsWith(prefix,StringComparison.Ordinal) &&
                int.TryParse(cargo.cargoNativeId.Substring(prefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out number) && number > 0;
        }
        private static bool ChangedCargoIdentity(PhysicalCargoRecord cargo,Pawn[] party)
        {
            int number;
            return CargoNumber(cargo,out number) && party.SelectMany(p => p.inventory.innerContainer).Any(t => t.thingIDNumber == number && t.GetUniqueLoadID() != cargo.cargoNativeId);
        }
        private static string LossCause(PhysicalTransportRecord r,PhysicalLossRecord loss)
        { return "physical-loss/"+Hash(new[] {r.cause,loss.cause}); }
        private static string LossIdentity(PhysicalTransportRecord r,PhysicalLossRecord loss)
        {
            return "physical-loss:"+Hash(new[] {r.shipmentId,r.caravanId,loss.cause,loss.previousReceipt}.Concat(
                r.cargo.SelectMany((c,i) => new[] {c.cargoNativeId,loss.remaining[i].ToString(CultureInfo.InvariantCulture)})));
        }
        // A loss may be acknowledged only when all original live player pawns are together in their native caravan or destination.
        private string ObserveLoss(PhysicalTransportRecord r,out int[] counts)
        {
            if (r.departureReceipt == null || r.arrivalReceipt != null || r.cancelReceipt != null ||
                !(r.stage == PhysicalStage.NeedsAttention || r.stage == PhysicalStage.InTransit) || r.losses.Any(l => l.receipt == null))
                throw new InvalidOperationException("Transport is not ready for loss reconciliation");
            var party=Party(r);
            if (party.Any(p => p == null || p.Dead || p.Faction != Faction.OfPlayer || p.inventory == null)) throw new InvalidOperationException("Original transport party requires rescue or reconciliation");
            var caravan=Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == r.caravanId);
            string location;
            if (caravan != null && !caravan.Destroyed && caravan.Faction == Faction.OfPlayer && party.All(p => !p.Spawned && caravan.PawnsListForReading.Contains(p))) location="transit";
            else if (r.destinationTile < 0 && caravan == null && party.All(p => p.Spawned && p.Map == DirectMap(r.destinationId))) location="destination";
            else throw new InvalidOperationException("Native transport location is ambiguous");
            counts=new int[r.cargo.Count]; bool reduced=false;
            for (int i=0;i<r.cargo.Count;i++)
            {
                var c=r.cargo[i]; var found=party.SelectMany(p => p.inventory.innerContainer).Where(t => t.GetUniqueLoadID() == c.cargoNativeId).ToArray();
                int number;
                if (!CargoNumber(c,out number) || ChangedCargoIdentity(c,party)) throw new InvalidOperationException("Native cargo ID cannot be reconciled safely");
                if (found.Length > 1 || found.Any(t => t.Destroyed || t.def.defName != c.defName || t.stackCount <= 0)) throw new InvalidOperationException("Native cargo identity is invalid");
                int actual=found.Length == 0 ? 0 : found[0].stackCount;
                if (actual > ExpectedCargo(r,i)) throw new InvalidOperationException("Cargo exceeds the last certified quantity");
                counts[i]=c.journeySupply ? c.count : actual;
                if (!c.journeySupply && actual < ExpectedCargo(r,i)) reduced=true;
            }
            if (!reduced) throw new InvalidOperationException("No new delivery loss to acknowledge");
            return Hash(new[] {state.scope,r.shipmentId,r.caravanId,location,r.losses.Count == 0 ? null : r.losses.Last().receipt}
                .Concat(counts.Select(n => n.ToString(CultureInfo.InvariantCulture))));
        }
        public PhysicalLossPreview PreviewPhysicalLoss(string cause)
        {
            Gate(); PhysicalGate(); var r=PhysicalJob(cause); int[] counts; string observation=ObserveLoss(r,out counts);
            var items=r.cargo.Select((c,i) => new { Cargo=c, Index=i }).Where(x => !x.Cargo.journeySupply)
                .Select(x => new PhysicalLossItem(DefDatabase<ThingDef>.GetNamed(x.Cargo.defName).label,ExpectedCargo(r,x.Index),counts[x.Index])).ToArray();
            return new PhysicalLossPreview(state.scope,r,observation,counts,items);
        }
        public string AcceptPhysicalLoss(PhysicalLossPreview preview)
        {
            Gate(); PhysicalGate(); if (preview == null || preview.Scope != state.scope) throw new InvalidOperationException("Loss preview belongs to another world");
            var r=PhysicalJob(preview.TripCause);
            if (r.shipmentId != preview.Shipment) throw new InvalidOperationException("Loss preview refers to another shipment");
            var prior=r.losses.SingleOrDefault(l => l.cause == preview.ActionCause);
            if (prior != null)
            {
                if (prior.previousReceipt != preview.Previous || !prior.remaining.SequenceEqual(preview.Counts)) throw new InvalidOperationException("Loss action was reused for another quantity");
                return prior.receipt;
            }
            int[] observed; string observation=ObserveLoss(r,out observed);
            if (observation != preview.Observation || !observed.SequenceEqual(preview.Counts)) throw new InvalidOperationException("Loss changed since the preview; inspect it again");
            var loss=new PhysicalLossRecord {cause=preview.ActionCause,previousReceipt=preview.Previous,remaining=observed.ToList()};
            // One synchronous game-thread command. No native objects are moved and no save can run inside it.
            loss.receipt=Create(LossCause(r,loss),new EntitySpec("lease",ownerId:state.communityId,identityKey:LossIdentity(r,loss)));
            r.losses.Add(loss); r.lossCount=r.losses.Count;
            r.stage=PhysicalStage.NeedsAttention; RecheckPhysical(r.cause); Validate(); return loss.receipt;
        }
        public void RecheckPhysical(string cause)
        {
            Gate(); PhysicalGate(); var r=PhysicalJob(cause);
            if (r.stage == PhysicalStage.Arrived) return;
            if (r.stage != PhysicalStage.NeedsAttention || r.departureReceipt == null || r.losses.Any(l => l.receipt == null)) throw new InvalidOperationException("Only a departed held transport can be rechecked");
            if (PhysicalTransitMatches(r)) { r.stage=PhysicalStage.InTransit; r.problem=null; }
            else if (DeliveredMatches(r)) { r.stage=PhysicalStage.Arriving; CompletePhysicalArrival(r); }
            else throw new InvalidOperationException("Original native party or cargo still does not match");
            Validate();
        }
        private void ValidatePhysicalLosses(PhysicalTransportRecord r,HashSet<string> known)
        {
            if (r.losses == null || r.lossCount != r.losses.Count || r.losses.Any(l => l == null || string.IsNullOrEmpty(l.cause) || l.remaining == null) || r.losses.Select(l => l.cause).Distinct().Count() != r.losses.Count)
                throw new InvalidOperationException("Invalid physical loss table");
            var expected=r.cargo.Select(c => c.count).ToArray(); string previous=null;
            for (int n=0;n<r.losses.Count;n++)
            {
                var loss=r.losses[n];
                if (r.departureReceipt == null || r.caravanId == null || r.cargo.Any(c => c.cargoNativeId == null) || loss.previousReceipt != previous || loss.remaining.Count != expected.Length ||
                    loss.remaining.Where((amount,i) => amount < 0 || amount > expected[i] || r.cargo[i].journeySupply && amount != r.cargo[i].count).Any() ||
                    !loss.remaining.Where((amount,i) => !r.cargo[i].journeySupply && amount < expected[i]).Any() ||
                    loss.receipt == null && (n != r.losses.Count-1 || r.stage != PhysicalStage.NeedsAttention)) throw new InvalidOperationException("Invalid or nonmonotonic physical loss certificate");
                var op=ledger.FindOperation(Actor,"create",LossCause(r,loss));
                if (op == null || op.Stage != OperationStage.Committed || loss.receipt != null && loss.receipt != op.ResultId) throw new InvalidOperationException("Physical loss certificate is missing");
                var certificate=ledger.GetEntity(op.ResultId);
                if (!known.Add(certificate.Id) || certificate.Kind != "lease" || certificate.Provider != Actor || certificate.OwnerId != state.communityId || certificate.IdentityKey != LossIdentity(r,loss))
                    throw new InvalidOperationException("Physical loss differs from Core certificate");
                expected=loss.remaining.ToArray(); previous=op.ResultId;
            }
        }
        private void RestoreLossReceipt(PhysicalTransportRecord r)
        {
            if (r.losses.Count == 0 || r.losses.Last().receipt != null) return;
            var loss=r.losses.Last(); loss.receipt=ledger.FindOperation(Actor,"create",LossCause(r,loss)).ResultId;
            // Validation has already proved the certificate. Restore only its reference and native location observations.
            if (PhysicalTransitMatches(r)) { r.stage=PhysicalStage.InTransit; r.problem=null; }
            else if (DeliveredMatches(r)) { r.stage=PhysicalStage.Arriving; CompletePhysicalArrival(r); }
        }
    }
}
