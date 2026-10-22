using System;
using System.Linq;
using System.Reflection;
using Inheritance.Core;
using Inheritance.Colony;
using UnityEngine;
using Verse;
namespace Inheritance.ColonySmoke
{
    public sealed class ReservationScopeProbe : GameComponent
    {
        private bool done; private int frames;
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static bool Has(string suffix) { return GenCommandLine.CommandLineArgPassed("inh-reservation-scope-"+suffix); }
        public ReservationScopeProbe(Game game) { }
        public override void FinalizeInit() { if(Has("load") || Has("final")) Find.TickManager.Pause(); }
        private static object Call(object target,string name,params object[] args)
        { try { return target.GetType().GetMethod(name,Private).Invoke(target,args); } catch(TargetInvocationException e) {throw e.InnerException ?? e;} }
        private static void Assert(bool value,string message)
        { if(!value) throw new InvalidOperationException(message); Log.Message("[INH-RESERVATION-SCOPE] PASS "+message); }
        public override void GameComponentUpdate()
        {
            if(done || !(Has("load") || Has("final")) || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if(!core.IsReady && core.BlockReason == null) return;
            try {
                Find.TickManager.Pause(); var colony=ColonyMod.Current;
                if(core.IsReady && colony != null && frames++ < 2) return;
                Assert(core.IsReady && colony != null,"Actual saved world is validated before reservation scope test");
                var helper=new SupplyTransferProbe(Current.Game);
                typeof(SupplyTransferProbe).GetField("core",Private).SetValue(helper,core);
                typeof(SupplyTransferProbe).GetField("service",Private).SetValue(helper,colony);
                Call(helper,"Bind"); colony.SaveSnapshot(core);
                string previous=(string)Call(helper,"Restore","INH_LegacyPendingSupplyFinal");
                var reserve=core.Ledger.FindOperation(ColonyService.Actor,"reserve","legacy-pending-supply-reserve");
                Assert(reserve != null && reserve.Stage == OperationStage.Committed,"Historical committed reservation remains identified");
                if(Has("final")) {
                    var release=core.Ledger.FindOperation(ColonyService.Actor,"release","reservation-scope-release");
                    Assert(release != null && release.Stage == OperationStage.Committed && core.Ledger.Reserved(reserve.SubjectId) == 0 && core.Ledger.TotalAbstractQuantity("ludeon.rimworld:Steel") == 100,
                        "Separate final reload retains exactly released stock without native conversion");
                    Call(helper,"ReadOnly",(Action)delegate {core.Ledger.Commit(release.Id); colony.Tick();},"Committed release replay preserves raw journals, native originals and clock");
                    done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); return;
                }
                var destination=core.Ledger.Entities.First(e => e.Kind == "shipment" && e.Status == EntityStatus.Active && e.Provider == ColonyService.Actor);
                // Caller-owned operation allocation is outside the measured Prepare boundary.
                string depart=core.Ledger.NewOperationId(ColonyService.Actor),releaseId=core.Ledger.NewOperationId(ColonyService.Actor);
                colony.SaveSnapshot(core);
                string transfer=(string)Call(helper,"Arm","reservation-scope-arm");
                long sequence=core.Header.NextSequence; int operations=core.Ledger.OperationCount;
                try {
                    Call(helper,"RefuseExact",(Action)delegate {core.Ledger.PrepareDeparture(depart,"reservation-scope-depart",reserve.ResultId,destination.Id);},
                        "Reservation departure resolves original custody before result allocation",true);
                } catch(Exception) {
                    Log.Message("[INH-RESERVATION-SCOPE] OBSERVED sequenceDelta="+(core.Header.NextSequence-sequence)+" proofDelta="+(core.Ledger.OperationCount-operations)); throw;
                }
                Call(helper,"RefuseExact",(Action)delegate {core.Ledger.PrepareRelease(releaseId,"reservation-scope-release",reserve.ResultId);},
                    "Reservation release resolves original custody before pending proof",true);
                Call(helper,"Disarm",transfer,"reservation-scope-disarm");
                string native=(string)Call(helper,"NativePersistent"),clock=(string)Call(helper,"Clock");
                var planned=core.Ledger.PrepareDeparture(depart,"reservation-scope-depart",reserve.ResultId,destination.Id);
                Assert(planned.Stage == OperationStage.Prepared,"Same preallocated departure is valid after explicit disarm");
                core.Ledger.Abort(depart);
                core.Ledger.PrepareRelease(releaseId,"reservation-scope-release",reserve.ResultId); core.Ledger.Commit(releaseId);
                Assert(core.Ledger.Reserved(reserve.SubjectId) == 0 && core.Ledger.TotalAbstractQuantity("ludeon.rimworld:Steel") == 100 && native == (string)Call(helper,"NativePersistent") && clock == (string)Call(helper,"Clock"),
                    "Disarm permits original release once without loss, food conversion or time advancement");
                colony.SaveSnapshot(core); Call(helper,"Save","INH_LegacyPendingSupplyFinal",transfer); done=true;
            } catch(Exception error) {done=true; Log.Error("[INH-RESERVATION-SCOPE] FAIL "+error); Application.Quit(1);}
        }
    }
}
