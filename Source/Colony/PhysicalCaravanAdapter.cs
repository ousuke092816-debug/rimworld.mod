using System;
using System.Collections.Generic;
using System.Linq;
using Inheritance.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Colony
{
    public sealed class TransportMass
    {
        public readonly float Usage, Capacity;
        public bool Fits { get { return !float.IsNaN(Usage) && !float.IsInfinity(Usage) && !float.IsNaN(Capacity) && !float.IsInfinity(Capacity) && Usage <= Capacity; } }
        internal TransportMass(float usage,float capacity) { Usage=usage; Capacity=capacity; }
    }
    public sealed class CargoSelection
    {
        public readonly Thing Source;
        public readonly int Count;
        public readonly bool JourneySupply;
        public CargoSelection(Thing source, int count, bool journeySupply = false) { Source = source; Count = count; JourneySupply=journeySupply; }
    }
    // Technical prototype only: native caravans remain the physical owner and save authority.
    // The Colony journal and command UI coordinate reservations and receipts; delegated custody remains pending.
    public static class PhysicalCaravanAdapter
    {
        public static bool Eligible(Pawn pawn,Map origin)
        {
            return pawn != null && pawn.Spawned && pawn.Map == origin && pawn.Faction == Faction.OfPlayer && !pawn.Dead && !pawn.Downed && !pawn.Drafted && !pawn.InMentalState && pawn.inventory != null &&
                (pawn.RaceProps.Animal || pawn.RaceProps.Humanlike && pawn.IsColonist && CaravanUtility.IsOwner(pawn,Faction.OfPlayer) && !pawn.DevelopmentalStage.Baby());
        }
        public static TransportMass Measure(Pawn[] party,CargoSelection[] selections)
        {
            if (party == null || selections == null || party.Any(p => p == null || p.inventory == null) || selections.Any(s => s == null || s.Source == null || s.Count < 0)) return new TransportMass(float.NaN,0);
            // Use precisely the same aggregate calculation as Caravan.MassUsage/MassCapacity.
            float usage=CollectionsMassCalculator.MassUsage(party.ToList(),IgnorePawnsInventoryMode.DontIgnore,false,false);
            foreach (var s in selections) usage+=s.Source.GetStatValue(StatDefOf.Mass)*s.Count;
            return new TransportMass(usage,CollectionsMassCalculator.Capacity(party.ToList(),null));
        }
        private static void Gate(CoreWorldComponent core)
        {
            if (!ColonyMod.PhysicalAllowed) throw new InvalidOperationException("Physical transport is disabled in Colony settings");
            if (core == null || Find.World == null || core != Find.World.GetComponent<CoreWorldComponent>()) throw new InvalidOperationException("Physical adapter belongs to another world");
            core.Ledger.AssertWriteAccess(ColonyService.Actor);
        }
        public static void ValidateDeparture(CoreWorldComponent core, Map origin, Pawn[] party, CargoSelection[] selections)
        {
            Gate(core);
            if (origin == null || !Find.Maps.Contains(origin) || origin.ParentFaction != Faction.OfPlayer || party == null || party.Length == 0 || selections == null)
                throw new InvalidOperationException("A player map and a transport party are required");
            if (party.Any(p => !Eligible(p,origin)) || !party.Any(p => p.RaceProps.Humanlike && CaravanUtility.IsOwner(p,Faction.OfPlayer)) || party.Distinct().Count() != party.Length)
                throw new InvalidOperationException("Transport party changed or cannot depart");
            if (selections.Any(s => s == null || s.Source == null || !s.Source.Spawned || s.Source.Map != origin || s.Source.Destroyed || s.Source.def.category != ThingCategory.Item || s.Count <= 0 || s.Count > s.Source.stackCount) ||
                selections.Select(s => s.Source.GetUniqueLoadID()).Distinct().Count() != selections.Length)
                throw new InvalidOperationException("Physical cargo selection is stale, duplicate or overdrawn");
            if (selections.Any(s => s.JourneySupply && !s.Source.def.IsNutritionGivingIngestible)) throw new InvalidOperationException("Journey supplies must be nutritious food");
            foreach (var pawn in party) core.AssertNoActiveSupplyTransfer(nativeId:pawn.GetUniqueLoadID());
            foreach (var selection in selections) core.AssertNoActiveSupplyTransfer(foodId:selection.Source.GetUniqueLoadID());
            if (!Measure(party,selections).Fits) throw new InvalidOperationException("Selected party cannot carry this cargo weight");
        }
        public static Caravan Depart(CoreWorldComponent core, Map origin, Pawn[] party, CargoSelection[] selections)
        { Thing[] moved; return Depart(core,origin,party,selections,out moved); }
        public static Caravan Depart(CoreWorldComponent core, Map origin, Pawn[] party, CargoSelection[] selections, out Thing[] moved)
        {
            moved = null;
            ValidateDeparture(core,origin,party,selections);
            var positions = party.Select(p => p.Position).ToArray();
            var cargo = new List<Thing>(); var cargoCells = selections.Select(s => s.Source.Position).ToArray();
            Caravan caravan = null;
            try
            {
                foreach (var selection in selections)
                {
                    Thing part = selection.Source.SplitOff(selection.Count); cargo.Add(part);
                    if (part.Spawned) part.DeSpawn();
                    // Disable merging so the split cargo keeps its own native ID through save/reload.
                    var carrier=party.Where(MassUtility.CanEverCarryAnything).OrderByDescending(p => MassUtility.FreeSpace(p)).FirstOrDefault();
                    if (carrier == null || !carrier.inventory.innerContainer.TryAdd(part,false)) throw new InvalidOperationException("Carrier could not accept the selected cargo");
                }
                foreach (var pawn in party) pawn.DeSpawn();
                caravan = CaravanMaker.MakeCaravan(party,Faction.OfPlayer,origin.Tile,true);
                if (caravan == null || party.Any(p => !caravan.PawnsListForReading.Contains(p)) || cargo.Any(t => !party.Any(p => p.inventory.innerContainer.Contains(t))) || caravan.MassUsage > caravan.MassCapacity)
                    throw new InvalidOperationException("Native caravan did not retain the original party and cargo");
                moved = cargo.ToArray(); return caravan;
            }
            catch
            {
                // Keep original objects. A failed rollback is reported, never replaced with newly generated pawns/items.
                if (caravan != null && !caravan.Destroyed)
                    CaravanEnterMapUtility.Enter(caravan,origin,CaravanEnterMode.Edge,CaravanDropInventoryMode.DoNotDrop,false,null);
                for (int i = 0; i < party.Length; i++) if (!party[i].Spawned) GenSpawn.Spawn(party[i],positions[i],origin);
                for (int i = 0; i < cargo.Count; i++)
                {
                    Thing t = cargo[i]; if (t.Destroyed || t.Spawned) continue;
                    if (t.holdingOwner != null) t.holdingOwner.Remove(t);
                    if (!GenPlace.TryPlaceThing(t,cargoCells[i],origin,ThingPlaceMode.Near)) throw new InvalidOperationException("Failed transfer needs reconciliation: original cargo could not be restored");
                }
                throw;
            }
        }
        public static void EnterDestination(CoreWorldComponent core, Caravan caravan, Map destination)
        {
            Gate(core);
            if (caravan == null || caravan.Destroyed || !Find.WorldObjects.Caravans.Contains(caravan) || destination == null || !Find.Maps.Contains(destination) ||
                destination.ParentFaction != Faction.OfPlayer || caravan.Tile != destination.Tile)
                throw new InvalidOperationException("Caravan has not reached a valid player destination");
            var original = caravan.PawnsListForReading.ToArray();
            foreach (var pawn in original) {
                core.AssertNoActiveSupplyTransfer(nativeId:pawn.GetUniqueLoadID());
                if (pawn.inventory != null) foreach (var item in pawn.inventory.innerContainer) core.AssertNoActiveSupplyTransfer(foodId:item.GetUniqueLoadID());
            }
            CaravanEnterMapUtility.Enter(caravan,destination,CaravanEnterMode.Edge,CaravanDropInventoryMode.DoNotDrop,false,null);
            if (original.Any(p => !p.Spawned || p.Map != destination)) throw new InvalidOperationException("Destination entry needs reconciliation");
        }
    }
}
