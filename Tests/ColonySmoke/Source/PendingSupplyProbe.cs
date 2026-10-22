using System;
using System.Linq;
using System.Reflection;
using Inheritance.Core;
using Inheritance.Colony;
using UnityEngine;
using Verse;
namespace Inheritance.ColonySmoke
{
    public sealed class PendingSupplyProbe : GameComponent
    {
        private bool done; private int frames;
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static bool Has(string suffix) { return GenCommandLine.CommandLineArgPassed("inh-pending-supply-"+suffix); }
        private static bool Selected { get { return Has("start") || Has("load") || Has("final"); } }
        public PendingSupplyProbe(Game game) { }
        public override void FinalizeInit() { if(Selected) Find.TickManager.Pause(); }
        private static object Call(object target,string name,params object[] args)
        { try { return target.GetType().GetMethod(name,Private).Invoke(target,args); } catch(TargetInvocationException e) {throw e.InnerException ?? e;} }
        private static void Assert(bool value,string message)
        { if(!value) throw new InvalidOperationException(message); Log.Message("[INH-PENDING-SUPPLY] PASS "+message); }
        private static void Read(SupplyTransferProbe helper,Action action,string text) { Call(helper,"ReadOnly",action,text); }
        private static void Refuse(SupplyTransferProbe helper,Action action,string text) { Call(helper,"RefuseExact",action,text,true); }
        public override void GameComponentUpdate()
        {
            if(done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if(!core.IsReady && core.BlockReason == null) return;
            try {
                Find.TickManager.Pause(); var colony=ColonyMod.Current;
                if(core.IsReady && colony != null && frames++ < 2) return;
                Assert(core.IsReady && colony != null,"Legacy pending supply input loads as a validated world");
                var helper=new SupplyTransferProbe(Current.Game);
                typeof(SupplyTransferProbe).GetField("core",Private).SetValue(helper,core);
                typeof(SupplyTransferProbe).GetField("service",Private).SetValue(helper,colony);
                Call(helper,"Bind"); string transferId;
                if(Has("start")) {
                    Assert(core.Ledger.GetType().GetMethod("HasPendingCityReference") == null,"Setup uses the unchanged released 0.4.15 Core DLL, not the improved stage");
                    Call(helper,"CheckSourceEvidence","INH_SupplyIntentPrepared");
                    string intentId=(string)typeof(SupplyTransferProbe).GetField("intentId",Private).GetValue(helper);
                    var intent=colony.DelegatedSupplyIntents.Single(i => i.Id == intentId);
                    string native=(string)Call(helper,"NativePersistent"),clock=(string)Call(helper,"Clock");
                    string lot=colony.RegisterAbstractStock("legacy-pending-supply-stock",intent.CityId,"ludeon.rimworld:Steel",100);
                    string pending=core.Ledger.NewOperationId(ColonyService.Actor);
                    core.Ledger.PrepareReservation(pending,"legacy-pending-supply-reserve",lot,1,core.Ledger.GetEntity(lot).Revision);
                    Assert(!core.Ledger.HasPendingReference(intent.CityId),"Original version does not detect the genuine indirect city precondition");
                    Assert(native == (string)Call(helper,"NativePersistent") && clock == (string)Call(helper,"Clock"),"Legacy setup changes no original food, Pawn, native owner or clock");
                    colony.SaveSnapshot(core);
                    transferId=(string)Call(helper,"Arm","legacy-pending-supply-arm");
                    Refuse(helper,delegate {core.Ledger.Commit(pending);},"Actual released Core already refuses pending stock commit while original city is Armed");
                    Read(helper,delegate {colony.Tick();},"Original released automatic maintenance holds the Armed city without changing pending proof");
                    Call(helper,"Save","INH_LegacyPendingSupplyArmed",transferId); done=true; return;
                }
                Assert(core.Ledger.GetType().GetMethod("HasPendingCityReference") != null,"Load uses the improved independent v2 stage DLL");
                colony.SaveSnapshot(core);
                transferId=(string)Call(helper,"Restore",Has("load") ? "INH_LegacyPendingSupplyArmed" : "INH_LegacyPendingSupplyFinal");
                var transfer=colony.DelegatedSupplyTransfers.Single(t => t.Id == transferId);
                var operation=core.Ledger.FindOperation(ColonyService.Actor,"reserve","legacy-pending-supply-reserve");
                Assert(operation != null,"The original low-level reservation proof retains its historical identity");
                if(Has("load")) {
                    Assert(transfer.Stage == RationSupplyTransferStage.Armed && operation.Stage == OperationStage.Prepared && core.Ledger.Reserved(operation.SubjectId) == 0,
                        "Separate v2 reload retains Armed responsibility and the still uncommitted reservation, without implicit stock reservation");
                    Refuse(helper,delegate {core.Ledger.Commit(operation.Id);},"Reloaded pending commit refuses +0 while its custody city is Armed");
                    Refuse(helper,delegate {core.Ledger.Abort(operation.Id);},"Reloaded pending abort refuses +0 while its custody city is Armed");
                    Read(helper,delegate {colony.Tick();},"Reloaded pending proof and Armed city remain exact during automatic update");
                    Call(helper,"Disarm",transferId,"legacy-pending-supply-disarm");
                    string native=(string)Call(helper,"NativePersistent"),clock=(string)Call(helper,"Clock"); long sequence=core.Header.NextSequence;
                    var result=core.Ledger.Commit(operation.Id);
                    Assert(result.Stage == OperationStage.Committed && core.Header.NextSequence == sequence && core.Ledger.Reserved(operation.SubjectId) == 1 &&
                        core.Ledger.TotalAbstractQuantity("ludeon.rimworld:Steel") == 100 && native == (string)Call(helper,"NativePersistent") && clock == (string)Call(helper,"Clock"),
                        "Explicit disarm permits the same pending reservation once, with no new ID or native conversion");
                    Read(helper,delegate {core.Ledger.Commit(operation.Id); colony.Tick();},"Committed reservation replay preserves every raw journal, original and clock");
                    Call(helper,"Save","INH_LegacyPendingSupplyFinal",transferId); done=true; return;
                }
                Assert(transfer.Stage == RationSupplyTransferStage.Cancelled && operation.Stage == OperationStage.Committed && core.Ledger.Reserved(operation.SubjectId) == 1 &&
                    core.Ledger.TotalAbstractQuantity("ludeon.rimworld:Steel") == 100,"Final separate reload preserves exact committed reservation and disarmed metadata responsibility");
                Read(helper,delegate {core.Ledger.Commit(operation.Id); colony.Tick();},"Final reservation replay allocates +0 and changes no raw state");
                done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            } catch(Exception error) {done=true; Log.Error("[INH-PENDING-SUPPLY] FAIL "+error); Application.Quit(1);}
        }
    }
}
