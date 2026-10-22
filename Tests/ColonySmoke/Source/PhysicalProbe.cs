using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    public sealed class PhysicalProbe : GameComponent
    {
        private bool done;
        public PhysicalProbe(Game game) { }
        private static string Meta(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-physical-" + key + ".txt"); }
        private static void Assert(bool value,string text) { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-P04-PHYSICAL] PASS " + text); }
        private static void Refuses(Action action,string text) { bool refused = false; try { action(); } catch (InvalidOperationException) { refused = true; } Assert(refused,text); }
        private static string PawnState(Pawn pawn)
        {
            return string.Join("|",new[] {
                pawn.GetUniqueLoadID(),pawn.Name.ToStringFull,pawn.gender.ToString(),pawn.Faction.GetUniqueLoadID(),pawn.ageTracker.AgeBiologicalTicks.ToString(),
                string.Join(",",pawn.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+s.xpSinceLastLevel.ToString("R")).ToArray()),
                string.Join(",",pawn.health.hediffSet.hediffs.Select(h => h.def.defName+":"+h.Severity.ToString("R")+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                pawn.genes == null ? "" : string.Join(",",pawn.genes.GenesListForReading.Select(g => g.def.defName+":"+g.Active).ToArray()),
                pawn.Ideo == null ? "" : pawn.Ideo.GetUniqueLoadID(),
                string.Join(",",pawn.apparel.WornApparel.Select(t => t.GetUniqueLoadID()).ToArray()),
                string.Join(",",pawn.equipment.AllEquipmentListForReading.Select(t => t.GetUniqueLoadID()).ToArray()) });
        }
        private static long TotalSteel()
        {
            var pawns = Find.Maps.SelectMany(m => m.mapPawns.AllPawns).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().ToArray();
            return Find.Maps.Sum(m => m.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t => (long)t.stackCount)) +
                pawns.Where(p => p.inventory != null).Sum(p => p.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Sum(t => (long)t.stackCount));
        }
        private static bool SameSavedPawn(Pawn pawn)
        {
            var expected=File.ReadAllText(Meta("pawn")).Split('|'); var actual=PawnState(pawn).Split('|');
            if (expected.Length != actual.Length) return false;
            // A map pawn can receive its pending native age tick after loading. Never treat this as a cloned age.
            long elapsed=long.Parse(actual[4])-long.Parse(expected[4]);
            if (elapsed < 0 || elapsed > 3) return false;
            for (int i=0; i<expected.Length; i++) if (i != 4 && expected[i] != actual[i]) return false;
            return true;
        }
        public override void GameComponentUpdate()
        {
            if (done || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null || !GenCommandLine.CommandLineArgPassed("inh-physical-probe") || GenCommandLine.CommandLineArgPassed("inh-local-custody") || GenCommandLine.CommandLineArgPassed("inh-delegated-custody-probe") || GenCommandLine.CommandLineArgPassed("inh-delegated-route-probe") || GenCommandLine.CommandLineArgPassed("inh-ration-probe")) return;
            var core = Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            done = true;
            try
            {
                Assert(core.IsReady && ColonyMod.Current != null,"Core plus Colony physical probe initialized without World");
                Find.TickManager.Pause();
                if (GenCommandLine.CommandLineArgPassed("inh-physical-save"))
                {
                    Map origin = Find.CurrentMap;
                    var neighbors = new List<PlanetTile>(); Find.World.grid.GetTileNeighbors(origin.Tile,neighbors);
                    PlanetTile tile = neighbors.First(i => !Find.World.grid[i].WaterCovered && Find.World.grid[i].PrimaryBiome.canBuildBase &&
                        !Find.World.grid[i].PrimaryBiome.impassable && Find.World.grid[i].hilliness != Hilliness.Impassable && !Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == i));
                    var parent = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement); parent.Tile = tile; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                    Map destination = GetOrGenerateMapUtility.GetOrGenerateMap(tile,new IntVec3(100,1,100),WorldObjectDefOf.Settlement,null,false);
                    var service = ColonyMod.Current;
                    string from=service.RegisterDirectMap(origin),to=service.RegisterDirectMap(destination);
                    Pawn pawn = origin.mapPawns.FreeColonistsSpawned.First(p => !p.Dead && !p.Downed && !p.skills.GetSkill(SkillDefOf.Crafting).TotallyDisabled);
                    pawn.skills.GetSkill(SkillDefOf.Crafting).Level = 12;
                    if (ModsConfig.BiotechActive)
                    {
                        pawn.genes.AddGene(DefDatabase<GeneDef>.GetNamed("Robust"),false);
                        pawn.genes.AddGene(DefDatabase<GeneDef>.GetNamed("DarkVision"),true);
                        Assert(pawn.genes.Endogenes.Any(g => g.def.defName == "Robust") && pawn.genes.Xenogenes.Any(g => g.def.defName == "DarkVision"),"Physical probe includes a functional endogene and xenogene");
                    }
                    var wound = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Scratch"),pawn,pawn.RaceProps.body.AllParts.First(p => p.def == BodyPartDefOf.Torso)); wound.Severity = 0.5f; pawn.health.AddHediff(wound);
                    Thing source = ThingMaker.MakeThing(ThingDefOf.Steel); source.stackCount = 75;
                    IntVec3 cell = origin.AllCells.First(c => c.Standable(origin) && c.GetThingList(origin).Count == 0); GenSpawn.Spawn(source,cell,origin);
                    string before = PawnState(pawn); long total = TotalSteel(); int caravansBefore = Find.WorldObjects.Caravans.Count;
                    Refuses(delegate { PhysicalCaravanAdapter.Depart(core,origin,new[] {pawn},new[] {new CargoSelection(source,76)}); },"Overdrawn real stack is refused before anything moves");
                    Assert(source.stackCount == 75 && pawn.Spawned && Find.WorldObjects.Caravans.Count == caravansBefore,"Invalid physical request leaves pawn, stack and world unchanged");
                    service.ReservePhysicalTransport("physical-cancel",from,to,new[] {pawn},new[] {new CargoSelection(source,10)});
                    service.CancelPhysical("physical-cancel"); service.CancelPhysical("physical-cancel");
                    int peopleBefore=core.Ledger.Entities.Count(e => e.Provider == ColonyService.Actor && e.Kind == "person");
                    string shipment=service.ReservePhysicalTransport("physical-trip",from,to,new[] {pawn},new[] {new CargoSelection(source,30)});
                    Assert(service.ReservePhysicalTransport("physical-trip",from,to,new[] {pawn},new[] {new CargoSelection(source,30)}) == shipment && source.stackCount == 75 && pawn.Spawned,"Physical reservation replays without moving or deducting native objects");
                    Refuses(delegate { service.ReservePhysicalTransport("physical-trip",from,to,new[] {pawn},new[] {new CargoSelection(source,31)}); },"Changed physical request is refused against the immutable manifest");
                    Refuses(delegate { service.ReservePhysicalTransport("other-trip",from,to,new[] {pawn},new[] {new CargoSelection(source,1)}); },"One native pawn cannot be reserved for two active transports");
                    Pawn other=origin.mapPawns.FreeColonistsSpawned.First(p => p != pawn && !p.Dead && !p.Downed);
                    int beforeRejected=core.Ledger.EntityCount;
                    Refuses(delegate { service.ReservePhysicalTransport("overbook",from,to,new[] {other},new[] {new CargoSelection(source,46)}); },"Different parties cannot overbook the same real stack");
                    Assert(core.Ledger.EntityCount == beforeRejected && other.Spawned,"Rejected overbooking creates no person, shipment or physical side effect");
                    Assert(core.Ledger.Entities.Count(e => e.Provider == ColonyService.Actor && e.Kind == "person") == peopleBefore && peopleBefore == 1,"Cancelled and later trips reuse one permanent native person ID");
                    GameDataSaveLoader.SaveGame("INH_PhysicalReserved");
                    service.BeginPhysicalDeparture("physical-trip"); GameDataSaveLoader.SaveGame("INH_PhysicalDepartureIntent");
                    var caravan = service.DispatchPhysical("physical-trip");
                    var cargo = pawn.inventory.innerContainer.Single(t => t.def == ThingDefOf.Steel && t.stackCount == 30);
                    Assert(source.stackCount == 45 && cargo.stackCount == 30 && TotalSteel() == total,"Real stack splits 45 on the map and 30 in native caravan inventory without extra stock");
                    Assert(!pawn.Spawned && caravan.PawnsListForReading.Single() == pawn && PawnState(pawn) == before,"Original pawn retains identity, injury, skills, genes, ideology and equipment on departure");
                    Refuses(delegate { PhysicalCaravanAdapter.Depart(core,origin,new[] {pawn},new[] {new CargoSelection(source,30)}); },"Repeating physical departure cannot make a second pawn or caravan");
                    Assert(service.DispatchPhysical("physical-trip") == caravan && service.PhysicalTransports.Single(t => t.Cause == "physical-trip").Stage == PhysicalStage.InTransit,"Physical departure journal replays against the same native caravan");
                    Refuses(delegate { service.CancelPhysical("physical-trip"); },"Departed real cargo cannot be released as an undeparted reservation");
                    service.StartPhysicalRoute("physical-trip");
                    Assert(caravan.pather.Moving && caravan.pather.Destination == destination.Tile,"Native path follower starts a real route to the registered destination");
                    File.WriteAllLines(Meta("ids"),new[] {core.Header.SaveScopeId,pawn.GetUniqueLoadID(),source.GetUniqueLoadID(),cargo.GetUniqueLoadID(),caravan.GetUniqueLoadID(),destination.uniqueID.ToString(),origin.uniqueID.ToString(),total.ToString()});
                    File.WriteAllText(Meta("pawn"),before);
                    File.WriteAllLines(Meta("inventory"),pawn.inventory.innerContainer.Select(t => t.GetUniqueLoadID()).OrderBy(x => x).ToArray());
                    Assert(core.Ledger.TotalAbstractQuantity("ludeon.rimworld:Steel") == 0,"Physical cargo is not counted as abstract warehouse stock");
                    GameDataSaveLoader.SaveGame("INH_PhysicalTransit");
                }
                else
                {
                    var ids = File.ReadAllLines(Meta("ids")); Map origin = Find.Maps.Single(m => m.uniqueID.ToString() == ids[6]), destination = Find.Maps.Single(m => m.uniqueID.ToString() == ids[5]);
                    Assert(core.Header.SaveScopeId == ids[0] && TotalSteel().ToString() == ids[7],"Physical save reload retains world identity and total steel");
                    Pawn pawn;
                    var service=ColonyMod.Current;
                    var trip=service.PhysicalTransports.Single(t => t.Cause == "physical-trip");
                    if (GenCommandLine.CommandLineArgPassed("inh-physical-checkpoint"))
                        Assert(core.Ledger.EntityCount.ToString() == File.ReadAllText(Meta("checkpoint-count")),"Native-effect checkpoint reuses committed receipts without adding a registry entity");
                    Assert(core.Ledger.Entities.Single(e => e.Provider == ColonyService.Actor && e.Kind == "person").BoundThingLoadId == ids[1],"Permanent person registry retains the original Pawn reference through Scribe");
                    if (GenCommandLine.CommandLineArgPassed("inh-physical-reserved") || GenCommandLine.CommandLineArgPassed("inh-physical-departure-intent"))
                    {
                        pawn=origin.mapPawns.FreeColonistsSpawned.Single(p => p.GetUniqueLoadID() == ids[1]);
                        Thing source=origin.listerThings.ThingsOfDef(ThingDefOf.Steel).Single(t => t.GetUniqueLoadID() == ids[2]);
                        Assert(trip.Stage == PhysicalStage.Reserved && source.stackCount == 75,"Reservation or departure intent reload creates no caravan and moves no native assets");
                        if (!SameSavedPawn(pawn))
                        { Log.Message("[INH-P04-PHYSICAL] Expected pawn: " + File.ReadAllText(Meta("pawn"))); Log.Message("[INH-P04-PHYSICAL] Actual pawn: " + PawnState(pawn)); }
                        Assert(SameSavedPawn(pawn),"Original personal state survives reservation save and reload with at most three native age ticks");
                        int count=Find.WorldObjects.Caravans.Count;
                        source.stackCount=20;
                        Refuses(delegate { service.DispatchPhysical("physical-trip"); },"Consumed source stock puts dispatch on hold before any physical move");
                        Assert(service.PhysicalTransports.Single(t => t.Cause == "physical-trip").Stage == PhysicalStage.NeedsAttention && pawn.Spawned && source.stackCount == 20 && Find.WorldObjects.Caravans.Count == count,"Shortage does not manufacture cargo or move the reserved pawn");
                        source.stackCount=75;
                        var resumed=service.DispatchPhysical("physical-trip");
                        Assert(resumed.PawnsListForReading.Contains(pawn) && TotalSteel().ToString() == ids[7],"Explicit retry after restored stock dispatches original objects exactly once");
                        Assert(service.DispatchPhysical("physical-trip") == resumed,"Replayed resumed departure returns the same caravan");
                        Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); return;
                    }
                    if (GenCommandLine.CommandLineArgPassed("inh-physical-load") || GenCommandLine.CommandLineArgPassed("inh-physical-arrival-intent"))
                    {
                        Caravan caravan = Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[4]); pawn = caravan.PawnsListForReading.Single(p => p.GetUniqueLoadID() == ids[1]);
                        Thing source = origin.listerThings.ThingsOfDef(ThingDefOf.Steel).Single(t => t.GetUniqueLoadID() == ids[2]);
                        Thing cargo = pawn.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == ids[3]);
                        Assert(source.stackCount == 45 && cargo.stackCount == 30 && SameSavedPawn(pawn),"Native caravan Scribe restores the same split cargo and complete original pawn state");
                        if (GenCommandLine.CommandLineArgPassed("inh-physical-load"))
                        {
                            Assert(caravan.pather.Moving && caravan.pather.Destination == destination.Tile,"Saved native path and destination survive process restart");
                            Refuses(delegate { service.DeliverPhysical("physical-trip"); },"Delivery is refused before the native caravan reaches its destination");
                            // Accelerate the native path follower only. No tile teleport, food, pawn or map simulation is substituted here.
                            for (int i=0; i<2000 && (caravan.Tile != destination.Tile || caravan.pather.Moving); i++)
                            { Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame+250); caravan.pather.PatherTickInterval(250); }
                            Assert(caravan.Tile == destination.Tile && !caravan.pather.Moving,"Native path follower reaches the adjacent destination without teleporting");
                            service.BeginPhysicalArrival("physical-trip"); GameDataSaveLoader.SaveGame("INH_PhysicalArrivalIntent");
                        }
                        else Assert(trip.Stage == PhysicalStage.InTransit && caravan.Tile == destination.Tile,"Arrival intent reload observes native location without recreating or moving objects");
                        service.DeliverPhysical("physical-trip");
                        Assert(pawn.Spawned && pawn.Map == destination && caravan.Destroyed && pawn.inventory.innerContainer.Contains(cargo),"The same pawn and cargo enter the second map and the transit caravan is removed");
                        Refuses(delegate { PhysicalCaravanAdapter.EnterDestination(core,caravan,destination); },"Repeated entry cannot duplicate the delivered party");
                        service.DeliverPhysical("physical-trip");
                        Assert(service.PhysicalTransports.Single(t => t.Cause == "physical-trip").Stage == PhysicalStage.Arrived,"Physical arrival receipt makes repeated delivery harmless");
                        GameDataSaveLoader.SaveGame("INH_PhysicalDelivered");
                    }
                    else pawn = destination.mapPawns.AllPawns.Single(p => p.GetUniqueLoadID() == ids[1]);
                    Assert(service.PhysicalTransports.Single(t => t.Cause == "physical-trip").Stage == PhysicalStage.Arrived,"Delivered physical journal survives final save and reload");
                    Assert(SameSavedPawn(pawn) && pawn.inventory.innerContainer.Select(t => t.GetUniqueLoadID()).OrderBy(x => x).SequenceEqual(File.ReadAllLines(Meta("inventory"))),"Delivered pawn, personal state and inventory IDs survive entry and reload");
                    Assert(TotalSteel().ToString() == ids[7] && core.Ledger.TotalAbstractQuantity("ludeon.rimworld:Steel") == 0,"Physical transfer never duplicates or abstracts native map and caravan items");
                }
                Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception e) { Log.Error("[INH-P04] FAIL " + e); Application.Quit(1); }
        }
    }
}
