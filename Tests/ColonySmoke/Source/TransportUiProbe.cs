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
    public sealed class TransportUiProbe : GameComponent
    {
        private bool done,started,captured;
        private int frames;
        private TransportOrderWindow window;
        public TransportUiProbe(Game game) { }
        private static string Meta { get { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-ui-ids.txt"); } }
        private static void Assert(bool value,string text) { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-UI] PASS " + text); }
        private static void Refuses(Action action,string text) { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,text); }
        private static long Steel() { return Find.Maps.Sum(m => m.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t => (long)t.stackCount))+Find.Maps.SelectMany(m => m.mapPawns.AllPawns).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().Where(p => p.inventory != null).Sum(p => p.inventory.innerContainer.Where(t => t.def == ThingDefOf.Steel).Sum(t => (long)t.stackCount)); }
        private static Thing Spawn(Map map,ThingDef def,int count)
        { var t=ThingMaker.MakeThing(def); t.stackCount=count; GenSpawn.Spawn(t,map.AllCells.First(c => c.Standable(map) && c.GetThingList(map).Count == 0),map); return t; }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("inh-ui") || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current; AssertOnce(core != null && core.IsReady && service != null);
                if (GenCommandLine.CommandLineArgPassed("inh-ui-render")) { Render(service); return; }
                done=true; Find.TickManager.Pause();
                Assert(!GenCommandLine.CommandLineArgPassed("inh-physical-probe"),"Normal UI command path works without a physical developer argument");
                var mod=LoadedModManager.GetMod<ColonyMod>(); var settings=mod.GetSettings<ColonySettings>();
                var controller=new TransportCommandController(service);
                if (GenCommandLine.CommandLineArgPassed("inh-ui-save"))
                {
                    Map origin=Find.CurrentMap; var neighbors=new List<PlanetTile>(); Find.World.grid.GetTileNeighbors(origin.Tile,neighbors);
                    PlanetTile tile=neighbors.First(i => !Find.World.grid[i].WaterCovered && Find.World.grid[i].PrimaryBiome.canBuildBase && !Find.World.grid[i].PrimaryBiome.impassable && Find.World.grid[i].hilliness != Hilliness.Impassable && !Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == i));
                    var parent=(Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement); parent.Tile=tile; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                    Map destination=GetOrGenerateMapUtility.GetOrGenerateMap(tile,new IntVec3(100,1,100),WorldObjectDefOf.Settlement,null,false);
                    string from=service.RegisterDirectMap(origin),to=service.RegisterDirectMap(destination);
                    Pawn pawn=origin.mapPawns.FreeColonistsSpawned.First(p => !p.Downed && !p.Dead);
                    Thing steel=Spawn(origin,ThingDefOf.Steel,75),food=Spawn(origin,DefDatabase<ThingDef>.GetNamed("MealSimple"),8);
                    settings.physicalEnabled=false;
                    int count=core.Ledger.EntityCount;
                    Refuses(delegate { service.ReservePhysicalTransport("disabled",from,to,new[] {pawn},new[] {new CargoSelection(steel,30)}); },"Disabled setting refuses native reservations without creating records");
                    Assert(core.Ledger.EntityCount == count && pawn.Spawned && steel.stackCount == 75,"Disabled UI transport has no physical or registry side effect");
                    settings.physicalEnabled=true; mod.WriteSettings();
                    var draft=new TransportDraft(service); draft.SetSource(from); draft.SetDestination(to); draft.SelectPawn(pawn,true); draft.SetCargo(steel,30,false);
                    pawn.drafter.Drafted=true; Assert(!service.AvailableTransportPawns(from).Contains(pawn),"Drafted pawns are excluded from selectable transport party"); pawn.drafter.Drafted=false;
                    int before=core.Ledger.EntityCount; steel.stackCount=20;
                    Refuses(delegate { draft.Submit(); },"A stale UI quantity is revalidated before reservation");
                    Assert(core.Ledger.EntityCount == before && pawn.Spawned,"Rejected stale draft creates no person or shipment"); steel.stackCount=75;
                    var invalid=new TransportDraft(service); invalid.SetSource(from); invalid.SetDestination(to); invalid.SelectPawn(pawn,true); invalid.SetCargo(steel,1,true);
                    Refuses(delegate { invalid.Submit(); },"Non-food cannot bypass delivery checks as a travel supply");
                    var cancelled=new TransportDraft(service); cancelled.SetSource(from); cancelled.SetDestination(to); cancelled.SelectPawn(pawn,true); cancelled.SetCargo(steel,5,false); cancelled.Submit(); controller.Cancel(cancelled.Cause); controller.Cancel(cancelled.Cause);
                    var reset=new TransportDraft(service); reset.SetSource(from); reset.SelectPawn(pawn,true); reset.SetCargo(steel,4,false); reset.SetSource(to);
                    Assert(reset.SelectedPeople == 0 && reset.SelectedItems == 0,"Changing draft origin clears its former pawn and cargo selections");
                    draft.SetCargo(food,6,true); string shipment=draft.Submit();
                    Assert(draft.Submit() == shipment && pawn.Spawned && steel.stackCount == 75,"The same UI reserve action returns one shipment and does not move assets");
                    Refuses(delegate { draft.SetDestination(from); },"Submitted UI request cannot be silently edited");
                    Assert(!service.AvailableTransportPawns(from).Contains(pawn) && service.AvailableTransportCargo(from).Single(c => c.Thing == steel).Available == 45,"UI choices exclude reserved pawns and display only remaining unreserved quantity");
                    GameDataSaveLoader.SaveGame("INH_UiReserved"); controller.Dispatch(draft.Cause); controller.Dispatch(draft.Cause);
                    var record=((ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core)))).physical.Single(r => r.cause == draft.Cause);
                    var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == record.caravanId);
                    Thing carriedFood=pawn.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == record.cargo.Single(c => c.journeySupply).cargoNativeId);
                    carriedFood.SplitOff(1).Destroy(); service.Tick();
                    Assert(service.PhysicalTransports.Single(t => t.Cause == draft.Cause).Stage == PhysicalStage.InTransit && carriedFood.stackCount == 5,"A spent travel meal does not mark intact delivery cargo as lost");
                    File.WriteAllLines(Meta,new[] {core.Header.SaveScopeId,from,to,pawn.GetUniqueLoadID(),record.cargo.Single(c => !c.journeySupply).cargoNativeId,carriedFood.GetUniqueLoadID(),record.caravanId,origin.uniqueID.ToString(),destination.uniqueID.ToString(),draft.Cause,shipment,core.Ledger.Entities.Single(e => e.BoundThingLoadId == pawn.GetUniqueLoadID()).Id,Steel().ToString()});
                    Assert(caravan.pather.Moving && caravan.pather.Destination == destination.Tile && steel.stackCount == 45,"UI departure controller starts the native route exactly once");
                    GameDataSaveLoader.SaveGame("INH_UiTransit");
                }
                else
                {
                    var ids=File.ReadAllLines(Meta); Assert(ColonyMod.PhysicalEnabled,"Opt-in transport setting survives a new game process");
                    Assert(core.Header.SaveScopeId == ids[0] && core.Ledger.GetEntity(ids[11]).BoundThingLoadId == ids[3],"UI save retains shipment and permanent original-person binding");
                    Map destination=Find.Maps.Single(m => m.uniqueID.ToString() == ids[8]); Pawn pawn;
                    if (GenCommandLine.CommandLineArgPassed("inh-ui-loss") || GenCommandLine.CommandLineArgPassed("inh-ui-loss-final"))
                    {
                        var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[6]); pawn=caravan.PawnsListForReading.Single(p => p.GetUniqueLoadID() == ids[3]);
                        Thing cargo=pawn.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == ids[4]); int before=core.Ledger.EntityCount;
                        if (GenCommandLine.CommandLineArgPassed("inh-ui-loss")) { cargo.SplitOff(1).Destroy(); service.Tick(); }
                        Assert(service.PhysicalTransports.Single(t => t.Cause == ids[9]).Stage == PhysicalStage.NeedsAttention && cargo.stackCount == 29 && Steel() == long.Parse(ids[12])-1,"Real delivery loss is held without recreating cargo or changing its identity");
                        Refuses(delegate { controller.Dispatch(ids[9]); },"Held transit cannot be retried as a fresh departure");
                        Refuses(delegate { controller.Cancel(ids[9]); },"Held departed cargo cannot be cancelled as an undeparted reservation");
                        Assert(core.Ledger.EntityCount == before && caravan.PawnsListForReading.Contains(pawn),"Loss handling retains original party and registry without added objects");
                        if (GenCommandLine.CommandLineArgPassed("inh-ui-loss")) GameDataSaveLoader.SaveGame("INH_UiLossHeld");
                        Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); return;
                    }
                    if (GenCommandLine.CommandLineArgPassed("inh-ui-load"))
                    {
                        var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[6]); pawn=caravan.PawnsListForReading.Single(p => p.GetUniqueLoadID() == ids[3]);
                        Refuses(delegate { controller.Deliver(ids[9]); },"Normal UI arrival action refuses a caravan still on its route");
                        Assert(pawn.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == ids[5]).stackCount == 5,"Spent travel supplies stay spent after save and reload");
                        for (int i=0; i<2000 && (caravan.Tile != destination.Tile || caravan.pather.Moving); i++) { Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame+250); caravan.pather.PatherTickInterval(250); }
                        service.Tick(); Assert(caravan.Destroyed && pawn.Spawned && pawn.Map == destination,"Normal enabled transport automatically enters its reached destination");
                        controller.Deliver(ids[9]); GameDataSaveLoader.SaveGame("INH_UiDelivered");
                    }
                    else pawn=destination.mapPawns.AllPawns.Single(p => p.GetUniqueLoadID() == ids[3]);
                    Assert(service.PhysicalTransports.Single(t => t.Cause == ids[9]).Stage == PhysicalStage.Arrived && pawn.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == ids[4]).stackCount == 30 && Steel().ToString() == ids[12],"UI workflow preserves delivery quantity, original cargo identity and total physical stock");
                    Assert(pawn.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == ids[5]).stackCount == 5,"Delivery does not regenerate consumed travel food");
                }
                Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception e) { done=true; Log.Error("[INH-UI] FAIL " + e); Application.Quit(1); }
        }
        private void AssertOnce(bool ready) { if (!started) { Assert(ready,"Colony UI workflow initialized"); started=true; } }
        private void Render(ColonyService service)
        {
            string language=GenCommandLine.CommandLineArgPassed("inh-ui-japanese") ? "Japanese" : "English";
            string path=Path.Combine(GenFilePaths.SaveDataFolderPath,"TransportOrder-"+language+".png");
            if (window == null)
            {
                Assert(LanguageDatabase.activeLanguage.folderName.StartsWith(language,StringComparison.Ordinal),"Render process starts in the requested language without reloading live game data");
                File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"translation-errors-"+language+".txt"),LanguageDatabase.activeLanguage.loadErrors.Concat(LanguageDatabase.activeLanguage.defInjections.SelectMany(p => p.loadErrors)).ToArray());
                Find.TickManager.Pause(); window=new TransportOrderWindow(service);
                var ids=File.ReadAllLines(Meta); window.Draft.SetSource(ids[1]); window.Draft.SetDestination(ids[2]);
                var pawn=service.AvailableTransportPawns(ids[1]).First(); window.Draft.SelectPawn(pawn,true);
                var steel=service.AvailableTransportCargo(ids[1]).First(c => c.Thing.def == ThingDefOf.Steel); window.Draft.SetCargo(steel.Thing,5,false);
                window.CargoSearch=steel.Thing.LabelCap.ToString();
                if (File.Exists(path)) File.Delete(path);
                Find.WindowStack.Add(window); return;
            }
            frames++;
            if (!captured && window.Rendered && frames > 30) { ScreenCapture.CaptureScreenshot(path); captured=true; }
            if (captured && File.Exists(path) && new FileInfo(path).Length > 1000)
            { Assert(window.Rendered,"Native order window repaints and captures "+language+" screenshot"); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
        }
    }
}
