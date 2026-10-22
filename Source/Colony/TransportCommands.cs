using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Inheritance.Colony
{
    public sealed class CargoOption
    {
        public readonly Thing Thing;
        public readonly int Available;
        public readonly bool Food;
        internal CargoOption(Thing thing,int available) { Thing=thing; Available=available; Food=thing.def.IsNutritionGivingIngestible; }
    }
    public sealed partial class ColonyService
    {
        public bool CommandsReady { get { return core.IsReady && ColonyMod.PhysicalAllowed; } }
        public Pawn[] AvailableTransportPawns(string baseId)
        {
            var held=new HashSet<string>(state.physical.Where(ActivePhysical).SelectMany(r => r.party).Select(id => state.people.Single(p => p.id == id).nativeId));
            var map=DirectMap(baseId);
            return map.mapPawns.FreeColonistsSpawned.Where(p => PhysicalCaravanAdapter.Eligible(p,map) && !held.Contains(p.GetUniqueLoadID()) && !NativeCustodyReserved(p) &&
                !ledger.Entities.Any(e => e.BoundThingLoadId == p.GetUniqueLoadID() && e.Provider != Actor)).OrderBy(p => p.GetUniqueLoadID(),StringComparer.Ordinal).ToArray();
        }
        public Pawn[] AvailableTransportAnimals(string baseId)
        {
            var held=new HashSet<string>(state.physical.Where(ActivePhysical).SelectMany(r => r.animals).Select(id => state.animals.Single(a => a.id == id).nativeId));
            var map=DirectMap(baseId);
            return map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Animal && PhysicalCaravanAdapter.Eligible(p,map) && !held.Contains(p.GetUniqueLoadID()) && !NativeCustodyReserved(p) &&
                !ledger.Entities.Any(e => e.BoundThingLoadId == p.GetUniqueLoadID() && e.Provider != Actor)).OrderBy(p => p.GetUniqueLoadID(),StringComparer.Ordinal).ToArray();
        }
        public CargoOption[] AvailableTransportCargo(string baseId)
        {
            var held=state.physical.Where(r => ActivePhysical(r) && r.caravanId == null && r.departureReceipt == null).SelectMany(r => r.cargo).GroupBy(c => c.sourceNativeId).ToDictionary(g => g.Key,g => g.Sum(c => (long)c.count));
            return DirectMap(baseId).listerThings.AllThings.Where(t => t.Spawned && !t.Destroyed && t.def.category == ThingCategory.Item && !t.IsForbidden(Faction.OfPlayer))
                .Select(t => new CargoOption(t,(int)Math.Max(0,t.stackCount-(held.ContainsKey(t.GetUniqueLoadID()) ? held[t.GetUniqueLoadID()] : 0))))
                .Where(c => c.Available > 0).OrderBy(c => c.Thing.LabelCap.ToString(),StringComparer.Ordinal).ThenBy(c => c.Thing.GetUniqueLoadID(),StringComparer.Ordinal).ToArray();
        }
    }
    public sealed class TransportDraft
    {
        private readonly ColonyService service;
        private readonly string cause="order/"+Guid.NewGuid().ToString("N");
        private readonly HashSet<string> people=new HashSet<string>();
        private readonly HashSet<string> animals=new HashSet<string>();
        private readonly Dictionary<string,int> quantities=new Dictionary<string,int>();
        private readonly HashSet<string> supplies=new HashSet<string>();
        private string shipment;
        public string SourceId { get; private set; }
        public string DestinationId { get; private set; }
        public string Cause { get { return cause; } }
        public bool Submitted { get { return shipment != null; } }
        public int SelectedPeople { get { return people.Count; } }
        public int SelectedAnimals { get { return animals.Count; } }
        public long SelectedItems { get { return quantities.Values.Sum(n => (long)n); } }
        public TransportMass Mass
        {
            get
            {
                if (SourceId == null) return new TransportMass(0,0);
                try { Pawn[] party; CargoSelection[] cargo; ReadSelections(out party,out cargo); return PhysicalCaravanAdapter.Measure(party,cargo); }
                catch (InvalidOperationException) { return new TransportMass(float.NaN,0); }
            }
        }
        public bool CanSubmit { get { return !Submitted && service.CommandsReady && SourceId != null && DestinationId != null && SourceId != DestinationId && people.Count > 0 && quantities.Values.Any(n => n > 0) && Mass.Fits; } }
        public float FoodDays
        {
            get
            {
                if (SourceId == null) return float.NaN;
                try { Pawn[] party; CargoSelection[] cargo; ReadSelections(out party,out cargo); return service.EstimateTransportFood(SourceId,party,cargo); }
                catch (InvalidOperationException) { return float.NaN; }
            }
        }
        public TransportDraft(ColonyService service) { this.service=service; }
        private void Editable() { if (Submitted) throw new InvalidOperationException("Submitted transport draft is immutable"); }
        public void SetSource(string id)
        { Editable(); if (SourceId != id) { SourceId=id; people.Clear(); animals.Clear(); quantities.Clear(); supplies.Clear(); if (DestinationId == id) DestinationId=null; } }
        public void SetDestination(string id) { Editable(); DestinationId=id; }
        public bool PawnSelected(Pawn pawn) { return people.Contains(pawn.GetUniqueLoadID()) || animals.Contains(pawn.GetUniqueLoadID()); }
        public void SelectPawn(Pawn pawn,bool select) { Editable(); if (pawn == null) throw new InvalidOperationException("A pawn is required"); var selected=pawn.RaceProps.Animal ? animals : people; if (select) selected.Add(pawn.GetUniqueLoadID()); else selected.Remove(pawn.GetUniqueLoadID()); }
        public int Quantity(Thing thing) { int count; return quantities.TryGetValue(thing.GetUniqueLoadID(),out count) ? count : 0; }
        public bool Supply(Thing thing) { return supplies.Contains(thing.GetUniqueLoadID()); }
        public void SetCargo(Thing thing,int count,bool supply)
        { Editable(); if (count < 0) throw new InvalidOperationException("Cargo count cannot be negative"); string id=thing.GetUniqueLoadID(); if (count == 0) quantities.Remove(id); else quantities[id]=count; if (supply) supplies.Add(id); else supplies.Remove(id); }
        public string Submit()
        {
            if (shipment != null) return shipment;
            if (!CanSubmit) throw new InvalidOperationException("Select different direct bases, a party and cargo");
            Pawn[] party; CargoSelection[] cargo; ReadSelections(out party,out cargo);
            shipment=service.ReservePhysicalTransport(cause,SourceId,DestinationId,party,cargo); return shipment;
        }
        private void ReadSelections(out Pawn[] party,out CargoSelection[] cargo)
        {
            var candidates=service.AvailableTransportPawns(SourceId).Concat(service.AvailableTransportAnimals(SourceId)).ToArray();
            party=people.OrderBy(id => id,StringComparer.Ordinal).Concat(animals.OrderBy(id => id,StringComparer.Ordinal)).Select(id => candidates.SingleOrDefault(p => p.GetUniqueLoadID() == id)).ToArray();
            var options=service.AvailableTransportCargo(SourceId);
            cargo=quantities.OrderBy(kv => kv.Key,StringComparer.Ordinal).Select(kv =>
            {
                var option=options.SingleOrDefault(c => c.Thing.GetUniqueLoadID() == kv.Key);
                if (option == null || option.Available < kv.Value) throw new InvalidOperationException("Selected physical cargo changed before reservation");
                return new CargoSelection(option.Thing,kv.Value,supplies.Contains(kv.Key));
            }).ToArray();
        }
    }
    public sealed class TransportCommandController
    {
        private readonly ColonyService service;
        public TransportCommandController(ColonyService service) { this.service=service; }
        public void Dispatch(string cause) { service.DispatchPhysical(cause); service.StartPhysicalRoute(cause); }
        public void Cancel(string cause) { service.CancelPhysical(cause); }
        public void Route(string cause) { service.StartPhysicalRoute(cause); }
        public void Deliver(string cause) { service.DeliverPhysical(cause); }
        public void Recheck(string cause) { service.RecheckPhysical(cause); }
        public string AcceptLoss(PhysicalLossPreview preview) { return service.AcceptPhysicalLoss(preview); }
    }
}
