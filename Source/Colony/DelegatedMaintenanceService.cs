using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Inheritance.Core;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Colony
{
    public sealed class DelegatedMaintenanceResidentSnapshot
    {
        public readonly string EntityId,Name,Kind,CaravanId,Problem;
        public readonly float AvailableNutrition,DailyDemand;
        internal DelegatedMaintenanceResidentSnapshot(string entity,Pawn pawn,string caravan,string problem,float available,float demand)
        { EntityId=entity; Name=pawn.LabelShort; Kind=pawn.RaceProps.Animal ? "animal" : "person"; CaravanId=caravan; Problem=problem; AvailableNutrition=available; DailyDemand=demand; }
    }
    public sealed class DelegatedMaintenanceSnapshot
    {
        public readonly string Id,Cause,CityId,EntityId,Name,Problem;
        public readonly int StartDay,NextDay,LastPaidDay;
        public readonly float AvailableNutrition,DailyDemand;
        public readonly DelegatedMaintenanceStage Stage;
        internal DelegatedMaintenanceSnapshot(DelegatedMaintenanceRecord plan,DelegatedMaintenanceStage stage,string problem,int next,int paid,Pawn pawn,RationQuote quote)
        { Id=plan.id; Cause=plan.cause; CityId=plan.cityId; EntityId=plan.entityId; Name=pawn == null ? null : pawn.LabelShort; StartDay=plan.startDay; NextDay=next; LastPaidDay=paid; Stage=stage; Problem=problem; AvailableNutrition=quote == null ? -1f : quote.AvailableNutrition; DailyDemand=quote == null ? -1f : quote.DailyDemand; }
    }

    // Only maintenance accounting is enabled. The original pawn remains frozen.
    // The clock is derived from sealed controls and Core daily receipts, not mutable XML dates.
    public sealed partial class ColonyService
    {
        private const string MaintenanceControlPrefix="maintenance-control/",MaintenanceDayPrefix="maintenance-day/";
        private int maintenanceObservedDay=-1,maintenanceNextPollTick;
        public bool HasDelegatedMaintenance { get { return state.maintenance.Count > 0 || DelegatedStored().Length > 0; } }
        public bool MaintenanceCommandsReady { get { return core.IsReady && ColonyMod.MaintenanceAllowed; } }
        private static int MaintenanceDay { get { return Find.TickManager.TicksGame/60000; } }
        private static string DayText(int day) { return day.ToString(CultureInfo.InvariantCulture); }
        private MaintenanceControlRecord LastControl(DelegatedMaintenanceRecord plan) { return plan.controls.Last(); }
        private bool MaintenanceCauseUsed(string cause)
        { return state.maintenance.Any(p => p.controls.Any(c => c.cause == cause)); }
        private void RejectMaintenanceCause(string cause)
        {
            RejectSupplyIntentCause(cause);
            if (cause != null && (cause.StartsWith(MaintenanceDayPrefix,StringComparison.Ordinal) || cause.StartsWith(MaintenanceControlPrefix,StringComparison.Ordinal) || MaintenanceCauseUsed(cause)))
                throw new InvalidOperationException("Cause already belongs to delegated maintenance");
        }
        private void CheckNewMaintenanceCause(string cause)
        {
            if (string.IsNullOrEmpty(cause)) throw new InvalidOperationException("Maintenance requires a cause");
            RejectReturnCause(cause);
            if (state.physical.Any(r => r.cause == cause) || state.transports.Any(r => r.cause == cause) ||
                core.ReadPawnCustodyTransfers(Actor).Any(r => r.Cause == cause))
                throw new InvalidOperationException("Maintenance cause belongs to another transition");
        }
        private string ControlIdentity(DelegatedMaintenanceRecord plan,MaintenanceControlRecord control)
        {
            return "maintenance-control-v1:"+Hash(new[] {plan.cause,plan.cityId,plan.entityId,plan.storeTransferId,DayText(plan.startDay),
                control.cause,control.kind.ToString(),DayText(control.day),control.previousReceipt,control.problem});
        }
        private void AddMaintenanceControl(DelegatedMaintenanceRecord plan,string cause,MaintenanceControlKind kind,string problem)
        {
            if (plan.controls.Count > 0 && MaintenanceDay < LastControl(plan).day) throw new InvalidOperationException("Maintenance clock moved before its last control");
            var record=new MaintenanceControlRecord {cause=cause,kind=kind,day=MaintenanceDay,problem=problem,
                previousReceipt=plan.controls.Count == 0 ? null : LastControl(plan).receipt};
            // No Core reference edges: a pending food certificate may lock the city.
            // The full city, individual and original store identity are sealed in the hash.
            record.receipt=Create(MaintenanceControlPrefix+cause,new EntitySpec("lease",identityKey:ControlIdentity(plan,record)));
            plan.controls.Add(record);
            if (kind == MaintenanceControlKind.Start) plan.id=record.receipt;
        }
        private DelegatedMaintenanceRecord MaintenancePlan(string id)
        { var plan=state.maintenance.SingleOrDefault(p => p.id == id); if (plan == null) throw new InvalidOperationException("Unknown maintenance plan"); return plan; }
        private PawnCustodyResidentSnapshot PlanResident(DelegatedMaintenanceRecord plan)
        { return DelegatedStored().SingleOrDefault(r => r.EntityId == plan.entityId && r.CityId == plan.cityId && r.StoreTransferId == plan.storeTransferId); }
        private RationDebitSnapshot[] PlanRations(DelegatedMaintenanceRecord plan)
        { return DelegatedRations.Where(r => r.EntityId == plan.entityId && r.CityId == plan.cityId && r.StoreTransferId == plan.storeTransferId).ToArray(); }
        private int NextMaintenanceDay(DelegatedMaintenanceRecord plan)
        {
            int next=plan.controls.Last(c => c.kind == MaintenanceControlKind.Start || c.kind == MaintenanceControlKind.Resume).day;
            var paid=new HashSet<int>(PlanRations(plan).Where(r => r.Stage == RationDebitStage.Committed).Select(r => r.Day));
            while (paid.Contains(next)) next=checked(next+1);
            return next;
        }
        private string MaintenanceResidentProblem(PawnCustodyResidentSnapshot resident,out Pawn pawn,out RationQuote quote)
        {
            pawn=null; quote=null;
            try { pawn=Original(resident.EntityId); } catch (InvalidOperationException) { return "ResidentUnavailable"; }
            var caravan=Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == resident.CaravanId);
            if (caravan == null || caravan.Destroyed) return "AnchorMissing";
            if (core.ReadPawnCustodyTransfers(Actor).Any(t => t.EntityId == resident.EntityId && t.Stage != PawnCustodyStage.Committed) ||
                DelegatedRations.Any(r => r.EntityId == resident.EntityId && r.Stage != RationDebitStage.Committed) || ledger.HasPendingReference(resident.CityId)) return "Pending";
            if (TransportReserved(resident.EntityId)) return "ResidentUnavailable";
            if (pawn.inventory != null && pawn.inventory.innerContainer.Any(t => t.def.IsNutritionGivingIngestible &&
                state.physical.SelectMany(r => r.cargo).Any(c => c.cargoNativeId == t.GetUniqueLoadID()))) return "CargoReserved";
            if (!core.IsReady) return "ResidentUnavailable";
            try { quote=core.Rations.Quote(Actor,resident.EntityId,resident.CityId); }
            catch (InvalidOperationException) { return "Unsupported"; }
            return quote.CanPay ? "None" : "Shortage";
        }
        public DelegatedMaintenanceResidentSnapshot[] DelegatedResidentMaintenance(string city)
        {
            var site=RequireBase(city);
            if (site.mode != BaseMode.Delegated) throw new InvalidOperationException("Maintenance belongs to a delegated site");
            if (site.tile < 0) return new DelegatedMaintenanceResidentSnapshot[0];
            return DelegatedStored().Where(r => r.CityId == city).Select(r => {
                Pawn pawn; RationQuote quote; string problem=MaintenanceResidentProblem(r,out pawn,out quote);
                if (pawn == null) pawn=Original(r.EntityId);
                return new DelegatedMaintenanceResidentSnapshot(r.EntityId,pawn,r.CaravanId,problem,quote == null ? -1f : quote.AvailableNutrition,quote == null ? -1f : quote.DailyDemand);
            }).ToArray();
        }
        public DelegatedMaintenanceSnapshot[] DelegatedMaintenance
        {
            get
            {
                return state.maintenance.Select(plan => {
                    var resident=PlanResident(plan); Pawn pawn=null; RationQuote quote=null;
                    var last=LastControl(plan); string problem="None";
                    var stage=last.kind == MaintenanceControlKind.Stop ? DelegatedMaintenanceStage.Stopped :
                        last.kind == MaintenanceControlKind.Hold ? DelegatedMaintenanceStage.Held : DelegatedMaintenanceStage.Active;
                    if (resident == null)
                    {
                        stage=DelegatedMaintenanceStage.Released; problem="ResidenceChanged";
                        try { pawn=Original(plan.entityId); } catch (InvalidOperationException) { }
                    }
                    else
                    {
                        string current=MaintenanceResidentProblem(resident,out pawn,out quote);
                        if (stage == DelegatedMaintenanceStage.Held) problem=last.problem;
                        else if (stage == DelegatedMaintenanceStage.Active && current != "None") problem=current;
                    }
                    var paid=PlanRations(plan).Where(r => r.Stage == RationDebitStage.Committed).Select(r => r.Day).ToArray();
                    return new DelegatedMaintenanceSnapshot(plan,stage,problem,NextMaintenanceDay(plan),paid.Length == 0 ? -1 : paid.Max(),pawn,quote);
                }).ToArray();
            }
        }
        public string StartDelegatedMaintenance(string cause,string city,string entity)
        {
            Gate();
            var prior=state.maintenance.SingleOrDefault(p => p.cause == cause);
            if (prior != null)
            { if (prior.cityId != city || prior.entityId != entity) throw new InvalidOperationException("Maintenance start cause was reused"); return prior.id; }
            CheckNewMaintenanceCause(cause);
            if (!MaintenanceCommandsReady) throw new InvalidOperationException("New maintenance plans require the maintenance setting");
            DelegatedSite(city);
            var resident=DelegatedStored().SingleOrDefault(r => r.CityId == city && r.EntityId == entity);
            if (resident == null || state.maintenance.Any(p => p.storeTransferId == resident.StoreTransferId))
                throw new InvalidOperationException("The original store already has a plan or is unavailable");
            Pawn pawn; RationQuote quote;
            string problem=MaintenanceResidentProblem(resident,out pawn,out quote);
            if (problem != "None" && problem != "Shortage") throw new InvalidOperationException("Maintenance original is unavailable: "+problem);
            if (DelegatedRations.Any(r => r.EntityId == entity && r.Day == MaintenanceDay && (r.CityId != city || r.StoreTransferId != resident.StoreTransferId)))
                throw new InvalidOperationException("The individual already paid in another residence today");
            var plan=new DelegatedMaintenanceRecord {cause=cause,cityId=city,entityId=entity,storeTransferId=resident.StoreTransferId,startDay=MaintenanceDay};
            core.AssertNoActiveSupplyTransfer(city,entity,resident.NativeId); AddMaintenanceControl(plan,cause,MaintenanceControlKind.Start,null); state.maintenance.Add(plan);
            TickMaintenancePlan(plan); Validate(); return plan.id;
        }
        public void StopDelegatedMaintenance(string cause,string id)
        {
            Gate(); var plan=MaintenancePlan(id);
            if (ReplayMaintenanceControl(plan,cause,MaintenanceControlKind.Stop)) return;
            CheckNewMaintenanceCause(cause);
            if (LastControl(plan).kind == MaintenanceControlKind.Stop) throw new InvalidOperationException("Maintenance is already stopped");
            AddMaintenanceControl(plan,cause,MaintenanceControlKind.Stop,null); Validate();
        }
        public void ResumeDelegatedMaintenance(string cause,string id)
        {
            Gate(); var plan=MaintenancePlan(id);
            if (ReplayMaintenanceControl(plan,cause,MaintenanceControlKind.Resume)) return;
            CheckNewMaintenanceCause(cause);
            if (!core.IsReady || PlanResident(plan) == null) throw new InvalidOperationException("Only the same original residence can resume");
            if (LastControl(plan).kind == MaintenanceControlKind.Start || LastControl(plan).kind == MaintenanceControlKind.Resume)
                throw new InvalidOperationException("Maintenance is already active");
            // Explicit acknowledgement: unpaid skipped days stay frozen and are not charged.
            core.AssertNoActiveSupplyTransfer(plan.cityId,plan.entityId); AddMaintenanceControl(plan,cause,MaintenanceControlKind.Resume,null); TickMaintenancePlan(plan); Validate();
        }
        private bool ReplayMaintenanceControl(DelegatedMaintenanceRecord plan,string cause,MaintenanceControlKind kind)
        {
            var prior=state.maintenance.SelectMany(p => p.controls.Select(c => new {Plan=p,Control=c})).SingleOrDefault(x => x.Control.cause == cause);
            if (prior == null) return false;
            if (prior.Plan != plan || prior.Control.kind != kind) throw new InvalidOperationException("Maintenance control cause was reused");
            return true;
        }
        private void HoldMaintenance(DelegatedMaintenanceRecord plan,string problem)
        {
            string cause="maintenance-hold/"+Hash(new[] {plan.id,LastControl(plan).receipt,DayText(MaintenanceDay),problem});
            AddMaintenanceControl(plan,cause,MaintenanceControlKind.Hold,problem);
        }
        private void TickMaintenancePlan(DelegatedMaintenanceRecord plan)
        {
            var last=LastControl(plan);
            if (last.kind == MaintenanceControlKind.Stop || last.kind == MaintenanceControlKind.Hold || PlanResident(plan) == null) return;
            if (core.SupplyTransfers.HasActiveForCity(plan.cityId) || core.SupplyTransfers.HasActiveForEntity(plan.entityId)) return;
            int next=NextMaintenanceDay(plan),today=MaintenanceDay;
            if (next > today) return;
            if (next < today) { HoldMaintenance(plan,"DayGap"); return; }
            var resident=PlanResident(plan); Pawn pawn; RationQuote quote;
            string problem=MaintenanceResidentProblem(resident,out pawn,out quote);
            if (problem != "None") { HoldMaintenance(plan,problem); return; }
            if (DelegatedRations.Any(r => r.EntityId == plan.entityId && r.Day == today && (r.CityId != plan.cityId || r.StoreTransferId != plan.storeTransferId)))
            { HoldMaintenance(plan,"ResidenceChanged"); return; }
            string cause=MaintenanceDayPrefix+plan.id+"/"+DayText(today);
            try { string debit=PrepareDelegatedRations(cause,plan.cityId,plan.entityId,true); ResumeDelegatedRations(debit); }
            catch (InvalidOperationException error)
            {
                // Never retry a native mutation automatically. The exact Core checkpoint stays recoverable.
                HoldMaintenance(plan,DelegatedRations.Any(r => r.EntityId == plan.entityId && r.Stage != RationDebitStage.Committed) ? "Pending" : "ResidentUnavailable");
                Log.Message("[Inheritance-Colony] Maintenance held: "+error.Message);
            }
        }
        private void TickDelegatedMaintenance()
        {
            if (!core.IsReady || state.maintenance.Count == 0) return;
            int tick=Find.TickManager.TicksGame,day=MaintenanceDay;
            if (day == maintenanceObservedDay && tick < maintenanceNextPollTick) return;
            maintenanceObservedDay=day; maintenanceNextPollTick=checked(tick+250);
            foreach (var plan in state.maintenance.ToArray()) TickMaintenancePlan(plan);
        }
        private void ValidateDelegatedMaintenance(HashSet<string> known)
        {
            if (state.maintenance.Any(p => p == null || string.IsNullOrEmpty(p.id) || string.IsNullOrEmpty(p.cause) || p.startDay < 0 || p.controls == null || p.controls.Count == 0) ||
                state.maintenance.Select(p => p.id).Distinct().Count() != state.maintenance.Count ||
                state.maintenance.Select(p => p.storeTransferId).Distinct().Count() != state.maintenance.Count)
                throw new InvalidOperationException("Invalid or duplicate maintenance plans");
            var causes=new HashSet<string>(StringComparer.Ordinal); var operations=new HashSet<string>(StringComparer.Ordinal);
            foreach (var plan in state.maintenance)
            {
                DelegatedSite(plan.cityId);
                var store=core.ReadPawnCustodyTransfers(Actor).SingleOrDefault(t => t.Id == plan.storeTransferId);
                if (store == null || store.Actor != Actor || store.IsRelease || store.CaravanId == null || store.Stage != PawnCustodyStage.Committed ||
                    store.EntityId != plan.entityId || store.CityId != plan.cityId || plan.id != plan.controls[0].receipt ||
                    plan.controls[0].kind != MaintenanceControlKind.Start || plan.controls[0].day != plan.startDay || plan.controls[0].cause != plan.cause)
                    throw new InvalidOperationException("Maintenance plan disagrees with its original residence");
                string previous=null; int day=-1; MaintenanceControlKind kind=MaintenanceControlKind.Stop;
                foreach (var control in plan.controls)
                {
                    if (control == null) throw new InvalidOperationException("Missing maintenance control");
                    bool validHold=control.problem == "Shortage" || control.problem == "DayGap" || control.problem == "Pending" || control.problem == "AnchorMissing" ||
                        control.problem == "Unsupported" || control.problem == "CargoReserved" || control.problem == "ResidentUnavailable" || control.problem == "ResidenceChanged";
                    if (control == null || string.IsNullOrEmpty(control.cause) || !causes.Add(control.cause) || control.day < day || control.day < 0 || control.previousReceipt != previous ||
                        !Enum.IsDefined(typeof(MaintenanceControlKind),control.kind) || (control.kind == MaintenanceControlKind.Hold ? !validHold : control.problem != null) ||
                        previous != null && (control.kind == MaintenanceControlKind.Start || control.kind == MaintenanceControlKind.Hold && (kind == MaintenanceControlKind.Stop || kind == MaintenanceControlKind.Hold) ||
                            control.kind == MaintenanceControlKind.Resume && (kind == MaintenanceControlKind.Start || kind == MaintenanceControlKind.Resume) || control.kind == MaintenanceControlKind.Stop && kind == MaintenanceControlKind.Stop))
                        throw new InvalidOperationException("Maintenance control chain is invalid");
                    var op=ledger.FindOperation(Actor,"create",MaintenanceControlPrefix+control.cause);
                    if (op == null || op.Stage != OperationStage.Committed || op.ResultId != control.receipt || !operations.Add(op.Id))
                        throw new InvalidOperationException("Maintenance control lacks its committed Core operation");
                    var receipt=ledger.GetEntity(control.receipt);
                    if (!known.Add(receipt.Id) || receipt.Provider != Actor || receipt.Kind != "lease" || receipt.Status != EntityStatus.Active || receipt.Authority != SimulationAuthority.Abstract ||
                        receipt.IdentityKey != ControlIdentity(plan,control) || receipt.Quantity != 0 || receipt.CustodyId != null || receipt.OwnerId != null || receipt.BoundThingLoadId != null)
                        throw new InvalidOperationException("Maintenance control differs from its immutable Core certificate");
                    if (state.physical.Any(r => r.cause == control.cause) || state.transports.Any(r => r.cause == control.cause) || state.returns.Any(r => r.cause == control.cause) ||
                        DelegatedRations.Any(r => r.Cause == control.cause) || core.ReadPawnCustodyTransfers(Actor).Any(t => t.Cause == control.cause))
                        throw new InvalidOperationException("Maintenance cause overlaps another transition");
                    previous=control.receipt; day=control.day; kind=control.kind;
                }
            }
            if (ledger.Operations.Any(op => op.Actor == Actor && op.CauseId != null && op.CauseId.StartsWith(MaintenanceControlPrefix,StringComparison.Ordinal) && !operations.Contains(op.Id)))
                throw new InvalidOperationException("Orphan maintenance Core operation");
            foreach (var ration in DelegatedRations.Where(r => r.Cause.StartsWith(MaintenanceDayPrefix,StringComparison.Ordinal)))
            {
                var plan=state.maintenance.SingleOrDefault(p => ration.Cause == MaintenanceDayPrefix+p.id+"/"+DayText(ration.Day));
                if (plan == null || plan.entityId != ration.EntityId || plan.cityId != ration.CityId || plan.storeTransferId != ration.StoreTransferId || ration.Day < plan.startDay)
                    throw new InvalidOperationException("Maintenance daily debit is detached from its original plan");
            }
        }
    }
}
