using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Inheritance.Core;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Colony
{
    public sealed class DelegatedSupplyCargoHistorySnapshot
    {
        public readonly string ShipmentId, Stage, SourceNativeId, CargoNativeId;
        public readonly bool JourneySupply, MatchesSourceId, MatchesCargoId;
        internal DelegatedSupplyCargoHistorySnapshot(PhysicalTransportRecord transport,PhysicalCargoRecord cargo,string native)
        {
            ShipmentId=transport.shipmentId; Stage=transport.stage.ToString();
            SourceNativeId=cargo.sourceNativeId; CargoNativeId=cargo.cargoNativeId;
            JourneySupply=cargo.journeySupply;
            MatchesSourceId=cargo.sourceNativeId == native; MatchesCargoId=cargo.cargoNativeId == native;
        }
    }

    public sealed class DelegatedSupplyFoodSnapshot
    {
        public readonly Pawn SourcePawn;
        public readonly Thing Food;
        public readonly string SourceName,SourceKind,SourceNativeId,SourceEntityId,FoodName,NativeId,DefName,StuffDef;
        public readonly int Count,HitPoints;
        public readonly float UnitNutrition,DailyDemand;
        public readonly double StackNutrition;
        public readonly bool CanPreview;
        public readonly ReadOnlyCollection<string> Problems;
        public readonly ReadOnlyCollection<DelegatedSupplyCargoHistorySnapshot> CargoHistory;
        internal DelegatedSupplyFoodSnapshot(Pawn source,string sourceEntity,Thing food,RationSupplyCandidateQuote quote,
            IEnumerable<string> problems,IEnumerable<DelegatedSupplyCargoHistorySnapshot> history)
        {
            SourcePawn=source; Food=food; SourceName=source.LabelShort;
            SourceKind=source.RaceProps.Animal ? "animal" : "person";
            SourceNativeId=source.GetUniqueLoadID(); SourceEntityId=sourceEntity;
            FoodName=food.Label; NativeId=food.GetUniqueLoadID(); Count=food.stackCount; HitPoints=food.HitPoints;
            DefName=food.def.defName; StuffDef=food.Stuff == null ? null : food.Stuff.defName;
            UnitNutrition=quote == null ? -1f : quote.UnitNutrition; DailyDemand=quote == null ? -1f : quote.DailyDemand;
            StackNutrition=UnitNutrition < 0f ? -1d : (double)UnitNutrition*Count;
            Problems=Array.AsReadOnly(problems.Distinct(StringComparer.Ordinal).ToArray());
            CargoHistory=Array.AsReadOnly(history.ToArray());
            CanPreview=Problems.Count == 0 && quote != null && quote.CanPreview;
        }
    }

    public sealed class DelegatedSupplyPreviewSnapshot
    {
        public readonly string CityId,CityName,EntityId,RecipientName,RecipientKind,OriginalCaravanId,CaravanName;
        public readonly Pawn Recipient;
        public readonly Caravan Caravan;
        public readonly float DailyDemand;
        public readonly bool CanPreview;
        public readonly ReadOnlyCollection<string> Problems;
        public readonly ReadOnlyCollection<DelegatedSupplyFoodSnapshot> Foods;
        internal DelegatedSupplyPreviewSnapshot(string city,string cityName,string entity,string anchor,Caravan caravan,
            RationSupplyCandidateQuote recipientQuote,IEnumerable<string> problems,IEnumerable<DelegatedSupplyFoodSnapshot> foods)
        {
            CityId=city; CityName=cityName; EntityId=entity; OriginalCaravanId=anchor; Caravan=caravan;
            CaravanName=caravan == null ? null : caravan.Label;
            Recipient=recipientQuote == null ? null : recipientQuote.Recipient;
            RecipientName=Recipient == null ? null : Recipient.LabelShort;
            RecipientKind=Recipient == null ? null : Recipient.RaceProps.Animal ? "animal" : "person";
            DailyDemand=recipientQuote == null ? -1f : recipientQuote.DailyDemand;
            Problems=Array.AsReadOnly(problems.Distinct(StringComparer.Ordinal).ToArray());
            Foods=Array.AsReadOnly(foods.ToArray());
            CanPreview=Problems.Count == 0 && Recipient != null && DailyDemand > 0f;
        }
    }

    // Staged only. Read-only inventory observations; no new IDs, receipt, cause or holder.
    public sealed partial class ColonyService
    {
        private static void SupplyPreviewProblem(List<string> problems,string problem)
        { if (problem != null && problem != "None" && !problems.Contains(problem)) problems.Add(problem); }

        private DelegatedSupplyCargoHistorySnapshot[] SupplyPreviewCargoHistory(string native)
        {
            // All stages remain immutable history. Journey supplies are not exempt.
            return state.physical.SelectMany(r => r.cargo.Where(c => c.sourceNativeId == native || c.cargoNativeId == native)
                .Select(c => new DelegatedSupplyCargoHistorySnapshot(r,c,native))).ToArray();
        }

        public DelegatedSupplyPreviewSnapshot PreviewDelegatedFoodSupply(string city,string entity,Caravan caravan)
        {
            var problems=new List<string>(); var foods=new List<DelegatedSupplyFoodSnapshot>();
            BaseRecord site=null; PawnCustodyResidentSnapshot resident=null; RationSupplyCandidateQuote recipientQuote=null;
            if (!core.IsReady)
            {
                problems.Add("WorldUnavailable");
                return new DelegatedSupplyPreviewSnapshot(city,null,entity,null,caravan,null,problems,foods);
            }
            // Existing gate only checks permission, thread and active validated world.
            // This preview is intentionally unavailable during bootstrap/load/save.
            Gate();
            try { site=DelegatedSite(city); }
            catch (InvalidOperationException) { problems.Add("Unlocated"); }
            resident=DelegatedStored().SingleOrDefault(r => r.EntityId == entity && r.CityId == city);
            if (resident == null) problems.Add("ResidentUnavailable");
            SupplyPreviewProblem(problems,CaravanSiteProblem(site,caravan));
            if (resident != null && (caravan == null || caravan.GetUniqueLoadID() != resident.CaravanId))
                SupplyPreviewProblem(problems,"NotOriginalCaravan");
            if (site != null && ledger.HasPendingReference(city)) problems.Add("CityLocked");
            if (resident != null)
            {
                if (TransportReserved(entity)) problems.Add("RecipientTransportReserved");
                if (core.ReadPawnCustodyTransfers(Actor).Any(t => t.EntityId == entity && t.Stage != PawnCustodyStage.Committed))
                    problems.Add("RecipientPendingCustody");
                if (core.Rations.HasPending(entity)) problems.Add("RecipientPendingRations");
                recipientQuote=core.Rations.QuoteSupplyCandidate(Actor,entity,city,null,null);
                SupplyPreviewProblem(problems,recipientQuote.RecipientProblem);
                // null source/food here intentionally obtains only recipient eligibility and demand.
                if (recipientQuote.Recipient != null && recipientQuote.Recipient.inventory != null)
                {
                    var recipientHistory=recipientQuote.Recipient.inventory.innerContainer
                        .Where(t => t.def.IsNutritionGivingIngestible)
                        .SelectMany(t => SupplyPreviewCargoHistory(t.GetUniqueLoadID())).ToArray();
                    if (recipientHistory.Any(h => !h.JourneySupply)) problems.Add("RecipientHistoricalCargo");
                    if (recipientHistory.Any(h => h.JourneySupply)) problems.Add("RecipientHistoricalJourneySupply");
                }
            }
            if (ManagedCaravan(caravan))
            {
                foreach (Pawn source in caravan.PawnsListForReading.Where(p => p != null && (p.RaceProps.Humanlike || p.RaceProps.Animal))
                    .OrderBy(p => p.LabelShort,StringComparer.Ordinal).ThenBy(p => p.GetUniqueLoadID(),StringComparer.Ordinal))
                {
                    if (source.inventory == null || source.inventory.innerContainer == null) continue;
                    var sourceProblems=new List<string>(problems);
                    string sourceEntity=RegisteredPawnId(source);
                    if (!CaravanOriginal(caravan,source)) sourceProblems.Add("SourceIneligible");
                    if (NativeCustodyReserved(source)) sourceProblems.Add("SourceNativeCustodyReserved");
                    if (sourceEntity != null && TransportReserved(sourceEntity)) sourceProblems.Add("SourceTransportReserved");
                    if (sourceEntity != null && core.Rations.HasPending(sourceEntity)) sourceProblems.Add("SourcePendingRations");
                    try { CheckDelegatedOriginal(source,sourceEntity); }
                    catch (InvalidOperationException) { SupplyPreviewProblem(sourceProblems,"SourceIneligible"); }
                    // Broad discovery only: rejected nutrition-giving foods remain visible.
                    // Core PlainFood is the sole eligibility predicate.
                    foreach (Thing food in source.inventory.innerContainer.Where(t => t != null && t.def.IsNutritionGivingIngestible)
                        .OrderBy(t => t.GetUniqueLoadID(),StringComparer.Ordinal))
                    {
                        var rowProblems=new List<string>(sourceProblems);
                        var history=SupplyPreviewCargoHistory(food.GetUniqueLoadID());
                        if (history.Any(h => !h.JourneySupply)) rowProblems.Add("HistoricalCargo");
                        if (history.Any(h => h.JourneySupply)) rowProblems.Add("HistoricalJourneySupply");
                        RationSupplyCandidateQuote quote=resident == null ? null : core.Rations.QuoteSupplyCandidate(Actor,entity,city,source,food);
                        if (quote != null)
                        {
                            SupplyPreviewProblem(rowProblems,quote.RecipientProblem);
                            SupplyPreviewProblem(rowProblems,quote.FoodProblem);
                        }
                        foods.Add(new DelegatedSupplyFoodSnapshot(source,sourceEntity,food,quote,rowProblems,history));
                    }
                }
            }
            return new DelegatedSupplyPreviewSnapshot(city,site == null ? null : site.name,entity,
                resident == null ? null : resident.CaravanId,caravan,recipientQuote,problems,foods);
        }
    }
}
