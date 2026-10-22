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
    // Five separate real-game processes retain the same native food and pawn identities.
    public sealed class RationDebitProbe : GameComponent
    {
        private const string HumanCause="ration-person-day", AnimalCause="ration-animal-day";
        private static readonly string[] Modes={"prepare","apply","commit","release","reload","checkpoint"};
        private bool done;
        private string[] ids;
        private Pawn human,animal;
        private Caravan caravan;
        public RationDebitProbe(Game game) { }
        private static bool Has(string mode) { return GenCommandLine.CommandLineArgPassed("inh-ration-"+mode); }
        private static bool Selected { get { return GenCommandLine.CommandLineArgPassed("inh-ration-probe") && Modes.Any(Has); } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-ration-"+key+".txt"); }
        private static string BackupPathFor(string name,string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,name+"-ration-"+key+".txt"); }
        private static string Number(float value) { return value.ToString("R",CultureInfo.InvariantCulture); }
        private static string Number(double value) { return value.ToString("R",CultureInfo.InvariantCulture); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-RATION] PASS "+message); }
        private static void Refuse(Action action,string message)
        { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,message); }
        private static void RefuseBecause(Action action,string expected,string message)
        { bool refused=false; try { action(); } catch (InvalidOperationException error) { refused=error.Message == expected; } Assert(refused,message); }
        private static ColonyState State(ColonyService service,CoreWorldComponent core)
        { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core))); }
        private static string Item(Thing thing)
        { return thing.GetUniqueLoadID()+":"+thing.def.defName+":"+(thing.Stuff == null ? "" : thing.Stuff.defName)+":"+thing.stackCount+":"+thing.HitPoints; }
        private static string Inventory(Pawn pawn)
        { return string.Join(";",pawn.inventory.innerContainer.Select(Item).OrderBy(x => x,StringComparer.Ordinal).ToArray()); }
        private static string Biology(Pawn pawn)
        {
            return string.Join("|",new[] {pawn.GetUniqueLoadID(),pawn.def.defName,pawn.Name == null ? "" : pawn.Name.ToStringFull,
                pawn.gender.ToString(),pawn.Faction.GetUniqueLoadID(),pawn.ageTracker.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture),
                pawn.skills == null ? "" : string.Join(",",pawn.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+Number(s.xpSinceLastLevel)).ToArray()),
                string.Join(",",pawn.health.hediffSet.hediffs.Select(h => h.def.defName+":"+Number(h.Severity)+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                pawn.genes == null ? "" : string.Join(",",pawn.genes.GenesListForReading.Select(g => g.def.defName+":"+g.Active).ToArray()),
                pawn.Ideo == null ? "" : pawn.Ideo.GetUniqueLoadID(),
                pawn.needs == null ? "" : string.Join(",",pawn.needs.AllNeeds.OrderBy(n => n.def.defName,StringComparer.Ordinal).Select(n => n.def.defName+":"+Number(n.CurLevel)).ToArray()),
                pawn.story == null ? "" : string.Join(",",pawn.story.traits.allTraits.Select(t => t.def.defName+":"+t.Degree).ToArray()),
                pawn.apparel == null ? "" : string.Join(",",pawn.apparel.WornApparel.Select(a => Item(a)).OrderBy(s => s,StringComparer.Ordinal).ToArray()),
                pawn.equipment == null ? "" : string.Join(",",pawn.equipment.AllEquipmentListForReading.Select(e => Item(e)).OrderBy(s => s,StringComparer.Ordinal).ToArray())});
        }
        private static bool SameSavedBiology(Pawn pawn,string expected)
        {
            var actual=Biology(pawn).Split('|'); var old=expected.Split('|');
            if (actual.Length != old.Length || !actual.Where((s,i) => i != 5).SequenceEqual(old.Where((s,i) => i != 5))) return false;
            long change=long.Parse(actual[5],CultureInfo.InvariantCulture)-long.Parse(old[5],CultureInfo.InvariantCulture);
            return change >= 0 && change <= 3;
        }
        private static Pawn Original(CoreWorldComponent core,string native)
        {
            return core.GetDirectlyHeldThings().Cast<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns))
                .Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Concat(Find.WorldPawns.AllPawnsAliveOrDead)
                .Distinct().Single(p => p.GetUniqueLoadID() == native);
        }
        private Pawn[] Residents { get { return new[] {human,animal}; } }
        private static string AbstractStocks(CoreWorldComponent core)
        { return string.Join(";",core.Ledger.Entities.Where(e => e.Kind == "lot" && e.Authority == SimulationAuthority.Abstract).OrderBy(e => e.Id,StringComparer.Ordinal).Select(e => e.Id+":"+e.Quantity).ToArray()); }
        private static int Individuals(CoreWorldComponent core)
        { return core.Ledger.Entities.Count(e => e.Kind == "person" || e.Kind == "animal"); }
        private static bool Held(Pawn pawn,CoreWorldComponent core)
        { return !pawn.Spawned && pawn.holdingOwner == core.GetDirectlyHeldThings() && core.GetDirectlyHeldThings().Contains(pawn) && !Find.WorldPawns.Contains(pawn) && !Find.WorldObjects.Caravans.Any(c => c.PawnsListForReading.Contains(pawn)); }
        private static bool Travelling(Pawn pawn,Caravan caravan)
        { return !pawn.Spawned && pawn.holdingOwner == caravan.GetDirectlyHeldThings() && caravan.PawnsListForReading.Contains(pawn) && Find.WorldPawns.Contains(pawn) && Find.WorldObjects.Caravans.Count(c => c.PawnsListForReading.Contains(pawn)) == 1; }
        private static string Receipt(RationDebitSnapshot receipt)
        {
            return string.Join("|",new[] {receipt.Id,receipt.Actor,receipt.Cause,receipt.EntityId,receipt.NativeId,receipt.CityId,receipt.StoreTransferId,
                receipt.Day.ToString(CultureInfo.InvariantCulture),Number(receipt.Demand),Number(receipt.PaidNutrition),receipt.Stage.ToString(),receipt.AppliedReceiptId,
                string.Join(";",receipt.Lines.Select(line => string.Join(",",new[] {line.SourceNativeId,line.SpentNativeId,line.DefName,line.StuffDef,
                    line.HitPoints.ToString(CultureInfo.InvariantCulture),line.BeforeCount.ToString(CultureInfo.InvariantCulture),line.UsedCount.ToString(CultureInfo.InvariantCulture),
                    line.AfterCount.ToString(CultureInfo.InvariantCulture),Number(line.UnitNutrition)})).ToArray())});
        }
        private RationDebitSnapshot[] Receipts(CoreWorldComponent core)
        { return new[] {ids[10],ids[11]}.Select(id => core.Rations.Snapshots.Single(r => r.Id == id)).ToArray(); }
        private void CheckRegistry(CoreWorldComponent core)
        {
            Assert(core.Header.SaveScopeId == ids[0] && Individuals(core) == int.Parse(ids[12],CultureInfo.InvariantCulture) && AbstractStocks(core) == ids[13],
                "Maintenance preserves world scope, every permanent individual and all abstract stocks");
            for (int i=0;i<2;i++)
            {
                Assert(core.Ledger.Entities.Count(e => e.BoundThingLoadId == ids[3+i]) == 1 && core.Ledger.GetEntity(ids[5+i]).BoundThingLoadId == ids[3+i] &&
                    ReferenceEquals(Residents[i],Original(core,ids[3+i])),"Resident food maintenance retains the sole original native pawn and Core identity");
            }
            var steel=Residents.Concat(caravan.PawnsListForReading).Distinct().SelectMany(p => p.inventory.innerContainer).Where(t => t.def.defName == "Steel").Select(Item).OrderBy(s => s,StringComparer.Ordinal);
            Assert(string.Join(";",steel.ToArray()) == ids[14],"Existing native steel cargo retains its exact ID, count and durability");
        }
        private void CheckReceipts(CoreWorldComponent core,string stage)
        {
            var receipts=Receipts(core); var spent=core.Rations.GetDirectlyHeldThings().Cast<Thing>().ToArray();
            for (int i=0;i<2;i++)
            {
                var receipt=receipts[i]; var pawn=Residents[i];
                Assert(receipt.Stage.ToString() == stage && receipt.Actor == ColonyService.Actor && receipt.Cause == (i == 0 ? HumanCause : AnimalCause) &&
                    receipt.EntityId == ids[5+i] && receipt.NativeId == ids[3+i] && receipt.CityId == ids[1] && receipt.StoreTransferId == ids[7+i] &&
                    receipt.Day == int.Parse(ids[9],CultureInfo.InvariantCulture),"Food receipt retains the exact actor, resident, original store, city, current prepaid day and phase");
                Assert(receipt.Demand > 0 && Math.Abs(receipt.Demand-pawn.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed,true)*60000f) < 0.00001f &&
                    receipt.PaidNutrition >= receipt.Demand && receipt.Lines.Count() >= 2 && receipt.Lines.Any(l => l.AfterCount == 0) && receipt.Lines.Any(l => l.AfterCount > 0),
                    "The real game food budget is fully paid by a mixture of whole and partial native stacks");
                Assert(Math.Abs(receipt.Lines.Sum(l => l.UsedCount*l.UnitNutrition)-receipt.PaidNutrition) < 0.00001f,"Certified nutrition equals its exact native food portions");
                var applied=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+receipt.Cause);
                if (stage == "Prepared")
                    Assert(string.IsNullOrEmpty(receipt.AppliedReceiptId) && applied == null,"Preparation creates no native spent proof before applying its existing food");
                else
                    Assert(!string.IsNullOrEmpty(receipt.AppliedReceiptId) && receipt.AppliedReceiptId != receipt.Id && applied != null && applied.Stage == OperationStage.Committed &&
                        applied.ResultId == receipt.AppliedReceiptId && core.Ledger.GetEntity(receipt.AppliedReceiptId).Kind == "lease",
                        "Applied native food identities have exactly one distinct committed Core proof and its original operation");
                foreach (var line in receipt.Lines)
                {
                    Assert(line.UsedCount > 0 && line.BeforeCount == line.UsedCount+line.AfterCount && line.UnitNutrition > 0,"Each certified food line conserves its original native count");
                    var original=pawn.inventory.innerContainer.SingleOrDefault(t => t.GetUniqueLoadID() == line.SourceNativeId);
                    if (stage == "Prepared")
                    {
                        Assert(original != null && original.stackCount == line.BeforeCount && string.IsNullOrEmpty(line.SpentNativeId),"Prepared food stays in its sole original inventory without spent allocation");
                    }
                    else
                    {
                        Assert((line.AfterCount == 0 && original == null) || (line.AfterCount > 0 && original != null && original.stackCount == line.AfterCount),"Applied food has precisely its certified surviving original count");
                        var portion=spent.Single(t => t.GetUniqueLoadID() == line.SpentNativeId);
                        Assert(!portion.Destroyed && !portion.Spawned && portion.holdingOwner == core.Rations.GetDirectlyHeldThings() && portion.stackCount == line.UsedCount &&
                            portion.def.defName == line.DefName && (portion.Stuff == null ? "" : portion.Stuff.defName) == (line.StuffDef ?? "") && portion.HitPoints == line.HitPoints,
                            "The exact consumed native food portion is sealed in the Core spent holder with its certified definition, material, durability and count");
                        Assert(line.AfterCount != 0 || line.SpentNativeId == line.SourceNativeId,"Whole-stack debit seals the same original native item ID");
                        Assert(line.AfterCount == 0 || line.SpentNativeId != line.SourceNativeId,"Partial debit preserves its original inventory stack ID and certifies the new split portion ID");
                    }
                }
            }
            Assert(spent.Length == (stage == "Prepared" ? 0 : receipts.Sum(r => r.Lines.Count())) && spent.Sum(t => t.stackCount) == (stage == "Prepared" ? 0 : receipts.Sum(r => r.Lines.Sum(l => l.UsedCount))),
                "The spent holder contains exactly the certified native food and no untracked duplicate or missing portion");
        }
        private void CheckReplay(ColonyService service,CoreWorldComponent core)
        {
            int before=core.Ledger.EntityCount; var records=Receipts(core).Select(Receipt).ToArray(); var inventory=Residents.Select(Inventory).ToArray();
            var spent=core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray();
            for (int i=0;i<2;i++)
            {
                string cause=i == 0 ? HumanCause : AnimalCause;
                Assert(core.Rations.Prepare(ColonyService.Actor,cause,ids[5+i],ids[1],int.Parse(ids[9],CultureInfo.InvariantCulture)).Id == ids[10+i],"Exact Core preparation replay returns its existing day receipt");
                if (records[i].Split('|')[10] != "Prepared") { service.ApplyDelegatedRations(ids[10+i]); service.ApplyDelegatedRations(ids[10+i]); }
                if (records[i].Split('|')[10] == "Committed") { service.CommitDelegatedRations(ids[10+i]); service.ResumeDelegatedRations(ids[10+i]); }
                Refuse(() => core.Rations.Prepare(ColonyService.Actor,cause+"-duplicate",ids[5+i],ids[1],int.Parse(ids[9],CultureInfo.InvariantCulture)),"Another cause cannot charge the same original resident for its already prepaid day");
            }
            Assert(core.Ledger.EntityCount == before && Receipts(core).Select(Receipt).SequenceEqual(records) && Residents.Select(Inventory).SequenceEqual(inventory) &&
                core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).SequenceEqual(spent),"Receipt replay allocates nothing and changes no journal, native inventory or spent food");
        }
        private void RefuseRelease(ColonyService service,CoreWorldComponent core)
        {
            int count=core.Ledger.EntityCount; string colony=service.SaveSnapshot(core); var inventory=Residents.Select(Inventory).ToArray();
            for (int i=0;i<2;i++) Refuse(() => service.ReleaseDelegatedPawn("ration-pending-release-"+i,ids[1],ids[5+i],caravan),"A pending food debit refuses releasing its original resident");
            Assert(core.Ledger.EntityCount == count && service.SaveSnapshot(core) == colony && Residents.All(p => Held(p,core)) && Residents.Select(Inventory).SequenceEqual(inventory),
                "Pending debit release rejection preserves all certificates, residents and native food counts");
        }
        private void CheckCauseCollisions(ColonyService service,CoreWorldComponent core)
        {
            const string rationReason="Cause already belongs to a delegated ration debit";
            const string returnReason="Return cause already belongs to another Colony command";
            int before=core.Ledger.EntityCount; string colony=service.SaveSnapshot(core); var inventory=Residents.Select(Inventory).ToArray();
            var biology=Residents.Select(Biology).ToArray(); var receipts=Receipts(core).Select(Receipt).ToArray();
            var spent=core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray();
            var state=State(service,core); var outward=state.physical.Single(p => p.cause == "delegated-route-outward");
            Pawn escort=caravan.PawnsListForReading.First(p => p.RaceProps.Humanlike && p != human);
            RefuseBecause(() => service.BeginDelegatedReturn(HumanCause,outward.cause),returnReason,"A food cause refuses a return specifically because another Colony command owns its cause");
            RefuseBecause(() => service.StoreLocalPawn(HumanCause,outward.sourceId,escort),rationReason,"A food cause refuses local storage before endpoint validation or native registration");
            RefuseBecause(() => service.ReleaseLocalPawn(HumanCause,outward.sourceId,ids[5]),rationReason,"A food cause refuses local release before endpoint validation or original movement");
            RefuseBecause(() => service.StoreDelegatedPawn(HumanCause,ids[1],caravan,escort),rationReason,"A food cause refuses delegated storage before native registration or resident allocation");
            RefuseBecause(() => service.ReleaseDelegatedPawn(HumanCause,ids[1],ids[5],caravan),rationReason,"A food cause refuses delegated release before moving its resident");
            var steel=Residents.Concat(caravan.PawnsListForReading).Distinct().SelectMany(p => p.inventory.innerContainer).First(t => t.def.defName == "Steel");
            RefuseBecause(() => service.ReservePhysicalTransport(HumanCause,outward.sourceId,ids[1],new[] {human,animal,escort},new[] {new CargoSelection(steel,1)}),
                rationReason,"A food cause refuses physical reservation before source validation or Core allocation");
            var stock=state.stocks.FirstOrDefault(); string lot=stock == null ? ids[5] : stock.lotId;
            RefuseBecause(() => service.ReserveTransport(HumanCause,lot,ids[1],1,Find.TickManager.TicksGame+600),rationReason,
                "A food cause refuses an abstract transport specifically at its common cause guard before lot validation");
            Assert(core.Ledger.EntityCount == before && service.SaveSnapshot(core) == colony && Residents.Select(Inventory).SequenceEqual(inventory) && Residents.Select(Biology).SequenceEqual(biology) &&
                Receipts(core).Select(Receipt).SequenceEqual(receipts) && core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).SequenceEqual(spent),
                "Every cross-command food cause collision preserves Core count, the complete Colony journal, native residents, surviving food, spent food and both immutable proofs");
        }
        private void Save(CoreWorldComponent core,string name)
        {
            File.WriteAllLines(PathFor("ids"),ids); File.WriteAllLines(PathFor("biology"),Residents.Select(Biology).ToArray());
            File.WriteAllLines(PathFor("inventories"),Residents.Select(Inventory).ToArray()); File.WriteAllLines(PathFor("receipts"),Receipts(core).Select(Receipt).ToArray());
            File.WriteAllLines(PathFor("spent"),core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray());
            File.WriteAllText(PathFor("count"),core.Ledger.EntityCount.ToString(CultureInfo.InvariantCulture));
            foreach (string key in new[] {"ids","biology","inventories","receipts","spent","count"}) File.Copy(PathFor(key),BackupPathFor(name,key),true);
            GameDataSaveLoader.SaveGame(name); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private void Restore(CoreWorldComponent core)
        {
            ids=File.ReadAllLines(PathFor("ids")); human=Original(core,ids[3]); animal=Original(core,ids[4]); caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[2]);
            Assert(core.Ledger.EntityCount == int.Parse(File.ReadAllText(PathFor("count")),CultureInfo.InvariantCulture),"Process restart observes existing food certificates without allocating on load");
            var biology=File.ReadAllLines(PathFor("biology"));
            Assert(Residents.Select((p,i) => SameSavedBiology(p,biology[i])).All(v => v) && Residents.Select(Inventory).SequenceEqual(File.ReadAllLines(PathFor("inventories"))),
                "Process restart preserves original resident biology, health, genes, needs and every surviving native inventory stack");
            Assert(Receipts(core).Select(Receipt).SequenceEqual(File.ReadAllLines(PathFor("receipts"))) &&
                core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).SequenceEqual(File.ReadAllLines(PathFor("spent"))),
                "Process restart preserves each saved food phase, receipt ID and sealed native spent portion");
            CheckRegistry(core);
        }
        private static void AddFixtureFood(Pawn pawn,bool person)
        {
            var food=DefDatabase<ThingDef>.GetNamed(person ? "MealSurvivalPack" : "Kibble");
            var stacks=new List<Thing>();
            for (int i=0;i<3;i++)
            {
                var item=ThingMaker.MakeThing(food); item.stackCount=food.stackLimit;
                Assert(item.IngestibleNow && FoodUtility.WillEat(pawn,item,null,true,false) && FoodUtility.NutritionForEater(pawn,item) > 0,"Only accepted real vanilla food is used by the fixture");
                Assert(pawn.inventory.innerContainer.TryAdd(item,false),"Fixture adds an existing native food stack without merging its identity"); stacks.Add(item);
            }
            stacks.OrderBy(t => t.GetUniqueLoadID(),StringComparer.Ordinal).First().stackCount=1;
        }
        private void Prepare(ColonyService service,CoreWorldComponent core)
        {
            var residents=core.ReadPawnCustodyResidents(ColonyService.Actor).Where(r => r.CaravanId != null).ToArray();
            Assert(residents.Length == 2,"Input holds the existing route person and animal in their delegated city");
            var person=residents.Single(r => core.Ledger.GetEntity(r.EntityId).Kind == "person"); var beast=residents.Single(r => core.Ledger.GetEntity(r.EntityId).Kind == "animal");
            human=Original(core,person.NativeId); animal=Original(core,beast.NativeId); caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == person.CaravanId);
            int day=Find.TickManager.TicksGame/60000;
            ids=new[] {core.Header.SaveScopeId,person.CityId,person.CaravanId,person.NativeId,beast.NativeId,person.EntityId,beast.EntityId,person.StoreTransferId,beast.StoreTransferId,
                day.ToString(CultureInfo.InvariantCulture),"","",Individuals(core).ToString(CultureInfo.InvariantCulture),AbstractStocks(core),
                string.Join(";",Residents.Concat(caravan.PawnsListForReading).Distinct().SelectMany(p => p.inventory.innerContainer).Where(t => t.def.defName == "Steel").Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray())};
            Assert(person.CityId == beast.CityId && person.CaravanId == beast.CaravanId && !caravan.pather.Moving && Residents.All(p => Held(p,core)),"Both original residents and the stopped original escort caravan share the saved delegated endpoint");
            int before=core.Ledger.EntityCount; var biology=Residents.Select(Biology).ToArray(); var inventory=Residents.Select(Inventory).ToArray();
            for (int i=0;i<2;i++)
            {
                string cause=i == 0 ? HumanCause : AnimalCause;
                Refuse(() => service.PrepareDelegatedRations(cause,ids[1],ids[5+i]),"Insufficient actual food refuses daily maintenance before any certificate allocation");
                Refuse(() => core.Rations.Prepare(ColonyService.Actor,cause+"-past",ids[5+i],ids[1],day-1),"A past day cannot be purchased with a new native food debit");
                Refuse(() => core.Rations.Prepare(ColonyService.Actor,cause+"-future",ids[5+i],ids[1],day+1),"A future day cannot be purchased with a new native food debit");
            }
            Assert(core.Ledger.EntityCount == before && core.Rations.Snapshots.Count() == 0 && Residents.Select(Biology).SequenceEqual(biology) && Residents.Select(Inventory).SequenceEqual(inventory),
                "Invalid or underfunded preparation leaves no certificate, partial journal, native mutation or replacement individual");
            AddFixtureFood(human,true); AddFixtureFood(animal,false);
            ids[10]=service.PrepareDelegatedRations(HumanCause,ids[1],ids[5]); ids[11]=service.PrepareDelegatedRations(AnimalCause,ids[1],ids[6]);
            Assert(core.Ledger.EntityCount == before+4,"Preparing two daily maintenance charges creates exactly their two original certificates and two operations");
            CheckReceipts(core,"Prepared"); CheckReplay(service,core); RefuseRelease(service,core); CheckRegistry(core); CheckCauseCollisions(service,core);
            Assert(Residents.Select(Biology).SequenceEqual(biology),"Successful preparation does not tick needs, health, age, genes or skills");
            Save(core,"INH_RationPrepared");
        }
        private void Checkpoint(ColonyService service,CoreWorldComponent core)
        {
            bool applying=GenCommandLine.CommandLineArgPassed("inh-ration-checkpoint-applying");
            Assert(applying || GenCommandLine.CommandLineArgPassed("inh-ration-checkpoint-prepared"),"A pending applied-proof recovery checkpoint was explicitly selected");
            int before=core.Ledger.EntityCount; var records=Receipts(core); var pending=records[1];
            var proof=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+AnimalCause);
            Assert(records.All(r => r.Stage == RationDebitStage.Applied) && proof != null && proof.Stage == OperationStage.Prepared &&
                proof.ResultId == pending.AppliedReceiptId && proof.RecoveryCount == (applying ? 1 : 0),
                "Reload observes the existing pending animal native-food proof, restores its original reference and only normalizes an interrupted Applying operation");
            var personProof=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+HumanCause);
            Assert(personProof != null && personProof.Stage == OperationStage.Committed && personProof.ResultId == records[0].AppliedReceiptId,
                "The earlier original human native-food proof remains committed while only the final animal proof awaits recovery");
            var biology=Residents.Select(Biology).ToArray(); var inventory=Residents.Select(Inventory).ToArray();
            var spent=core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray();
            service.ResumeDelegatedRations(ids[11]); service.ResumeDelegatedRations(ids[11]);
            service.ResumeDelegatedRations(ids[10]); service.ResumeDelegatedRations(ids[10]);
            CheckReceipts(core,"Committed"); CheckReplay(service,core); CheckRegistry(core); CheckCauseCollisions(service,core);
            proof=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+AnimalCause);
            Assert(proof.Stage == OperationStage.Committed && proof.ResultId == pending.AppliedReceiptId && proof.RecoveryCount == (applying ? 1 : 0) &&
                core.Ledger.EntityCount == before && Residents.Select(Biology).SequenceEqual(biology) && Residents.Select(Inventory).SequenceEqual(inventory) &&
                core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).SequenceEqual(spent),
                "Explicit pending-proof recovery and repeated resume commit the same proof IDs without reallocating, resplitting, recharging or mutating native originals");
            done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current; Assert(core.IsReady && service != null,"Core and Colony initialize the native delegated food maintenance probe"); Find.TickManager.Pause();
                if (Has("prepare")) { Prepare(service,core); return; }
                Restore(core); var biology=Residents.Select(Biology).ToArray();
                if (Has("checkpoint")) { Checkpoint(service,core); return; }
                if (Has("apply"))
                {
                    CheckReceipts(core,"Prepared"); int count=core.Ledger.EntityCount;
                    foreach (string id in new[] {ids[10],ids[11]}) { service.ApplyDelegatedRations(id); service.ApplyDelegatedRations(id); }
                    Assert(core.Ledger.EntityCount == count+4,"Applying two charges creates exactly two new native-portion proof certificates and their operations");
                    CheckReceipts(core,"Applied"); CheckReplay(service,core); RefuseRelease(service,core); CheckRegistry(core); CheckCauseCollisions(service,core);
                    Assert(Residents.Select(Biology).SequenceEqual(biology),"Native debit moves food portions while resident health, needs and biological age remain frozen");
                    Save(core,"INH_RationApplied"); return;
                }
                if (Has("commit"))
                {
                    CheckReceipts(core,"Applied"); int before=core.Ledger.EntityCount;
                    foreach (string id in new[] {ids[10],ids[11]}) { service.CommitDelegatedRations(id); service.CommitDelegatedRations(id); }
                    Assert(core.Ledger.EntityCount == before,"Committing both native food charges preserves their existing preparation and application proofs without allocation");
                    CheckReceipts(core,"Committed"); CheckReplay(service,core); CheckRegistry(core); CheckCauseCollisions(service,core);
                    var inventory=Residents.Select(Inventory).ToArray(); var spent=core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray(); int count=core.Ledger.EntityCount;
                    for (int tick=0;tick<1200;tick++) Find.TickManager.DoSingleTick(); Find.TickManager.Pause();
                    Assert(Residents.Select(Biology).SequenceEqual(biology) && Residents.Select(Inventory).SequenceEqual(inventory) &&
                        core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).SequenceEqual(spent) && core.Ledger.EntityCount == count,
                        "1200 ordinary game ticks keep stored biological ages, needs, health, inventories and sealed food unchanged without a second food charge");
                    Save(core,"INH_RationCommitted"); return;
                }
                if (Has("release"))
                {
                    CheckReceipts(core,"Committed"); CheckReplay(service,core);
                    for (int i=0;i<2;i++) { service.ReleaseDelegatedPawn("ration-release-"+i,ids[1],ids[5+i],caravan); service.ReleaseDelegatedPawn("ration-release-"+i,ids[1],ids[5+i],caravan); }
                    Assert(Residents.All(p => Travelling(p,caravan)) && !core.ReadPawnCustodyResidents(ColonyService.Actor).Any(r => r.EntityId == ids[5] || r.EntityId == ids[6]),"Committed daily maintenance allows releasing both exact original residents into the original native caravan");
                    CheckReceipts(core,"Committed"); CheckReplay(service,core); CheckRegistry(core); CheckCauseCollisions(service,core);
                    Assert(Residents.Select(Biology).SequenceEqual(biology),"Release and historical food replay do not alter original biology, needs or health");
                    Save(core,"INH_RationReleased"); return;
                }
                CheckReceipts(core,"Committed"); CheckReplay(service,core); CheckRegistry(core); CheckCauseCollisions(service,core);
                Assert(Residents.All(p => Travelling(p,caravan)),"Final reload retains both released original individuals and their surviving native food exclusively in the original caravan");
                done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception error) { done=true; Log.Error("[INH-RATION] FAIL "+error); Application.Quit(1); }
        }
    }
}
