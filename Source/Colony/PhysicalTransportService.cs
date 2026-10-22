using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Inheritance.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Colony
{
    public sealed partial class ColonyService
    {
        public IEnumerable<PhysicalTransportSnapshot> PhysicalTransports { get { return state.physical.Select(r => new PhysicalTransportSnapshot(r)).ToArray(); } }
        private static void PhysicalGate()
        { if (!ColonyMod.PhysicalAllowed) throw new InvalidOperationException("Physical transport is disabled in Colony settings"); }
        private Map DirectMap(string baseId)
        {
            var b = RequireBase(baseId);
            if (b.mode != BaseMode.Direct) throw new InvalidOperationException("Physical transport requires a direct destination map");
            return Find.Maps.Single(m => m.uniqueID == b.mapId && m.ParentFaction == Faction.OfPlayer);
        }
        private Pawn NativePawn(string nativeId)
        {
            return Find.Maps.SelectMany(m => m.mapPawns.AllPawns).Concat(Find.WorldPawns.AllPawnsAliveOrDead)
                .Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Concat(core.GetDirectlyHeldThings().Cast<Pawn>()).Distinct().SingleOrDefault(p => p.GetUniqueLoadID() == nativeId);
        }
        private Pawn[] Party(PhysicalTransportRecord r)
        { return r.party.Select(id => NativePawn(state.people.Single(p => p.id == id).nativeId)).Concat(r.animals.Select(id => NativePawn(state.animals.Single(a => a.id == id).nativeId))).ToArray(); }
        private CargoSelection[] Selections(PhysicalTransportRecord r)
        {
            Map map = DirectMap(r.sourceId);
            return r.cargo.Select(c => new CargoSelection(map.listerThings.AllThings.SingleOrDefault(t => t.GetUniqueLoadID() == c.sourceNativeId && t.def.defName == c.defName),c.count,c.journeySupply)).ToArray();
        }
        private PhysicalTransportRecord PhysicalJob(string cause)
        { var r = state.physical.SingleOrDefault(p => p.cause == cause); if (r == null) throw new InvalidOperationException("Unknown physical transport"); return r; }
        private static string Hash(IEnumerable<string> parts)
        {
            var text = new StringBuilder();
            foreach (var part in parts) { if (part == null) text.Append("-1:"); else text.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part); }
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
        }
        private string PhysicalManifest(PhysicalTransportRecord r)
        {
            var parts = new List<string> { r.cause,r.sourceId,r.destinationId,r.party.Count.ToString(CultureInfo.InvariantCulture),r.cargo.Count.ToString(CultureInfo.InvariantCulture) };
            foreach (string id in r.party) { parts.Add(id); parts.Add(state.people.Single(p => p.id == id).nativeId); }
            foreach (var c in r.cargo) { parts.Add(c.sourceNativeId); parts.Add(c.defName); parts.Add(c.count.ToString(CultureInfo.InvariantCulture)); }
            // Preserve old hashes for ordinary cargo. Supply requests add an explicit purpose extension.
            if (r.cargo.Any(c => c.journeySupply)) { parts.Add("journey-supply-v1"); parts.AddRange(r.cargo.Select(c => c.journeySupply ? "1" : "0")); }
            if (r.animals.Count > 0)
            {
                parts.Add("native-animals-v1"); parts.Add(r.animals.Count.ToString(CultureInfo.InvariantCulture));
                foreach (string id in r.animals) { var a=state.animals.Single(x => x.id == id); parts.Add(id); parts.Add(a.nativeId); parts.Add(a.defName); }
            }
            if (r.destinationTile >= 0)
            { parts.Add("delegated-destination-v1"); parts.Add(r.destinationTile.ToString(CultureInfo.InvariantCulture)); }
            return "physical:" + Hash(parts);
        }
        private static string ReceiptIdentity(PhysicalTransportRecord r,string phase)
        {
            var parts=new List<string>(new[] {r.shipmentId,r.caravanId}.Concat(r.cargo.Select(c => c.cargoNativeId)));
            if (phase == "arrive" && r.losses.Count > 0) { parts.Add("accepted-loss-v1"); parts.Add(r.losses.Last().receipt); }
            return "physical-" + phase + ":" + Hash(parts);
        }
        private bool ActivePhysical(PhysicalTransportRecord r)
        { return r.stage != PhysicalStage.Arrived && r.stage != PhysicalStage.Cancelled || ActiveDelegatedReturn(r); }
        private PlanetTile PhysicalDestination(PhysicalTransportRecord r)
        { return r.destinationTile >= 0 ? new PlanetTile(r.destinationTile,0) : DirectMap(r.destinationId).Tile; }
        private bool PhysicalTransitMatches(PhysicalTransportRecord r)
        { return r.destinationTile >= 0 ? DelegatedTransitMatches(r) : TransitMatches(r); }
        // Ownership is independent of eligibility for frozen storage: wounded arrivals remain observable.
        private bool DelegatedTransitMatches(PhysicalTransportRecord r)
        {
            if (!TransitMatches(r)) return false;
            var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == r.caravanId);
            var party=Party(r);
            return caravan.Spawned && caravan.IsPlayerControlled && caravan.PawnsListForReading.Count == party.Length &&
                party.All(p => !p.Destroyed && p.holdingOwner == caravan.GetDirectlyHeldThings() && Find.WorldPawns.Contains(p) &&
                    Find.WorldObjects.Caravans.Count(c => c.PawnsListForReading.Contains(p)) == 1);
        }
        public string ReservePhysicalTransport(string cause,string sourceId,string destinationId,Pawn[] party,CargoSelection[] cargo)
        {
            Gate(); PhysicalGate();
            RejectReturnCause(cause);
            if (string.IsNullOrEmpty(cause) || sourceId == destinationId || party == null || cargo == null || cargo.Length == 0) throw new InvalidOperationException("Invalid physical transport request");
            var prior = state.physical.SingleOrDefault(r => r.cause == cause);
            if (prior != null)
            {
                if (prior.sourceId != sourceId || prior.destinationId != destinationId ||
                    party.Any(p => p == null) || !prior.party.Select(id => state.people.Single(p => p.id == id).nativeId).SequenceEqual(party.Where(p => p.RaceProps.Humanlike).Select(p => p.GetUniqueLoadID())) ||
                    !prior.animals.Select(id => state.animals.Single(a => a.id == id).nativeId).SequenceEqual(party.Where(p => p.RaceProps.Animal).Select(p => p.GetUniqueLoadID())) || party.Any(p => !p.RaceProps.Humanlike && !p.RaceProps.Animal) ||
                    !prior.cargo.Select(c => c.sourceNativeId + "/" + c.count.ToString(CultureInfo.InvariantCulture) + "/" + c.journeySupply).SequenceEqual(cargo.Select(c => c == null || c.Source == null ? null : c.Source.GetUniqueLoadID() + "/" + c.Count.ToString(CultureInfo.InvariantCulture) + "/" + c.JourneySupply)))
                    throw new InvalidOperationException("Physical cause was reused for a different request");
                return prior.shipmentId;
            }
            Map origin = DirectMap(sourceId);
            var destination=RequireBase(destinationId);
            int targetTile=-1;
            if (destination.mode == BaseMode.Delegated)
            {
                if (!DelegatedCustodyCommandsReady) throw new InvalidOperationException("New delegated routes require the development bridge");
                targetTile=DelegatedSite(destinationId).tile;
                if (origin.Tile == new PlanetTile(targetTile,0)) throw new InvalidOperationException("Delegated route must travel to a different tile");
            }
            else DirectMap(destinationId);
            PhysicalCaravanAdapter.ValidateDeparture(core,origin,party,cargo);
            if (party.Any(NativeCustodyReserved)) throw new InvalidOperationException("Pawn is reserved for local custody");
            foreach (var pawn in party)
            {
                string native = pawn.GetUniqueLoadID();
                string registered=pawn.RaceProps.Animal ? state.animals.Where(a => a.nativeId == native).Select(a => a.id).SingleOrDefault() : state.people.Where(p => p.nativeId == native).Select(p => p.id).SingleOrDefault();
                if (registered == null && ledger.Entities.Any(e => e.BoundThingLoadId == native)) throw new InvalidOperationException("Pawn is already registered by another provider");
                if (registered != null && state.physical.Any(r => ActivePhysical(r) && r.party.Concat(r.animals).Contains(registered))) throw new InvalidOperationException("Pawn is reserved for another physical transport");
            }
            foreach (var c in cargo)
            {
                long held = state.physical.Where(r => ActivePhysical(r) && r.caravanId == null && r.departureReceipt == null)
                    .SelectMany(r => r.cargo).Where(x => x.sourceNativeId == c.Source.GetUniqueLoadID()).Sum(x => (long)x.count);
                if (held + c.Count > c.Source.stackCount) throw new InvalidOperationException("Physical stack is reserved for another transport");
            }
            core.AssertNoActiveSupplyTransfer(cityId:sourceId); core.AssertNoActiveSupplyTransfer(cityId:destinationId);
            foreach (var pawn in party) core.AssertNoActiveSupplyTransfer(nativeId:pawn.GetUniqueLoadID());
            foreach (var selection in cargo) core.AssertNoActiveSupplyTransfer(foodId:selection.Source.GetUniqueLoadID());
            EnsureCommunity();
            var record = new PhysicalTransportRecord { cause=cause,sourceId=sourceId,destinationId=destinationId,destinationTile=targetTile,stage=PhysicalStage.Reserved };
            foreach (var pawn in party)
            {
                if (pawn.RaceProps.Animal)
                {
                    string animalNative=pawn.GetUniqueLoadID(); var animal=state.animals.SingleOrDefault(a => a.nativeId == animalNative);
                    if (animal == null)
                    {
                        string animalId=Create("native-animal/"+animalNative,new EntitySpec("animal",pawn.LabelShort,ownerId:state.communityId,communityId:state.communityId,affiliationId:state.communityId,
                            authority:SimulationAuthority.Physical,identityKey:"native-animal/"+animalNative+"/"+pawn.def.defName,binding:pawn));
                        animal=new AnimalRecord { id=animalId,nativeId=animalNative,defName=pawn.def.defName }; state.animals.Add(animal);
                    }
                    record.animals.Add(animal.id); continue;
                }
                string native = pawn.GetUniqueLoadID(); var person = state.people.SingleOrDefault(p => p.nativeId == native);
                if (person == null)
                {
                    // The native game is the custody authority; this record supplies identity and membership only.
                    string id = Create("native-person/" + native,new EntitySpec("person",pawn.LabelShort,ownerId:state.communityId,communityId:state.communityId,affiliationId:state.communityId,
                        authority:SimulationAuthority.Physical,identityKey:"native-person/" + native,binding:pawn));
                    person = new PersonRecord { id=id,nativeId=native }; state.people.Add(person);
                }
                record.party.Add(person.id);
            }
            foreach (var c in cargo) record.cargo.Add(new PhysicalCargoRecord { sourceNativeId=c.Source.GetUniqueLoadID(),defName=c.Source.def.defName,count=c.Count,journeySupply=c.JourneySupply });
            record.partyCount=record.party.Count; record.cargoCount=record.cargo.Count; record.animalCount=record.animals.Count;
            record.shipmentId = Create("physical-shipment/" + cause,new EntitySpec("shipment",cause,ownerId:state.communityId,identityKey:PhysicalManifest(record)));
            state.physical.Add(record); Validate(); return record.shipmentId;
        }
        public void BeginPhysicalDeparture(string cause)
        {
            Gate(); PhysicalGate(); var r = PhysicalJob(cause);
            if (r.stage == PhysicalStage.InTransit || r.stage == PhysicalStage.Arrived) return;
            if (r.stage != PhysicalStage.Reserved && !(r.stage == PhysicalStage.NeedsAttention && r.departureReceipt == null && r.caravanId == null)) throw new InvalidOperationException("Physical transport cannot depart");
            AssertPhysicalSupplyFree(r); PhysicalCaravanAdapter.ValidateDeparture(core,DirectMap(r.sourceId),Party(r),Selections(r));
            if (Party(r).Any(NativeCustodyReserved)) throw new InvalidOperationException("Pawn is reserved for local custody");
            r.stage = PhysicalStage.Departing; r.problem = null;
        }
        public Caravan DispatchPhysical(string cause)
        {
            Gate(); PhysicalGate(); var r = PhysicalJob(cause);
            if (r.stage == PhysicalStage.InTransit || r.stage == PhysicalStage.Arrived) return Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == r.caravanId);
            AssertPhysicalSupplyFree(r); if (r.stage != PhysicalStage.Departing)
            {
                try { BeginPhysicalDeparture(cause); }
                catch (InvalidOperationException e) { if (r.stage == PhysicalStage.Reserved) { r.stage=PhysicalStage.NeedsAttention; r.problem=e.Message; } throw; }
            }
            if (Party(r).Any(NativeCustodyReserved)) throw new InvalidOperationException("Pawn is reserved for local custody");
            if (r.caravanId != null) { RestorePhysicalRecord(r); return Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == r.caravanId); }
            // A synchronous game-thread transition: no background save is allowed inside this adapter.
            try
            {
                var party = Party(r); Thing[] moved;
                var caravan = PhysicalCaravanAdapter.Depart(core,DirectMap(r.sourceId),party,Selections(r),out moved); r.caravanId=caravan.GetUniqueLoadID();
                for (int i=0; i<r.cargo.Count; i++)
                {
                    var item = moved[i];
                    r.cargo[i].cargoNativeId=item.GetUniqueLoadID();
                }
                CompletePhysicalDeparture(r); Validate(); return caravan;
            }
            catch (Exception e) { r.stage=PhysicalStage.NeedsAttention; r.problem=e.Message; throw; }
        }
        private void CompletePhysicalDeparture(PhysicalTransportRecord r)
        {
            AssertPhysicalSupplyFree(r); if (!PhysicalTransitMatches(r)) throw new InvalidOperationException("Native transit does not match its physical manifest");
            r.departureReceipt = Create("physical-departure/" + r.cause,new EntitySpec("lease",ownerId:state.communityId,identityKey:ReceiptIdentity(r,"depart")));
            r.stage=PhysicalStage.InTransit; r.problem=null;
        }
        public void StartPhysicalRoute(string cause)
        {
            Gate(); PhysicalGate(); var r = PhysicalJob(cause);
            AssertPhysicalSupplyFree(r); if (r.stage != PhysicalStage.InTransit || !(r.destinationTile >= 0 ? DelegatedTransitMatches(r) : TransitMatches(r))) throw new InvalidOperationException("Physical transport is not ready to travel");
            var caravan = Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == r.caravanId);
            if (!caravan.pather.StartPath(PhysicalDestination(r),null,true,false)) throw new InvalidOperationException("Native caravan cannot find a route");
        }
        public void BeginPhysicalArrival(string cause)
        {
            Gate(); PhysicalGate(); var r=PhysicalJob(cause);
            if (r.stage == PhysicalStage.Arrived || r.stage == PhysicalStage.Arriving) return;
            AssertPhysicalSupplyFree(r); if (r.stage != PhysicalStage.InTransit || !PhysicalTransitMatches(r)) throw new InvalidOperationException("Physical transit requires attention before arrival");
            var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == r.caravanId);
            if (caravan.Tile != PhysicalDestination(r) || r.destinationTile >= 0 && (caravan.pather.Moving || !DelegatedTransitMatches(r))) throw new InvalidOperationException("Native caravan has not stopped at the destination");
            r.stage=PhysicalStage.Arriving;
        }
        public void DeliverPhysical(string cause)
        {
            Gate(); PhysicalGate(); var r=PhysicalJob(cause);
            if (r.stage == PhysicalStage.Arrived) return;
            AssertPhysicalSupplyFree(r); if (r.stage != PhysicalStage.Arriving) BeginPhysicalArrival(cause);
            if (DeliveredMatches(r)) { CompletePhysicalArrival(r); return; }
            AssertPhysicalSupplyFree(r); if (!PhysicalTransitMatches(r)) { r.stage=PhysicalStage.NeedsAttention; r.problem="Native caravan or selected cargo changed before arrival"; throw new InvalidOperationException(r.problem); }
            var caravan = Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == r.caravanId);
            if (r.destinationTile < 0) PhysicalCaravanAdapter.EnterDestination(core,caravan,DirectMap(r.destinationId));
            CompletePhysicalArrival(r); Validate();
        }
        private void CompletePhysicalArrival(PhysicalTransportRecord r)
        {
            if (!DeliveredMatches(r)) throw new InvalidOperationException("Native delivered party does not match its physical manifest");
            r.arrivalReceipt=Create("physical-arrival/" + r.cause,new EntitySpec("lease",ownerId:state.communityId,identityKey:ReceiptIdentity(r,"arrive")));
            r.stage=PhysicalStage.Arrived; r.problem=null;
        }
        public void CancelPhysical(string cause)
        {
            Gate(); PhysicalGate(); var r=PhysicalJob(cause);
            if (r.stage == PhysicalStage.Cancelled) return;
            if (r.stage != PhysicalStage.Reserved && !(r.stage == PhysicalStage.NeedsAttention && r.caravanId == null && r.departureReceipt == null)) throw new InvalidOperationException("Only an undeparted physical transport can be cancelled");
            r.cancelReceipt=Create("physical-cancel/" + r.cause,new EntitySpec("lease",ownerId:state.communityId,identityKey:"physical-cancel:" + r.shipmentId));
            r.stage=PhysicalStage.Cancelled; r.problem=null; Validate();
        }
        private bool CargoMatches(PhysicalTransportRecord r,Pawn[] party)
        {
            if (party.Any(p => p == null || p.Dead || p.inventory == null)) return false;
            return r.cargo.Select((c,i) => new { Cargo=c, Expected=ExpectedCargo(r,i) }).All(entry =>
            {
                var c=entry.Cargo;
                if (ChangedCargoIdentity(c,party)) return false;
                var items=party.SelectMany(p => p.inventory.innerContainer).Where(t => t.GetUniqueLoadID() == c.cargoNativeId).ToArray();
                if (items.Length == 0) return c.journeySupply || entry.Expected == 0;
                return items.Length == 1 && items[0].def.defName == c.defName && (c.journeySupply ? items[0].stackCount <= c.count : items[0].stackCount == entry.Expected);
            });
        }
        private bool TransitMatches(PhysicalTransportRecord r)
        {
            var caravan=Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == r.caravanId);
            var party=Party(r);
            return caravan != null && !caravan.Destroyed && caravan.Faction == Faction.OfPlayer && party.All(p => p != null && p.Faction == Faction.OfPlayer && !p.Spawned && caravan.PawnsListForReading.Contains(p)) && CargoMatches(r,party);
        }
        private bool DeliveredMatches(PhysicalTransportRecord r)
        {
            if (r.destinationTile >= 0)
            {
                var caravan=Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == r.caravanId);
                return caravan != null && caravan.Tile == PhysicalDestination(r) && caravan.pather != null && !caravan.pather.Moving && DelegatedTransitMatches(r);
            }
            var party=Party(r); return !Find.WorldObjects.Caravans.Any(c => c.GetUniqueLoadID() == r.caravanId) && party.All(p => p != null && p.Faction == Faction.OfPlayer && p.Spawned && p.Map == DirectMap(r.destinationId)) && CargoMatches(r,party);
        }
        private void RestorePhysical()
        { foreach (var r in state.physical) { if (PhysicalSupplyBusyDuringRestore(r)) continue; RestoreLossReceipt(r); RestorePhysicalRecord(r); } }
        private void RestorePhysicalRecord(PhysicalTransportRecord r)
        {
            if (PhysicalSupplyBusyDuringRestore(r)) return;
            // Recover only observations/receipts. Loading a save never recreates a caravan or moves a pawn.
            if (r.stage == PhysicalStage.Departing)
            {
                if (r.caravanId != null && PhysicalTransitMatches(r)) CompletePhysicalDeparture(r);
                else if (r.caravanId == null && r.cargo.All(c => c.cargoNativeId == null) && Party(r).All(p => p != null && p.Spawned && p.Map == DirectMap(r.sourceId))) r.stage=PhysicalStage.Reserved;
                else { r.stage=PhysicalStage.NeedsAttention; r.problem="Departure checkpoint has an ambiguous native location"; }
            }
            else if (r.stage == PhysicalStage.Arriving)
            {
                if (DeliveredMatches(r)) CompletePhysicalArrival(r);
                else if (PhysicalTransitMatches(r)) r.stage=PhysicalStage.InTransit;
                else { r.stage=PhysicalStage.NeedsAttention; r.problem="Arrival checkpoint has an ambiguous native location"; }
            }
            else if (r.stage == PhysicalStage.InTransit && !PhysicalTransitMatches(r)) { r.stage=PhysicalStage.NeedsAttention; r.problem="Native transit party or cargo changed"; }
        }
        private void TickPhysical()
        {
            foreach (var r in state.physical.Where(p => p.stage == PhysicalStage.InTransit).ToArray())
            {
                if (PhysicalSupplyBusy(r)) continue; if (!PhysicalTransitMatches(r)) { r.stage=PhysicalStage.NeedsAttention; r.problem="Native transit party or cargo changed"; continue; }
                var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == r.caravanId);
                bool automatic=GenCommandLine.CommandLineArgPassed("inh-physical-auto") || ColonyMod.PhysicalEnabled && !GenCommandLine.CommandLineArgPassed("inh-physical-probe");
                if (caravan.Tile == PhysicalDestination(r) && !caravan.pather.Moving && ColonyMod.PhysicalAllowed && automatic) DeliverPhysical(r.cause);
            }
        }
        private void ValidatePhysical(HashSet<string> known)
        {
            if (state.people.Any(p => p == null || string.IsNullOrEmpty(p.nativeId)) || state.people.Select(p => p.id).Distinct().Count() != state.people.Count || state.people.Select(p => p.nativeId).Distinct().Count() != state.people.Count ||
                state.animals.Any(a => a == null || string.IsNullOrEmpty(a.nativeId) || string.IsNullOrEmpty(a.defName)) || state.animals.Select(a => a.id).Distinct().Count() != state.animals.Count || state.animals.Select(a => a.nativeId).Distinct().Count() != state.animals.Count ||
                state.people.Select(p => p.nativeId).Intersect(state.animals.Select(a => a.nativeId)).Any() ||
                state.physical.Any(r => r == null || string.IsNullOrEmpty(r.cause) || r.party == null || r.animals == null || r.cargo == null || r.losses == null) || state.physical.Select(r => r.cause).Distinct().Count() != state.physical.Count) throw new InvalidOperationException("Invalid physical transport tables");
            foreach (var person in state.people)
            {
                var entity=ledger.GetEntity(person.id);
                if (!known.Add(entity.Id) || entity.Provider != Actor || entity.Kind != "person" || entity.Authority != SimulationAuthority.Physical || entity.BoundThingLoadId != person.nativeId || entity.IdentityKey != "native-person/" + person.nativeId || entity.CommunityId != state.communityId || entity.AffiliationId != state.communityId || entity.CustodyId != null)
                    throw new InvalidOperationException("Permanent native person binding changed");
            }
            foreach (var animal in state.animals)
            {
                var entity=ledger.GetEntity(animal.id); var def=DefDatabase<ThingDef>.GetNamedSilentFail(animal.defName);
                if (!known.Add(entity.Id) || entity.Provider != Actor || entity.Kind != "animal" || entity.Authority != SimulationAuthority.Physical || entity.BoundThingLoadId != animal.nativeId || entity.IdentityKey != "native-animal/"+animal.nativeId+"/"+animal.defName || entity.CommunityId != state.communityId || entity.AffiliationId != state.communityId || entity.CustodyId != null || def == null || def.race == null || !def.race.Animal)
                    throw new InvalidOperationException("Permanent native animal binding changed");
            }
            var parties=new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in state.physical)
            {
                RequireBase(r.sourceId); RequireBase(r.destinationId);
                if (RequireBase(r.sourceId).mode != BaseMode.Direct || r.destinationTile < -1 ||
                    (r.destinationTile >= 0 ? DelegatedSite(r.destinationId).tile != r.destinationTile : RequireBase(r.destinationId).mode != BaseMode.Direct))
                    throw new InvalidOperationException("Physical route endpoint changed");
                if (r.sourceId == r.destinationId || r.party.Count == 0 || r.cargo.Count == 0 || r.partyCount != r.party.Count || r.animalCount != r.animals.Count || r.animals.Distinct().Count() != r.animals.Count || r.animals.Any(id => !state.animals.Any(a => a.id == id)) || r.cargoCount != r.cargo.Count || !Enum.IsDefined(typeof(PhysicalStage),r.stage) ||
                    r.party.Distinct().Count() != r.party.Count || r.party.Any(id => !state.people.Any(p => p.id == id)) || r.cargo.Any(c => c == null || c.count <= 0 || string.IsNullOrEmpty(c.sourceNativeId) || DefDatabase<ThingDef>.GetNamedSilentFail(c.defName) == null) ||
                    r.cargo.Select(c => c.sourceNativeId).Distinct().Count() != r.cargo.Count || r.cargo.Any(c => c.journeySupply && !DefDatabase<ThingDef>.GetNamed(c.defName).IsNutritionGivingIngestible) || ActivePhysical(r) && r.party.Concat(r.animals).Any(id => !parties.Add(id))) throw new InvalidOperationException("Invalid or overlapping physical manifest");
                var shipment=ledger.GetEntity(r.shipmentId);
                if (!known.Add(shipment.Id) || shipment.Kind != "shipment" || shipment.Provider != Actor || shipment.OwnerId != state.communityId || shipment.IdentityKey != PhysicalManifest(r)) throw new InvalidOperationException("Physical manifest differs from Core journal");
                ValidatePhysicalReceipt(r,r.departureReceipt,"physical-departure/","depart",known);
                ValidatePhysicalReceipt(r,r.arrivalReceipt,"physical-arrival/","arrive",known);
                ValidatePhysicalReceipt(r,r.cancelReceipt,"physical-cancel/","cancel",known);
                ValidatePhysicalLosses(r,known);
                bool outputs = !string.IsNullOrEmpty(r.caravanId) && r.cargo.All(c => !string.IsNullOrEmpty(c.cargoNativeId)) && r.cargo.Select(c => c.cargoNativeId).Distinct().Count() == r.cargo.Count;
                if (((r.stage == PhysicalStage.InTransit || r.stage == PhysicalStage.Arriving || r.stage == PhysicalStage.Arrived) && (r.departureReceipt == null || !outputs)) ||
                    (r.stage == PhysicalStage.Arrived && r.arrivalReceipt == null) || (r.stage != PhysicalStage.Arrived && r.stage != PhysicalStage.Arriving && r.arrivalReceipt != null) ||
                    (r.stage == PhysicalStage.Cancelled && (r.cancelReceipt == null || r.caravanId != null || r.departureReceipt != null)) || (r.stage != PhysicalStage.Cancelled && r.cancelReceipt != null) ||
                    (r.stage == PhysicalStage.Reserved && (r.caravanId != null || r.departureReceipt != null || r.cargo.Any(c => c.cargoNativeId != null)))) throw new InvalidOperationException("Physical checkpoint or receipt phase mismatch");
            }
        }
        private void ValidatePhysicalReceipt(PhysicalTransportRecord r,string id,string prefix,string phase,HashSet<string> known)
        {
            var op=ledger.FindOperation(Actor,"create",prefix + r.cause);
            if (id == null)
            {
                if (op != null && op.Stage == OperationStage.Committed)
                {
                    if (!(phase == "depart" && r.stage == PhysicalStage.Departing || phase == "arrive" && r.stage == PhysicalStage.Arriving)) throw new InvalidOperationException("Physical receipt record is missing");
                    ValidatePhysicalReceipt(r,op.ResultId,prefix,phase,known);
                }
                return;
            }
            var entity=ledger.GetEntity(id);
            string identity=phase == "cancel" ? "physical-cancel:" + r.shipmentId : ReceiptIdentity(r,phase);
            if (!known.Add(id) || op == null || op.Stage != OperationStage.Committed || op.ResultId != id || entity.Kind != "lease" || entity.Provider != Actor || entity.OwnerId != state.communityId || entity.IdentityKey != identity) throw new InvalidOperationException("Physical receipt journal mismatch");
        }
    }
}
