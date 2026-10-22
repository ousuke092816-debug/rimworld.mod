using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
namespace Inheritance.ColonySmoke
{
    public sealed class PhysicalSupplyProbe : GameComponent
    {
        private bool done; private int frames;
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static bool Has(string suffix) { return GenCommandLine.CommandLineArgPassed("inh-physical-supply-"+suffix); }
        private static bool Selected { get { return Has("start") || Has("load") || Has("final"); } }
        public PhysicalSupplyProbe(Game game) { }
        public override void FinalizeInit() { if(Selected) Find.TickManager.Pause(); }
        private static object Call(object target,string name,params object[] args)
        { try { return target.GetType().GetMethod(name,Private).Invoke(target,args); } catch(TargetInvocationException e) {throw e.InnerException ?? e;} }
        private static void Assert(bool value,string message)
        { if(!value) throw new InvalidOperationException(message); Log.Message("[INH-PHYSICAL-SUPPLY] PASS "+message); }
        private static void Read(SupplyTransferProbe helper,Action action,string text) { Call(helper,"ReadOnly",action,text); }
        private static void Refuse(SupplyTransferProbe helper,Action action,string text) { Call(helper,"RefuseExact",action,text,true); }
        private static string EvidencePath { get { return Path.Combine(GenFilePaths.SaveDataFolderPath,"physical-supply-originals.txt"); } }
        private const string Route="physical-supply-independent-route";
        public override void GameComponentUpdate()
        {
            if(done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if(!core.IsReady && core.BlockReason == null) return;
            try {
                Find.TickManager.Pause(); var colony=ColonyMod.Current;
                if(core.IsReady && colony != null && frames++ < 2) return;
                Assert(core.IsReady && colony != null,"Independent physical supply fixture loads as a validated world");
                var helper=new SupplyTransferProbe(Current.Game);
                typeof(SupplyTransferProbe).GetField("core",Private).SetValue(helper,core);
                typeof(SupplyTransferProbe).GetField("service",Private).SetValue(helper,colony);
                Call(helper,"Bind"); string transferId;
                if(Has("start")) {
                    Call(helper,"CheckSourceEvidence","INH_SupplyIntentPrepared");
                    string intentId=(string)typeof(SupplyTransferProbe).GetField("intentId",Private).GetValue(helper);
                    var intent=colony.DelegatedSupplyIntents.Single(i => i.Id == intentId);
                    var source=colony.Bases.Where(b => b.Mode == BaseMode.Direct).First(b => colony.AvailableTransportPawns(b.Id).Any() && Find.Maps.Single(m => m.uniqueID == b.MapId).Tile.tileId != intent.Tile);
                    var map=Find.Maps.Single(m => m.uniqueID == source.MapId); var pawn=colony.AvailableTransportPawns(source.Id).First();
                    string native=(string)Call(helper,"NativePersistent"),clock=(string)Call(helper,"Clock");
                    // Explicit, isolated non-food test fixture. Never a product import or a replacement for original food.
                    var steel=ThingMaker.MakeThing(ThingDefOf.Steel); steel.stackCount=5;
                    var cell=map.AllCells.First(c => c.Standable(map) && !c.GetThingList(map).Any(t => t.def.category == ThingCategory.Item));
                    GenSpawn.Spawn(steel,cell,map);
                    var party=new[] {pawn}; var cargo=new[] {new CargoSelection(steel,5)};
                    PhysicalCaravanAdapter.ValidateDeparture(core,map,party,cargo);
                    Assert(native == (string)Call(helper,"NativePersistent") && clock == (string)Call(helper,"Clock"),"Adding only the explicit five-steel map fixture leaves every original food, stored Pawn, owner and clock exact");
                    Assert(pawn.GetUniqueLoadID() != intent.NativeId && pawn.GetUniqueLoadID() != intent.SourceNativeId,"The real eligible map party is distinct from original recipient and food-source Pawn");
                    colony.SaveSnapshot(core);
                    transferId=(string)Call(helper,"Arm","physical-supply-new-route-arm");
                    Refuse(helper,delegate {colony.ReservePhysicalTransport("physical-supply-blocked-route",source.Id,intent.CityId,party,cargo);},"An otherwise valid real direct-map departure is rejected before registering/reserving a new route to the Armed city");
                    Call(helper,"Disarm",transferId,"physical-supply-new-route-disarm");
                    colony.ReservePhysicalTransport(Route,source.Id,intent.CityId,party,cargo); colony.BeginPhysicalDeparture(Route);
                    Assert(colony.PhysicalTransports.Single(t => t.Cause == Route).Stage == PhysicalStage.Departing && pawn.Spawned && pawn.Map == map && steel.Spawned && steel.Map == map && steel.stackCount == 5,
                        "The same positive request reserves successfully after disarm and reaches only its metadata departure checkpoint");
                    colony.SaveSnapshot(core);
                    transferId=(string)Call(helper,"Arm","physical-supply-existing-route-arm");
                    Refuse(helper,delegate {colony.DispatchPhysical(Route);},"Existing independent native departure refuses before moving the real map Pawn or steel while destination is Armed");
                    Read(helper,delegate {colony.Tick();},"Armed independent physical checkpoint waits without raw metadata, native or clock changes");
                    File.WriteAllLines(EvidencePath,new[] {pawn.GetUniqueLoadID(),steel.GetUniqueLoadID(),source.Id,map.uniqueID.ToString()});
                    Call(helper,"Save","INH_PhysicalSupplyHeld",transferId); done=true; return;
                }
                colony.SaveSnapshot(core);
                transferId=(string)Call(helper,"Restore",Has("load") ? "INH_PhysicalSupplyHeld" : "INH_PhysicalSupplyReleased");
                var evidence=File.ReadAllLines(EvidencePath); var route=colony.PhysicalTransports.Single(t => t.Cause == Route);
                var originalMap=Find.Maps.Single(m => m.uniqueID.ToString() == evidence[3]);
                if(Has("load")) {
                    var pawn=originalMap.mapPawns.FreeColonistsSpawned.Single(p => p.GetUniqueLoadID() == evidence[0]);
                    var steel=originalMap.listerThings.AllThings.Single(t => t.GetUniqueLoadID() == evidence[1]);
                    Assert(route.Stage == PhysicalStage.Departing && route.CaravanId == null && steel.stackCount == 5,
                        "Bootstrap retains the exact original Departing checkpoint while Armed, without resetting it or generating a caravan");
                    Refuse(helper,delegate {colony.DispatchPhysical(Route);},"Reloaded original native departure remains a +0 refusal while Armed");
                    Read(helper,delegate {colony.Tick();},"Reloaded Armed physical checkpoint remains exact during automatic update");
                    Call(helper,"Disarm",transferId,"physical-supply-existing-route-disarm");
                    var result=colony.DispatchPhysical(Route);
                    Assert(result != null && result.PawnsListForReading.Contains(pawn) && !pawn.Spawned && pawn.inventory.innerContainer.Contains(steel) && steel.stackCount == 5 && steel.GetUniqueLoadID() == evidence[1] &&
                        colony.PhysicalTransports.Single(t => t.Cause == Route).Stage == PhysicalStage.InTransit,
                        "Explicit disarm permits ordinary actual departure using the same Pawn and the same whole five-steel object exactly once");
                    Assert(result.Tile == originalMap.Tile && !result.pather.Moving,"The test caravan remains at its true departure tile; no simulated travel or destination relocation is used");
                    Read(helper,delegate {Assert(colony.DispatchPhysical(Route) == result,"Dispatch replay returns the identical original caravan"); colony.Tick();},"Completed actual departure replay adds no caravan, cargo, proof or clock change");
                    Call(helper,"Save","INH_PhysicalSupplyReleased",transferId); done=true; return;
                }
                var caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == route.CaravanId);
                var carrier=caravan.PawnsListForReading.Single(p => p.GetUniqueLoadID() == evidence[0]);
                Assert(route.Stage == PhysicalStage.InTransit && carrier.inventory.innerContainer.Count(t => t.GetUniqueLoadID() == evidence[1] && t.stackCount == 5) == 1 && caravan.Tile == originalMap.Tile && !caravan.pather.Moving,
                    "Separate final reload preserves the single original carrier, whole cargo and departure tile without synthetic movement");
                Read(helper,delegate {Assert(colony.DispatchPhysical(Route) == caravan,"Final dispatch replay keeps its same historical caravan"); colony.Tick();},"Final native departure replay preserves all raw histories, original food/Pawns and clock");
                done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            } catch(Exception error) {done=true; Log.Error("[INH-PHYSICAL-SUPPLY] FAIL "+error); Application.Quit(1);}
        }
    }
}
