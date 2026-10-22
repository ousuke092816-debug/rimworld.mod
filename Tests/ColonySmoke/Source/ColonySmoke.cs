using System;
using System.IO;
using System.Linq;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    public sealed class SmokeMod : Mod
    {
        public SmokeMod(ModContentPack content) : base(content)
        {
            if (GenCommandLine.CommandLineArgPassed("inh-delegated-ui-probe")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-DELEGATED-UI] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-maintenance-probe") || GenCommandLine.CommandLineArgPassed("inh-maintenance-city-setup") || GenCommandLine.CommandLineArgPassed("inh-maintenance-city-lock")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-MAINTENANCE] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-delegated-custody-probe")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-DELEGATED-CUSTODY] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-local-custody")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-LOCAL-CUSTODY] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-colony-save") || GenCommandLine.CommandLineArgPassed("inh-colony-load") || GenCommandLine.CommandLineArgPassed("inh-colony-completed") || GenCommandLine.CommandLineArgPassed("inh-colony-final") || GenCommandLine.CommandLineArgPassed("inh-colony-resume") || GenCommandLine.CommandLineArgPassed("inh-physical-probe"))
                Application.logMessageReceived += delegate(string message,string stack,LogType type)
                {
                    if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu"))
                    { Log.Error("[INH-P04] FAIL unexpected load rejection: " + message); Application.Quit(1); }
                };
            if (GenCommandLine.CommandLineArgPassed("inh-loss")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-LOSS] FAIL unexpected load rejection: " + message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-animal")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-ANIMAL] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-journey")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-JOURNEY] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-custody")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-CUSTODY] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-supply-preview") || GenCommandLine.CommandLineArgPassed("inh-supply-preview-city-lock") || GenCommandLine.CommandLineArgPassed("inh-supply-preview-render") || GenCommandLine.CommandLineArgPassed("inh-supply-preview-render-japanese")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-SUPPLY-PREVIEW] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-supply-intent")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-SUPPLY-INTENT] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            if (GenCommandLine.CommandLineArgPassed("inh-supply-transfer")) Application.logMessageReceived += delegate(string message,string stack,LogType type)
            { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-SUPPLY-TRANSFER] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
            string save = GenCommandLine.CommandLineArgPassed("inh-supply-transfer-checkpoint") ? "INH_SupplyTransferCheckpoint" :
                GenCommandLine.CommandLineArgPassed("inh-supply-transfer-checkpoint-reload") ? "INH_SupplyTransferCheckpointRecovered" :
                GenCommandLine.CommandLineArgPassed("inh-supply-transfer-arm") ? "INH_SupplyIntentPrepared" :
                GenCommandLine.CommandLineArgPassed("inh-supply-transfer-load") || GenCommandLine.CommandLineArgPassed("inh-supply-transfer-stale-armed") ? "INH_SupplyTransferArmed" :
                GenCommandLine.CommandLineArgPassed("inh-supply-transfer-stale-armed-load") ? "INH_SupplyTransferArmedStale" :
                GenCommandLine.CommandLineArgPassed("inh-supply-transfer-final-load") || GenCommandLine.CommandLineArgPassed("inh-supply-transfer-animal") ? "INH_SupplyTransferDisarmed" :
                GenCommandLine.CommandLineArgPassed("inh-supply-transfer-stale") ? "INH_SupplyIntentStalePending" :
                GenCommandLine.CommandLineArgPassed("inh-supply-intent-city-lock") ? "INH_DelegatedUiCityLock" :
                GenCommandLine.CommandLineArgPassed("inh-supply-intent-prepare") ? "INH_MaintenanceResumed" :
                GenCommandLine.CommandLineArgPassed("inh-supply-intent-load") || GenCommandLine.CommandLineArgPassed("inh-supply-intent-stale") || GenCommandLine.CommandLineArgPassed("inh-supply-intent-stale-pending") ? "INH_SupplyIntentPrepared" :
                GenCommandLine.CommandLineArgPassed("inh-supply-intent-stale-pending-load") ? "INH_SupplyIntentStalePending" :
                GenCommandLine.CommandLineArgPassed("inh-supply-intent-cancelled-load") ? "INH_SupplyIntentCancelled" :
                GenCommandLine.CommandLineArgPassed("inh-supply-intent-stale-load") ? "INH_SupplyIntentStale" :
                GenCommandLine.CommandLineArgPassed("inh-supply-preview-city-lock") ? "INH_DelegatedUiCityLock" :
                GenCommandLine.CommandLineArgPassed("inh-supply-preview") || GenCommandLine.CommandLineArgPassed("inh-supply-preview-render") || GenCommandLine.CommandLineArgPassed("inh-supply-preview-render-japanese") ? "INH_MaintenanceResumed" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-city-setup-prepared") || GenCommandLine.CommandLineArgPassed("inh-delegated-ui-city-setup-applied") ? "INH_MaintenancePendingAppliedStopped" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-city-lock") ? "INH_DelegatedUiCityLock" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-city-reload") ? "INH_DelegatedUiCityRecovered" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-start") ? "INH_DelegatedCustodyReturned" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-store") ? "INH_DelegatedUiStorePrepared" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-prepare-release") ? "INH_DelegatedUiStored" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-render") || GenCommandLine.CommandLineArgPassed("inh-delegated-ui-render-japanese") ? "INH_DelegatedUiReleasePrepared" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-release") ? "INH_DelegatedUiReleasePrepared" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-reload") || GenCommandLine.CommandLineArgPassed("inh-delegated-ui-register") ? "INH_DelegatedUiReleased" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-ui-registered-reload") ? "INH_DelegatedUiRegistered" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-city-setup") ? "INH_MaintenancePendingAppliedStopped" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-city-lock") ? "INH_MaintenanceCityLock" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-fresh-start") ? "INH_RationCommitted" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-pending-stop-prepared") || GenCommandLine.CommandLineArgPassed("inh-maintenance-pending-stop-applied") || GenCommandLine.CommandLineArgPassed("inh-maintenance-reentry") ? "INH_MaintenanceStarted" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-pending-recover") ? (GenCommandLine.CommandLineArgPassed("inh-maintenance-recover-applied") ? "INH_MaintenancePendingAppliedStopped" : "INH_MaintenancePendingStopped") :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-start") ? "INH_RationCommitted" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-day") ? "INH_MaintenanceStarted" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-shortage") ? "INH_MaintenanceDayPaid" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-resume") || GenCommandLine.CommandLineArgPassed("inh-maintenance-render") ? "INH_MaintenanceShortage" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-stop") ? "INH_MaintenanceResumed" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-reload") ? "INH_MaintenanceStopped" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-gap-resume") ? "INH_MaintenanceGapHeld" :
                GenCommandLine.CommandLineArgPassed("inh-maintenance-gap") ? "INH_MaintenanceGap" :
                GenCommandLine.CommandLineArgPassed("inh-ration-checkpoint") ? "INH_RationCheckpoint" :
                GenCommandLine.CommandLineArgPassed("inh-ration-receipt-missing") ? "INH_RationReceiptMissing" :
                GenCommandLine.CommandLineArgPassed("inh-ration-prepare") ? "INH_DelegatedRouteStored" :
                GenCommandLine.CommandLineArgPassed("inh-ration-apply") ? "INH_RationPrepared" :
                GenCommandLine.CommandLineArgPassed("inh-ration-commit") ? "INH_RationApplied" :
                GenCommandLine.CommandLineArgPassed("inh-ration-release") ? "INH_RationCommitted" :
                GenCommandLine.CommandLineArgPassed("inh-ration-reload") ? "INH_RationReleased" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-checkpoint") ? "INH_DelegatedRouteCheckpoint" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-uncertifiedsave") ? "INH_DelegatedRouteEntering" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-reserve") ? "INH_LocalCustodyRecoveredFinal" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-transit") ? "INH_DelegatedRouteReserved" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-arrival") ? "INH_DelegatedRouteTransit" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-stored") ? "INH_DelegatedRouteArrival" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-return") ? "INH_DelegatedRouteStored" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-entering") ? "INH_DelegatedRouteReturn" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-final") ? "INH_DelegatedRouteEntering" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-route-reloadfinal") ? "INH_DelegatedRouteHome" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-custody-prepare") ? "INH_LocalCustodyRecoveredFinal" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-custody-apply") ? "INH_DelegatedCustodyPrepared" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-custody-commit") ? "INH_DelegatedCustodyApplied" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-custody-release-prepare") ? "INH_DelegatedCustodyStored" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-custody-release-apply") ? "INH_DelegatedCustodyReleasePrepared" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-custody-release") ? "INH_DelegatedCustodyReleaseApplied" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-custody-final") ? "INH_DelegatedCustodyReturned" :
                GenCommandLine.CommandLineArgPassed("inh-delegated-custody-reloadfinal") ? "INH_DelegatedCustodyHome" :
                GenCommandLine.CommandLineArgPassed("inh-local-custody-pending-final") ? "INH_LocalCustodyRecovered" :
                GenCommandLine.CommandLineArgPassed("inh-local-custody-pending-load") ? "INH_LocalCustodyPending" :
                GenCommandLine.CommandLineArgPassed("inh-local-custody-pending-save") ? "INH_LocalCustodyReturned" :
                GenCommandLine.CommandLineArgPassed("inh-local-custody-save") ? "INH_AnimalRender" :
                GenCommandLine.CommandLineArgPassed("inh-local-custody-load") || GenCommandLine.CommandLineArgPassed("inh-local-custody-render") ? "INH_LocalCustodyStored" :
                GenCommandLine.CommandLineArgPassed("inh-local-custody-final") ? "INH_LocalCustodyReturned" :
                GenCommandLine.CommandLineArgPassed("inh-custody-final") ? "INH_CustodyReturned" :
                GenCommandLine.CommandLineArgPassed("inh-custody-load") ? "INH_CustodyStored" :
                GenCommandLine.CommandLineArgPassed("inh-custody") ? "INH_JourneyDelivered" :
                GenCommandLine.CommandLineArgPassed("inh-journey-deplete") && GenCommandLine.CommandLineArgPassed("inh-journey-load") ? "INH_JourneyDepleted" :
                GenCommandLine.CommandLineArgPassed("inh-journey-final") ? "INH_JourneyDelivered" :
                GenCommandLine.CommandLineArgPassed("inh-journey-load") ? "INH_JourneyHalf" :
                GenCommandLine.CommandLineArgPassed("inh-journey") ? "INH_AnimalRender" :
                GenCommandLine.CommandLineArgPassed("inh-animal-checkpoint") ? "INH_AnimalCheckpoint" :
                GenCommandLine.CommandLineArgPassed("inh-animal-final") ? "INH_AnimalDelivered" :
                GenCommandLine.CommandLineArgPassed("inh-animal-load") ? "INH_AnimalTransit" :
                GenCommandLine.CommandLineArgPassed("inh-animal-reserved") ? "INH_AnimalReserved" :
                GenCommandLine.CommandLineArgPassed("inh-animal-render") ? "INH_AnimalRender" :
                GenCommandLine.CommandLineArgPassed("inh-animal") ? "INH_UiDelivered" :
                GenCommandLine.CommandLineArgPassed("inh-loss-checkpoint") ? "INH_LossCheckpoint" :
                GenCommandLine.CommandLineArgPassed("inh-loss-zero-final") ? "INH_LossZeroDelivered" :
                GenCommandLine.CommandLineArgPassed("inh-loss-final") ? "INH_LossDelivered" :
                GenCommandLine.CommandLineArgPassed("inh-loss-load") ? "INH_LossAccepted" :
                GenCommandLine.CommandLineArgPassed("inh-loss") ? "INH_UiLossHeld" :
                GenCommandLine.CommandLineArgPassed("inh-ui-loss-final") ? "INH_UiLossHeld" :
                GenCommandLine.CommandLineArgPassed("inh-ui-load") || GenCommandLine.CommandLineArgPassed("inh-ui-loss") ? "INH_UiTransit" :
                GenCommandLine.CommandLineArgPassed("inh-ui-final") || GenCommandLine.CommandLineArgPassed("inh-ui-render") ? "INH_UiDelivered" :
                GenCommandLine.CommandLineArgPassed("inh-physical-checkpoint") ? "INH_PhysicalCheckpoint" :
                GenCommandLine.CommandLineArgPassed("inh-physical-auto") ? "INH_PhysicalArrivalIntent" :
                GenCommandLine.CommandLineArgPassed("inh-physical-reserved") ? "INH_PhysicalReserved" :
                GenCommandLine.CommandLineArgPassed("inh-physical-departure-intent") ? "INH_PhysicalDepartureIntent" :
                GenCommandLine.CommandLineArgPassed("inh-physical-arrival-intent") ? "INH_PhysicalArrivalIntent" :
                GenCommandLine.CommandLineArgPassed("inh-physical-load") ? "INH_PhysicalTransit" :
                GenCommandLine.CommandLineArgPassed("inh-physical-final") ? "INH_PhysicalDelivered" :
                GenCommandLine.CommandLineArgPassed("inh-colony-load") ? "INH_ColonyReserved" :
                GenCommandLine.CommandLineArgPassed("inh-colony-completed") ? "INH_ColonyTransit" :
                GenCommandLine.CommandLineArgPassed("inh-colony-final") ? "INH_ColonyFinal" :
                GenCommandLine.CommandLineArgPassed("inh-colony-resume") ? "INH_ColonyCheckpoint" : null;
            if (save != null) LongEventHandler.ExecuteWhenFinished(delegate { GameDataSaveLoader.LoadGame(save); });
        }
    }
    public sealed class Probe : GameComponent
    {
        private bool done;
        private const string Item = "ludeon.rimworld:Steel";
        public Probe(Game game) { }
        private static void Assert(bool value,string text) { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-P04] PASS " + text); }
        private static void Refuses(Action action,string text) { bool refused = false; try { action(); } catch (InvalidOperationException) { refused = true; } Assert(refused,text); }
        private static string FilePath(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-" + key + ".txt"); }
        public override void GameComponentUpdate()
        {
            if (done || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return;
            bool save = GenCommandLine.CommandLineArgPassed("inh-colony-save"), load = GenCommandLine.CommandLineArgPassed("inh-colony-load"),
                completed = GenCommandLine.CommandLineArgPassed("inh-colony-completed"), final = GenCommandLine.CommandLineArgPassed("inh-colony-final"), resume = GenCommandLine.CommandLineArgPassed("inh-colony-resume");
            if (!save && !load && !completed && !final && !resume) return;
            var core = Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            done = true;
            try
            {
                Assert(core.IsReady && ColonyMod.Current != null,"Core and Colony lifecycle gates completed");
                var colony = ColonyMod.Current;
                string first,second,delegated,lot;
                if (save)
                {
                    var map1 = Find.CurrentMap;
                    int tile = Enumerable.Range(0,Find.World.grid.Surface.TilesCount).First(i =>
                        !Find.World.grid.Surface[i].WaterCovered && Find.World.grid.Surface[i].PrimaryBiome.canBuildBase && !Find.World.grid.Surface[i].PrimaryBiome.impassable &&
                        Find.World.grid.Surface[i].hilliness != Hilliness.Impassable && !Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == (PlanetTile)i));
                    var parent = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                    parent.Tile = tile; parent.SetFaction(Faction.OfPlayer); Find.WorldObjects.Add(parent);
                    var map2 = GetOrGenerateMapUtility.GetOrGenerateMap(tile,new IntVec3(100,1,100),WorldObjectDefOf.Settlement,null,false);
                    first = colony.RegisterDirectMap(map1); second = colony.RegisterDirectMap(map2);
                    delegated = colony.RegisterDelegatedBase("test-delegated","委任倉庫");
                    Assert(colony.Bases.Count(b => b.Mode == BaseMode.Direct) == 2 && colony.Bases.Count(b => b.Mode == BaseMode.Delegated) == 1 && Find.Maps.Count >= 2,"Two real player maps plus one delegated base registered");
                    Assert(colony.RegisterDirectMap(map1) == first,"Registering the same map preserves its city ID");
                    lot = colony.RegisterAbstractStock("test-import",first,Item,100);
                    Assert(colony.RegisterAbstractStock("test-import",first,Item,100) == lot && core.Ledger.TotalAbstractQuantity(Item) == 100,"Same stock import does not add stock");
                    long due = Find.TickManager.TicksGame+60000L;
                    string shipment = colony.ReserveTransport("A-B",lot,second,30,due);
                    Assert(colony.ReserveTransport("A-B",lot,second,30,due) == shipment && colony.Reserved(lot) == 30 && colony.Inventory(first,Item) == 100,"Reservation is part of stock, not an extra cargo copy");
                    Refuses(delegate { colony.ReserveTransport("A-B",lot,second,31,due); },"Changed transport request is refused");
                    Refuses(delegate { colony.ReserveTransport("too-many",lot,second,71,due); },"Over-reservation is refused");
                    colony.ReserveTransport("cancel",lot,delegated,10,due); colony.Cancel("cancel"); colony.Cancel("cancel");
                    Assert(colony.Reserved(lot) == 30 && core.Ledger.TotalAbstractQuantity(Item) == 100,"Cancellation releases only its reservation and repeats safely");
                    File.WriteAllLines(FilePath("ids"),new[] {core.Header.SaveScopeId,first,second,delegated,lot,shipment});
                    File.WriteAllText(FilePath("physical-steel"),colony.Bases.Sum(b => b.PhysicalSteel).ToString());
                    File.WriteAllText(FilePath("colonists"),colony.Bases.Sum(b => b.Colonists).ToString());
                    GameDataSaveLoader.SaveGame("INH_ColonyReserved");
                }
                else
                {
                    var ids = File.ReadAllLines(FilePath("ids")); first = ids[1]; second = ids[2]; delegated = ids[3]; lot = ids[4];
                    Assert(core.Header.SaveScopeId == ids[0] && colony.Bases.Select(b => b.Id).OrderBy(x => x).SequenceEqual(ids.Skip(1).Take(3).OrderBy(x => x)) && colony.Transports.Single(t => t.Cause == "A-B").ShipmentId == ids[5],"World, base, stock and shipment identities survive a process restart");
                    var trip = colony.Transports.Single(t => t.Cause == "A-B");
                    if (load || resume)
                    {
                        if (load) Assert(trip.Stage == TransportStage.Reserved && colony.Inventory(first,Item) == 100 && colony.Reserved(lot) == 30,"Reserved shipment resumes without deducting stock");
                        else Assert(trip.Stage == TransportStage.InTransit,"Departure intent checkpoint resumes once");
                        colony.Depart("A-B"); colony.Depart("A-B");
                        Assert(colony.Inventory(first,Item) == 70 && colony.Inventory(second,Item) == 0 && core.Ledger.Inventory(ids[5],Item) == 30 && colony.Reserved(lot) == 0 && core.Ledger.TotalAbstractQuantity(Item) == 100,"Departure splits 70/30 exactly once and keeps cargo out of destination stock");
                        Refuses(delegate { colony.Arrive("A-B"); },"Early arrival is refused");
                        Refuses(delegate { colony.Cancel("A-B"); },"A departed shipment cannot be cancelled as an unspent reservation");
                        GameDataSaveLoader.SaveGame("INH_ColonyTransit");
                    }
                    else if (completed)
                    {
                        Assert(trip.Stage == TransportStage.InTransit && core.Ledger.Inventory(ids[5],Item) == 30,"Transit cargo survives saving and restart");
                        Find.TickManager.DebugSetTicksGame((int)trip.DueTick); colony.Tick(); colony.Arrive("A-B");
                        Assert(colony.Inventory(second,Item) == 30 && core.Ledger.Inventory(ids[5],Item) == 0,"Arrival moves the same cargo lot once");
                        string cargo = colony.Transports.Single(t => t.Cause == "A-B").CargoId;
                        colony.ReserveTransport("B-C",cargo,delegated,20,Find.TickManager.TicksGame); colony.Depart("B-C"); colony.Tick();
                        string cargo2 = colony.Transports.Single(t => t.Cause == "B-C").CargoId;
                        colony.ReserveTransport("C-A",cargo2,first,5,Find.TickManager.TicksGame); colony.Depart("C-A"); colony.Tick();
                        Assert(colony.Inventory(first,Item) == 75 && colony.Inventory(second,Item) == 10 && colony.Inventory(delegated,Item) == 15 && core.Ledger.TotalAbstractQuantity(Item) == 100,"A to B to delegated C to A preserves total 100 through chained shipments");
                        colony.Tick(); colony.Arrive("C-A");
                        File.WriteAllLines(FilePath("cargo"),colony.Transports.Where(t => t.Stage == TransportStage.Arrived).Select(t => t.CargoId).OrderBy(x => x).ToArray());
                        GameDataSaveLoader.SaveGame("INH_ColonyFinal");
                    }
                    else if (final)
                    {
                        Assert(colony.Inventory(first,Item) == 75 && colony.Inventory(second,Item) == 10 && colony.Inventory(delegated,Item) == 15 && core.Ledger.TotalAbstractQuantity(Item) == 100,"Completed cyclic transport retains the same distributed stock on restart");
                        Assert(colony.Transports.Where(t => t.Stage == TransportStage.Arrived).Select(t => t.CargoId).OrderBy(x => x).SequenceEqual(File.ReadAllLines(FilePath("cargo"))),"Cargo IDs are not recreated on final reload");
                        colony.Tick(); colony.Arrive("A-B"); colony.Arrive("B-C"); colony.Arrive("C-A");
                        Assert(core.Ledger.TotalAbstractQuantity(Item) == 100 && colony.Transports.Count() == 4,"Replaying completed arrivals adds neither cargo nor shipments");
                    }
                }
                Assert(colony.Bases.Sum(b => b.PhysicalSteel).ToString() == File.ReadAllText(FilePath("physical-steel")) &&
                    colony.Bases.Sum(b => b.Colonists).ToString() == File.ReadAllText(FilePath("colonists")),"Abstract shipment accounting does not alter map items or pawns");
                Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception e) { Log.Error("[INH-P04] FAIL " + e); Application.Quit(1); }
        }
    }
}
