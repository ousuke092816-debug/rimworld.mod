using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Inheritance.Core;
using Inheritance.Colony;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    // Controlled metadata-only checkpoints derived from a real successful save.
    // This does not simulate a native move or prove atomic failure of the allocator window.
    public sealed class SupplyTransferCheckpointProbe : GameComponent
    {
        private bool done;
        private int pausedFrames;
        private static bool Selected { get { return GenCommandLine.CommandLineArgPassed("inh-supply-transfer-checkpoint") || Reload; } }
        private static bool Reload { get { return GenCommandLine.CommandLineArgPassed("inh-supply-transfer-checkpoint-reload"); } }
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public SupplyTransferCheckpointProbe(Game game) { }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static object Field(object target,string name) { return target.GetType().GetField(name,Private|BindingFlags.Public).GetValue(target); }
        private static object Invoke(object target,string name,params object[] args)
        {
            try { return target.GetType().GetMethod(name,Private).Invoke(target,args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        private static string Except(object target,params string[] fields)
        { return (string)typeof(SupplyIntentProbe).GetMethod("Except",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {target,fields}); }
        private static object RawOperation(CoreLedger ledger,string id)
        { return ((IEnumerable)Field(Field(ledger,"state"),"operations")).Cast<object>().Single(o => (string)Field(o,"id") == id); }
        private static void Assert(bool condition,string message)
        { if (!condition) throw new InvalidOperationException(message); Log.Message("[INH-SUPPLY-CHECKPOINT] PASS "+message); }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                Find.TickManager.Pause(); var colony=ColonyMod.Current;
                if (core.IsReady && colony != null && pausedFrames++ < 2) return;
                Assert(core.IsReady && colony != null,"Controlled checkpoint uses the validated ordinary Core/Colony lifecycle");
                var helper=new SupplyTransferProbe(Current.Game);
                typeof(SupplyTransferProbe).GetField("core",Private).SetValue(helper,core);
                typeof(SupplyTransferProbe).GetField("service",Private).SetValue(helper,colony);
                Invoke(helper,"Bind"); colony.SaveSnapshot(core); // Before the measured boundary only.
                if (Reload)
                {
                    string restored=(string)Invoke(helper,"Restore","INH_SupplyTransferCheckpointRecovered");
                    var existing=colony.DelegatedSupplyTransfers.Single(t => t.Id == restored);
                    if (existing.ArmProofStage == OperationStage.NeedsReconciliation)
                    {
                        Assert(existing.Stage == RationSupplyTransferStage.Armed && !colony.InspectDelegatedSupplyTransfer(restored).CanResumeProof,
                            "Another process restart retains unresolved original preconditions and their Armed responsibility");
                        Invoke(helper,"Active",restored,true);
                        Invoke(helper,"ReadOnly",new Action(delegate { RefuseResume(colony,restored); }),"Unresolved proof replay/refusal remains an exact +0 no-op");
                        Finish(); return;
                    }
                    Assert(existing.ArmProofStage == OperationStage.Committed && (existing.DisarmCause == null ? existing.Stage == RationSupplyTransferStage.Armed :
                        existing.Stage == RationSupplyTransferStage.Cancelled && existing.DisarmProofStage == OperationStage.Committed),
                        "Separate reload retains the resumed original proof and the correct responsibility stage");
                    Invoke(helper,"ReadOnly",new Action(delegate { colony.ResumeDelegatedSupplyTransferProof(restored); }),"Completed proof replay is an exact +0 no-op after another process restart");
                    Finish(); return;
                }
                var info=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-supply-transfer-checkpoint.txt"));
                string phase=info[0],inputStage=info[1],originalName=info[2],id=info[3],proofId=info[4];
                var transfer=colony.DelegatedSupplyTransfers.Single(t => t.Id == id);
                typeof(SupplyTransferProbe).GetField("intentId",Private).SetValue(helper,transfer.IntentId);
                var evidence=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,originalName+"-supply-transfer-evidence.txt"))
                    .ToDictionary(line => line.Substring(0,line.IndexOf('=')),line => Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(line.IndexOf('=')+1))),StringComparer.Ordinal);
                Assert(evidence["scope"] == core.Header.SaveScopeId && evidence["native"] == (string)Invoke(helper,"NativePersistent") &&
                    evidence["clock"] == (string)Invoke(helper,"Clock") && evidence["colony"] == (string)Invoke(helper,"RawColony") && evidence["fixed"] == (string)Invoke(helper,"FixedIntent"),
                    "Metadata checkpoint changes no original individual, whole food, owner, clock, Colony or historical intent");
                var proof=core.Ledger.GetOperation(proofId);
                var expectedStage=inputStage == "Reconcile" ? OperationStage.NeedsReconciliation : OperationStage.Prepared;
                Assert(proof.Stage == expectedStage && proof.RecoveryCount == (inputStage == "Applying" ? 1 : 0) &&
                    core.Ledger.GetEntity(proof.ResultId).Status == EntityStatus.Allocated && transfer.Stage == RationSupplyTransferStage.Armed &&
                    (phase == "arm" ? transfer.ArmProofStage == expectedStage && transfer.DisarmCause == null :
                        transfer.ArmProofStage == OperationStage.Committed && transfer.DisarmProofStage == OperationStage.Prepared),
                    "Load leaves the named original proof pending; Applying recovers once without Commit, IDs or native action");
                Invoke(helper,"Active",id,true); Invoke(helper,"Blocks");
                Invoke(helper,"ReadOnly",new Action(delegate {
                    Assert(colony.BeginDelegatedSupplyTransfer(transfer.Cause,transfer.IntentId) == id,"Begin replay returns the same pending responsibility");
                    if (phase == "disarm") colony.DisarmDelegatedSupplyTransfer(transfer.DisarmCause,id);
                    bool refused=false; try { core.Ledger.Abort(proofId); } catch (InvalidOperationException error) {
                        refused=inputStage == "Reconcile" ? error.Message == "Only side-effect-free pending operations can abort" : error.Message.Contains("Supply transfer proof"); }
                    Assert(refused,"Abort cannot orphan the exact transfer proof");
                    Assert(colony.InspectDelegatedSupplyTransfer(id).CanResumeProof == (inputStage != "Reconcile"),"Ordinary inspection offers recovery only for committable original preconditions");
                }),"Pending replay, refusal and inspection preserve every raw journal/native/clock and allocate +0");
                if (inputStage == "Reconcile")
                {
                    Invoke(helper,"ReadOnly",new Action(delegate { RefuseResume(colony,id); }),"Failed reconciliation cannot rewrite a proof, release responsibility, allocate an ID or change native objects");
                    Invoke(helper,"Save","INH_SupplyTransferCheckpointRecovered",id); done=true; return;
                }
                var before=Invoke(helper,"Capture");
                var oldEntities=(Dictionary<string,string>)Field(before,"EntityRows"); var oldOperations=(Dictionary<string,string>)Field(before,"OperationRows");
                long revision=core.Ledger.GetEntity(transfer.CityId).Revision;
                string fixedProof=Except(RawOperation(core.Ledger,proof.Id),"stage"),fixedCity=Except(Invoke(helper,"RawCity"),"revision"),
                    fixedTransfer=Except(Invoke(helper,"RawTransfer",id),"stage");
                colony.ResumeDelegatedSupplyTransferProof(id);
                var after=Invoke(helper,"Capture");
                foreach (string key in new[] {"Colony","Native","Clock","Old","Supply","Reservations","LedgerOther"}) Assert((string)Field(before,key) == (string)Field(after,key),"Explicit proof resume preserves full "+key);
                var entities=(Dictionary<string,string>)Field(after,"EntityRows"); var operations=(Dictionary<string,string>)Field(after,"OperationRows");
                Assert(core.Header.NextSequence == (long)Field(before,"Sequence") && core.Ledger.EntityCount == (int)Field(before,"Entities") &&
                    core.Ledger.OperationCount == (int)Field(before,"Operations") && oldEntities.All(e => e.Key == proof.ResultId || phase == "arm" && e.Key == transfer.CityId || entities[e.Key] == e.Value) &&
                    oldOperations.All(o => o.Key == proof.Id || operations[o.Key] == o.Value),"Resume allocates +0 and changes only the original proof result and arm city revision");
                Assert(core.Ledger.GetOperation(proofId).Stage == OperationStage.Committed && core.Ledger.GetOperation(proofId).RecoveryCount == proof.RecoveryCount &&
                    core.Ledger.GetEntity(proof.ResultId).Revision == 1 && core.Ledger.GetEntity(proof.ResultId).Status == EntityStatus.Active &&
                    core.Ledger.GetEntity(transfer.CityId).Revision == revision+(phase == "arm" ? 1 : 0),"The exact own proof commits once with its expected existing entity revisions");
                Assert(fixedProof == Except(RawOperation(core.Ledger,proof.Id),"stage") && fixedCity == Except(Invoke(helper,"RawCity"),"revision") &&
                    fixedTransfer == Except(Invoke(helper,"RawTransfer",id),"stage"),"Original proof request/preconditions, every other city field and all fixed transfer fields remain exact");
                var resumed=colony.DelegatedSupplyTransfers.Single(t => t.Id == id);
                Assert(resumed.Stage == (phase == "arm" ? RationSupplyTransferStage.Armed : RationSupplyTransferStage.Cancelled),"Pending disarm releases responsibility only after its existing proof commits");
                Invoke(helper,"Active",id,phase == "arm");
                Invoke(helper,"Save","INH_SupplyTransferCheckpointRecovered",id); done=true;
            }
            catch (Exception error) { done=true; Log.Error("[INH-SUPPLY-CHECKPOINT] FAIL "+error); Application.Quit(1); }
        }
        private void Finish() { done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
        private static void RefuseResume(ColonyService colony,string id)
        {
            bool refused=false; try { colony.ResumeDelegatedSupplyTransferProof(id); }
            catch (InvalidOperationException error) { refused=error.Message.Contains("precondition reconciliation"); }
            Assert(refused,"Changed original preconditions require reconciliation and are not silently committed");
        }
    }
}
