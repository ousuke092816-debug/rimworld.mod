using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Inheritance.Core;
using Inheritance.Colony;
using UnityEngine;
using Verse;
namespace Inheritance.ColonySmoke
{
    public sealed class AbstractSupplyProbe : GameComponent
    {
        private bool done; private int frames;
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static bool Has(string suffix) { return GenCommandLine.CommandLineArgPassed("inh-abstract-supply-"+suffix); }
        private static bool Selected { get { return Has("start") || Has("load") || Has("final") || Has("checkpoint"); } }
        public AbstractSupplyProbe(Game game) { }
        public override void FinalizeInit() { if(Selected) Find.TickManager.Pause(); }
        private static object Call(object target,string name,params object[] args)
        { try { return target.GetType().GetMethod(name,Private).Invoke(target,args); } catch(TargetInvocationException e) {throw e.InnerException ?? e;} }
        private static void Assert(bool value,string message)
        { if(!value) throw new InvalidOperationException(message); Log.Message("[INH-ABSTRACT-SUPPLY] PASS "+message); }
        private static void Read(SupplyTransferProbe helper,Action action,string text) { Call(helper,"ReadOnly",action,text); }
        private static void Refuse(SupplyTransferProbe helper,Action action,string text) { Call(helper,"RefuseExact",action,text,true); }
        public override void GameComponentUpdate()
        {
            if(done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if(!core.IsReady && core.BlockReason == null) return;
            try {
                Find.TickManager.Pause(); var colony=ColonyMod.Current;
                if(core.IsReady && colony != null && frames++ < 2) return;
                Assert(core.IsReady && colony != null,"Staged variant is ready; production 0.4.15 evidence is separate");
                var helper=new SupplyTransferProbe(Current.Game);
                typeof(SupplyTransferProbe).GetField("core",Private).SetValue(helper,core);
                typeof(SupplyTransferProbe).GetField("service",Private).SetValue(helper,colony);
                Call(helper,"Bind");
                string id;
                if(Has("start")) {
                    Call(helper,"CheckSourceEvidence","INH_SupplyIntentPrepared");
                    string intentId=(string)typeof(SupplyTransferProbe).GetField("intentId",Private).GetValue(helper);
                    var intent=colony.DelegatedSupplyIntents.Single(i => i.Id == intentId);
                    string native=(string)Call(helper,"NativePersistent"),clock=(string)Call(helper,"Clock");
                    // An explicit abstract test import, never a native food or Pawn fixture.
                    string targetLot=colony.RegisterAbstractStock("abstract-supply-target",intent.CityId,"ludeon.rimworld:Steel",100);
                    string pending=core.Ledger.NewOperationId(ColonyService.Actor);
                    core.Ledger.PrepareReservation(pending,"abstract-supply-pending",targetLot,1,core.Ledger.GetEntity(targetLot).Revision);
                    Assert(!core.Ledger.HasPendingReference(intent.CityId) && core.Ledger.HasPendingCityReference(intent.CityId),"A genuine pending lot reservation holds its custody city although no direct city stamp exists");
                    Read(helper,delegate {
                        bool refused=false; try {core.SupplyTransfers.Begin(ColonyService.Actor,"abstract-supply-pending-arm",intentId);}
                        catch(InvalidOperationException e) {refused=e.Message == "Supply city is locked before transfer responsibility allocation";}
                        Assert(refused,"Core arm refuses that indirect pending city before allocating any transfer ID");
                    },"Indirect city-lock refusal keeps all raw journals, IDs, native originals and clock exact");
                    core.Ledger.Abort(pending);
                    string source=colony.Bases.First(b => b.Mode == BaseMode.Direct && b.Id != intent.CityId).Id;
                    string lot=colony.RegisterAbstractStock("abstract-supply-source",source,"ludeon.rimworld:Steel",100);
                    colony.ReserveTransport("abstract-supply-reserved",lot,intent.CityId,10,Find.TickManager.TicksGame+60000);
                    colony.ReserveTransport("abstract-supply-transit",lot,intent.CityId,20,Find.TickManager.TicksGame);
                    colony.Depart("abstract-supply-transit");
                    Assert(native == (string)Call(helper,"NativePersistent") && clock == (string)Call(helper,"Clock"),"Explicit abstract setup changes no original Pawn, food, owner or clock");
                    colony.SaveSnapshot(core); // Only once, after setup and before the measured arm boundary.
                    id=(string)Call(helper,"Arm","abstract-supply-arm");
                    Refuse(helper,delegate {colony.Depart("abstract-supply-reserved");},"Reserved abstract departure refuses before changing stage, allocating IDs or creating cargo");
                    Refuse(helper,delegate {colony.Arrive("abstract-supply-transit");},"Due abstract arrival refuses before changing cargo revision, stage or operation ID");
                    Read(helper,delegate {colony.Tick();},"Automatic due abstract arrival waits without an exception or any raw state change");
                    // Cancellation modifies only the source reservation, not the Armed destination.
                    colony.Cancel("abstract-supply-reserved");
                    Assert(colony.Transports.Single(t => t.Cause == "abstract-supply-reserved").Stage == TransportStage.Cancelled &&
                        colony.Reserved(lot) == 0 && colony.Inventory(source,"ludeon.rimworld:Steel") == 80 && core.Ledger.TotalAbstractQuantity("ludeon.rimworld:Steel") == 200 &&
                        native == (string)Call(helper,"NativePersistent") && clock == (string)Call(helper,"Clock"),"Safe unspent reservation cancellation is permitted while only its destination is Armed");
                    Read(helper,delegate {colony.Cancel("abstract-supply-reserved"); colony.Tick();},"Completed cancellation replay and waiting update are exact +0 no-ops");
                    Call(helper,"Save","INH_AbstractSupplyArmed",id); done=true; return;
                }
                colony.SaveSnapshot(core); // Baseline before reload evidence, never after a measured operation.
                id=(string)Call(helper,"Restore",(Has("load") || Has("checkpoint")) ? "INH_AbstractSupplyArmed" : "INH_AbstractSupplyFinal");
                var transfer=colony.DelegatedSupplyTransfers.Single(t => t.Id == id);
                if(Has("load") || Has("checkpoint")) {
                    Assert(colony.Transports.Single(t => t.Cause == "abstract-supply-transit").Stage == TransportStage.InTransit && transfer.Stage == RationSupplyTransferStage.Armed,
                        "Separate process retains the due original abstract cargo and its Armed destination without automatic arrival");
                    Read(helper,delegate {colony.Tick();},"Reloaded due cargo still waits with exact raw metadata/native/clock and +0 IDs");
                    Call(helper,"Disarm",id,"abstract-supply-disarm");
                    string native=(string)Call(helper,"NativePersistent"),clock=(string)Call(helper,"Clock");
                    colony.Arrive("abstract-supply-transit");
                    Assert(colony.Transports.Single(t => t.Cause == "abstract-supply-transit").Stage == TransportStage.Arrived &&
                        colony.Inventory(transfer.CityId,"ludeon.rimworld:Steel") == 120 && core.Ledger.TotalAbstractQuantity("ludeon.rimworld:Steel") == 200 &&
                        native == (string)Call(helper,"NativePersistent") && clock == (string)Call(helper,"Clock"),"Disarm permits one abstract delivery of the existing 20-unit lot without native conversion");
                    Read(helper,delegate {colony.Arrive("abstract-supply-transit"); colony.Depart("abstract-supply-transit"); colony.Cancel("abstract-supply-reserved"); colony.Tick();},"Completed transport replays after disarm add no stock, proof or native change");
                    Call(helper,"Save","INH_AbstractSupplyFinal",id); done=true; return;
                }
                Assert(transfer.Stage == RationSupplyTransferStage.Cancelled && colony.Inventory(transfer.CityId,"ludeon.rimworld:Steel") == 120 && core.Ledger.TotalAbstractQuantity("ludeon.rimworld:Steel") == 200,
                    "Final separate reload preserves cancelled responsibility and the exact abstract stock total");
                Read(helper,delegate {colony.Arrive("abstract-supply-transit"); colony.Cancel("abstract-supply-reserved"); colony.Tick();},"Final replay keeps every raw journal, original and clock exact");
                done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            } catch(Exception error) {done=true; Log.Error("[INH-ABSTRACT-SUPPLY] FAIL "+error); Application.Quit(1);}
        }
    }
}
