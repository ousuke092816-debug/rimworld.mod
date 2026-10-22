using System;
using System.Collections.Generic;
using System.Globalization;
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
    // All food and residents come from the existing native fixture. No test escrow is used.
    public sealed class MaintenanceCityLockProbe : GameComponent
    {
        private const string HumanCause="maintenance-pending-probe",AnimalCause="maintenance-city-animal",ResumeCause="maintenance-city-resume";
        private const string PendingSave="INH_MaintenancePendingAppliedStopped",SetupSave="INH_MaintenanceCitySetup";
        private bool done;
        private string[] ids;
        private Pawn human,animal;
        public MaintenanceCityLockProbe(Game game) { }
        private static bool Flag(string suffix) { return GenCommandLine.CommandLineArgPassed("inh-maintenance-city-"+suffix); }
        private static bool Selected { get { return Flag("setup") || Flag("lock"); } }
        private static int Today { get { return Find.TickManager.TicksGame/60000; } }
        private Pawn[] Residents { get { return new[] {human,animal}; } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-maintenance-city-"+key+".txt"); }
        private static string Backup(string name,string key,bool city)
        { return Path.Combine(GenFilePaths.SaveDataFolderPath,name+(city ? "-maintenance-city-" : "-maintenance-")+key+".txt"); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-MAINTENANCE-CITY] PASS "+message); }
        private static void RefuseBecause(Action action,string reason,string message)
        { bool refused=false; try { action(); } catch (InvalidOperationException error) { refused=error.Message == reason; } Assert(refused,message); }
        private static ColonyState State(ColonyService service,CoreWorldComponent core)
        { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core))); }
        private static string Number(float value) { return value.ToString("R",CultureInfo.InvariantCulture); }
        private static string Item(Thing item)
        { return item.GetUniqueLoadID()+":"+item.def.defName+":"+(item.Stuff == null ? "" : item.Stuff.defName)+":"+item.stackCount+":"+item.HitPoints; }
        private static string Inventory(Pawn pawn)
        { return string.Join(";",pawn.inventory.innerContainer.Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray()); }
        private static string Biology(Pawn pawn)
        {
            return string.Join("|",new[] {pawn.GetUniqueLoadID(),pawn.def.defName,pawn.Name == null ? "" : pawn.Name.ToStringFull,pawn.gender.ToString(),pawn.Faction.GetUniqueLoadID(),
                pawn.ageTracker.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture),
                pawn.skills == null ? "" : string.Join(",",pawn.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+Number(s.xpSinceLastLevel)).ToArray()),
                string.Join(",",pawn.health.hediffSet.hediffs.Select(h => h.def.defName+":"+Number(h.Severity)+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                pawn.genes == null ? "" : string.Join(",",pawn.genes.GenesListForReading.Select(g => g.def.defName+":"+g.Active).ToArray()),pawn.Ideo == null ? "" : pawn.Ideo.GetUniqueLoadID(),
                pawn.needs == null ? "" : string.Join(",",pawn.needs.AllNeeds.OrderBy(n => n.def.defName,StringComparer.Ordinal).Select(n => n.def.defName+":"+Number(n.CurLevel)).ToArray()),
                pawn.story == null ? "" : string.Join(",",pawn.story.traits.allTraits.Select(t => t.def.defName+":"+t.Degree).ToArray()),
                pawn.apparel == null ? "" : string.Join(",",pawn.apparel.WornApparel.Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray()),
                pawn.equipment == null ? "" : string.Join(",",pawn.equipment.AllEquipmentListForReading.Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray())});
        }
        private static bool SameSavedBiology(Pawn pawn,string expected)
        {
            string[] actual=Biology(pawn).Split('|'),old=expected.Split('|');
            long change=long.Parse(actual[5],CultureInfo.InvariantCulture)-long.Parse(old[5],CultureInfo.InvariantCulture);
            return actual.Where((s,i) => i != 5).SequenceEqual(old.Where((s,i) => i != 5)) && change >= 0 && change <= 3;
        }
        private static Pawn Original(CoreWorldComponent core,string native)
        { return core.GetDirectlyHeldThings().Cast<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns)).Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().Single(p => p.GetUniqueLoadID() == native); }
        private static string Receipt(RationDebitSnapshot r)
        { return string.Join("|",new[] {r.Id,r.Actor,r.Cause,r.EntityId,r.NativeId,r.CityId,r.StoreTransferId,r.Day.ToString(CultureInfo.InvariantCulture),Number(r.Demand),Number(r.PaidNutrition),r.Stage.ToString(),r.AppliedReceiptId,
            string.Join(";",r.Lines.Select(l => l.SourceNativeId+","+l.SpentNativeId+","+l.DefName+","+l.StuffDef+","+l.HitPoints+","+l.BeforeCount+","+l.UsedCount+","+l.AfterCount+","+Number(l.UnitNutrition)).ToArray())}); }
        private RationDebitSnapshot[] Rations(CoreWorldComponent core)
        { return core.Rations.Snapshots.Where(r => r.EntityId == ids[5] || r.EntityId == ids[6]).OrderBy(r => r.EntityId,StringComparer.Ordinal).ThenBy(r => r.Day).ToArray(); }
        private string[] Spent(CoreWorldComponent core)
        { return core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray(); }
        private string FoodTotals(CoreWorldComponent core)
        { return string.Join(";",Residents.SelectMany(p => p.inventory.innerContainer).Concat(core.Rations.GetDirectlyHeldThings().Cast<Thing>())
            .Where(t => t.def.defName == "MealSurvivalPack" || t.def.defName == "Kibble").GroupBy(t => t.def.defName).OrderBy(g => g.Key,StringComparer.Ordinal)
            .Select(g => g.Key+":"+g.Sum(t => t.stackCount)).ToArray()); }
        private DelegatedMaintenanceSnapshot Plan(ColonyService service,int index)
        { return service.DelegatedMaintenance.Single(p => p.Id == ids[10+index]); }
        private void Bind(CoreWorldComponent core)
        {
            ids=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-maintenance-ids.txt"));
            human=Original(core,ids[3]); animal=Original(core,ids[4]);
            Assert(core.Header.SaveScopeId == ids[0] && Residents.All(p => !p.Spawned && p.holdingOwner == core.GetDirectlyHeldThings() && !Find.WorldPawns.Contains(p)),
                "The city lock fixture retains both original residents exclusively in their original Core holder");
            Assert(core.Ledger.Entities.Count(e => e.Kind == "person" || e.Kind == "animal") == int.Parse(ids[12],CultureInfo.InvariantCulture) &&
                Residents.Select((p,i) => core.Ledger.GetEntity(ids[5+i]).BoundThingLoadId == p.GetUniqueLoadID() && core.Ledger.Entities.Count(e => e.BoundThingLoadId == p.GetUniqueLoadID()) == 1).All(v => v),
                "City checkpoint recovery preserves the world scope and every sole native individual binding");
            Assert(FoodTotals(core) == ids[15],"Every original native maintenance food unit remains in a resident inventory or its sealed spent witness");
        }
        private void CheckNativeBaseline(CoreWorldComponent core,string name,bool city)
        {
            string[] biology=File.ReadAllLines(Backup(name,"biology",city));
            Assert(Residents.Select((p,i) => SameSavedBiology(p,biology[i])).All(v => v) && Residents.Select(Inventory).SequenceEqual(File.ReadAllLines(Backup(name,"inventories",city))) &&
                Spent(core).SequenceEqual(File.ReadAllLines(Backup(name,"spent",city))) && Rations(core).Select(Receipt).SequenceEqual(File.ReadAllLines(Backup(name,"receipts",city))),
                "Checkpoint reload preserves resident needs, health, genes, native inventory counts, every food receipt and all immutable spent IDs");
        }
        private void Save(ColonyService service,CoreWorldComponent core,string name)
        {
            var data=new Dictionary<string,string[]> {{"ids",ids},{"count",new[] {core.Ledger.EntityCount.ToString(CultureInfo.InvariantCulture)}},
                {"biology",Residents.Select(Biology).ToArray()},{"inventories",Residents.Select(Inventory).ToArray()},{"spent",Spent(core)},
                {"receipts",Rations(core).Select(Receipt).ToArray()},{"colony",new[] {service.SaveSnapshot(core)}}};
            foreach (var pair in data) { File.WriteAllLines(PathFor(pair.Key),pair.Value); File.Copy(PathFor(pair.Key),Backup(name,pair.Key,true),true); }
            GameDataSaveLoader.SaveGame(name); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private void Setup(ColonyService service,CoreWorldComponent core)
        {
            int count=int.Parse(File.ReadAllLines(Backup(PendingSave,"count",false))[0],CultureInfo.InvariantCulture);
            Assert(core.Ledger.EntityCount == count && Plan(service,0).Stage == DelegatedMaintenanceStage.Stopped && Plan(service,1).Stage == DelegatedMaintenanceStage.Active,
                "City setup reloads the stopped human and active animal without any new proof allocation");
            CheckNativeBaseline(core,PendingSave,false);
            var biology=Residents.Select(Biology).ToArray(); var inventory=Residents.Select(Inventory).ToArray(); var spent=Spent(core); var receipts=Rations(core).Select(Receipt).ToArray();
            Find.TickManager.DebugSetTicksGame((Today+1)*60000+100); // Deliberate day-two concurrent checkpoint fixture only.
            var prepared=core.Rations.Prepare(ColonyService.Actor,AnimalCause,ids[6],ids[1],Today);
            Assert(core.Ledger.EntityCount == count+2 && prepared.Stage == RationDebitStage.Prepared && prepared.Day == Today && prepared.EntityId == ids[6] && prepared.AppliedReceiptId == null &&
                Residents.Select(Biology).SequenceEqual(biology) && Residents.Select(Inventory).SequenceEqual(inventory) && Spent(core).SequenceEqual(spent) &&
                Rations(core).Where(r => r.Id != prepared.Id).Select(Receipt).SequenceEqual(receipts),
                "A new day-two animal preparation adds exactly its original two Core IDs without consuming native food or mutating the existing human checkpoint");
            Assert(Plan(service,1).Stage == DelegatedMaintenanceStage.Active && Plan(service,1).NextDay == Today,
                "Setup saves before any normal update can create an animal Hold control");
            Save(service,core,SetupSave);
        }
        private void CheckControls(ColonyService service,CoreWorldComponent core)
        {
            foreach (var plan in State(service,core).maintenance)
            {
                string previous=null;
                foreach (var control in plan.controls)
                {
                    var op=core.Ledger.FindOperation(ColonyService.Actor,"create","maintenance-control/"+control.cause);
                    var proof=core.Ledger.GetEntity(control.receipt);
                    Assert(control.previousReceipt == previous && op != null && op.Stage == OperationStage.Committed && op.ResultId == control.receipt &&
                        proof.Kind == "lease" && proof.CustodyId == null && proof.OwnerId == null,
                        "Every city-independent maintenance control retains its exact committed proof without a city reference edge");
                    previous=control.receipt;
                }
            }
        }
        private void CheckPaidWitnesses(CoreWorldComponent core)
        {
            var ration=Rations(core); var holder=core.Rations.GetDirectlyHeldThings(); var spent=holder.Cast<Thing>().ToArray();
            Assert(ration.All(r => r.Stage == RationDebitStage.Committed) && ration.GroupBy(r => r.EntityId+"/"+r.Day).All(g => g.Count() == 1),
                "City unlock commits each exact original day receipt once with no duplicate individual charge");
            foreach (var r in ration)
            {
                var op=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+r.Cause);
                Assert(op != null && op.Stage == OperationStage.Committed && op.ResultId == r.AppliedReceiptId && r.PaidNutrition >= r.Demand,
                    "Every completed day retains its exact application proof and sufficient nutrition budget");
                foreach (var line in r.Lines)
                {
                    var item=spent.Single(t => t.GetUniqueLoadID() == line.SpentNativeId);
                    Assert(!item.Destroyed && !item.Spawned && item.holdingOwner == holder && item.stackCount == line.UsedCount && item.def.defName == line.DefName &&
                        (item.Stuff == null ? "" : item.Stuff.defName) == (line.StuffDef ?? "") && item.HitPoints == line.HitPoints && line.BeforeCount == line.UsedCount+line.AfterCount,
                        "All historical native spent witnesses retain their exact IDs, counts, material and health after city recovery");
                }
            }
            Assert(spent.Length == ration.Sum(r => r.Lines.Length),"City recovery has no unreceipted or missing native spent food");
        }
        private void CityLock(ColonyService service,CoreWorldComponent core)
        {
            bool other=Flag("other-prepared"),applying=Flag("applying"); string source=other ? SetupSave : PendingSave;
            int saved=int.Parse(File.ReadAllLines(Backup(source,"count",other))[0],CultureInfo.InvariantCulture);
            // Also exercises the ordinary paused service path if the world's first update has not yet run.
            service.Tick();
            Assert(core.Ledger.EntityCount == saved+2 && Plan(service,0).Stage == DelegatedMaintenanceStage.Stopped &&
                Plan(service,1).Stage == DelegatedMaintenanceStage.Held && Plan(service,1).Problem == "Pending" && Plan(service,1).NextDay == Today,
                "The ordinary paused update adds only the animal's Pending Hold control despite another resident's city lock");
            CheckNativeBaseline(core,source,other); CheckControls(service,core);
            var humanDebit=Rations(core).Single(r => r.Cause == HumanCause);
            var humanOp=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+HumanCause);
            Assert(humanDebit.Stage == RationDebitStage.Applied && humanOp != null && humanOp.Stage == OperationStage.Prepared &&
                humanOp.ResultId == humanDebit.AppliedReceiptId && humanOp.RecoveryCount == (applying ? 1 : 0) && core.Ledger.HasPendingReference(ids[1]),
                "Reload restores the same pending human proof reference and only normalizes an interrupted Applying checkpoint");
            Assert(Today == humanDebit.Day+1,"The city-lock fixture observes the next global day without ticking either original resident");
            int count=core.Ledger.EntityCount; string payload=service.SaveSnapshot(core); var inventory=Residents.Select(Inventory).ToArray(); var biology=Residents.Select(Biology).ToArray();
            var spent=Spent(core); var receipts=Rations(core).Select(Receipt).ToArray();
            if (other)
            {
                var prepared=Rations(core).Single(r => r.Cause == AnimalCause);
                Assert(prepared.Stage == RationDebitStage.Prepared && prepared.Day == Today && prepared.AppliedReceiptId == null,
                    "The other resident already owns its exact day-two Prepared food certificate");
                RefuseBecause(() => core.Rations.Apply(prepared.Id),"Ration city is locked before native food mutation",
                    "Applying the other resident's existing preparation refuses specifically at the city lock before a native split");
            }
            else
                RefuseBecause(() => core.Rations.Prepare(ColonyService.Actor,AnimalCause,ids[6],ids[1],Today),"Ration city is locked by a pending Core operation",
                    "Preparing the other resident's current-day food refuses specifically at the city lock before any proof allocation");
            Assert(core.Ledger.EntityCount == count && service.SaveSnapshot(core) == payload && Residents.Select(Inventory).SequenceEqual(inventory) &&
                Residents.Select(Biology).SequenceEqual(biology) && Spent(core).SequenceEqual(spent) && Rations(core).Select(Receipt).SequenceEqual(receipts),
                "The cross-resident city guard allocates nothing and preserves the complete plan, native food, biology and both sets of receipt IDs");
            service.ResumeDelegatedRations(humanDebit.Id); service.ResumeDelegatedRations(humanDebit.Id);
            var recovered=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+HumanCause);
            Assert(core.Ledger.EntityCount == count && recovered.Id == humanOp.Id && recovered.ResultId == humanOp.ResultId && recovered.Stage == OperationStage.Committed &&
                recovered.RecoveryCount == (applying ? 1 : 0) && !core.Ledger.HasPendingReference(ids[1]) && Residents.Select(Inventory).SequenceEqual(inventory) &&
                Residents.Select(Biology).SequenceEqual(biology) && Spent(core).SequenceEqual(spent),
                "Explicit human proof recovery unlocks the city using the same proof IDs with no second native food debit");
            string afterHuman=service.SaveSnapshot(core); var afterReceipts=Rations(core).Select(Receipt).ToArray();
            service.Tick(); service.Tick();
            Assert(core.Ledger.EntityCount == count && service.SaveSnapshot(core) == afterHuman && Rations(core).Select(Receipt).SequenceEqual(afterReceipts) &&
                Plan(service,1).Stage == DelegatedMaintenanceStage.Held && Residents.Select(Inventory).SequenceEqual(inventory) && Spent(core).SequenceEqual(spent),
                "Unlocking the city never automatically retries the animal's held plan");
            if (other)
            {
                var prepared=Rations(core).Single(r => r.Cause == AnimalCause);
                service.ResumeDelegatedRations(prepared.Id); service.ResumeDelegatedRations(prepared.Id);
                Assert(core.Ledger.EntityCount == count+2 && Rations(core).Single(r => r.Id == prepared.Id).Stage == RationDebitStage.Committed &&
                    Plan(service,1).Stage == DelegatedMaintenanceStage.Held && Rations(core).Count(r => r.EntityId == ids[6] && r.Day == Today) == 1,
                    "Explicit animal preparation recovery creates only its missing application proof and leaves the held plan unchanged");
                count+=2;
            }
            var beforeResumeInventory=Residents.Select(Inventory).ToArray(); var beforeResumeSpent=Spent(core); var beforeResumeReceipts=Rations(core).Select(Receipt).ToArray();
            service.ResumeDelegatedMaintenance(ResumeCause,ids[11]);
            Assert(core.Ledger.EntityCount == count+(other ? 2 : 6) && Plan(service,1).Stage == DelegatedMaintenanceStage.Active && Plan(service,1).LastPaidDay == Today &&
                Plan(service,1).NextDay == Today+1 && Plan(service,0).Stage == DelegatedMaintenanceStage.Stopped && !Rations(core).Any(r => r.EntityId == ids[5] && r.Day == Today) &&
                Residents.Select(Biology).SequenceEqual(biology) && Rations(core).Count(r => r.EntityId == ids[6] && r.Day == Today) == 1 && spent.All(s => Spent(core).Contains(s)),
                other ? "Explicit plan resume adds only its control and adopts the animal's existing current-day payment" :
                    "Explicit plan resume creates exactly one control and one complete current-day animal debit while the human remains stopped");
            if (other) Assert(Residents.Select(Inventory).SequenceEqual(beforeResumeInventory) && Spent(core).SequenceEqual(beforeResumeSpent) &&
                Rations(core).Select(Receipt).SequenceEqual(beforeResumeReceipts),"Adopting the already paid animal day changes no native food or original receipt");
            count=core.Ledger.EntityCount; payload=service.SaveSnapshot(core); inventory=Residents.Select(Inventory).ToArray(); spent=Spent(core); receipts=Rations(core).Select(Receipt).ToArray();
            service.ResumeDelegatedMaintenance(ResumeCause,ids[11]); service.Tick(); service.Tick();
            Assert(core.Ledger.EntityCount == count && service.SaveSnapshot(core) == payload && Residents.Select(Inventory).SequenceEqual(inventory) &&
                Residents.Select(Biology).SequenceEqual(biology) && Spent(core).SequenceEqual(spent) && Rations(core).Select(Receipt).SequenceEqual(receipts),
                "Repeated explicit resume and current-day updates create no duplicate plan, proof, resident or native food charge");
            CheckControls(service,core); CheckPaidWitnesses(core);
            Assert(FoodTotals(core) == ids[15],"City recovery moves only existing food units into sealed spent portions without creating or losing food");
            Save(service,core,"INH_MaintenanceCityRecovered");
        }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>();
            if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current;
                Assert(core.IsReady && service != null,"Core and Colony initialize the city-lock maintenance fixture");
                Find.TickManager.Pause(); Bind(core);
                if (Flag("setup")) Setup(service,core); else CityLock(service,core);
            }
            catch (Exception error) { done=true; Log.Error("[INH-MAINTENANCE-CITY] FAIL "+error); Application.Quit(1); }
        }
    }
}
