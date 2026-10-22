using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Inheritance.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Colony
{
    // A return is its own journal. The outward arrival remains historical evidence.
    // No original, cargo stack or caravan is manufactured to repair a broken trip.
    public sealed partial class ColonyService
    {
        private bool ActiveDelegatedReturn(PhysicalTransportRecord outward)
        { return state.returns.Any(r => r.outwardCause == outward.cause && r.stage != DelegatedReturnStage.Returned); }
        private void RejectReturnCause(string cause)
        {
            RejectMaintenanceCause(cause);
            if (core.ReadRationDebits(Actor).Any(r => r.Actor == Actor && r.Cause == cause))
                throw new InvalidOperationException("Cause already belongs to a delegated ration debit");
            if (state.returns.Any(r => r.cause == cause))
                throw new InvalidOperationException("Cause already belongs to a delegated return");
        }

        private DelegatedReturnRecord ReturnJob(string cause)
        {
            var r=state.returns.SingleOrDefault(x => x.cause == cause);
            if (r == null) throw new InvalidOperationException("Unknown delegated return");
            return r;
        }

        private PhysicalTransportRecord ReturnOutward(DelegatedReturnRecord r)
        {
            var outward=PhysicalJob(r.outwardCause);
            if (outward.destinationTile < 0 || outward.stage != PhysicalStage.Arrived ||
                outward.departureReceipt == null || outward.arrivalReceipt == null || outward.caravanId == null ||
                DelegatedSite(outward.destinationId).tile != outward.destinationTile || RequireBase(outward.sourceId).mode != BaseMode.Direct)
                throw new InvalidOperationException("Delegated return requires its completed outward journey");
            return outward;
        }

        private string ReturnManifest(DelegatedReturnRecord r,PhysicalTransportRecord outward)
        {
            var parts=new List<string> { r.cause,r.outwardCause,outward.shipmentId,outward.departureReceipt,outward.arrivalReceipt,
                outward.caravanId,outward.sourceId,outward.destinationId,outward.destinationTile.ToString(CultureInfo.InvariantCulture),
                RequireBase(outward.sourceId).mapId.ToString(CultureInfo.InvariantCulture),PhysicalManifest(outward),
                outward.party.Count.ToString(CultureInfo.InvariantCulture),outward.animals.Count.ToString(CultureInfo.InvariantCulture),
                outward.cargo.Count.ToString(CultureInfo.InvariantCulture) };
            foreach (string id in outward.party) { parts.Add(id); parts.Add(state.people.Single(p => p.id == id).nativeId); }
            foreach (string id in outward.animals) { parts.Add(id); parts.Add(state.animals.Single(a => a.id == id).nativeId); }
            foreach (var c in outward.cargo) parts.Add(c.cargoNativeId);
            parts.Add(outward.losses.Count == 0 ? null : outward.losses.Last().receipt);
            return "delegated-return-v1:"+Hash(parts);
        }

        private string ReturnReceiptIdentity(DelegatedReturnRecord r,PhysicalTransportRecord outward,string phase)
        { return "delegated-return-"+phase+":"+Hash(new[] {r.shipmentId,ReturnManifest(r,outward)}); }

        private static string ReturnReceiptCause(DelegatedReturnRecord r,string phase)
        { return "delegated-return-"+phase+"/"+r.cause; }

        private void ReturnReservations(DelegatedReturnRecord r,PhysicalTransportRecord outward)
        {
            var entities=new HashSet<string>(outward.party.Concat(outward.animals),StringComparer.Ordinal);
            var party=Party(outward);
            if (party.Any(p => p == null || NativeCustodyReserved(p)) || entities.Any(id => core.PawnCustody.IsStored(id)) ||
                state.physical.Any(other => other != outward && ActivePhysical(other) && other.party.Concat(other.animals).Any(entities.Contains)) ||
                state.returns.Any(other => other != r && other.outwardCause == outward.cause))
                throw new InvalidOperationException("Return originals are held or reserved by another transition");
        }

        private bool ReturnTransitMatches(PhysicalTransportRecord outward)
        { return DelegatedTransitMatches(outward); }

        private bool ReturnDeliveredMatches(PhysicalTransportRecord outward)
        {
            var party=Party(outward); var home=DirectMap(outward.sourceId);
            return !Find.WorldObjects.Caravans.Any(c => c.GetUniqueLoadID() == outward.caravanId) &&
                party.All(p => p != null && !p.Destroyed && !p.Dead && p.Faction == Faction.OfPlayer && p.Spawned && p.Map == home &&
                    p.holdingOwner == home.spawnedThings && !Find.WorldPawns.Contains(p) &&
                    !Find.WorldObjects.Caravans.Any(c => c.PawnsListForReading.Contains(p))) && CargoMatches(outward,party);
        }

        public string BeginDelegatedReturn(string cause,string outwardCause)
        {
            Gate(); PhysicalGate();
            RejectMaintenanceCause(cause);
            if (string.IsNullOrEmpty(cause) || string.IsNullOrEmpty(outwardCause)) throw new InvalidOperationException("A return and outward cause are required");
            var prior=state.returns.SingleOrDefault(r => r.cause == cause);
            if (prior != null)
            {
                if (prior.outwardCause != outwardCause) throw new InvalidOperationException("Return cause was reused for another outward journey");
                return prior.shipmentId;
            }
            if (state.physical.Any(r => r.cause == cause) || state.transports.Any(r => r.cause == cause) ||
                core.ReadRationDebits(Actor).Any(r => r.Actor == Actor && r.Cause == cause) ||
                core.ReadPawnCustodyTransfers(Actor).Any(t => t.Cause == cause))
                throw new InvalidOperationException("Return cause already belongs to another Colony command");
            var record=new DelegatedReturnRecord {cause=cause,outwardCause=outwardCause,stage=DelegatedReturnStage.InTransit};
            var outward=ReturnOutward(record);
            AssertPhysicalSupplyFree(outward); ReturnReservations(record,outward);
            if (!ReturnTransitMatches(outward)) throw new InvalidOperationException("All original live return travellers and their cargo must rejoin the original caravan");
            var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == outward.caravanId);
            CheckSiteCaravan(DelegatedSite(outward.destinationId),caravan);
            if (Party(outward).Any(p => !CaravanOriginal(caravan,p))) throw new InvalidOperationException("Return travellers are not ready to depart");
            DirectMap(outward.sourceId);
            record.shipmentId=Create("delegated-return-shipment/"+cause,new EntitySpec("shipment",cause,ownerId:state.communityId,identityKey:ReturnManifest(record,outward)));
            record.departureReceipt=Create(ReturnReceiptCause(record,"depart"),new EntitySpec("lease",ownerId:state.communityId,identityKey:ReturnReceiptIdentity(record,outward,"depart")));
            state.returns.Add(record); Validate(); return record.shipmentId;
        }

        public void StartDelegatedReturnRoute(string cause)
        {
            Gate(); PhysicalGate(); var r=ReturnJob(cause);
            if (r.stage == DelegatedReturnStage.Returned) return;
            var outward=ReturnOutward(r);
            AssertPhysicalSupplyFree(outward); ReturnReservations(r,outward);
            if (r.stage != DelegatedReturnStage.InTransit || !ReturnTransitMatches(outward)) throw new InvalidOperationException("Delegated return requires attention before travel");
            var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == outward.caravanId);
            var home=DirectMap(outward.sourceId);
            if (caravan.pather == null) throw new InvalidOperationException("Return caravan has no native path follower");
            // Reissuing an already running home route must not reset native movement progress.
            if (caravan.pather.Moving)
            {
                if (caravan.pather.Destination != home.Tile) throw new InvalidOperationException("Return caravan is already travelling to another destination");
                return;
            }
            if (caravan.Tile == home.Tile) return;
            if (!caravan.pather.StartPath(home.Tile,null,true,false)) throw new InvalidOperationException("Native return caravan cannot find a home route");
        }

        public void BeginDelegatedReturnArrival(string cause)
        {
            Gate(); PhysicalGate(); var r=ReturnJob(cause);
            if (r.stage == DelegatedReturnStage.Returned || r.stage == DelegatedReturnStage.Arriving) return;
            var outward=ReturnOutward(r); AssertPhysicalSupplyFree(outward); ReturnReservations(r,outward);
            if (r.stage != DelegatedReturnStage.InTransit || !ReturnTransitMatches(outward)) throw new InvalidOperationException("Delegated return requires attention before home arrival");
            var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == outward.caravanId);
            if (caravan.Tile != DirectMap(outward.sourceId).Tile || caravan.pather == null || caravan.pather.Moving)
                throw new InvalidOperationException("Original return caravan must be stopped at its original direct map");
            r.stage=DelegatedReturnStage.Arriving; r.problem=null;
        }

        public void DeliverDelegatedReturn(string cause)
        {
            Gate(); PhysicalGate(); var r=ReturnJob(cause);
            if (r.stage == DelegatedReturnStage.Returned) return;
            var outward=ReturnOutward(r); AssertPhysicalSupplyFree(outward); ReturnReservations(r,outward);
            if (r.stage != DelegatedReturnStage.Arriving) BeginDelegatedReturnArrival(cause);
            if (ReturnDeliveredMatches(outward)) { CompleteDelegatedReturn(r,outward); Validate(); return; }
            if (!ReturnTransitMatches(outward))
            { HoldDelegatedReturn(r,"Original return caravan, party or cargo changed before home arrival"); throw new InvalidOperationException(r.problem); }
            var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == outward.caravanId);
            if (caravan.Tile != DirectMap(outward.sourceId).Tile || caravan.pather == null || caravan.pather.Moving)
                throw new InvalidOperationException("Return arrival checkpoint is not at its original direct map");
            try
            {
                PhysicalCaravanAdapter.EnterDestination(core,caravan,DirectMap(outward.sourceId));
                CompleteDelegatedReturn(r,outward); Validate();
            }
            catch (Exception e) { HoldDelegatedReturn(r,e.Message); throw; }
        }

        private void CompleteDelegatedReturn(DelegatedReturnRecord r,PhysicalTransportRecord outward)
        {
            if (!ReturnDeliveredMatches(outward)) throw new InvalidOperationException("Native returned party does not match its original manifest");
            r.arrivalReceipt=Create(ReturnReceiptCause(r,"arrive"),new EntitySpec("lease",ownerId:state.communityId,identityKey:ReturnReceiptIdentity(r,outward,"arrive")));
            r.stage=DelegatedReturnStage.Returned; r.problem=null;
        }

        private static void HoldDelegatedReturn(DelegatedReturnRecord r,string problem)
        { r.stage=DelegatedReturnStage.NeedsAttention; r.problem=problem; }

        public void RecheckDelegatedReturn(string cause)
        {
            Gate(); PhysicalGate(); var r=ReturnJob(cause);
            if (r.stage == DelegatedReturnStage.Returned) return;
            if (r.stage != DelegatedReturnStage.NeedsAttention) throw new InvalidOperationException("Only a held delegated return can be rechecked");
            var outward=ReturnOutward(r); AssertPhysicalSupplyFree(outward); ReturnReservations(r,outward);
            if (ReturnTransitMatches(outward)) { r.stage=DelegatedReturnStage.InTransit; r.problem=null; }
            else if (ReturnDeliveredMatches(outward)) CompleteDelegatedReturn(r,outward);
            else throw new InvalidOperationException("Original return travellers or cargo still require rescue or reconciliation");
            Validate();
        }

        private string CommittedReturnReceipt(DelegatedReturnRecord r,string phase)
        {
            var op=ledger.FindOperation(Actor,"create",ReturnReceiptCause(r,phase));
            return op != null && op.Stage == OperationStage.Committed ? op.ResultId : null;
        }

        private void RestoreDelegatedReturns()
        {
            foreach (var r in state.returns)
            {
                if (PhysicalSupplyBusyDuringRestore(ReturnOutward(r))) continue;
                // Validation already proved these existing Core certificates. Loading never allocates a receipt or moves native objects.
                if (r.departureReceipt == null) r.departureReceipt=CommittedReturnReceipt(r,"depart");
                if (r.arrivalReceipt == null) r.arrivalReceipt=CommittedReturnReceipt(r,"arrive");
                if (r.arrivalReceipt != null) { r.stage=DelegatedReturnStage.Returned; r.problem=null; continue; }
                if (r.stage == DelegatedReturnStage.Returned || r.stage == DelegatedReturnStage.NeedsAttention) continue;
                var outward=ReturnOutward(r);
                if (ReturnTransitMatches(outward)) { r.stage=DelegatedReturnStage.InTransit; r.problem=null; }
                else HoldDelegatedReturn(r,ReturnDeliveredMatches(outward) ? "Original party is home; explicitly recheck to certify return" : "Native return party or cargo changed");
            }
        }

        private void TickDelegatedReturns()
        {
            foreach (var r in state.returns.Where(x => x.stage == DelegatedReturnStage.InTransit).ToArray())
            {
                var outward=ReturnOutward(r);
                if (PhysicalSupplyBusy(outward)) continue;
                if (!ReturnTransitMatches(outward)) { HoldDelegatedReturn(r,"Native return party or cargo changed"); continue; }
                var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == outward.caravanId);
                bool automatic=GenCommandLine.CommandLineArgPassed("inh-physical-auto") || ColonyMod.PhysicalEnabled && !GenCommandLine.CommandLineArgPassed("inh-physical-probe");
                if (ColonyMod.PhysicalAllowed && automatic && caravan.Tile == DirectMap(outward.sourceId).Tile && !caravan.pather.Moving)
                {
                    try { DeliverDelegatedReturn(r.cause); }
                    catch (InvalidOperationException e) { HoldDelegatedReturn(r,e.Message); }
                }
            }
        }

        private void ValidateDelegatedReturns(HashSet<string> known)
        {
            if (state.returns == null || state.returns.Any(r => r == null || string.IsNullOrEmpty(r.cause) || string.IsNullOrEmpty(r.outwardCause) ||
                !Enum.IsDefined(typeof(DelegatedReturnStage),r.stage)) || state.returns.Select(r => r.cause).Distinct().Count() != state.returns.Count ||
                state.returns.Select(r => r.outwardCause).Distinct().Count() != state.returns.Count)
                throw new InvalidOperationException("Invalid or duplicate delegated return journal");
            foreach (var r in state.returns)
            {
                var outward=ReturnOutward(r);
                if (state.physical.Any(x => x.cause == r.cause) || state.transports.Any(x => x.cause == r.cause) ||
                    core.ReadPawnCustodyTransfers(Actor).Any(t => t.Cause == r.cause)) throw new InvalidOperationException("Return cause collides with another Colony command");
                var op=ledger.FindOperation(Actor,"create","delegated-return-shipment/"+r.cause);
                var shipment=ledger.GetEntity(r.shipmentId);
                if (!known.Add(shipment.Id) || op == null || op.Stage != OperationStage.Committed || op.ResultId != shipment.Id ||
                    shipment.Provider != Actor || shipment.Kind != "shipment" || shipment.OwnerId != state.communityId || shipment.IdentityKey != ReturnManifest(r,outward))
                    throw new InvalidOperationException("Return manifest differs from its immutable Core journal");
                string departure=ValidateReturnReceipt(r,outward,"depart",known);
                string arrival=ValidateReturnReceipt(r,outward,"arrive",known);
                if (departure == null || r.stage == DelegatedReturnStage.Returned && arrival == null ||
                    arrival != null && r.stage != DelegatedReturnStage.Arriving && r.stage != DelegatedReturnStage.Returned ||
                    r.stage != DelegatedReturnStage.NeedsAttention && r.problem != null)
                    throw new InvalidOperationException("Delegated return checkpoint or receipt phase mismatch");
                if (r.stage != DelegatedReturnStage.Returned && outward.party.Concat(outward.animals).Any(id => core.ReadPawnCustodyResidents(Actor).Any(resident => resident.EntityId == id) ||
                    core.ReadPawnCustodyTransfers(Actor).Any(t => t.EntityId == id && t.Stage != PawnCustodyStage.Committed)))
                    throw new InvalidOperationException("Active delegated return overlaps held or pending originals");
            }
        }

        private string ValidateReturnReceipt(DelegatedReturnRecord r,PhysicalTransportRecord outward,string phase,HashSet<string> known)
        {
            string reference=phase == "depart" ? r.departureReceipt : r.arrivalReceipt;
            var op=ledger.FindOperation(Actor,"create",ReturnReceiptCause(r,phase));
            if (op == null)
            {
                if (reference != null) throw new InvalidOperationException("Return receipt lacks its original Core operation");
                return null;
            }
            if (op.Stage != OperationStage.Committed || reference != null && reference != op.ResultId)
                throw new InvalidOperationException("Return receipt reference differs from its Core operation");
            var receipt=ledger.GetEntity(op.ResultId);
            if (!known.Add(receipt.Id) || receipt.Provider != Actor || receipt.Kind != "lease" || receipt.OwnerId != state.communityId ||
                receipt.IdentityKey != ReturnReceiptIdentity(r,outward,phase)) throw new InvalidOperationException("Return receipt differs from its original manifest");
            return receipt.Id;
        }
    }
}
