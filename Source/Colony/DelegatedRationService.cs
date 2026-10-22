using System;
using System.Collections.Generic;
using System.Linq;
using Inheritance.Core;
using Verse;

namespace Inheritance.Colony
{
    // Explicit development command: one prepaid daily maintenance charge.
    // This does not run ordinary pawn needs, health, skills or ingestion callbacks.
    public sealed partial class ColonyService
    {
        public RationDebitSnapshot[] DelegatedRations
        { get { return core.ReadRationDebits(Actor).Where(r => r.Actor == Actor).ToArray(); } }

        public string PrepareDelegatedRations(string cause,string city,string entity)
        { return PrepareDelegatedRations(cause,city,entity,false); }
        private string PrepareDelegatedRations(string cause,string city,string entity,bool maintenance)
        {
            Gate();
            int day=Find.TickManager.TicksGame/60000;
            var prior=DelegatedRations.SingleOrDefault(r => r.Cause == cause);
            if (prior != null) return core.Rations.Prepare(Actor,cause,entity,city,prior.Day).Id;
            if (!core.IsReady || !maintenance && !GenCommandLine.CommandLineArgPassed("inh-delegated-rations"))
                throw new InvalidOperationException("New delegated ration charges require the development command");
            if (!maintenance) RejectMaintenanceCause(cause);
            if (state.returns.Any(r => r.cause == cause) || state.physical.Any(r => r.cause == cause) || state.transports.Any(r => r.cause == cause) ||
                core.ReadPawnCustodyTransfers(Actor).Any(r => r.Actor == Actor && r.Cause == cause))
                throw new InvalidOperationException("Ration cause already belongs to another transition");
            DelegatedSite(city);
            var resident=DelegatedStored().SingleOrDefault(r => r.EntityId == entity && r.CityId == city);
            if (resident == null || TransportReserved(entity)) throw new InvalidOperationException("Rations require an unreserved delegated resident");
            Pawn pawn=Original(entity);
            if (pawn.inventory.innerContainer.Any(t => t.def.IsNutritionGivingIngestible &&
                state.physical.SelectMany(r => r.cargo).Any(c => c.cargoNativeId == t.GetUniqueLoadID())))
                throw new InvalidOperationException("Shipment food remains reserved by its immutable cargo manifest");
            string id=core.Rations.Prepare(Actor,cause,entity,city,day).Id;
            Validate(); return id;
        }
        private RationDebitSnapshot RationFor(string id)
        {
            var r=DelegatedRations.SingleOrDefault(x => x.Id == id);
            if (r == null) throw new InvalidOperationException("Unknown delegated ration debit");
            return r;
        }
        public void ApplyDelegatedRations(string id)
        { Gate(); RationFor(id); core.Rations.Apply(id); Validate(); }
        public void CommitDelegatedRations(string id)
        { Gate(); RationFor(id); core.Rations.Commit(id); Validate(); }
        public void ResumeDelegatedRations(string id)
        { ApplyDelegatedRations(id); CommitDelegatedRations(id); }

        private void ValidateDelegatedRations(HashSet<string> known)
        {
            var transfers=core.ReadPawnCustodyTransfers(Actor);
            foreach (var r in DelegatedRations)
            {
                DelegatedSite(r.CityId);
                var original=transfers.SingleOrDefault(t => t.Id == r.StoreTransferId);
                if (!known.Add(r.Id) || original == null || original.Actor != Actor || original.IsRelease || original.CaravanId == null ||
                    original.Stage != PawnCustodyStage.Committed || original.EntityId != r.EntityId || original.CityId != r.CityId || original.NativeId != r.NativeId ||
                    ledger.GetEntity(r.EntityId).Provider != Actor ||
                    state.physical.Where(p => p.caravanId == original.CaravanId && p.destinationId == r.CityId)
                        .SelectMany(p => p.cargo).Any(c => r.Lines.Any(l => l.SourceNativeId == c.cargoNativeId)))
                    throw new InvalidOperationException("Delegated ration certificate disagrees with its resident or cargo reservation");
                if (state.returns.Any(t => t.cause == r.Cause) || state.physical.Any(t => t.cause == r.Cause) || state.transports.Any(t => t.cause == r.Cause) ||
                    transfers.Any(t => t.Actor == Actor && t.Cause == r.Cause))
                    throw new InvalidOperationException("Ration cause overlaps another Colony transition");
                if (r.AppliedReceiptId != null && !known.Add(r.AppliedReceiptId)) throw new InvalidOperationException("Duplicate applied ration certificate");
            }
        }
    }
}
