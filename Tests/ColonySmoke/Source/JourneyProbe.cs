using System;
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
    // Advances full ordinary game ticks, not debug time jumps or isolated path ticks.
    public sealed class JourneyProbe : GameComponent
    {
        private bool initialized,done,travelStarted;
        private string[] ids;
        private int startTick,lastReport;
        private Pawn human,animal;
        private Caravan caravan;
        private bool sawNight,sawFood,sawRest,sawAnimalFood;
        public JourneyProbe(Game game) { }
        private static string FilePath(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,(GenCommandLine.CommandLineArgPassed("inh-journey-deplete") ? "p04-journey-deplete-" : "p04-journey-")+key+".txt"); }
        private static void Assert(bool value,string text) { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-JOURNEY] PASS "+text); }
        private static ColonyState State(ColonyService service,CoreWorldComponent core) { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core))); }
        private static Thing Spawn(Map map,ThingDef def,int count)
        { var t=ThingMaker.MakeThing(def); t.stackCount=count; GenSpawn.Spawn(t,map.AllCells.First(c => c.Standable(map) && c.GetThingList(map).Count == 0),map); return t; }
        private static int Remaining(PhysicalTransportRecord trip,string def,Caravan caravan)
        {
            var cargo=trip.cargo.Single(c => c.defName == def);
            return caravan.PawnsListForReading.SelectMany(p => p.inventory.innerContainer).Where(t => t.GetUniqueLoadID() == cargo.cargoNativeId).Sum(t => t.stackCount);
        }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("inh-journey") || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current;
                if (!initialized)
                {
                    Assert(core.IsReady && service != null && ColonyMod.PhysicalEnabled,"Ordinary enabled transport is ready for full-tick journey checks");
                    Find.TickManager.Pause();
                    if (GenCommandLine.CommandLineArgPassed("inh-journey-save")) Setup(service,core);
                    else Restore(service,core);
                    initialized=true;
                    if (done) return;
                }
                // Bounded per-frame batch lets the harness and application continue to update.
                for (int i=0; i<300; i++)
                {
                    int tick=Find.TickManager.TicksGame; Find.TickManager.DoSingleTick();
                    if (Find.TickManager.TicksGame != tick+1) throw new InvalidOperationException("Journey check requires one ordinary game tick per step");
                    if (!caravan.Destroyed)
                    {
                        sawNight |= caravan.NightResting;
                        sawFood |= human.needs.food.CurLevel > 0.4f;
                        sawAnimalFood |= animal.needs.food.CurLevel > 0.4f;
                        sawRest |= human.needs.rest.CurLevel > 0.5f;
                    }
                }
                service.Tick(); int elapsed=Find.TickManager.TicksGame-startTick;
                if (elapsed-lastReport >= 6000) { lastReport=elapsed; Log.Message("[INH-JOURNEY] STEP full ordinary ticks="+elapsed+"; human food="+human.needs.food.CurLevel+"; animal food="+animal.needs.food.CurLevel+"; rest="+human.needs.rest.CurLevel+"; night="+sawNight); }
                var trip=State(service,core).physical.Single(r => r.cause == ids[3]);
                if (GenCommandLine.CommandLineArgPassed("inh-journey-deplete") && elapsed >= 1200)
                {
                    Assert(trip.stage == PhysicalStage.InTransit && Remaining(trip,"MealSimple",caravan) == 0 && sawFood && trip.losses.Count == 0,"Native eating exhausts a travel-food stack without holding delivery or recreating it");
                    File.WriteAllText(FilePath("count"),core.Ledger.EntityCount.ToString()); GameDataSaveLoader.SaveGame("INH_JourneyDepleted"); Finish(); return;
                }
                if (GenCommandLine.CommandLineArgPassed("inh-journey-save") && elapsed >= 30000)
                {
                    Assert(trip.stage == PhysicalStage.InTransit && !caravan.Destroyed && sawFood && sawAnimalFood && sawRest,"Human eating, animal feeding and rest recover during half a day of full simulation");
                    Assert(Remaining(trip,"MealSimple",caravan) < 10 && trip.losses.Count == 0,"Native eating consumes marked travel meals without delivery-loss receipts");
                    SaveMetrics(trip); GameDataSaveLoader.SaveGame("INH_JourneyHalf"); Finish(); return;
                }
                if (!GenCommandLine.CommandLineArgPassed("inh-journey-save") && !travelStarted && elapsed >= 60000)
                {
                    Assert(sawNight && sawFood && sawAnimalFood && sawRest,"A full day includes native night rest, eating and animal feeding");
                    Assert(trip.stage == PhysicalStage.InTransit && trip.losses.Count == 0 && Remaining(trip,"MealSimple",caravan) < 10,"Spent supplies remain spent throughout normal-time waiting");
                    new TransportCommandController(service).Route(ids[3]); travelStarted=true;
                    Assert(caravan.pather.Moving,"The same native caravan resumes its route after waiting");
                }
                if (travelStarted && trip.stage == PhysicalStage.Arrived)
                {
                    Assert(caravan.Destroyed && human.Spawned && animal.Spawned && human.Map == animal.Map && human.Map.uniqueID.ToString() == ids[7],"Full ordinary ticks deliver original people and animals without tile edits");
                    var delivery=trip.cargo.Single(c => !c.journeySupply);
                    Assert(new[] {human,animal}.SelectMany(p => p.inventory.innerContainer).Single(t => t.GetUniqueLoadID() == delivery.cargoNativeId).stackCount == 10 && trip.losses.Count == 0,"Normal feeding leaves the non-food delivery cargo intact");
                    File.WriteAllLines(FilePath("delivered"),new[] {RemainingInParty(trip,"MealSimple").ToString(),RemainingInParty(trip,"Hay").ToString(),core.Ledger.EntityCount.ToString(),Find.TickManager.TicksGame.ToString(),human.GetUniqueLoadID(),animal.GetUniqueLoadID()});
                    GameDataSaveLoader.SaveGame("INH_JourneyDelivered"); Finish(); return;
                }
                if (elapsed > 180000) throw new InvalidOperationException("Native journey did not finish within three simulated days");
            }
            catch (Exception e) { done=true; Log.Error("[INH-JOURNEY] FAIL "+e); Application.Quit(1); }
        }
        private void Setup(ColonyService service,CoreWorldComponent core)
        {
            var prior=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-animal-ids.txt"));
            var ui=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-ui-ids.txt"));
            Map origin=Find.Maps.Single(m => m.uniqueID.ToString() == ui[7]);
            human=service.AvailableTransportPawns(ui[1]).First(); animal=service.AvailableTransportAnimals(ui[1]).Single(p => p.GetUniqueLoadID() == prior[4]);
            // Test fixture: start hungry and tired. All later need updates use the real game loop.
            human.needs.food.CurLevel=0.05f; animal.needs.food.CurLevel=0.05f; human.needs.rest.CurLevel=0.1f;
            int meals=GenCommandLine.CommandLineArgPassed("inh-journey-deplete") ? 1 : 10;
            Thing steel=Spawn(origin,ThingDefOf.Steel,10),food=Spawn(origin,DefDatabase<ThingDef>.GetNamed("MealSimple"),meals),hay=Spawn(origin,DefDatabase<ThingDef>.GetNamed("Hay"),80);
            var draft=new TransportDraft(service); draft.SetSource(ui[1]); draft.SetDestination(ui[2]); draft.SelectPawn(human,true); draft.SelectPawn(animal,true); draft.SetCargo(steel,10,false);
            int before=core.Ledger.EntityCount; float empty=draft.FoodDays;
            draft.SetCargo(food,meals,true); draft.SetCargo(hay,80,true); float fed=draft.FoodDays;
            Assert(!float.IsNaN(fed) && fed > empty && fed > 0 && core.Ledger.EntityCount == before,"Food forecast responds to supplies without creating records");
            string shipment=draft.Submit(); new TransportCommandController(service).Dispatch(draft.Cause);
            var trip=State(service,core).physical.Single(r => r.cause == draft.Cause);
            caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == trip.caravanId); caravan.pather.StopDead();
            Assert(!caravan.pather.Moving && trip.stage == PhysicalStage.InTransit,"A stopped caravan remains the original in-transit physical owner");
            startTick=Find.TickManager.TicksGame;
            ids=new[] {core.Header.SaveScopeId,human.GetUniqueLoadID(),animal.GetUniqueLoadID(),draft.Cause,shipment,trip.caravanId,startTick.ToString(),ui[8]}; File.WriteAllLines(FilePath("ids"),ids);
            File.WriteAllLines(FilePath("cargo"),trip.cargo.Select(c => c.cargoNativeId).ToArray());
            GameDataSaveLoader.SaveGame("INH_JourneyStart");
        }
        private void Restore(ColonyService service,CoreWorldComponent core)
        {
            ids=File.ReadAllLines(FilePath("ids")); startTick=int.Parse(ids[6]);
            var trip=State(service,core).physical.Single(r => r.cause == ids[3]);
            var all=Find.Maps.SelectMany(m => m.mapPawns.AllPawns).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Distinct().ToArray();
            human=all.Single(p => p.GetUniqueLoadID() == ids[1]); animal=all.Single(p => p.GetUniqueLoadID() == ids[2]);
            Assert(core.Header.SaveScopeId == ids[0] && trip.shipmentId == ids[4] && trip.caravanId == ids[5] && trip.cargo.Select(c => c.cargoNativeId).SequenceEqual(File.ReadAllLines(FilePath("cargo"))),"Mid-journey process restart retains all original native and shipment IDs");
            if (GenCommandLine.CommandLineArgPassed("inh-journey-deplete"))
            {
                caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[5]);
                Assert(trip.stage == PhysicalStage.InTransit && Remaining(trip,"MealSimple",caravan) == 0 && trip.losses.Count == 0 && core.Ledger.EntityCount == int.Parse(File.ReadAllText(FilePath("count"))),"Exhausted travel-food stack stays absent after process restart with no extra receipts");
                Assert(trip.cargo.Single(c => c.defName == "MealSimple").count == 1 && trip.cargo.Single(c => c.defName == "MealSimple").journeySupply,"Food exhaustion preserves the original immutable supply request");
                Finish(); return;
            }
            if (GenCommandLine.CommandLineArgPassed("inh-journey-final"))
            {
                var delivered=File.ReadAllLines(FilePath("delivered"));
                Assert(trip.stage == PhysicalStage.Arrived && RemainingInParty(trip,"MealSimple") == int.Parse(delivered[0]) && RemainingInParty(trip,"Hay") == int.Parse(delivered[1]),"Delivered reload preserves naturally consumed supplies without replenishment");
                int before=core.Ledger.EntityCount; new TransportCommandController(service).Deliver(ids[3]);
                Assert(before == core.Ledger.EntityCount && before == int.Parse(delivered[2]) && human.Spawned && animal.Spawned && human.Map == animal.Map,"Final replay adds no person, animal, cargo or arrival receipt");
                Finish(); return;
            }
            caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[5]); var metrics=File.ReadAllLines(FilePath("half"));
            Assert(trip.stage == PhysicalStage.InTransit && !caravan.pather.Moving && Remaining(trip,"MealSimple",caravan) == int.Parse(metrics[0]) && Remaining(trip,"Hay",caravan) == int.Parse(metrics[1]),"Mid-journey reload retains stopped route and remaining native food");
            Assert(Math.Abs(human.needs.food.CurLevel-float.Parse(metrics[2],System.Globalization.CultureInfo.InvariantCulture)) < 0.03f && Math.Abs(animal.needs.food.CurLevel-float.Parse(metrics[3],System.Globalization.CultureInfo.InvariantCulture)) < 0.03f && Math.Abs(human.needs.rest.CurLevel-float.Parse(metrics[4],System.Globalization.CultureInfo.InvariantCulture)) < 0.03f,"Hunger and rest survive saving without reset");
            sawNight=bool.Parse(metrics[5]); sawFood=bool.Parse(metrics[6]); sawAnimalFood=bool.Parse(metrics[7]); sawRest=bool.Parse(metrics[8]); lastReport=Find.TickManager.TicksGame-startTick;
        }
        private int RemainingInParty(PhysicalTransportRecord trip,string def)
        { var cargo=trip.cargo.Single(c => c.defName == def); return new[] {human,animal}.SelectMany(p => p.inventory.innerContainer).Where(t => t.GetUniqueLoadID() == cargo.cargoNativeId).Sum(t => t.stackCount); }
        private void SaveMetrics(PhysicalTransportRecord trip)
        { File.WriteAllLines(FilePath("half"),new[] {Remaining(trip,"MealSimple",caravan).ToString(),Remaining(trip,"Hay",caravan).ToString(),human.needs.food.CurLevel.ToString("R",System.Globalization.CultureInfo.InvariantCulture),animal.needs.food.CurLevel.ToString("R",System.Globalization.CultureInfo.InvariantCulture),human.needs.rest.CurLevel.ToString("R",System.Globalization.CultureInfo.InvariantCulture),sawNight.ToString(),sawFood.ToString(),sawAnimalFood.ToString(),sawRest.ToString()}); }
        private void Finish() { done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
    }
}
