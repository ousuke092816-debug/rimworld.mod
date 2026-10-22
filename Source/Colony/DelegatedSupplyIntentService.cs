using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Inheritance.Core;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Colony
{
    public sealed class DelegatedSupplyIntentStatusSnapshot
    {
        public readonly RationSupplyIntentStatusSnapshot CoreStatus;
        public readonly ReadOnlyCollection<string> Problems;
        public readonly bool IsCurrent;
        internal DelegatedSupplyIntentStatusSnapshot(RationSupplyIntentStatusSnapshot status,IEnumerable<string> problems)
        {
            CoreStatus=status; Problems=Array.AsReadOnly(problems.Distinct(StringComparer.Ordinal).ToArray());
            IsCurrent=status.IsCurrent && Problems.Count == 0;
        }
    }

    // An intention fixes an observed whole stack. It reserves and delivers nothing.
    public sealed partial class ColonyService
    {
        public RationSupplyIntentSnapshot[] DelegatedSupplyIntents
        { get { return core.ReadRationSupplyIntents(Actor).Where(i => i.Actor == Actor).ToArray(); } }

        private RationSupplyIntentSnapshot SupplyIntentFor(string id)
        {
            var intent=DelegatedSupplyIntents.SingleOrDefault(i => i.Id == id);
            if (intent == null) throw new InvalidOperationException("Unknown delegated supply intention");
            return intent;
        }

        private void RejectSupplyIntentCause(string cause)
        {
            RejectSupplyTransferCause(cause);
            if (DelegatedSupplyIntents.Any(i => i.Cause == cause || i.CancelCause == cause))
                throw new InvalidOperationException("Cause already belongs to a delegated supply intention");
        }

        private void CheckNewSupplyIntentCause(string cause)
        {
            if (string.IsNullOrEmpty(cause)) throw new InvalidOperationException("Supply intention requires a cause");
            RejectReturnCause(cause);
            if (state.transports.Any(t => t.cause == cause) || state.physical.Any(t => t.cause == cause) ||
                core.ReadPawnCustodyTransfers(Actor).Any(t => t.Actor == Actor && t.Cause == cause))
                throw new InvalidOperationException("Supply intention cause belongs to another transition");
        }

        public string PrepareDelegatedSupplyIntent(string cause,string city,string entity,Caravan caravan,Pawn source,Thing food,int wholeCount)
        {
            Gate();
            var prior=DelegatedSupplyIntents.SingleOrDefault(i => i.Cause == cause);
            if (prior != null) return core.SupplyIntents.Prepare(Actor,cause,entity,city,caravan,source,food,wholeCount).Id;
            if (!GenCommandLine.CommandLineArgPassed("inh-supply-intent"))
                throw new InvalidOperationException("New supply intentions require the development command");
            CheckNewSupplyIntentCause(cause);
            var preview=PreviewDelegatedFoodSupply(city,entity,caravan);
            var selected=preview.Foods.SingleOrDefault(f => f.SourcePawn == source && f.Food == food);
            if (!preview.CanPreview || selected == null || !selected.CanPreview || selected.Count != wholeCount)
                throw new InvalidOperationException("Supply intention requires the exact current eligible whole stack");
            var prepared=core.SupplyIntents.Prepare(Actor,cause,entity,city,caravan,source,food,wholeCount);
            Validate(); return prepared.Id;
        }

        public void CancelDelegatedSupplyIntent(string cancelCause,string id)
        {
            Gate();
            var intent=SupplyIntentFor(id);
            if (intent.CancelCause == null) CheckNewSupplyIntentCause(cancelCause);
            core.SupplyIntents.Cancel(Actor,cancelCause,id); Validate();
        }

        public void ResumeDelegatedSupplyIntentProof(string id)
        { Gate(); SupplyIntentFor(id); core.SupplyIntents.ResumeProof(Actor,id); Validate(); }

        public DelegatedSupplyIntentStatusSnapshot InspectDelegatedSupplyIntent(string id)
        {
            Gate(); var intent=SupplyIntentFor(id);
            var status=core.SupplyIntents.Inspect(Actor,id); var problems=new List<string>();
            if (status.Problem != "None") problems.Add(status.Problem);
            // Later ordinary transport is allowed. It makes the intention stale,
            // rather than invalidating its historical certificate on save/load.
            if (intent.Stage != RationSupplyIntentStage.Cancelled)
            {
                var history=SupplyPreviewCargoHistory(intent.FoodNativeId);
                if (history.Any(h => h.JourneySupply)) problems.Add("HistoricalJourneySupply");
                if (history.Any(h => !h.JourneySupply)) problems.Add("HistoricalCargo");
                if (TransportReserved(intent.EntityId)) problems.Add("RecipientTransportReserved");
                if (intent.SourceEntityId != null && TransportReserved(intent.SourceEntityId)) problems.Add("SourceTransportReserved");
            }
            return new DelegatedSupplyIntentStatusSnapshot(status,problems);
        }

        private void ValidateDelegatedSupplyIntents(HashSet<string> known)
        {
            foreach (var intent in DelegatedSupplyIntents)
            {
                var site=DelegatedSite(intent.CityId);
                var store=core.ReadPawnCustodyTransfers(Actor).SingleOrDefault(t => t.Id == intent.StoreTransferId);
                if (site.tile != intent.Tile || store == null || store.Actor != Actor || store.IsRelease ||
                    store.Stage != PawnCustodyStage.Committed || store.EntityId != intent.EntityId || store.NativeId != intent.NativeId ||
                    store.CityId != intent.CityId || store.CaravanId != intent.CaravanId || store.Tile != intent.Tile || !known.Add(intent.Id))
                    throw new InvalidOperationException("Delegated supply intention disagrees with its original stored history");
                if (intent.CancelReceiptId != null && !known.Add(intent.CancelReceiptId))
                    throw new InvalidOperationException("Duplicated supply cancellation certificate");
                // Current residency, food count/owner and later cargo history are
                // observations, never immutable load preconditions for this intent.
                foreach (string cause in new[] {intent.Cause,intent.CancelCause}.Where(c => c != null))
                    if (state.transports.Any(t => t.cause == cause) || state.physical.Any(t => t.cause == cause) ||
                        state.returns.Any(t => t.cause == cause) || MaintenanceCauseUsed(cause) ||
                        DelegatedRations.Any(r => r.Cause == cause) ||
                        core.ReadPawnCustodyTransfers(Actor).Any(t => t.Actor == Actor && t.Cause == cause))
                        throw new InvalidOperationException("Supply intention cause overlaps another Colony transition");
            }
        }
    }
}
