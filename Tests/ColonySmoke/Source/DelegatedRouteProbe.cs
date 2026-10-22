using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    // Real game route checkpoints: all movement uses Colony commands and ordinary ticks.
    public sealed class DelegatedRouteProbe : GameComponent
    {
        private const string Outward="delegated-route-outward", Return="delegated-route-return";
        private const string StorePerson="delegated-route-store-person", StoreAnimal="delegated-route-store-animal";
        private const string ReleasePerson="delegated-route-release-person", ReleaseAnimal="delegated-route-release-animal";
        private static readonly string[] Modes={"reserve","transit","arrival","stored","return","entering","final","reloadfinal","checkpoint","uncertifiedsave"};
        private bool done,restored;
        private int travelTicks;
        private string[] ids;
        private Pawn human,animal,escort;
        private Map origin;
        private Caravan caravan;
        public DelegatedRouteProbe(Game game) { }
        private static bool Has(string mode) { return GenCommandLine.CommandLineArgPassed("inh-delegated-route-"+mode); }
        private static bool Selected { get { return Modes.Any(Has); } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-delegated-route-"+key+".txt"); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-DELEGATED-ROUTE] PASS "+message); }
        private static void Refuse(Action action,string message)
        { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,message); }
        private static void RefuseReturnCause(Action action,string message)
        {
            bool refused=false; try { action(); } catch (InvalidOperationException e) { refused=e.Message == "Cause already belongs to a delegated return"; }
            Assert(refused,message);
        }
        private static ColonyState State(ColonyService service,CoreWorldComponent core)
        { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core))); }
        private static XmlNode ReturnRecord(ColonyService service,CoreWorldComponent core)
        { var xml=new XmlDocument(); xml.LoadXml(service.SaveSnapshot(core)); return xml.SelectSingleNode("/ColonyState/returns/li[cause='"+Return+"']"); }
        private static string Field(XmlNode node,string name)
        { var child=node == null ? null : node.SelectSingleNode(name); return child == null ? null : child.InnerText; }
        private static string Item(Thing t)
        { return t.GetUniqueLoadID()+":"+t.def.defName+":"+(t.Stuff == null ? "" : t.Stuff.defName)+":"+t.stackCount+":"+t.HitPoints; }
        private static IEnumerable<Thing> Possessions(Pawn p)
        { return p.inventory.innerContainer.Cast<Thing>().Concat(p.apparel == null ? new Thing[0] : p.apparel.WornApparel.Cast<Thing>()).Concat(p.equipment == null ? new Thing[0] : p.equipment.AllEquipmentListForReading.Cast<Thing>()); }
        private static string PawnState(Pawn p)
        {
            return string.Join("|",new[] {p.GetUniqueLoadID(),p.def.defName,p.Name == null ? "" : p.Name.ToStringFull,p.gender.ToString(),p.Faction.GetUniqueLoadID(),
                p.ageTracker.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture),p.ageTracker.AgeChronologicalTicks.ToString(CultureInfo.InvariantCulture),
                p.skills == null ? "" : string.Join(",",p.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+s.xpSinceLastLevel.ToString("R",CultureInfo.InvariantCulture)).ToArray()),
                string.Join(",",p.health.hediffSet.hediffs.Select(h => h.def.defName+":"+h.Severity.ToString("R",CultureInfo.InvariantCulture)+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                p.genes == null ? "" : "endo:"+string.Join(",",p.genes.Endogenes.Select(g => g.def.defName+":"+g.Active).ToArray())+";xeno:"+string.Join(",",p.genes.Xenogenes.Select(g => g.def.defName+":"+g.Active).ToArray()),
                p.Ideo == null ? "" : p.Ideo.GetUniqueLoadID(),string.Join(",",Possessions(p).Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray()),
                p.needs == null ? "" : string.Join(",",p.needs.AllNeeds.OrderBy(n => n.def.defName,StringComparer.Ordinal).Select(n => n.def.defName+":"+n.CurLevel.ToString("R",CultureInfo.InvariantCulture)).ToArray()),
                p.story == null ? "" : string.Join(",",p.story.traits.allTraits.Select(t => t.def.defName+":"+t.Degree).ToArray())});
        }
        private static bool SamePawn(Pawn p,string expected)
        {
            var current=PawnState(p).Split('|'); var old=expected.Split('|');
            return current.Length == old.Length && current.Where((s,i) => i != 5 && i != 6).SequenceEqual(old.Where((s,i) => i != 5 && i != 6)) &&
                new[] {5,6}.All(i => long.Parse(current[i],CultureInfo.InvariantCulture)-long.Parse(old[i],CultureInfo.InvariantCulture) >= 0 &&
                    long.Parse(current[i],CultureInfo.InvariantCulture)-long.Parse(old[i],CultureInfo.InvariantCulture) <= 3);
        }
        private Pawn[] Party { get { return new[] {human,escort,animal}; } }
        private static string AbstractStock(CoreLedger ledger)
        { return string.Join(",",ledger.Entities.Where(e => e.Kind == "lot" && e.Authority == SimulationAuthority.Abstract).OrderBy(e => e.Id,StringComparer.Ordinal).Select(e => e.Id+":"+e.Quantity).ToArray()); }
        private static int Individuals(CoreLedger ledger) { return ledger.Entities.Count(e => e.Kind == "person" || e.Kind == "animal"); }
        private static string History(CoreWorldComponent core)
        { return string.Join(";",core.ReadPawnCustodyTransfers(ColonyService.Actor).Where(t => t.CaravanId == null).Select(t => string.Join(",",new[] {t.Id,t.Cause,t.EntityId,t.NativeId,t.CityId,t.IsRelease.ToString(),t.Stage.ToString(),t.MapId.ToString(),t.Cell.ToString()})).ToArray()); }
        private static Pawn Original(CoreWorldComponent core,string native)
        {
            return core.GetDirectlyHeldThings().Cast<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns)).Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading))
                .Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().Single(p => p.GetUniqueLoadID() == native);
        }
        private static bool InCaravan(Pawn p,Caravan c)
        { return c != null && !p.Spawned && p.holdingOwner == c.GetDirectlyHeldThings() && c.PawnsListForReading.Contains(p) && Find.WorldPawns.Contains(p) && Find.WorldObjects.Caravans.Count(v => v.PawnsListForReading.Contains(p)) == 1; }
        private static bool Held(Pawn p,CoreWorldComponent core)
        { return !p.Spawned && p.holdingOwner == core.GetDirectlyHeldThings() && !Find.WorldPawns.Contains(p) && !Find.WorldObjects.Caravans.Any(c => c.PawnsListForReading.Contains(p)); }
        private PhysicalTransportRecord OutwardRecord(ColonyService service,CoreWorldComponent core)
        { return State(service,core).physical.Single(r => r.cause == Outward); }
        private void CheckRegistry(ColonyService service,CoreWorldComponent core)
        {
            var state=State(service,core);
            Assert(state.people.Count(p => p.nativeId == ids[6] && p.id == ids[9]) == 1 && state.people.Count(p => p.nativeId == ids[8] && p.id == ids[11]) == 1 &&
                state.animals.Count(p => p.nativeId == ids[7] && p.id == ids[10]) == 1,"Both original humans and the original animal retain exactly one permanent registry binding");
            for (int i=0;i<3;i++) Assert(core.Ledger.Entities.Count(e => e.BoundThingLoadId == ids[6+i]) == 1 && core.Ledger.GetEntity(ids[9+i]).BoundThingLoadId == ids[6+i],"Permanent Core binding still selects its exact original native individual");
            Assert(AbstractStock(core.Ledger) == ids[15] && Individuals(core.Ledger) == int.Parse(ids[16],CultureInfo.InvariantCulture),"Ordinary delegated travel and custody create no abstract inventory or replacement individual");
            Assert(History(core) == ids[17] && core.Header.SaveScopeId == ids[0],"Historical local custody records and world scope are preserved throughout the new route");
            Assert(ReferenceEquals(Original(core,ids[6]),human) && ReferenceEquals(Original(core,ids[7]),animal) && ReferenceEquals(Original(core,ids[8]),escort),"Native identities resolve to the sole original pawn objects");
            if (!string.IsNullOrEmpty(ids[13])) Assert(Item(Party.SelectMany(p => p.inventory.innerContainer).Single(t => t.GetUniqueLoadID() == ids[13])) == ids[14],"The native steel shipment retains its original cargo ID, definition, material, count and durability");
        }
        private static string CheckpointMode
        {
            get
            {
                if (GenCommandLine.CommandLineArgPassed("inh-delegated-route-checkpoint-returnmissing")) return "returnmissing";
                if (GenCommandLine.CommandLineArgPassed("inh-delegated-route-checkpoint-arrivedmissing")) return "arrivedmissing";
                if (GenCommandLine.CommandLineArgPassed("inh-delegated-route-checkpoint-uncertifiedhome")) return "uncertifiedhome";
                throw new InvalidOperationException("A delegated return recovery checkpoint was not selected");
            }
        }
        private static XmlDocument CheckpointSave()
        { var xml=new XmlDocument(); xml.Load(Path.Combine(GenFilePaths.SaveDataFolderPath,"Saves/INH_DelegatedRouteCheckpoint.rws")); return xml; }
        private static XmlNode SavedCheckpointReturn()
        {
            var raw=CheckpointSave().SelectSingleNode("//inheritanceModulePayloads/li[owner='inheritance.colony']/data");
            if (raw == null) throw new InvalidOperationException("Checkpoint lacks its original Colony payload");
            var xml=new XmlDocument(); xml.LoadXml(raw.InnerText); return xml.SelectSingleNode("/ColonyState/returns/li[cause='"+Return+"']");
        }
        private void CheckFingerprints()
        {
            string key="states";
            if (Has("uncertifiedsave")) key="INH_DelegatedRouteEntering-states";
            if (Has("checkpoint"))
            {
                string stage=CheckpointMode == "returnmissing" ? "INH_DelegatedRouteReturn" : CheckpointMode == "arrivedmissing" ? "INH_DelegatedRouteHome" : "INH_DelegatedRouteUncertifiedHome";
                key=stage+"-states";
                if (!File.Exists(PathFor(key)) && CheckpointMode == "arrivedmissing") key="states";
            }
            var expected=File.ReadAllLines(PathFor(key)); Assert(Party.Select((p,i) => SamePawn(p,expected[i])).All(v => v),"Separate process reload preserves the last native checkpoint including traits, names, health, genes, skills, ideology, needs and belongings");
        }
        private void Save(CoreWorldComponent core,string name)
        {
            File.WriteAllLines(PathFor("ids"),ids); File.WriteAllLines(PathFor("states"),Party.Select(PawnState).ToArray());
            File.WriteAllText(PathFor("count"),core.Ledger.EntityCount.ToString(CultureInfo.InvariantCulture));
            File.WriteAllLines(PathFor(name+"-states"),Party.Select(PawnState).ToArray());
            File.WriteAllText(PathFor(name+"-count"),core.Ledger.EntityCount.ToString(CultureInfo.InvariantCulture));
            GameDataSaveLoader.SaveGame(name); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private void Restore(ColonyService service,CoreWorldComponent core)
        {
            ids=File.ReadAllLines(PathFor("ids")); origin=Find.Maps.Single(m => m.uniqueID.ToString(CultureInfo.InvariantCulture) == ids[2]);
            human=Original(core,ids[6]); animal=Original(core,ids[7]); escort=Original(core,ids[8]);
            caravan=Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == ids[5]);
            int expected=Has("checkpoint") ? int.Parse(CheckpointSave().SelectSingleNode("//inheritanceLedger/entityCount").InnerText,CultureInfo.InvariantCulture) :
                int.Parse(File.ReadAllText(PathFor(Has("uncertifiedsave") ? "INH_DelegatedRouteEntering-count" : "count")),CultureInfo.InvariantCulture);
            Assert(core.Ledger.EntityCount == expected+(Has("stored") ? 2 : 0),"Checkpoint recovery allocates only the delegated arrival receipt when observing its saved native arrival");
            CheckFingerprints(); CheckRegistry(service,core); restored=true;
        }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current; Find.TickManager.Pause();
                if (!restored) Assert(core.IsReady && service != null,"Core and Colony initialize the delegated physical route");
                if (Has("reserve")) { Reserve(service,core); return; }
                if (!restored) Restore(service,core);
                if (Has("transit")) Transit(service,core);
                else if (Has("arrival")) Arrival(service,core);
                else if (Has("stored")) Stored(service,core);
                else if (Has("return")) ReturnHome(service,core);
                else if (Has("entering")) Entering(service,core);
                else if (Has("final")) Final(service,core);
                else if (Has("reloadfinal")) ReloadFinal(service,core);
                else if (Has("uncertifiedsave")) UncertifiedSave(service,core);
                else if (Has("checkpoint")) Checkpoint(service,core);
            }
            catch (Exception e) { done=true; Log.Error("[INH-DELEGATED-ROUTE] FAIL "+e); Application.Quit(1); }
        }
        private void Reserve(ColonyService service,CoreWorldComponent core)
        {
            var state=State(service,core); var from=service.CustodyBases().First(b => service.AvailableCustodyPawns(b.Id).Count(p => p.IsColonist) == 2 && service.AvailableCustodyPawns(b.Id).Any(p => p.def.defName == "Muffalo"));
            origin=Find.Maps.Single(m => m.uniqueID == from.MapId); var available=service.AvailableCustodyPawns(from.Id);
            human=available.First(p => p.IsColonist && state.people.Any(r => r.nativeId == p.GetUniqueLoadID())); escort=available.Single(p => p.IsColonist && p != human);
            animal=available.First(p => p.def.defName == "Muffalo" && state.animals.Any(r => r.nativeId == p.GetUniqueLoadID()));
            var neighbors=new List<PlanetTile>(); Find.World.grid.GetTileNeighbors(origin.Tile,neighbors);
            var empty=neighbors.Where(t => t == new PlanetTile(t.tileId,0) && !Find.World.grid[t].WaterCovered && !Find.World.grid[t].PrimaryBiome.impassable && Find.World.grid[t].hilliness != Hilliness.Impassable &&
                !Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == t) && !state.bases.Any(b => b.tile == t.tileId)).Take(2).ToArray();
            Assert(empty.Length == 2,"Fixture has two empty passable neighboring surface tiles for distinct delegated sites");
            string site=service.RegisterDelegatedSite("delegated-route-site","Delegated route destination",empty[0].tileId);
            string other=service.RegisterDelegatedSite("delegated-route-other","Other delegated route destination",empty[1].tileId);
            string unlocated=State(service,core).bases.Where(b => b.mode == BaseMode.Delegated && b.tile < 0).Select(b => b.cityId).FirstOrDefault();
            if (unlocated == null) unlocated=service.RegisterDelegatedBase("delegated-route-old-site","Unlocated historical base");
            Thing steel=origin.listerThings.ThingsOfDef(ThingDefOf.Steel).First(t => t.stackCount >= 2); int stack=steel.stackCount;
            var cargo=new[] {new CargoSelection(steel,1)}; int before=core.Ledger.EntityCount;
            Refuse(() => service.ReservePhysicalTransport("delegated-route-no-location",from.Id,unlocated,Party,cargo),"An old unlocated delegated target refuses physical reservation before registration or allocation");
            Assert(core.Ledger.EntityCount == before && Party.All(p => p.Spawned && p.Map == origin) && steel.stackCount == stack,"Unlocated-target refusal leaves all original pawns and native steel untouched");
            service.ReservePhysicalTransport("delegated-route-cancel-control",from.Id,site,Party,cargo);
            before=core.Ledger.EntityCount; service.CancelPhysical("delegated-route-cancel-control"); service.CancelPhysical("delegated-route-cancel-control");
            var cancelled=State(service,core).physical.Single(r => r.cause == "delegated-route-cancel-control");
            Assert(cancelled.stage == PhysicalStage.Cancelled && cancelled.caravanId == null && cancelled.departureReceipt == null && !string.IsNullOrEmpty(cancelled.cancelReceipt) && core.Ledger.EntityCount == before+2,
                "Cancelling and replaying an undeparted delegated reservation use one certificate and move no native party");
            string shipment=service.ReservePhysicalTransport(Outward,from.Id,site,Party,cargo); before=core.Ledger.EntityCount;
            Assert(service.ReservePhysicalTransport(Outward,from.Id,site,Party,cargo) == shipment && core.Ledger.EntityCount == before,"Ordinary outbound reservation replays its existing shipment without another allocation");
            Refuse(() => service.ReservePhysicalTransport(Outward,from.Id,other,Party,cargo),"An existing outbound cause cannot change its immutable delegated destination");
            Assert(core.Ledger.EntityCount == before && Party.All(p => p.Spawned && p.Map == origin) && steel.stackCount == stack,"Reservation, replay and changed-target refusal do not move the native party or split cargo");
            state=State(service,core);
            ids=new[] {core.Header.SaveScopeId,from.Id,origin.uniqueID.ToString(CultureInfo.InvariantCulture),empty[0].tileId.ToString(CultureInfo.InvariantCulture),site,"",human.GetUniqueLoadID(),animal.GetUniqueLoadID(),escort.GetUniqueLoadID(),
                state.people.Single(p => p.nativeId == human.GetUniqueLoadID()).id,state.animals.Single(p => p.nativeId == animal.GetUniqueLoadID()).id,state.people.Single(p => p.nativeId == escort.GetUniqueLoadID()).id,
                shipment,"","",AbstractStock(core.Ledger),Individuals(core.Ledger).ToString(CultureInfo.InvariantCulture),History(core),"","","","","",steel.GetUniqueLoadID(),stack.ToString(CultureInfo.InvariantCulture),other};
            var job=OutwardRecord(service,core);
            Assert(job.stage == PhysicalStage.Reserved && job.shipmentId == shipment && job.destinationTile == empty[0].tileId && job.caravanId == null && job.departureReceipt == null && job.arrivalReceipt == null,"Reserved checkpoint has only the immutable located outbound manifest and no native caravan or arrival proof");
            CheckRegistry(service,core); Save(core,"INH_DelegatedRouteReserved");
        }
        private void Transit(ColonyService service,CoreWorldComponent core)
        {
            Assert(Party.All(p => p.Spawned && p.Map == origin),"Reserved reload leaves all original individuals on the source map");
            int before=core.Ledger.EntityCount; caravan=service.DispatchPhysical(Outward); ids[5]=caravan.GetUniqueLoadID();
            var job=OutwardRecord(service,core); ids[13]=job.cargo.Single().cargoNativeId; ids[14]=Item(Party.SelectMany(p => p.inventory.innerContainer).Single(t => t.GetUniqueLoadID() == ids[13]));
            Assert(job.stage == PhysicalStage.InTransit && !string.IsNullOrEmpty(job.departureReceipt) && job.arrivalReceipt == null && core.Ledger.EntityCount == before+2 && Party.All(p => InCaravan(p,caravan)),
                "Ordinary dispatch moves the exact original party into one native caravan and certifies departure once");
            Assert(ReferenceEquals(service.DispatchPhysical(Outward),caravan) && core.Ledger.EntityCount == before+2,"Dispatch replay returns the same caravan without repeating native movement or allocating a certificate");
            Assert(origin.listerThings.AllThings.Single(t => t.GetUniqueLoadID() == ids[23]).stackCount == int.Parse(ids[24],CultureInfo.InvariantCulture)-1 && job.cargo.Single().count == 1,"Native dispatch splits exactly one existing steel unit from the original source stack");
            service.StartPhysicalRoute(Outward); Assert(caravan.pather.Moving && caravan.Tile == origin.Tile,"Normal Colony route starts the original caravan toward the delegated tile");
            CheckRegistry(service,core); Save(core,"INH_DelegatedRouteTransit");
        }
        private bool Travel()
        {
            for (int i=0;i<300 && caravan.pather.Moving;i++)
            { int before=Find.TickManager.TicksGame; Find.TickManager.DoSingleTick(); if (Find.TickManager.TicksGame != before+1) throw new InvalidOperationException("Native route must advance one ordinary game tick"); travelTicks++; }
            if (travelTicks > 180000) throw new InvalidOperationException("Neighboring route exceeded three ordinary game days");
            if (travelTicks > 0 && travelTicks%6000 == 0) Log.Message("[INH-DELEGATED-ROUTE] STEP native travel ticks="+travelTicks);
            return !caravan.pather.Moving;
        }
        private void Arrival(ColonyService service,CoreWorldComponent core)
        {
            if (!Travel()) return;
            Assert(caravan.Tile == new PlanetTile(int.Parse(ids[3],CultureInfo.InvariantCulture),0) && Party.All(p => InCaravan(p,caravan)),"Ordinary native ticks reach the delegated tile with the same original party and stopped caravan");
            int before=core.Ledger.EntityCount; service.BeginPhysicalArrival(Outward); service.BeginPhysicalArrival(Outward); var job=OutwardRecord(service,core);
            Assert(job.stage == PhysicalStage.Arriving && job.arrivalReceipt == null && core.Ledger.EntityCount == before && !caravan.pather.Moving && !Find.Maps.Any(m => m.Tile == caravan.Tile),"Arriving checkpoint saves the mapless stopped caravan without native entry or premature arrival allocation");
            CheckRegistry(service,core); Save(core,"INH_DelegatedRouteArrival");
        }
        private void Stored(ColonyService service,CoreWorldComponent core)
        {
            var job=OutwardRecord(service,core); int before=core.Ledger.EntityCount;
            Assert(job.stage == PhysicalStage.Arrived && !string.IsNullOrEmpty(job.arrivalReceipt) && Party.All(p => InCaravan(p,caravan)) && !caravan.pather.Moving,"Reload observes delegated arrival and certifies it without spawning a map or moving originals");
            service.DeliverPhysical(Outward); service.DeliverPhysical(Outward);
            Assert(core.Ledger.EntityCount == before && Party.All(p => InCaravan(p,caravan)),"Completed outward delivery replays without additional receipts or native movement");
            ids[18]=service.StoreDelegatedPawn(StorePerson,ids[4],caravan,human); ids[19]=service.StoreDelegatedPawn(StoreAnimal,ids[4],caravan,animal);
            Assert(service.StoreDelegatedPawn(StorePerson,ids[4],caravan,human) == ids[18] && service.StoreDelegatedPawn(StoreAnimal,ids[4],caravan,animal) == ids[19] && core.Ledger.EntityCount == before+4,
                "Completed physical arrival unlocks custody of the original person and animal with one store intent each");
            Assert(Held(human,core) && Held(animal,core) && InCaravan(escort,caravan) && service.DelegatedResidents(ids[4]).Length == 2,"Delegated storage keeps two original residents exclusively Core-held and their escort in the same native caravan");
            before=core.Ledger.EntityCount; string[] states=Party.Select(PawnState).ToArray();
            Refuse(() => service.BeginDelegatedReturn(Return,Outward),"A return manifest refuses while original outbound members remain stored");
            Assert(core.Ledger.EntityCount == before && Party.Select(PawnState).SequenceEqual(states) && Held(human,core) && Held(animal,core),"Premature return refusal allocates no return record or Core entity and moves no stored original");
            CheckRegistry(service,core); Save(core,"INH_DelegatedRouteStored");
        }
        private void ReturnHome(ColonyService service,CoreWorldComponent core)
        {
            Assert(Held(human,core) && Held(animal,core) && InCaravan(escort,caravan),"Stored restart retains original residents and the native escort exclusively at their original endpoints");
            ids[20]=service.ReleaseDelegatedPawn(ReleasePerson,ids[4],ids[9],caravan); ids[21]=service.ReleaseDelegatedPawn(ReleaseAnimal,ids[4],ids[10],caravan);
            int before=core.Ledger.EntityCount;
            Assert(service.ReleaseDelegatedPawn(ReleasePerson,ids[4],ids[9],caravan) == ids[20] && service.ReleaseDelegatedPawn(ReleaseAnimal,ids[4],ids[10],caravan) == ids[21] && core.Ledger.EntityCount == before && Party.All(p => InCaravan(p,caravan)),"Custody release and replay restore the exact original party to its same caravan");
            var outward=OutwardRecord(service,core); string oldDeparture=outward.departureReceipt,oldArrival=outward.arrivalReceipt;
            ids[22]=service.BeginDelegatedReturn(Return,Outward); int created=core.Ledger.EntityCount;
            Assert(created == before+4 && State(service,core).returnCount == 1 && State(service,core).returns.Count == 1,
                "Beginning homeward travel creates exactly one separate shipment and departure lease with their two Core operations");
            Assert(service.BeginDelegatedReturn(Return,Outward) == ids[22] && core.Ledger.EntityCount == created,"A separate return manifest and departure certificate replay without another allocation");
            var record=ReturnRecord(service,core);
            Assert(Field(record,"outwardCause") == Outward && Field(record,"shipmentId") == ids[22] && Field(record,"stage") == "InTransit" && !string.IsNullOrEmpty(Field(record,"departureReceipt")),"The separate return journal references the original outward shipment and its own departure certificate");
            Assert(OutwardRecord(service,core).stage == PhysicalStage.Arrived && OutwardRecord(service,core).departureReceipt == oldDeparture && OutwardRecord(service,core).arrivalReceipt == oldArrival,"Starting homeward travel never rewrites the committed outward arrival history");
            CheckCauseCollisions(service,core);
            before=core.Ledger.EntityCount;
            Refuse(() => service.BeginDelegatedReturn(Return,"delegated-route-cancel-control"),"The immutable return cause cannot reference another outward manifest");
            Refuse(() => service.BeginDelegatedReturn("delegated-route-duplicate-return",Outward),"The completed outward shipment cannot allocate another concurrent return");
            Refuse(() => service.StoreDelegatedPawn("delegated-route-active-return-store",ids[4],caravan,human),"An active return reserves its original person against new delegated storage");
            Assert(core.Ledger.EntityCount == before && Party.All(p => InCaravan(p,caravan)),"Changed, duplicate and custody-conflicting return requests do not allocate or move native originals");
            service.StartDelegatedReturnRoute(Return); service.StartDelegatedReturnRoute(Return); Assert(caravan.pather.Moving && caravan.Tile == new PlanetTile(int.Parse(ids[3],CultureInfo.InvariantCulture),0) && core.Ledger.EntityCount == before,"Normal return routing and its replay start the same original caravan toward its original direct source map");
            CheckRegistry(service,core); Save(core,"INH_DelegatedRouteReturn");
        }
        private void Entering(ColonyService service,CoreWorldComponent core)
        {
            if (!Travel()) return;
            Assert(caravan.Tile == origin.Tile && Party.All(p => InCaravan(p,caravan)),"Ordinary native ticks return the original party and cargo to its exact original home tile");
            int before=core.Ledger.EntityCount; service.BeginDelegatedReturnArrival(Return); service.BeginDelegatedReturnArrival(Return);
            Assert(Field(ReturnRecord(service,core),"stage") == "Arriving" && core.Ledger.EntityCount == before && Party.All(p => InCaravan(p,caravan)),"Home arrival checkpoint retains native caravan ownership before standard map entry");
            CheckRegistry(service,core); Save(core,"INH_DelegatedRouteEntering");
        }
        private void Final(ColonyService service,CoreWorldComponent core)
        {
            Assert(Party.All(p => InCaravan(p,caravan)) && caravan.Tile == origin.Tile,"Reloading the entering checkpoint observes native originals and does not enter the map on load");
            int before=core.Ledger.EntityCount; service.DeliverDelegatedReturn(Return); service.DeliverDelegatedReturn(Return);
            Assert(Party.All(p => p.Spawned && p.Map == origin && !Find.WorldPawns.Contains(p)) && !Find.WorldObjects.Caravans.Any(c => c.GetUniqueLoadID() == ids[5]) && core.Ledger.EntityCount == before+2,
                "Standard Colony return delivery enters all originals into their original home map and creates one arrival certificate");
            Assert(Field(ReturnRecord(service,core),"stage") == "Returned" && !string.IsNullOrEmpty(Field(ReturnRecord(service,core),"arrivalReceipt")) && OutwardRecord(service,core).stage == PhysicalStage.Arrived && !service.DelegatedResidents(ids[4]).Any(),
                "Separate return completion retains outward history and leaves no phantom delegated resident");
            CheckRegistry(service,core); Save(core,"INH_DelegatedRouteHome");
        }
        private void ReloadFinal(ColonyService service,CoreWorldComponent core)
        {
            int before=core.Ledger.EntityCount; service.DeliverPhysical(Outward); service.DeliverPhysical(Outward); service.BeginDelegatedReturn(Return,Outward); service.BeginDelegatedReturn(Return,Outward);
            service.DeliverDelegatedReturn(Return); service.DeliverDelegatedReturn(Return); service.RecheckDelegatedReturn(Return);
            Assert(core.Ledger.EntityCount == before && Party.All(p => p.Spawned && p.Map == origin && !Find.WorldPawns.Contains(p)) && !Find.WorldObjects.Caravans.Any(c => c.GetUniqueLoadID() == ids[5]),"Final returned receipt replay and recheck tolerate the removed native caravan without allocation or moving originals");
            CheckCauseCollisions(service,core);
            CheckFingerprints(); CheckRegistry(service,core); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private void CheckCauseCollisions(ColonyService service,CoreWorldComponent core)
        {
            int before=core.Ledger.EntityCount; string snapshot=service.SaveSnapshot(core); string[] originals=Party.Select(PawnState).ToArray();
            Thing steel=origin.listerThings.AllThings.Single(t => t.GetUniqueLoadID() == ids[23]);
            RefuseReturnCause(() => service.ReservePhysicalTransport(Return,ids[1],ids[4],Party,new[] {new CargoSelection(steel,1)}),"A return cause refuses physical reservation before endpoint validation or allocation");
            RefuseReturnCause(() => service.StoreLocalPawn(Return,ids[1],escort),"A return cause refuses local storage before any native registration or movement");
            RefuseReturnCause(() => service.ReleaseLocalPawn(Return,ids[1],ids[9]),"A return cause refuses local release before any native movement");
            RefuseReturnCause(() => service.StoreDelegatedPawn(Return,ids[4],caravan,escort),"A return cause refuses delegated storage even after its original caravan has disappeared");
            RefuseReturnCause(() => service.ReleaseDelegatedPawn(Return,ids[4],ids[9],caravan),"A return cause refuses delegated release before any native movement");
            var stock=State(service,core).stocks.FirstOrDefault();
            if (stock != null) RefuseReturnCause(() => service.ReserveTransport(Return,stock.lotId,ids[4],1,Find.TickManager.TicksGame+600),"A return cause cannot allocate a parallel abstract shipment");
            Assert(core.Ledger.EntityCount == before && service.SaveSnapshot(core) == snapshot && Party.Select(PawnState).SequenceEqual(originals),"Return cause collisions preserve Core count, the complete Colony journal and every original native fingerprint");
        }
        private void UncertifiedSave(ColonyService service,CoreWorldComponent core)
        {
            Assert(caravan != null && !caravan.pather.Moving && caravan.Tile == origin.Tile && Party.All(p => InCaravan(p,caravan)),"Uncertified fixture starts with the existing stopped original return caravan at home");
            int before=core.Ledger.EntityCount; service.BeginDelegatedReturnArrival(Return);
            // Deliberate interruption fixture only: native entry completed before its Colony receipt.
            // The ordinary Final mode continues to use DeliverDelegatedReturn exclusively.
            PhysicalCaravanAdapter.EnterDestination(core,caravan,origin);
            Assert(Party.All(p => p.Spawned && p.Map == origin && !Find.WorldPawns.Contains(p)) && !Find.WorldObjects.Caravans.Any(c => c.GetUniqueLoadID() == ids[5]) &&
                Field(ReturnRecord(service,core),"stage") == "Arriving" && string.IsNullOrEmpty(Field(ReturnRecord(service,core),"arrivalReceipt")) &&
                core.Ledger.FindOperation(ColonyService.Actor,"create","delegated-return-arrive/"+Return) == null && core.Ledger.EntityCount == before,
                "Interruption fixture saves original native home entry without an arrival operation or replacement object");
            CheckRegistry(service,core); Save(core,"INH_DelegatedRouteUncertifiedHome");
        }
        private void Checkpoint(ColonyService service,CoreWorldComponent core)
        {
            string mode=CheckpointMode; XmlNode saved=SavedCheckpointReturn(), current=ReturnRecord(service,core); int before=core.Ledger.EntityCount;
            Assert(saved != null && current != null,"The recovery fixture and loaded Colony state contain the same separate return journal");
            if (mode == "returnmissing")
            {
                var op=core.Ledger.FindOperation(ColonyService.Actor,"create","delegated-return-depart/"+Return);
                Assert(string.IsNullOrEmpty(Field(saved,"departureReceipt")) && op != null && op.Stage == OperationStage.Committed && Field(current,"departureReceipt") == op.ResultId &&
                    Field(current,"stage") == "InTransit" && caravan != null && Party.All(p => InCaravan(p,caravan)),
                    "Reload recovers the missing return departure reference from its existing committed Core certificate without replacing native travellers");
                Assert(core.Ledger.GetEntity(op.ResultId).Kind == "lease" && caravan.pather.Moving,"Recovered departure proof retains the original moving native caravan");
            }
            else if (mode == "arrivedmissing")
            {
                var op=core.Ledger.FindOperation(ColonyService.Actor,"create","delegated-return-arrive/"+Return);
                Assert(string.IsNullOrEmpty(Field(saved,"arrivalReceipt")) && Field(saved,"stage") == "Returned" && op != null && op.Stage == OperationStage.Committed &&
                    Field(current,"arrivalReceipt") == op.ResultId && Field(current,"stage") == "Returned" && caravan == null && Party.All(p => p.Spawned && p.Map == origin && !Find.WorldPawns.Contains(p)),
                    "Reload recovers a missing returned arrival reference from its original committed Core certificate while native originals remain exclusively home");
                service.DeliverDelegatedReturn(Return); service.DeliverDelegatedReturn(Return); service.RecheckDelegatedReturn(Return);
            }
            else
            {
                Assert(Field(saved,"stage") == "Arriving" && string.IsNullOrEmpty(Field(saved,"arrivalReceipt")) && Field(current,"stage") == "NeedsAttention" && string.IsNullOrEmpty(Field(current,"arrivalReceipt")) &&
                    core.Ledger.FindOperation(ColonyService.Actor,"create","delegated-return-arrive/"+Return) == null && caravan == null && Party.All(p => p.Spawned && p.Map == origin && !Find.WorldPawns.Contains(p)),
                    "Reload observes uncertified native home entry and holds its missing arrival proof without moving originals or allocating on load");
                service.RecheckDelegatedReturn(Return); service.RecheckDelegatedReturn(Return);
                var op=core.Ledger.FindOperation(ColonyService.Actor,"create","delegated-return-arrive/"+Return);
                Assert(op != null && op.Stage == OperationStage.Committed && core.Ledger.GetEntity(op.ResultId).Kind == "lease" && Field(ReturnRecord(service,core),"arrivalReceipt") == op.ResultId &&
                    Field(ReturnRecord(service,core),"stage") == "Returned" && core.Ledger.EntityCount == before+2,
                    "Explicit home recheck and replay create exactly one missing arrival certificate and its operation for the existing original party");
                before+=2;
            }
            Assert(core.Ledger.EntityCount == before,"Recovery reference restoration and receipt replay do not allocate any additional Core entity");
            CheckFingerprints(); CheckRegistry(service,core); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
    }
}
