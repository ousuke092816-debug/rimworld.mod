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
    // The fixture escrow only preserves the same food while testing a shortage across save/load.
    // Product maintenance never owns or uses this test holder.
    public sealed class DelegatedMaintenanceProbe : GameComponent,IThingHolder
    {
        private static readonly string[] Modes={"start","fresh-start","day","shortage","resume","stop","reload","gap","gap-resume","render","render-japanese","pending-stop-prepared","pending-stop-applied","pending-recover","reentry"};
        private static readonly string[] StartCauses={"maintenance-probe-person","maintenance-probe-animal"};
        private ThingOwner<Thing> escrow;
        private bool done,restored,captured;
        private string[] ids;
        private Pawn human,animal,escort;
        private Caravan caravan;
        private int targetDay=-1,beforeAdvance,frames;
        private string[] frozenBefore;
        private DelegatedMaintenanceWindow window;
        public DelegatedMaintenanceProbe(Game game) { escrow=new ThingOwner<Thing>(this); }
        public IThingHolder ParentHolder { get { return null; } }
        public ThingOwner GetDirectlyHeldThings() { return escrow; }
        public void GetChildHolders(List<IThingHolder> children) { ThingOwnerUtility.AppendThingHoldersFromThings(children,escrow); }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Deep.Look(ref escrow,"maintenanceFixtureEscrow",this);
            if (Scribe.mode == LoadSaveMode.LoadingVars && escrow == null) escrow=new ThingOwner<Thing>(this);
        }
        private static bool Has(string mode) { return GenCommandLine.CommandLineArgPassed("inh-maintenance-"+mode); }
        private static bool Selected { get { return GenCommandLine.CommandLineArgPassed("inh-maintenance-probe") && Modes.Any(Has); } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static int Today { get { return Find.TickManager.TicksGame/60000; } }
        private static string Number(float value) { return value.ToString("R",CultureInfo.InvariantCulture); }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-maintenance-"+key+".txt"); }
        private static string Backup(string name,string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,name+"-maintenance-"+key+".txt"); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-MAINTENANCE] PASS "+message); }
        private static void Refuse(Action action,string message)
        { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,message); }
        private static ColonyState State(ColonyService service,CoreWorldComponent core)
        { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core))); }
        private static string Item(Thing item)
        { return item.GetUniqueLoadID()+":"+item.def.defName+":"+(item.Stuff == null ? "" : item.Stuff.defName)+":"+item.stackCount+":"+item.HitPoints; }
        private static string Inventory(Pawn pawn) { return string.Join(";",pawn.inventory.innerContainer.Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray()); }
        private static string Biology(Pawn pawn)
        {
            return string.Join("|",new[] {pawn.GetUniqueLoadID(),pawn.def.defName,pawn.Name == null ? "" : pawn.Name.ToStringFull,pawn.gender.ToString(),pawn.Faction.GetUniqueLoadID(),
                pawn.ageTracker.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture),
                pawn.skills == null ? "" : string.Join(",",pawn.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+Number(s.xpSinceLastLevel)).ToArray()),
                string.Join(",",pawn.health.hediffSet.hediffs.Select(h => h.def.defName+":"+Number(h.Severity)+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                pawn.genes == null ? "" : string.Join(",",pawn.genes.GenesListForReading.Select(g => g.def.defName+":"+g.Active).ToArray()),pawn.Ideo == null ? "" : pawn.Ideo.GetUniqueLoadID(),
                pawn.needs == null ? "" : string.Join(",",pawn.needs.AllNeeds.OrderBy(n => n.def.defName,StringComparer.Ordinal).Select(n => n.def.defName+":"+Number(n.CurLevel)).ToArray()),
                pawn.story == null ? "" : string.Join(",",pawn.story.traits.allTraits.Select(t => t.def.defName+":"+t.Degree).ToArray()),
                pawn.apparel == null ? "" : string.Join(",",pawn.apparel.WornApparel.Select(a => Item(a)).OrderBy(s => s,StringComparer.Ordinal).ToArray()),
                pawn.equipment == null ? "" : string.Join(",",pawn.equipment.AllEquipmentListForReading.Select(e => Item(e)).OrderBy(s => s,StringComparer.Ordinal).ToArray())});
        }
        private static bool SameSavedBiology(Pawn pawn,string expected)
        {
            string[] actual=Biology(pawn).Split('|'),old=expected.Split('|');
            long change=long.Parse(actual[5],CultureInfo.InvariantCulture)-long.Parse(old[5],CultureInfo.InvariantCulture);
            return actual.Where((s,i) => i != 5).SequenceEqual(old.Where((s,i) => i != 5)) && change >= 0 && change <= 3;
        }
        private Pawn[] Residents { get { return new[] {human,animal}; } }
        private static Pawn Original(CoreWorldComponent core,string native)
        { return core.GetDirectlyHeldThings().Cast<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns)).Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().Single(p => p.GetUniqueLoadID() == native); }
        private static bool Held(Pawn pawn,CoreWorldComponent core)
        { return !pawn.Spawned && pawn.holdingOwner == core.GetDirectlyHeldThings() && !Find.WorldPawns.Contains(pawn) && !Find.WorldObjects.Caravans.Any(c => c.PawnsListForReading.Contains(pawn)); }
        private static bool InCaravan(Pawn pawn,Caravan caravan)
        { return !pawn.Spawned && pawn.holdingOwner == caravan.GetDirectlyHeldThings() && caravan.PawnsListForReading.Contains(pawn) && Find.WorldPawns.Contains(pawn) && Find.WorldObjects.Caravans.Count(c => c.PawnsListForReading.Contains(pawn)) == 1; }
        private static int Individuals(CoreWorldComponent core) { return core.Ledger.Entities.Count(e => e.Kind == "person" || e.Kind == "animal"); }
        private static string Stocks(CoreWorldComponent core)
        { return string.Join(";",core.Ledger.Entities.Where(e => e.Kind == "lot" && e.Authority == SimulationAuthority.Abstract).OrderBy(e => e.Id,StringComparer.Ordinal).Select(e => e.Id+":"+e.Quantity).ToArray()); }
        private static string Receipt(RationDebitSnapshot r)
        { return string.Join("|",new[] {r.Id,r.Actor,r.Cause,r.EntityId,r.NativeId,r.CityId,r.StoreTransferId,r.Day.ToString(CultureInfo.InvariantCulture),Number(r.Demand),Number(r.PaidNutrition),r.Stage.ToString(),r.AppliedReceiptId,string.Join(";",r.Lines.Select(l => l.SourceNativeId+","+l.SpentNativeId+","+l.DefName+","+l.StuffDef+","+l.HitPoints+","+l.BeforeCount+","+l.UsedCount+","+l.AfterCount+","+Number(l.UnitNutrition)).ToArray())}); }
        private RationDebitSnapshot[] Rations(CoreWorldComponent core)
        { return core.Rations.Snapshots.Where(r => r.EntityId == ids[5] || r.EntityId == ids[6]).OrderBy(r => r.EntityId,StringComparer.Ordinal).ThenBy(r => r.Day).ToArray(); }
        private DelegatedMaintenanceSnapshot[] Plans(ColonyService service)
        { return new[] {ids[10],ids[11]}.Select(id => service.DelegatedMaintenance.Single(p => p.Id == id)).ToArray(); }
        private string[] Spent(CoreWorldComponent core) { return core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray(); }
        private string[] EscrowItems() { return escrow.Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray(); }
        private string TrackedFood(CoreWorldComponent core)
        {
            return string.Join(";",Residents.SelectMany(p => p.inventory.innerContainer).Concat(escrow).Concat(core.Rations.GetDirectlyHeldThings().Cast<Thing>())
                .Where(t => t.def.defName == "MealSurvivalPack" || t.def.defName == "Kibble").GroupBy(t => t.def.defName).OrderBy(g => g.Key,StringComparer.Ordinal).Select(g => g.Key+":"+g.Sum(t => t.stackCount)).ToArray());
        }
        private void CheckOriginals(CoreWorldComponent core)
        {
            Assert(core.Header.SaveScopeId == ids[0] && Individuals(core) == int.Parse(ids[12],CultureInfo.InvariantCulture) && Stocks(core) == ids[13],"Daily maintenance preserves the original world scope, all individual identities and every abstract stock");
            for (int i=0;i<2;i++) Assert(core.Ledger.Entities.Count(e => e.BoundThingLoadId == ids[3+i]) == 1 && core.Ledger.GetEntity(ids[5+i]).BoundThingLoadId == ids[3+i] && ReferenceEquals(Residents[i],Original(core,ids[3+i])),"Each daily charge retains the sole original native person or animal and its permanent Core binding");
            string steel=string.Join(";",Residents.Concat(caravan.PawnsListForReading).Distinct().SelectMany(p => p.inventory.innerContainer).Where(t => t.def.defName == "Steel").Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray());
            Assert(steel == ids[14] && TrackedFood(core) == ids[15],"Existing steel remains exact and all original maintenance food is conserved across surviving stacks, fixture escrow and sealed spent portions");
        }
        private void CheckProofs(ColonyService service,CoreWorldComponent core,int expectedPlans=2)
        {
            var records=State(service,core).maintenance; var ration=Rations(core); var spent=core.Rations.GetDirectlyHeldThings().Cast<Thing>().ToArray();
            Assert(records.Count == expectedPlans && records.All(p => p.controlCount == p.controls.Count),"The expected daily plans retain their complete durable control chains without a duplicate plan for an original store");
            foreach (var plan in records)
            {
                string previous=null;
                foreach (var control in plan.controls)
                {
                    var operation=core.Ledger.FindOperation(ColonyService.Actor,"create","maintenance-control/"+control.cause); var proof=core.Ledger.GetEntity(control.receipt);
                    Assert(control.previousReceipt == previous && operation != null && operation.Stage == OperationStage.Committed && operation.ResultId == control.receipt && proof.Kind == "lease" && proof.Provider == ColonyService.Actor,
                        "Every Start, Hold, Resume and Stop control has its sole existing committed Core proof in an unbroken chain"); previous=control.receipt;
                }
            }
            Assert(ration.GroupBy(r => r.EntityId+"/"+r.Day).All(g => g.Count() == 1),"Each original individual has at most one food debit for each paid day");
            foreach (var r in ration)
            {
                var operation=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+r.Cause);
                Assert(r.Stage == RationDebitStage.Committed && operation != null && operation.Stage == OperationStage.Committed && operation.ResultId == r.AppliedReceiptId && r.PaidNutrition >= r.Demand,
                    "Every paid daily budget retains its original committed preparation and native-food application proofs");
                foreach (var line in r.Lines)
                {
                    var item=spent.Single(t => t.GetUniqueLoadID() == line.SpentNativeId);
                    Assert(!item.Destroyed && !item.Spawned && item.holdingOwner == core.Rations.GetDirectlyHeldThings() && item.stackCount == line.UsedCount && item.def.defName == line.DefName &&
                        (item.Stuff == null ? "" : item.Stuff.defName) == (line.StuffDef ?? "") && item.HitPoints == line.HitPoints && line.BeforeCount == line.AfterCount+line.UsedCount,
                        "Historical daily food witnesses keep their exact native IDs, materials, durability and used counts when the surviving original stack is charged on another day");
                }
            }
            Assert(spent.Length == ration.Sum(r => r.Lines.Length),"No additional or missing spent native food exists outside the complete historical debit proofs");
        }
        private void CheckSameDay(ColonyService service,CoreWorldComponent core)
        {
            int count=core.Ledger.EntityCount; string colony=service.SaveSnapshot(core); string[] inventory=Residents.Select(Inventory).ToArray(),spent=Spent(core),escrowBefore=EscrowItems(),biology=Residents.Select(Biology).ToArray();
            service.Tick(); service.Tick();
            for (int i=0;i<2;i++) Assert(service.StartDelegatedMaintenance(StartCauses[i],ids[1],ids[5+i]) == ids[10+i],"Repeated historical start selects its existing maintenance plan");
            Assert(core.Ledger.EntityCount == count && service.SaveSnapshot(core) == colony && Residents.Select(Inventory).SequenceEqual(inventory) && Residents.Select(Biology).SequenceEqual(biology) && Spent(core).SequenceEqual(spent) && EscrowItems().SequenceEqual(escrowBefore),
                "Repeated same-day updates and start replay allocate nothing, charge no food and mutate no plan or native original");
        }
        private void Save(ColonyService service,CoreWorldComponent core,string name)
        {
            var data=new Dictionary<string,string[]> { {"ids",ids},{"biology",Residents.Select(Biology).ToArray()},{"inventories",Residents.Select(Inventory).ToArray()},
                {"receipts",Rations(core).Select(Receipt).ToArray()},{"spent",Spent(core)},{"escrow",EscrowItems()},{"count",new[] {core.Ledger.EntityCount.ToString(CultureInfo.InvariantCulture)}},{"colony",new[] {service.SaveSnapshot(core)}} };
            foreach (var entry in data) { File.WriteAllLines(PathFor(entry.Key),entry.Value); File.Copy(PathFor(entry.Key),Backup(name,entry.Key),true); }
            GameDataSaveLoader.SaveGame(name); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private void Restore(ColonyService service,CoreWorldComponent core)
        {
            ids=File.ReadAllLines(PathFor("ids")); human=Original(core,ids[3]); animal=Original(core,ids[4]); caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[2]); escort=caravan.PawnsListForReading.Single(p => p.IsColonist && p != human);
            bool gap=Has("gap"); int saved=int.Parse(File.ReadAllLines(PathFor("count"))[0],CultureInfo.InvariantCulture);
            Assert(core.Ledger.EntityCount == saved+(gap ? 4 : 0),gap ? "The first ordinary update observes two missed-day gaps and creates only their Hold controls" : "Separate process reload observes the existing daily plans and Core proofs without allocation");
            var biology=File.ReadAllLines(PathFor("biology"));
            Assert(Residents.Select((p,i) => SameSavedBiology(p,biology[i])).All(v => v) && Residents.Select(Inventory).SequenceEqual(File.ReadAllLines(PathFor("inventories"))) &&
                Rations(core).Select(Receipt).SequenceEqual(File.ReadAllLines(PathFor("receipts"))) && Spent(core).SequenceEqual(File.ReadAllLines(PathFor("spent"))) && EscrowItems().SequenceEqual(File.ReadAllLines(PathFor("escrow"))),
                "Reload preserves native resident biology, needs, health, all surviving and escrowed food, each historical daily proof and sealed spent item");
            if (!gap) Assert(service.SaveSnapshot(core) == string.Join(Environment.NewLine,File.ReadAllLines(PathFor("colony"))),"The complete saved Colony plan and control payload survives process reload unchanged");
            CheckOriginals(core); if (!Has("pending-recover")) CheckProofs(service,core); restored=true;
        }
        private void Start(ColonyService service,CoreWorldComponent core)
        {
            bool fresh=Has("fresh-start");
            if (fresh) Find.TickManager.DebugSetTicksGame((Today+1)*60000+100); // Deliberate clock fixture only.
            var residents=core.ReadPawnCustodyResidents(ColonyService.Actor).Where(r => r.CaravanId != null).ToArray();
            var person=residents.Single(r => core.Ledger.GetEntity(r.EntityId).Kind == "person"); var beast=residents.Single(r => core.Ledger.GetEntity(r.EntityId).Kind == "animal");
            human=Original(core,person.NativeId); animal=Original(core,beast.NativeId); caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == person.CaravanId); escort=caravan.PawnsListForReading.Single(p => p.IsColonist);
            // Supply the native escort from existing food, never by creating a replacement food item.
            var supply=human.inventory.innerContainer.Where(t => t.def.defName == "MealSurvivalPack").OrderByDescending(t => t.stackCount).First(); Thing transferred;
            Assert(supply.stackCount >= 10 && human.inventory.innerContainer.TryTransferToContainer(supply,escort.inventory.innerContainer,10,out transferred,false) == 10,
                "A fixture-only split of ten existing native meals supplies the live escort during several ordinary game days");
            ids=new[] {core.Header.SaveScopeId,person.CityId,person.CaravanId,person.NativeId,beast.NativeId,person.EntityId,beast.EntityId,person.StoreTransferId,beast.StoreTransferId,Today.ToString(CultureInfo.InvariantCulture),"","",
                Individuals(core).ToString(CultureInfo.InvariantCulture),Stocks(core),string.Join(";",Residents.Concat(caravan.PawnsListForReading).Distinct().SelectMany(p => p.inventory.innerContainer).Where(t => t.def.defName == "Steel").Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray()),""};
            ids[15]=TrackedFood(core); var biological=Residents.Select(Biology).ToArray(); var receipts=Rations(core).Select(Receipt).ToArray(); var inventory=Residents.Select(Inventory).ToArray(); var spent=Spent(core); int count=core.Ledger.EntityCount;
            Assert(Residents.All(p => Held(p,core)) && Rations(core).Length == 2 && Rations(core).All(r => r.Day == Today-(fresh ? 1 : 0) && r.Stage == RationDebitStage.Committed),"Both exact original residents retain their previous manual food payment before plan enrolment");
            for (int i=0;i<2;i++) ids[10+i]=service.StartDelegatedMaintenance(StartCauses[i],ids[1],ids[5+i]);
            if (fresh)
                Assert(core.Ledger.EntityCount == count+12 && Residents.Select(Biology).SequenceEqual(biological) && Rations(core).Where(r => r.Day < Today).Select(Receipt).SequenceEqual(receipts) &&
                    spent.All(s => Spent(core).Contains(s)) && Rations(core).Count(r => r.Day == Today) == 2,
                    "Fresh enrolment on an unpaid current day creates exactly two Start controls and two fully paid native food debits while keeping previous food proofs unchanged");
            else
                Assert(core.Ledger.EntityCount == count+4 && Residents.Select(Biology).SequenceEqual(biological) && Residents.Select(Inventory).SequenceEqual(inventory) && Rations(core).Select(Receipt).SequenceEqual(receipts) && Spent(core).SequenceEqual(spent),
                    "Enrolling two originals creates only two Start controls and adopts their existing current-day payments without charging food again");
            Assert(Plans(service).All(p => p.Stage == DelegatedMaintenanceStage.Active && p.StartDay == Today && p.LastPaidDay == Today && p.NextDay == Today+1),"Saved plans derive their next day from the original manual payment rather than duplicating it");
            CheckOriginals(core); CheckProofs(service,core); CheckSameDay(service,core); Save(service,core,fresh ? "INH_MaintenanceFreshStarted" : "INH_MaintenanceStarted");
        }
        private void PendingStop(ColonyService service,CoreWorldComponent core)
        {
            bool applied=Has("pending-stop-applied"); int count=core.Ledger.EntityCount; var biology=Residents.Select(Biology).ToArray();
            Find.TickManager.DebugSetTicksGame((Today+1)*60000+100); // Exact native checkpoint fixture, not ordinary time advancement.
            var pending=core.Rations.Prepare(ColonyService.Actor,"maintenance-pending-probe",ids[5],ids[1],Today);
            if (applied) core.Rations.Apply(pending.Id);
            service.StopDelegatedMaintenance("maintenance-pending-stop",ids[10]); service.StopDelegatedMaintenance("maintenance-pending-stop",ids[10]); service.Tick(); service.Tick();
            pending=Rations(core).Single(r => r.Cause == "maintenance-pending-probe");
            Assert(core.Ledger.EntityCount == count+(applied ? 10 : 8) && pending.Stage == (applied ? RationDebitStage.Applied : RationDebitStage.Prepared) &&
                Plans(service)[0].Stage == DelegatedMaintenanceStage.Stopped && Plans(service)[1].LastPaidDay == Today && Residents.Select(Biology).SequenceEqual(biology),
                "Stopping a plan during its existing native food checkpoint creates only its reference-free Stop control while the independent animal pays normally");
            CheckOriginals(core); Save(service,core,applied ? "INH_MaintenancePendingAppliedStopped" : "INH_MaintenancePendingStopped");
        }
        private void PendingRecover(ColonyService service,CoreWorldComponent core)
        {
            if (targetDay < 0)
            {
                var pending=Rations(core).Single(r => r.Cause == "maintenance-pending-probe"); int count=core.Ledger.EntityCount;
                bool prepared=pending.Stage == RationDebitStage.Prepared;
                Assert((prepared || pending.Stage == RationDebitStage.Applied) && Plans(service)[0].Stage == DelegatedMaintenanceStage.Stopped,
                    "Reload preserves the stopped plan and its sole original unfinished food debit instead of automatically resuming either operation");
                service.ResumeDelegatedRations(pending.Id); service.ResumeDelegatedRations(pending.Id);
                var after=Rations(core).Single(r => r.Id == pending.Id);
                Assert(after.Stage == RationDebitStage.Committed && core.Ledger.EntityCount == count+(prepared ? 2 : 0) && Plans(service)[0].Stage == DelegatedMaintenanceStage.Stopped,
                    "Explicit recovery finishes the exact existing native food debit and only its missing application proof while leaving the maintenance plan stopped");
                CheckOriginals(core); CheckProofs(service,core);
            }
            if (!Advance(service,core)) return;
            Assert(core.Ledger.EntityCount == beforeAdvance+4 && Plans(service)[0].Stage == DelegatedMaintenanceStage.Stopped && Plans(service)[0].LastPaidDay == Today-1 &&
                !Rations(core).Any(r => r.EntityId == ids[5] && r.Day == Today) && Rations(core).Count(r => r.EntityId == ids[6] && r.Day == Today) == 1,
                "After native food recovery another ordinary day charges only the independent active animal and never restarts the stopped original person's plan");
            CheckOriginals(core); CheckProofs(service,core); CheckSameDay(service,core); Save(service,core,"INH_MaintenancePendingRecovered");
        }
        private void Reentry(ColonyService service,CoreWorldComponent core)
        {
            int count=core.Ledger.EntityCount; var biology=Residents.Select(Biology).ToArray(); string[] receipts=Rations(core).Select(Receipt).ToArray(),spent=Spent(core);
            service.StopDelegatedMaintenance("maintenance-reentry-stop",ids[10]);
            service.ReleaseDelegatedPawn("maintenance-reentry-release",ids[1],ids[5],caravan);
            string newStore=service.StoreDelegatedPawn("maintenance-reentry-store",ids[1],caravan,human);
            Assert(core.Ledger.EntityCount == count+6 && newStore != ids[7] && Plans(service)[0].Stage == DelegatedMaintenanceStage.Released && Residents.All(p => Held(p,core)),
                "Reentry creates a new original store certificate for the same native person while preserving its released old maintenance plan");
            int before=core.Ledger.EntityCount; string payload=service.SaveSnapshot(core); string[] inventory=Residents.Select(Inventory).ToArray();
            Refuse(() => service.StartDelegatedMaintenance("maintenance-reentry-plan",ids[1],ids[5]),"The same individual cannot buy a second current-day payment by leaving and reentering its original city");
            Assert(core.Ledger.EntityCount == before && service.SaveSnapshot(core) == payload && Residents.Select(Inventory).SequenceEqual(inventory) && Rations(core).Select(Receipt).SequenceEqual(receipts) && Spent(core).SequenceEqual(spent),
                "Same-day reentry refusal allocates no plan or proof and changes no food or historical payment");
            Find.TickManager.DebugSetTicksGame((Today+1)*60000+100); service.Tick(); // Deliberate next-day eligibility fixture.
            before=core.Ledger.EntityCount; string newPlan=service.StartDelegatedMaintenance("maintenance-reentry-plan",ids[1],ids[5]);
            var plan=service.DelegatedMaintenance.Single(p => p.Id == newPlan);
            Assert(core.Ledger.EntityCount == before+6 && plan.Stage == DelegatedMaintenanceStage.Active && plan.LastPaidDay == Today && plan.NextDay == Today+1 &&
                Plans(service)[0].Stage == DelegatedMaintenanceStage.Released && State(service,core).maintenance.Single(p => p.id == newPlan).storeTransferId == newStore &&
                Residents.Select(Biology).SequenceEqual(biology),"On a later day the same native person can enrol its exact new store and pay once while its old released plan remains historical");
            service.Tick(); int replay=core.Ledger.EntityCount; Assert(service.StartDelegatedMaintenance("maintenance-reentry-plan",ids[1],ids[5]) == newPlan && core.Ledger.EntityCount == replay,
                "The new store's successful start is independently idempotent");
            CheckOriginals(core); CheckProofs(service,core,3); Save(service,core,"INH_MaintenanceReentered");
        }
        private bool Advance(ColonyService service,CoreWorldComponent core)
        {
            if (targetDay < 0) { targetDay=Today+1; beforeAdvance=core.Ledger.EntityCount; frozenBefore=Residents.Select(Biology).ToArray(); }
            int target=targetDay*60000;
            for (int ticks=0;ticks<1000 && Find.TickManager.TicksGame < target;ticks++) Find.TickManager.DoSingleTick(); Find.TickManager.Pause();
            if (Find.TickManager.TicksGame < target) return false;
            service.Tick();
            Assert(Today == targetDay && Residents.Select(Biology).SequenceEqual(frozenBefore) && Residents.All(p => Held(p,core)),"Ordinary native ticks cross one exact day boundary while stored biological age, health, needs and all original resident ownership remain frozen");
            return true;
        }
        private void EscrowHumanFood()
        {
            var foods=human.inventory.innerContainer.Where(t => t.def.IsNutritionGivingIngestible).ToArray();
            Assert(foods.Length > 0 && escrow.Count == 0,"Shortage fixture begins with the existing surviving native human food");
            foreach (var food in foods) Assert(human.inventory.innerContainer.TryTransferToContainer(food,escrow,false),"Fixture escrows the same whole native food object without splitting, merging or destroying it");
        }
        private void Day(ColonyService service,CoreWorldComponent core)
        {
            if (!Advance(service,core)) return;
            Assert(core.Ledger.EntityCount == beforeAdvance+8 && Plans(service).All(p => p.Stage == DelegatedMaintenanceStage.Active && p.LastPaidDay == Today && p.NextDay == Today+1) && Rations(core).Count(r => r.Day == Today) == 2,
                "The next ordinary day automatically charges each original exactly once and creates only their food preparation and application proofs");
            CheckOriginals(core); CheckProofs(service,core); CheckSameDay(service,core); Save(service,core,"INH_MaintenanceDayPaid");
        }
        private void Shortage(ColonyService service,CoreWorldComponent core)
        {
            if (targetDay < 0) EscrowHumanFood();
            if (!Advance(service,core)) return;
            var plans=Plans(service);
            Assert(core.Ledger.EntityCount == beforeAdvance+6 && plans[0].Stage == DelegatedMaintenanceStage.Held && plans[0].Problem == "Shortage" && plans[0].LastPaidDay == Today-1 && plans[0].NextDay == Today &&
                plans[1].Stage == DelegatedMaintenanceStage.Active && plans[1].LastPaidDay == Today && plans[1].NextDay == Today+1 && !Rations(core).Any(r => r.EntityId == ids[5] && r.Day == Today) && Rations(core).Count(r => r.EntityId == ids[6] && r.Day == Today) == 1,
                "An underfunded human is held with only one control proof while the funded original animal pays its single new day normally");
            CheckOriginals(core); CheckProofs(service,core); CheckSameDay(service,core); Save(service,core,"INH_MaintenanceShortage");
        }
        private void Resume(ColonyService service,CoreWorldComponent core,bool gap)
        {
            int count=core.Ledger.EntityCount; var biological=Residents.Select(Biology).ToArray(); int first=0,last=gap ? 2 : 1;
            if (!gap)
            {
                Assert(escrow.Count > 0 && Plans(service)[0].Problem == "Shortage","Saved food shortage retains the exact original food escrow and Hold control");
                foreach (var food in escrow.ToArray()) Assert(escrow.TryTransferToContainer(food,human.inventory.innerContainer,false),"Replenishment restores the same original food object and native ID to its original inventory");
            }
            for (int i=first;i<last;i++) { string cause=gap ? "maintenance-gap-resume-"+i : "maintenance-shortage-resume"; service.ResumeDelegatedMaintenance(cause,ids[10+i]); service.ResumeDelegatedMaintenance(cause,ids[10+i]); }
            Assert(core.Ledger.EntityCount == count+(gap ? 12 : 6) && Plans(service).All(p => p.Stage == DelegatedMaintenanceStage.Active && p.LastPaidDay == Today && p.NextDay == Today+1) && Residents.Select(Biology).SequenceEqual(biological),
                "Explicit current-day resume creates only each required Resume control and one unpaid day charge while preserving original resident biology");
            if (gap) Assert(!Rations(core).Any(r => r.Day > int.Parse(ids[9],CultureInfo.InvariantCulture) && r.Day < Today),"Skipped past days have no maintenance food debits after explicit current-day resume");
            CheckOriginals(core); CheckProofs(service,core); CheckSameDay(service,core); Save(service,core,gap ? "INH_MaintenanceGapResumed" : "INH_MaintenanceResumed");
        }
        private void Stop(ColonyService service,CoreWorldComponent core)
        {
            if (targetDay < 0)
            {
                int count=core.Ledger.EntityCount;
                for (int i=0;i<2;i++) { service.StopDelegatedMaintenance("maintenance-stop-"+i,ids[10+i]); service.StopDelegatedMaintenance("maintenance-stop-"+i,ids[10+i]); }
                Assert(core.Ledger.EntityCount == count+4 && Plans(service).All(p => p.Stage == DelegatedMaintenanceStage.Stopped),"Stopping two plans creates only their sole Stop controls and repeated stop changes nothing");
            }
            if (!Advance(service,core)) return;
            Assert(core.Ledger.EntityCount == beforeAdvance && Plans(service).All(p => p.Stage == DelegatedMaintenanceStage.Stopped && p.LastPaidDay == Today-1) && !Rations(core).Any(r => r.Day == Today),"Stopped plans do not charge food or allocate proofs after a further ordinary day passes");
            int before=core.Ledger.EntityCount; var inventory=Residents.Select(Inventory).ToArray(); var biological=Residents.Select(Biology).ToArray(); var spent=Spent(core);
            for (int i=0;i<2;i++) { service.ReleaseDelegatedPawn("maintenance-release-"+i,ids[1],ids[5+i],caravan); service.ReleaseDelegatedPawn("maintenance-release-"+i,ids[1],ids[5+i],caravan); }
            Assert(core.Ledger.EntityCount == before+4 && Residents.All(p => InCaravan(p,caravan)) && Plans(service).All(p => p.Stage == DelegatedMaintenanceStage.Released) &&
                Residents.Select(Inventory).SequenceEqual(inventory) && Residents.Select(Biology).SequenceEqual(biological) && Spent(core).SequenceEqual(spent),
                "After Stop both exact original residents return to the same original caravan with surviving food while every consumed food witness stays sealed");
            CheckOriginals(core); CheckProofs(service,core); CheckSameDay(service,core); Save(service,core,"INH_MaintenanceStopped");
        }
        private void Gap(ColonyService service,CoreWorldComponent core)
        {
            Assert(Today > int.Parse(ids[9],CultureInfo.InvariantCulture)+1 && Plans(service).All(p => p.Stage == DelegatedMaintenanceStage.Held && p.Problem == "DayGap" && p.NextDay == int.Parse(ids[9],CultureInfo.InvariantCulture)+1) && Rations(core).Length == 2,
                "A deliberately skipped-clock save observes an unpaid gap and holds both originals without issuing any past or current food charge");
            CheckSameDay(service,core); Save(service,core,"INH_MaintenanceGapHeld");
        }
        private void Render(ColonyService service,CoreWorldComponent core)
        {
            string language=Has("render-japanese") || Has("japanese") ? "Japanese" : "English"; string path=Path.Combine(GenFilePaths.SaveDataFolderPath,"DelegatedMaintenance-"+language+".png");
            if (window == null)
            {
                Assert(LanguageDatabase.activeLanguage.folderName.StartsWith(language,StringComparison.Ordinal),"Maintenance window uses the requested game language");
                File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"translation-errors-"+language+".txt"),LanguageDatabase.activeLanguage.loadErrors.Concat(LanguageDatabase.activeLanguage.defInjections.SelectMany(p => p.loadErrors)).ToArray());
                if (File.Exists(path)) File.Delete(path); window=new DelegatedMaintenanceWindow(service); window.SelectCity(ids[1]); Find.WindowStack.Add(window); return;
            }
            frames++;
            if (!captured && window.Rendered && frames > 30) { ScreenCapture.CaptureScreenshot(path); captured=true; }
            if (captured && File.Exists(path) && new FileInfo(path).Length > 1000) { CheckOriginals(core); CheckSameDay(service,core); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
        }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current; Find.TickManager.Pause(); Assert(core.IsReady && service != null,"Core and Colony initialize the daily maintenance checkpoint");
                if (Has("start") || Has("fresh-start")) { Start(service,core); return; }
                if (!restored) Restore(service,core);
                if (Has("pending-stop-prepared") || Has("pending-stop-applied")) PendingStop(service,core);
                else if (Has("pending-recover")) PendingRecover(service,core);
                else if (Has("reentry")) Reentry(service,core);
                else if (Has("day")) Day(service,core);
                else if (Has("shortage")) Shortage(service,core);
                else if (Has("resume")) Resume(service,core,false);
                else if (Has("stop")) Stop(service,core);
                else if (Has("gap")) Gap(service,core);
                else if (Has("gap-resume")) Resume(service,core,true);
                else if (Has("render") || Has("render-japanese")) Render(service,core);
                else
                {
                    Assert(Residents.All(p => InCaravan(p,caravan)) && Plans(service).All(p => p.Stage == DelegatedMaintenanceStage.Released),"Final daily maintenance reload retains only the original returned native residents and released plan history");
                    int count=core.Ledger.EntityCount;
                    for (int i=0;i<2;i++) { service.StopDelegatedMaintenance("maintenance-stop-"+i,ids[10+i]); Refuse(() => service.ResumeDelegatedMaintenance("maintenance-lost-resident-"+i,ids[10+i]),"A released original residence cannot silently restart its old daily plan"); }
                    foreach (var ration in Rations(core)) { core.Rations.Apply(ration.Id); core.Rations.Commit(ration.Id); }
                    Assert(core.Ledger.EntityCount == count,"Historical plan controls and food certificates remain idempotent after original resident release");
                    CheckSameDay(service,core); CheckOriginals(core); CheckProofs(service,core); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
                }
            }
            catch (Exception error) { done=true; Log.Error("[INH-MAINTENANCE] FAIL "+error); Application.Quit(1); }
        }
    }
}
