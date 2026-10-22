using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Inheritance.Core;
using Verse;

namespace Inheritance.Colony
{
    public sealed class DelegatedSupplyTransferStatusSnapshot
    {
        public readonly RationSupplyTransferSnapshot Transfer;
        public readonly ReadOnlyCollection<string> Problems;
        public readonly bool IsCurrent,CanDisarm,CanResumeProof;
        internal DelegatedSupplyTransferStatusSnapshot(RationSupplyTransferSnapshot transfer,IEnumerable<string> problems)
        {
            Transfer=transfer; Problems=Array.AsReadOnly(problems.Distinct(StringComparer.Ordinal).ToArray());
            IsCurrent=transfer.Stage == RationSupplyTransferStage.Armed && Problems.Count == 0;
            CanDisarm=transfer.Stage == RationSupplyTransferStage.Armed && transfer.ArmProofStage == OperationStage.Committed && transfer.DisarmCause == null;
            CanResumeProof=transfer.ArmProofStage == OperationStage.Prepared || transfer.ArmProofStage == OperationStage.Applying ||
                transfer.DisarmProofStage == OperationStage.Prepared || transfer.DisarmProofStage == OperationStage.Applying;
        }
    }
    public sealed partial class ColonyService
    {
        public RationSupplyTransferSnapshot[] DelegatedSupplyTransfers
        { get { return core.ReadRationSupplyTransfers(Actor).Where(t => t.Actor == Actor).ToArray(); } }
        private RationSupplyTransferSnapshot SupplyTransferFor(string id)
        {
            var transfer=DelegatedSupplyTransfers.SingleOrDefault(t => t.Id == id);
            if (transfer == null) throw new InvalidOperationException("Unknown delegated supply transfer preparation");
            return transfer;
        }
        private void RejectSupplyTransferCause(string cause)
        {
            if (DelegatedSupplyTransfers.Any(t => t.Cause == cause || t.DisarmCause == cause))
                throw new InvalidOperationException("Cause already belongs to a delegated supply transfer preparation");
        }
        public string BeginDelegatedSupplyTransfer(string cause,string intentId)
        {
            Gate();
            var previous=DelegatedSupplyTransfers.SingleOrDefault(t => t.Cause == cause);
            if (previous != null) return core.SupplyTransfers.Begin(Actor,cause,intentId).Id;
            if (!GenCommandLine.CommandLineArgPassed("inh-supply-transfer"))
                throw new InvalidOperationException("New supply transfer preparations require the development command");
            CheckNewSupplyIntentCause(cause);
            var intent=SupplyIntentFor(intentId);
            var status=InspectDelegatedSupplyIntent(intentId);
            if (intent.Stage != RationSupplyIntentStage.Prepared || intent.CancelCause != null || !status.IsCurrent)
                throw new InvalidOperationException("Supply transfer preparation requires the exact current uncancelled intention");
            if (state.transports.Any(t => (t.sourceId == intent.CityId || t.destinationId == intent.CityId) &&
                (t.stage == TransportStage.Planning || t.stage == TransportStage.Departing || t.stage == TransportStage.Arriving || t.stage == TransportStage.Cancelling)))
                throw new InvalidOperationException("Supply preparation must wait for the related abstract transport checkpoint");
            var site=DelegatedSite(intent.CityId);
            if (site.tile != intent.Tile) throw new InvalidOperationException("Supply preparation site disagrees with its original intention");
            var result=core.SupplyTransfers.Begin(Actor,cause,intentId);
            Validate(); return result.Id;
        }
        public void DisarmDelegatedSupplyTransfer(string disarmCause,string id)
        {
            Gate(); var transfer=SupplyTransferFor(id);
            if (transfer.DisarmCause == null) CheckNewSupplyIntentCause(disarmCause);
            core.SupplyTransfers.CancelBeforeMove(Actor,disarmCause,id); Validate();
        }
        public void ResumeDelegatedSupplyTransferProof(string id)
        { Gate(); SupplyTransferFor(id); core.SupplyTransfers.ResumeProof(Actor,id); Validate(); }
        public DelegatedSupplyTransferStatusSnapshot InspectDelegatedSupplyTransfer(string id)
        {
            Gate(); var transfer=SupplyTransferFor(id); var problems=new List<string>();
            if (transfer.Stage == RationSupplyTransferStage.Cancelled) problems.Add("Cancelled");
            if (transfer.ArmProofStage != OperationStage.Committed) problems.Add("ArmProof"+transfer.ArmProofStage);
            if (transfer.DisarmProofStage.HasValue && transfer.DisarmProofStage != OperationStage.Committed) problems.Add("DisarmProof"+transfer.DisarmProofStage);
            if (transfer.Stage == RationSupplyTransferStage.Armed)
            {
                var status=core.SupplyIntents.Inspect(Actor,transfer.IntentId);
                if (!status.IsCurrent) problems.Add(status.Problem);
                var history=SupplyPreviewCargoHistory(transfer.FoodNativeId);
                if (history.Any(h => h.JourneySupply)) problems.Add("HistoricalJourneySupply");
                if (history.Any(h => !h.JourneySupply)) problems.Add("HistoricalCargo");
                if (TransportReserved(transfer.EntityId)) problems.Add("RecipientTransportReserved");
                if (transfer.SourceEntityId != null && TransportReserved(transfer.SourceEntityId)) problems.Add("SourceTransportReserved");
            }
            return new DelegatedSupplyTransferStatusSnapshot(transfer,problems);
        }
        private void ValidateDelegatedSupplyTransfers(HashSet<string> known)
        {
            foreach (var transfer in DelegatedSupplyTransfers)
            {
                var intent=SupplyIntentFor(transfer.IntentId); var site=DelegatedSite(transfer.CityId);
                if (site.tile != transfer.Tile || intent.EntityId != transfer.EntityId || intent.CityId != transfer.CityId || !known.Add(transfer.Id))
                    throw new InvalidOperationException("Delegated supply preparation disagrees with original intention history");
                if (transfer.DisarmReceiptId != null && !known.Add(transfer.DisarmReceiptId))
                    throw new InvalidOperationException("Duplicated supply disarm certificate");
                foreach (string cause in new[] {transfer.Cause,transfer.DisarmCause}.Where(c => c != null))
                    if (state.transports.Any(t => t.cause == cause) || state.physical.Any(t => t.cause == cause) ||
                        state.returns.Any(t => t.cause == cause) || MaintenanceCauseUsed(cause) ||
                        DelegatedRations.Any(r => r.Cause == cause) || DelegatedSupplyIntents.Any(i => i.Cause == cause || i.CancelCause == cause) ||
                        core.ReadPawnCustodyTransfers(Actor).Any(t => t.Actor == Actor && t.Cause == cause))
                        throw new InvalidOperationException("Supply preparation cause overlaps another Colony transition");
            }
        }
        private void AssertPhysicalSupplyFree(PhysicalTransportRecord transport)
        {
            core.AssertNoActiveSupplyTransfer(cityId:transport.sourceId);
            core.AssertNoActiveSupplyTransfer(cityId:transport.destinationId);
            foreach (string entity in transport.party.Concat(transport.animals)) core.AssertNoActiveSupplyTransfer(entityId:entity);
            foreach (var cargo in transport.cargo)
            {
                core.AssertNoActiveSupplyTransfer(foodId:cargo.sourceNativeId);
                core.AssertNoActiveSupplyTransfer(foodId:cargo.cargoNativeId);
            }
        }
        private bool PhysicalSupplyBusy(PhysicalTransportRecord transport)
        {
            var journal=core.SupplyTransfers;
            return journal.HasActiveForCity(transport.sourceId) || journal.HasActiveForCity(transport.destinationId) ||
                transport.party.Concat(transport.animals).Any(id => journal.HasActiveForEntity(id)) ||
                transport.cargo.Any(c => journal.HasActiveForFood(c.sourceNativeId) || journal.HasActiveForFood(c.cargoNativeId));
        }
        private bool PhysicalSupplyBusyDuringRestore(PhysicalTransportRecord transport)
        {
            // Bootstrap has a validated ledger but public ready-only journals are not available yet.
            // Observe responsibility without repairing a receipt, changing a stage or allocating an ID.
            return core.ReadRationSupplyTransfers(Actor).Any(t => t.Stage == RationSupplyTransferStage.Armed &&
                (t.CityId == transport.sourceId || t.CityId == transport.destinationId ||
                 transport.party.Concat(transport.animals).Any(id => id == t.EntityId || id == t.SourceEntityId) ||
                 transport.cargo.Any(c => c.sourceNativeId == t.FoodNativeId || c.cargoNativeId == t.FoodNativeId)));
        }
    }
}
