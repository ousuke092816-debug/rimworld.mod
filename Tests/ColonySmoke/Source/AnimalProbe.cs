using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    public sealed class AnimalProbe : GameComponent
    {
        private bool done,captured;
        private int frames;
        private TransportOrderWindow window;
        public AnimalProbe(Game game) { }
        private static string Meta { get { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-animal-ids.txt"); } }
        private static void Assert(bool value,string text) { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-ANIMAL] PASS "+text); }
        private static void Refuses(Action action,string text) { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,text); }
        private static ColonyState State(ColonyService service,CoreWorldComponent core) { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core))); }
        private static Thing SpawnSteel(Map map)
        { var t=ThingMaker.MakeThing(ThingDefOf.Steel); t.stackCount=75; GenSpawn.Spawn(t,map.AllCells.First(c => c.Standable(map) && c.GetThingList(map).Count == 0),map); return t; }
        private static IEnumerable<Pawn> AllPawns()
        { return Find.Maps.SelectMany(m => m.mapPawns.AllPawns).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Distinct(); }
        private static string AnimalState(Pawn p)
        { return p.GetUniqueLoadID()+"|"+p.def.defName+"|"+p.Name+"|"+p.gender+"|"+p.ageTracker.AgeBiologicalTicks+"|"+p.ageTracker.AgeChronologicalTicks+"|"+p.Faction.GetUniqueLoadID()+"|"+string.Join(",",p.health.hediffSet.hediffs.Select(h => h.def.defName+":"+h.Severity.ToString("R")).ToArray()); }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("inh-animal") || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current; Assert(core.IsReady && service != null,"Colony animal workflow is ready");
                if (GenCommandLine.CommandLineArgPassed("inh-animal-render")) { Render(service); return; }
                done=true; Find.TickManager.Pause();
                var controller=new TransportCommandController(service);
                if (GenCommandLine.CommandLineArgPassed("inh-animal-checkpoint")) Assert(core.Ledger.EntityCount == int.Parse(File.ReadAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-animal-checkpoint-count.txt"))),"Animal checkpoint recovery reuses the existing receipt without adding Core identities");
                if (GenCommandLine.CommandLineArgPassed("inh-animal-save"))
                {
                    var old=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-ui-ids.txt"));
                    Map origin=Find.Maps.Single(m => m.uniqueID.ToString() == old[7]);
                    Pawn human=service.AvailableTransportPawns(old[1]).First();
                    var animal=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Muffalo"),Faction.OfPlayer);
                    animal.Name=new NameSingle("Porter"); animal.ageTracker.AgeBiologicalTicks=3600000L*5;
                    animal.ageTracker.AgeChronologicalTicks=animal.ageTracker.AgeBiologicalTicks;
                    GenSpawn.Spawn(animal,origin.AllCells.First(c => c.Standable(origin) && c.GetThingList(origin).Count == 0),origin);
                    var wound=HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Scratch"),animal,animal.RaceProps.body.corePart); wound.Severity=0.5f; animal.health.AddHediff(wound);
                    Thing steel=SpawnSteel(origin); int before=core.Ledger.EntityCount;
                    var draft=new TransportDraft(service); draft.SetSource(old[1]); draft.SetDestination(old[2]); draft.SelectPawn(human,true); draft.SetCargo(steel,75,false);
                    Assert(!draft.Mass.Fits && !draft.CanSubmit,"One colonist cannot carry the selected heavy cargo");
                    Refuses(delegate { draft.Submit(); },"Overweight UI request is refused before identity allocation");
                    Refuses(delegate { service.ReservePhysicalTransport("overweight",old[1],old[2],new[] {human},new[] {new CargoSelection(steel,75)}); },"Direct service calls also enforce native carrying capacity");
                    Refuses(delegate { service.ReservePhysicalTransport("animal-only",old[1],old[2],new[] {animal},new[] {new CargoSelection(steel,1)}); },"Animals cannot travel without a colonist owner");
                    Assert(core.Ledger.EntityCount == before && human.Spawned && animal.Spawned && steel.stackCount == 75,"All rejected requests leave native assets and Core records intact");
                    animal.SetFaction(null); Assert(!service.AvailableTransportAnimals(old[1]).Contains(animal),"Wild animals are excluded"); animal.SetFaction(Faction.OfPlayer);
                    Assert(service.AvailableTransportAnimals(old[1]).Contains(animal),"A player pack animal appears in normal transport choices");
                    draft.SelectPawn(animal,true); Assert(draft.Mass.Fits && draft.CanSubmit && draft.SelectedPeople == 1 && draft.SelectedAnimals == 1,"A pack animal adds native carrying capacity without becoming a person");
                    var cancelled=new TransportDraft(service); cancelled.SetSource(old[1]); cancelled.SetDestination(old[2]); cancelled.SelectPawn(human,true); cancelled.SelectPawn(animal,true); cancelled.SetCargo(steel,75,false);
                    cancelled.Submit(); controller.Cancel(cancelled.Cause); controller.Cancel(cancelled.Cause);
                    Assert(animal.Spawned && steel.stackCount == 75 && service.AvailableTransportAnimals(old[1]).Contains(animal),"Repeated cancellation releases the same animal and untouched cargo");
                    GameDataSaveLoader.SaveGame("INH_AnimalRender");
                    string shipment=draft.Submit(); Assert(draft.Submit() == shipment,"Repeated animal order submission returns one shipment");
                    Assert(!service.AvailableTransportAnimals(old[1]).Contains(animal),"Reserved animals cannot be selected by a second order");
                    Refuses(delegate { service.ReservePhysicalTransport("double-animal",old[1],old[2],new[] {service.AvailableTransportPawns(old[1]).First(),animal},new[] {new CargoSelection(steel,1)}); },"The service refuses an already reserved animal");
                    var state=State(service,core); var trip=state.physical.Single(r => r.cause == draft.Cause);
                    Assert(state.animals.Count == 1 && trip.animals.Count == 1 && trip.party.Count == 1 && core.Ledger.GetEntity(state.animals[0].id).Kind == "animal","Animal identity uses its own permanent Core binding and manifest");
                    File.WriteAllLines(Meta,new[] {core.Header.SaveScopeId,old[1],old[2],human.GetUniqueLoadID(),animal.GetUniqueLoadID(),steel.GetUniqueLoadID(),draft.Cause,shipment,state.animals[0].id,AnimalState(animal)});
                    GameDataSaveLoader.SaveGame("INH_AnimalReserved");
                }
                else
                {
                    var ids=File.ReadAllLines(Meta); var state=State(service,core); var trip=state.physical.Single(r => r.cause == ids[6]);
                    Pawn animal=AllPawns().Single(p => p.GetUniqueLoadID() == ids[4]); Pawn human=AllPawns().Single(p => p.GetUniqueLoadID() == ids[3]);
                    Assert(core.Header.SaveScopeId == ids[0] && trip.shipmentId == ids[7] && trip.animals.Single() == ids[8] && core.Ledger.GetEntity(ids[8]).BoundThingLoadId == ids[4],"Animal, world and shipment bindings survive a new game process");
                    string expectedAnimal=GenCommandLine.CommandLineArgPassed("inh-animal-final") ? File.ReadAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-animal-final-state.txt")) : ids[9];
                    var expectedParts=expectedAnimal.Split('|'); var actualParts=AnimalState(animal).Split('|');
                    Assert(actualParts.Take(4).SequenceEqual(expectedParts.Take(4)) && actualParts.Skip(6).SequenceEqual(expectedParts.Skip(6)) &&
                        long.Parse(actualParts[4])-long.Parse(expectedParts[4]) >= 0 && long.Parse(actualParts[4])-long.Parse(expectedParts[4]) <= 3 &&
                        long.Parse(actualParts[5])-long.Parse(expectedParts[5]) >= 0 && long.Parse(actualParts[5])-long.Parse(expectedParts[5]) <= 3,"Original animal retains name, race, sex, age, faction and injury (at most three startup ticks)");
                    Map destination=Find.Maps.Single(m => m.uniqueID == service.Bases.Single(b => b.Id == ids[2]).MapId);
                    if (GenCommandLine.CommandLineArgPassed("inh-animal-reserved"))
                    {
                        Assert(trip.stage == PhysicalStage.Reserved && animal.Spawned && human.Spawned,"Reserved reload moves no animal or pawn");
                        var extra=new List<Thing>(); for (int i=0; i<10; i++) { var t=ThingMaker.MakeThing(ThingDefOf.Steel); t.stackCount=75; human.inventory.innerContainer.TryAdd(t,false); extra.Add(t); }
                        int before=core.Ledger.EntityCount;
                        Refuses(delegate { controller.Dispatch(ids[6]); },"Departure rechecks inventory weight after reservation");
                        Assert(core.Ledger.EntityCount == before && animal.Spawned && human.Spawned && service.PhysicalTransports.Single(r => r.Cause == ids[6]).Stage == PhysicalStage.NeedsAttention,"Overweight departure stays at origin without splitting cargo or adding receipts");
                        foreach (Thing t in extra) { human.inventory.innerContainer.Remove(t); t.Destroy(); }
                        var selections=new[] {new CargoSelection(Find.Maps.SelectMany(m => m.listerThings.AllThings).Single(t => t.GetUniqueLoadID() == ids[5]),75)};
                        var mass=PhysicalCaravanAdapter.Measure(new[] {human,animal},selections);
                        controller.Dispatch(ids[6]); controller.Dispatch(ids[6]);
                        trip=State(service,core).physical.Single(r => r.cause == ids[6]); var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == trip.caravanId);
                        Assert(caravan.PawnsListForReading.Contains(animal) && caravan.PawnsListForReading.Contains(human) && !animal.Spawned,"Departure keeps the exact original animal in the native caravan");
                        Assert(Math.Abs(caravan.MassUsage-mass.Usage) < 0.001f && Math.Abs(caravan.MassCapacity-mass.Capacity) < 0.001f && !caravan.ImmobilizedByMass,"Weight preview matches the native caravan after loading");
                        Assert(animal.inventory.innerContainer.Any(t => t.GetUniqueLoadID() == trip.cargo.Single().cargoNativeId),"Heavy cargo is assigned to the pack animal without merging its native ID");
                        File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-animal-transit.txt"),new[] {trip.caravanId,trip.cargo.Single().cargoNativeId});
                        GameDataSaveLoader.SaveGame("INH_AnimalTransit");
                    }
                    else if (GenCommandLine.CommandLineArgPassed("inh-animal-load"))
                    {
                        var transit=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-animal-transit.txt")); var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == transit[0]);
                        Assert(trip.stage == PhysicalStage.InTransit && caravan.PawnsListForReading.Contains(animal),"Original travelling animal resumes after process restart");
                        caravan.RemovePawn(animal); service.Tick(); int before=core.Ledger.EntityCount;
                        Refuses(delegate { service.PreviewPhysicalLoss(ids[6]); },"A missing animal cannot be written off as a cargo loss");
                        Assert(core.Ledger.EntityCount == before && service.PhysicalTransports.Single(r => r.Cause == ids[6]).Stage == PhysicalStage.NeedsAttention,"Missing animal blocks reconciliation without replacement or added receipts");
                        caravan.AddPawn(animal,true); controller.Recheck(ids[6]);
                        Thing cargo=animal.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == transit[1]); cargo.SplitOff(1).Destroy(); service.Tick();
                        var preview=service.PreviewPhysicalLoss(ids[6]); string receipt=controller.AcceptLoss(preview); Assert(controller.AcceptLoss(preview) == receipt && cargo.stackCount == 74,"Loss acknowledgement works on animal inventory and replays once");
                        for (int i=0; i<2000 && (caravan.Tile != destination.Tile || caravan.pather.Moving); i++) { Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame+250); caravan.pather.PatherTickInterval(250); }
                        service.Tick(); controller.Deliver(ids[6]);
                        Assert(caravan.Destroyed && animal.Spawned && human.Spawned && animal.Map == destination && human.Map == destination && cargo.stackCount == 74,"Native travel delivers the same animal and remaining cargo to the destination");
                        // Store actual post-travel age; path ticks advance game time while maps stay paused.
                        File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-animal-final-state.txt"),AnimalState(animal));
                        GameDataSaveLoader.SaveGame("INH_AnimalDelivered");
                    }
                    else
                    {
                        var transit=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-animal-transit.txt"));
                        Assert(trip.stage == PhysicalStage.Arrived && trip.losses.Count == 1 && animal.Spawned && animal.Map == destination && animal.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == transit[1]).stackCount == 74,"Delivered reload preserves animal cargo identity and acknowledged loss");
                        int before=core.Ledger.EntityCount; controller.Deliver(ids[6]); controller.Deliver(ids[6]);
                        Assert(core.Ledger.EntityCount == before && AllPawns().Count(p => p.GetUniqueLoadID() == ids[4]) == 1,"Repeated final delivery creates no animal, shipment or receipt");
                    }
                }
                Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception e) { done=true; Log.Error("[INH-ANIMAL] FAIL "+e); Application.Quit(1); }
        }
        private void Render(ColonyService service)
        {
            string language=GenCommandLine.CommandLineArgPassed("inh-animal-japanese") ? "Japanese" : "English";
            string path=Path.Combine(GenFilePaths.SaveDataFolderPath,"TransportAnimals-"+language+".png");
            if (window == null)
            {
                Assert(LanguageDatabase.activeLanguage.folderName.StartsWith(language,StringComparison.Ordinal),"Render process uses the requested language");
                File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"translation-errors-"+language+".txt"),LanguageDatabase.activeLanguage.loadErrors.Concat(LanguageDatabase.activeLanguage.defInjections.SelectMany(p => p.loadErrors)).ToArray());
                Find.TickManager.Pause(); var old=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-ui-ids.txt"));
                window=new TransportOrderWindow(service); window.Draft.SetSource(old[1]); window.Draft.SetDestination(old[2]);
                Pawn human=service.AvailableTransportPawns(old[1]).First(); window.Draft.SelectPawn(human,true);
                var ids=File.ReadAllLines(Meta);
                Pawn animal=service.AvailableTransportAnimals(old[1]).Single(p => p.GetUniqueLoadID() == ids[4]); window.Draft.SelectPawn(animal,true);
                Thing steel=service.AvailableTransportCargo(old[1]).Single(c => c.Thing.GetUniqueLoadID() == ids[5]).Thing; window.Draft.SetCargo(steel,75,false); window.CargoSearch=steel.LabelCap.ToString();
                if (File.Exists(path)) File.Delete(path); Find.WindowStack.Add(window); return;
            }
            frames++;
            if (!captured && window.Rendered && frames > 30) { ScreenCapture.CaptureScreenshot(path); captured=true; }
            if (captured && File.Exists(path) && new FileInfo(path).Length > 1000) { done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
        }
    }
}
