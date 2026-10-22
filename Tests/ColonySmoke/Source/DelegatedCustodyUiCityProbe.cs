using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    // Root owns fixture cloning. This probe never changes XML or the native caravan tile.
    public sealed class DelegatedCustodyUiCityProbe : GameComponent
    {
        private const string PendingSave="INH_MaintenancePendingAppliedStopped",PreparedSave="INH_DelegatedUiCitySetupPrepared",AppliedSave="INH_DelegatedUiCitySetupApplied";
        private const string HumanCause="maintenance-pending-probe",AnimalReleaseCause="delegated-ui-city-animal-release",RecoveredSave="INH_DelegatedUiCityRecovered";
        private bool done;
        private string[] ids;
        private Pawn human,animal,escort;
        private Caravan caravan;
        private DelegatedCustodyWindow window;
        private bool captured;
        private int renderFrames;
        private string renderStamp,renderPayload;
        private string[] renderStates,renderInventories,renderOwners,renderReceipts,renderSpent,renderCustody;
        public DelegatedCustodyUiCityProbe(Game game) { }
        private static bool Has(string suffix) { return GenCommandLine.CommandLineArgPassed("inh-delegated-ui-city-"+suffix); }
        private static bool Selected { get { return Has("setup-prepared") || Has("setup-applied") || Has("lock") || Has("reload"); } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private Pawn[] Party { get { return new[] {human,animal,escort}; } }
        private Pawn[] Residents { get { return new[] {human,animal}; } }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-delegated-ui-city-"+key+".txt"); }
        private static string Backup(string name,string key,bool city)
        { return Path.Combine(GenFilePaths.SaveDataFolderPath,name+(city ? "-delegated-ui-city-" : "-maintenance-")+key+".txt"); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-DELEGATED-UI-CITY] PASS "+message); }
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
            // Chronological age follows the deliberately cloned global day. Native biological age must stay frozen.
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
            long delta=long.Parse(actual[5],CultureInfo.InvariantCulture)-long.Parse(old[5],CultureInfo.InvariantCulture);
            return actual.Where((s,i) => i != 5).SequenceEqual(old.Where((s,i) => i != 5)) && delta >= 0 && delta <= 3;
        }
        private static Pawn Original(CoreWorldComponent core,string native)
        { return core.GetDirectlyHeldThings().Cast<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns)).Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().Single(p => p.GetUniqueLoadID() == native); }
        private static bool Held(Pawn pawn,CoreWorldComponent core)
        { return !pawn.Spawned && pawn.holdingOwner == core.GetDirectlyHeldThings() && core.GetDirectlyHeldThings().Contains(pawn) && !Find.WorldPawns.Contains(pawn) && !Find.WorldObjects.Caravans.Any(c => c.PawnsListForReading.Contains(pawn)); }
        private static bool InCaravan(Pawn pawn,Caravan caravan)
        { return !pawn.Spawned && pawn.holdingOwner == caravan.GetDirectlyHeldThings() && caravan.PawnsListForReading.Contains(pawn) && Find.WorldPawns.Contains(pawn) && Find.WorldObjects.Caravans.Count(c => c.PawnsListForReading.Contains(pawn)) == 1; }
        private string Owner(Pawn pawn,CoreWorldComponent core)
        { return Held(pawn,core) ? "Core" : InCaravan(pawn,caravan) ? caravan.GetUniqueLoadID() : "Unexpected"; }
        private static void Enabled(bool value)
        {
            var field=typeof(ColonyMod).GetField("settings",BindingFlags.NonPublic|BindingFlags.Static); var settings=field.GetValue(null);
            settings.GetType().GetField("delegatedEnabled",BindingFlags.Public|BindingFlags.Instance).SetValue(settings,value);
            Assert(ColonyMod.DelegatedEnabled == value && ColonyMod.DelegatedAllowed == value,"The city probe uses the ordinary management setting without a development bypass");
        }
        private static string Receipt(RationDebitSnapshot r)
        { return string.Join("|",new[] {r.Id,r.Actor,r.Cause,r.EntityId,r.NativeId,r.CityId,r.StoreTransferId,r.Day.ToString(CultureInfo.InvariantCulture),Number(r.Demand),Number(r.PaidNutrition),r.Stage.ToString(),r.AppliedReceiptId,
            string.Join(";",r.Lines.Select(l => l.SourceNativeId+","+l.SpentNativeId+","+l.DefName+","+l.StuffDef+","+l.HitPoints+","+l.BeforeCount+","+l.UsedCount+","+l.AfterCount+","+Number(l.UnitNutrition)).ToArray())}); }
        private RationDebitSnapshot[] Rations(CoreWorldComponent core)
        { return core.Rations.Snapshots.Where(r => r.EntityId == ids[5] || r.EntityId == ids[6]).OrderBy(r => r.EntityId,StringComparer.Ordinal).ThenBy(r => r.Day).ToArray(); }
        private string[] Spent(CoreWorldComponent core)
        { return core.Rations.GetDirectlyHeldThings().Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray(); }
        private string[] History(CoreWorldComponent core)
        { return core.ReadPawnCustodyTransfers(ColonyService.Actor).OrderBy(t => t.Id,StringComparer.Ordinal).Select(t =>
            string.Join("|",new[] {t.Id,t.Actor,t.Cause,t.EntityId,t.NativeId,t.CityId,t.CaravanId,t.Tile.ToString(CultureInfo.InvariantCulture),t.IsRelease.ToString(),t.Stage.ToString()})).ToArray(); }
        private static string IdStamp(CoreWorldComponent core)
        { return core.Header.NextSequence.ToString(CultureInfo.InvariantCulture)+"|"+string.Join(";",core.Ledger.Entities.Select(e => e.Id).Concat(core.Ledger.Operations.Select(o => o.Id)).OrderBy(s => s,StringComparer.Ordinal).ToArray()); }
        private PawnCustodySnapshot Transfer(CoreWorldComponent core)
        { return core.ReadPawnCustodyTransfers(ColonyService.Actor).Single(t => t.Id == ids[16]); }
        private static string SavedEscortBinding(string native)
        {
            // The maintenance fixture's physical departure already registered its escort.
            // Read that original saved identity rather than assuming the escort was unregistered.
            var saved=new XmlDocument(); saved.XmlResolver=null;
            saved.Load(Path.Combine(GenFilePaths.SaveDataFolderPath,"Saves",PendingSave+".rws"));
            var rows=saved.SelectNodes("//inheritanceLedger/entities/li").Cast<XmlNode>()
                .Where(n => n.SelectSingleNode("binding") != null && n.SelectSingleNode("binding").InnerText == native).ToArray();
            Assert(rows.Length <= 1,"The original maintenance save contains at most one permanent escort binding");
            return rows.Length == 0 ? "" : rows[0].SelectSingleNode("id").InnerText;
        }
        private bool EscortBindingPreserved(CoreWorldComponent core)
        {
            var rows=core.Ledger.Entities.Where(e => e.BoundThingLoadId == ids[17]).ToArray();
            return string.IsNullOrEmpty(ids[19]) ? rows.Length == 0 : rows.Length == 1 && rows[0].Id == ids[19] &&
                rows[0].Provider == ColonyService.Actor && rows[0].Kind == "person" && rows[0].Status == EntityStatus.Active &&
                rows[0].Authority == SimulationAuthority.Physical && rows[0].IdentityKey == "native-person/"+ids[17];
        }
        private void Bind(CoreWorldComponent core,bool own)
        {
            if (own) ids=File.ReadAllLines(Backup(Has("reload") ? RecoveredSave : Has("pending-applied") ? AppliedSave : PreparedSave,"ids",true));
            else ids=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-maintenance-ids.txt")).Concat(new[] {"","","",""}).ToArray();
            human=Original(core,ids[3]); animal=Original(core,ids[4]); caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[2]);
            escort=caravan.PawnsListForReading.Single(p => p.IsColonist && p != human); ids[17]=escort.GetUniqueLoadID();
            if (!own) ids[19]=SavedEscortBinding(ids[17]);
            Assert(ids.Length == 20,"The city fixture metadata includes the original saved escort binding identity");
            Assert(!caravan.pather.Moving && InCaravan(escort,caravan) && caravan.PawnsListForReading.Count(p => p.IsColonist) == 1,
                "The city fixture has the sole original escort in the same stopped anchor while the human resident stays held");
            Assert(core.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion && core.Header.SaveScopeId == ids[0] && core.Ledger.Entities.Count(e => e.Kind == "person" || e.Kind == "animal") == int.Parse(ids[12],CultureInfo.InvariantCulture) &&
                EscortBindingPreserved(core) && Held(human,core) && Owner(animal,core) != "Unexpected" &&
                core.Ledger.GetEntity(ids[5]).BoundThingLoadId == ids[3] && core.Ledger.GetEntity(ids[6]).BoundThingLoadId == ids[4] &&
                Residents.All(p => core.Ledger.Entities.Count(e => e.BoundThingLoadId == p.GetUniqueLoadID()) == 1),
                "Both permanent resident identities and the original saved escort binding are preserved exactly once in their original Core7 world");
        }
        private void NativeBaseline(CoreWorldComponent core,string name,bool own)
        {
            var pawns=own ? Party : Residents; string key=own ? "states" : "biology"; var old=File.ReadAllLines(Backup(name,key,own));
            Assert(pawns.Select((p,i) => SameSavedBiology(p,old[i])).All(v => v) && pawns.Select(Inventory).SequenceEqual(File.ReadAllLines(Backup(name,"inventories",own))) &&
                Rations(core).Select(Receipt).SequenceEqual(File.ReadAllLines(Backup(name,"receipts",own))) && Spent(core).SequenceEqual(File.ReadAllLines(Backup(name,"spent",own))),
                "Checkpoint load preserves original biological ages, skills, health, genes, needs, native food quantities and every original food-proof and spent ID");
            if (own) Assert(Party.Select(p => Owner(p,core)).SequenceEqual(File.ReadAllLines(Backup(name,"owners",true))) && History(core).SequenceEqual(File.ReadAllLines(Backup(name,"custody",true))),
                "The existing animal custody intent and its exact native endpoint survive the proof checkpoint clone");
        }
        private void Unchanged(ColonyService service,CoreWorldComponent core,Action action,string message)
        {
            string stamp=IdStamp(core),payload=service.SaveSnapshot(core); var states=Party.Select(Biology).ToArray(); var inventories=Party.Select(Inventory).ToArray();
            var owners=Party.Select(p => Owner(p,core)).ToArray(); var ration=Rations(core).Select(Receipt).ToArray(); var spent=Spent(core); var custody=History(core);
            action();
            Assert(IdStamp(core) == stamp && service.SaveSnapshot(core) == payload && Party.Select(Biology).SequenceEqual(states) && Party.Select(Inventory).SequenceEqual(inventories) &&
                Party.Select(p => Owner(p,core)).SequenceEqual(owners) && Rations(core).Select(Receipt).SequenceEqual(ration) && Spent(core).SequenceEqual(spent) && History(core).SequenceEqual(custody),message);
        }
        private void Save(ColonyService service,CoreWorldComponent core,string name)
        {
            var data=new Dictionary<string,string[]> {{"ids",ids},{"count",new[] {core.Ledger.EntityCount.ToString(CultureInfo.InvariantCulture)}},
                {"states",Party.Select(Biology).ToArray()},{"inventories",Party.Select(Inventory).ToArray()},{"owners",Party.Select(p => Owner(p,core)).ToArray()},
                {"receipts",Rations(core).Select(Receipt).ToArray()},{"spent",Spent(core)},{"colony",new[] {service.SaveSnapshot(core)}},{"custody",History(core)}};
            foreach (var entry in data) { File.WriteAllLines(PathFor(entry.Key),entry.Value); File.Copy(PathFor(entry.Key),Backup(name,entry.Key,true),true); }
            GameDataSaveLoader.SaveGame(name); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private void Setup(ColonyService service,CoreWorldComponent core)
        {
            Bind(core,false); Enabled(false); NativeBaseline(core,PendingSave,false);
            int before=int.Parse(File.ReadAllLines(Backup(PendingSave,"count",false))[0],CultureInfo.InvariantCulture);
            Assert(core.Ledger.EntityCount == before && Held(animal,core) && !core.Ledger.HasPendingReference(ids[1]) && !core.Rations.HasPending(ids[6]),
                "Setup starts before the human proof is cloned pending, with a freely releasable original animal");
            var states=Party.Select(Biology).ToArray(); var inventory=Party.Select(Inventory).ToArray(); var spent=Spent(core); var ration=Rations(core).Select(Receipt).ToArray();
            var prepared=core.PawnCustody.PrepareCaravanRelease(ColonyService.Actor,AnimalReleaseCause,ids[6],ids[1],caravan,caravan.Tile.tileId); ids[16]=prepared.Id;
            if (Has("setup-applied")) core.PawnCustody.ApplyNative(prepared.Id);
            var transfer=Transfer(core); ids[18]=transfer.Stage.ToString();
            Assert(core.Ledger.EntityCount == before+2 && transfer.Stage == (Has("setup-applied") ? PawnCustodyStage.Applying : PawnCustodyStage.Prepared) &&
                transfer.IsRelease && transfer.NativeId == animal.GetUniqueLoadID() && transfer.CaravanId == caravan.GetUniqueLoadID() &&
                (Has("setup-applied") ? InCaravan(animal,caravan) : Held(animal,core)) && Party.Select(Biology).SequenceEqual(states) && Party.Select(Inventory).SequenceEqual(inventory) &&
                Spent(core).SequenceEqual(spent) && Rations(core).Select(Receipt).SequenceEqual(ration),
                "Setup adds only the original animal release intent's two IDs and preserves every native item and existing human food checkpoint");
            Save(service,core,Has("setup-applied") ? AppliedSave : PreparedSave);
        }
        private void Locked(ColonyService service,CoreWorldComponent core)
        {
            bool pending=Has("pending-prepared") || Has("pending-applied"),applying=Has("applying"); Bind(core,pending); Enabled(true);
            string source=pending ? Has("pending-applied") ? AppliedSave : PreparedSave : PendingSave;
            int before=int.Parse(File.ReadAllLines(Backup(source,"count",pending))[0],CultureInfo.InvariantCulture); service.Tick();
            Assert(core.Ledger.EntityCount == before+2 && core.Ledger.HasPendingReference(ids[1]) && service.DelegatedMaintenance.Single(p => p.Id == ids[10]).Stage == DelegatedMaintenanceStage.Stopped &&
                service.DelegatedMaintenance.Single(p => p.Id == ids[11]).Stage == DelegatedMaintenanceStage.Held && service.DelegatedMaintenance.Single(p => p.Id == ids[11]).Problem == "Pending",
                "The ordinary paused update adds only the animal Pending Hold control while the stopped human's original second food proof locks the city");
            NativeBaseline(core,source,pending);
            var humanDebit=Rations(core).Single(r => r.Cause == HumanCause); var proof=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+HumanCause);
            Assert(humanDebit.Stage == RationDebitStage.Applied && proof.Stage == OperationStage.Prepared && proof.ResultId == humanDebit.AppliedReceiptId && proof.RecoveryCount == (applying ? 1 : 0),
                "The same existing human second food proof is Prepared with its original ID and expected interrupted-Applying recovery count");
            var commands=new DelegatedCustodyCommandController(service);
            Unchanged(service,core,delegate {
                var candidates=service.AvailableDelegatedStorePawns(ids[1],caravan); var residents=service.DelegatedCustodyResidents(ids[1]);
                Assert(candidates.Any(r => r.Pawn == escort) && candidates.All(r => r.Problem == "CityLocked" && !r.CanStore) &&
                    residents.Length == 2 && residents.All(r => r.Problem == "CityLocked" && !r.CanRelease),
                    "Ordinary candidate and resident rows report CityLocked before escort, pending-food or native-registration checks");
                RefuseBecause(() => commands.Store(ids[1],caravan,escort),"Delegated city is locked before individual registration",
                    "The ordinary Store controller refuses before touching the sole original escort's saved individual registration");
                RefuseBecause(() => commands.Release(ids[1],ids[6],caravan),"Delegated city is locked before release preparation",
                    "The ordinary Release controller refuses another original animal before any release-intent allocation");
                if (!pending)
                    RefuseBecause(() => core.PawnCustody.PrepareCaravanRelease(ColonyService.Actor,"delegated-ui-city-core-new-release",ids[6],ids[1],caravan,caravan.Tile.tileId),
                        "Custody city is locked before intent allocation","Core independently rejects a valid new animal release before requesting a new operation ID");
                else
                {
                    var row=service.DelegatedCustodyPending(ids[1]).Single(r => r.Id == ids[16]);
                    Assert(row.Problem == "CityLocked" && !row.CanResume && row.IsRelease && row.Cause == AnimalReleaseCause && row.Stage == ids[18],
                        "The original Prepared or Applying animal release remains visible with its exact pending identity and locked recovery reason");
                    RefuseBecause(() => core.PawnCustody.ApplyNative(ids[16]),"Custody city is locked before native transfer",
                        "Core native Apply refuses the existing animal intent before moving any original");
                    RefuseBecause(() => core.PawnCustody.Commit(ids[16]),"Custody city is locked before transfer commit",
                        "Core Commit refuses the same animal intent before altering its resident or operation state");
                    RefuseBecause(() => commands.Resume(ids[16]),"Custody city is locked before native transfer",
                        "The ordinary Resume controller keeps the existing animal checkpoint intact while the shared city is locked");
                }
            },"CityLocked refusals preserve every Core ID and allocator value, Colony record, native owner, possession, food witness and biological state");
            string stamp=IdStamp(core); var states=Party.Select(Biology).ToArray(); var inventories=Party.Select(Inventory).ToArray(); var owners=Party.Select(p => Owner(p,core)).ToArray(); var spentBefore=Spent(core); var custody=History(core);
            service.ResumeDelegatedRations(humanDebit.Id); service.ResumeDelegatedRations(humanDebit.Id);
            var recovered=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+HumanCause);
            Assert(IdStamp(core) == stamp && recovered.Id == proof.Id && recovered.ResultId == proof.ResultId && recovered.Stage == OperationStage.Committed && recovered.RecoveryCount == (applying ? 1 : 0) &&
                !core.Ledger.HasPendingReference(ids[1]) && Rations(core).Single(r => r.Id == humanDebit.Id).Stage == RationDebitStage.Committed &&
                Party.Select(Biology).SequenceEqual(states) && Party.Select(Inventory).SequenceEqual(inventories) && Party.Select(p => Owner(p,core)).SequenceEqual(owners) &&
                Spent(core).SequenceEqual(spentBefore) && History(core).SequenceEqual(custody),
                "Explicit food recovery commits the same second proof IDs and unlocks the city without resplitting food or moving a native original");
            Unchanged(service,core,delegate { service.Tick(); service.Tick(); },"Unlocked city updates do not automatically retry the held plan or existing custody intent");
            Assert(service.AvailableDelegatedStorePawns(ids[1],caravan).Single(r => r.Pawn == escort).Problem == "NoEscort" && EscortBindingPreserved(core),
                "After unlocking, the sole original escort is correctly unavailable for NoEscort and retains its exact original saved binding");
            if (pending)
            {
                Assert(service.DelegatedCustodyPending(ids[1]).Single(r => r.Id == ids[16]).CanResume,"The original animal release becomes recoverable at the same stopped anchor after unlocking");
                commands.Resume(ids[16]); commands.Resume(ids[16]);
                Assert(IdStamp(core) == stamp && Transfer(core).Id == ids[16] && Transfer(core).Stage == PawnCustodyStage.Committed && InCaravan(animal,caravan) && Held(human,core) &&
                    Party.Select(Biology).SequenceEqual(states) && Party.Select(Inventory).SequenceEqual(inventories) && Spent(core).SequenceEqual(spentBefore) &&
                    service.DelegatedCustodyResidents(ids[1]).Length == 1 && service.DelegatedCustodyPending(ids[1]).Length == 0 &&
                    service.DelegatedMaintenance.Single(p => p.Id == ids[11]).Stage == DelegatedMaintenanceStage.Released,
                    "Explicit custody recovery completes that exact original animal intent with zero new IDs, unchanged native items and no second release");
            }
            else Assert(service.DelegatedCustodyResidents(ids[1]).All(r => r.CanRelease) && Held(animal,core),"Clean unlocked residents become available for ordinary return without any hidden new custody operation");
            Unchanged(service,core,delegate { if (pending) commands.Resume(ids[16]); service.Tick(); },"Completed recovery replay changes no food, ID, original resident or paused maintenance history");
            Assert(service.DelegatedMaintenance.Single(p => p.Id == ids[10]).Stage == DelegatedMaintenanceStage.Stopped,"Food and animal recovery never restart the stopped human's daily maintenance");
            Enabled(false); Save(service,core,RecoveredSave);
        }
        private void Reload(ColonyService service,CoreWorldComponent core)
        {
            Bind(core,true); Enabled(false); NativeBaseline(core,RecoveredSave,true);
            Assert(core.Ledger.EntityCount == int.Parse(File.ReadAllLines(Backup(RecoveredSave,"count",true))[0],CultureInfo.InvariantCulture) &&
                service.SaveSnapshot(core) == string.Join(Environment.NewLine,File.ReadAllLines(Backup(RecoveredSave,"colony",true))),
                "The recovered ordinary UI city state reloads with its exact saved proof count and complete immutable Colony payload");
            Unchanged(service,core,delegate {
                var r=Rations(core).Single(d => d.Cause == HumanCause); service.ResumeDelegatedRations(r.Id);
                if (!string.IsNullOrEmpty(ids[16])) new DelegatedCustodyCommandController(service).Resume(ids[16]); service.Tick();
            },"Separate-process recovered food and custody replay allocates nothing and never returns sealed spent food");
            done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private void Render(ColonyService service,CoreWorldComponent core)
        {
            string language=Has("render-japanese") ? "Japanese" : "English";
            string path=Path.Combine(GenFilePaths.SaveDataFolderPath,"DelegatedCustodyUiCity-"+language+".png");
            if (window == null)
            {
                Assert(core.IsReady && service != null && Has("lock") && !Has("pending-prepared") && !Has("pending-applied") && !Has("applying"),
                    "CityLocked rendering selects the original clean Prepared food-proof fixture without native recovery");
                Assert(!GenCommandLine.CommandLineArgPassed("inh-delegated-custody"),"The city-lock render probe has no development storage bypass");
                Assert(LanguageDatabase.activeLanguage.folderName.StartsWith(language,StringComparison.Ordinal),"The CityLocked screen uses the requested native game language");
                Bind(core,false); Enabled(true); service.Tick();
                int before=int.Parse(File.ReadAllLines(Backup(PendingSave,"count",false))[0],CultureInfo.InvariantCulture);
                var held=service.DelegatedMaintenance.Single(p => p.Id == ids[11]);
                Assert(core.Ledger.EntityCount == before+2 && held.Stage == DelegatedMaintenanceStage.Held && held.Problem == "Pending" &&
                    service.DelegatedMaintenance.Single(p => p.Id == ids[10]).Stage == DelegatedMaintenanceStage.Stopped && core.Ledger.HasPendingReference(ids[1]),
                    "The ordinary paused render update retains the city lock and adds only its independent animal Hold control");
                NativeBaseline(core,PendingSave,false);
                var candidates=service.AvailableDelegatedStorePawns(ids[1],caravan); var residents=service.DelegatedCustodyResidents(ids[1]);
                Assert(candidates.Any(r => r.Pawn == escort) && candidates.All(r => r.Problem == "CityLocked" && !r.CanStore) &&
                    residents.Length == 2 && residents.All(r => r.Problem == "CityLocked" && !r.CanRelease),
                    "Every original candidate and resident row displays CityLocked with its unsafe action disabled");
                var proof=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+HumanCause);
                Assert(proof.Stage == OperationStage.Prepared && proof.RecoveryCount == 0 && Rations(core).Single(r => r.Cause == HumanCause).Stage == RationDebitStage.Applied,
                    "Rendering keeps the same human second proof Prepared and never resumes its native-food checkpoint");
                var formatter=typeof(DelegatedCustodyWindow).GetMethod("Problem",BindingFlags.NonPublic|BindingFlags.Static);
                string expected="INH_DelegatedProblem_CityLocked".Translate().ToString();
                string actual=formatter == null ? null : (string)formatter.Invoke(null,new object[] {"CityLocked"});
                Assert(formatter != null && expected != "INH_DelegatedProblem_CityLocked" && actual == expected &&
                    actual != "INH_DelegatedProblem_Unknown".Translate().ToString(),
                    "The actual window maps CityLocked to its exact translated explanation instead of the Unknown fallback");
                File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"delegated-ui-city-translation-errors-"+language+".txt"),
                    LanguageDatabase.activeLanguage.loadErrors.Concat(LanguageDatabase.activeLanguage.defInjections.SelectMany(p => p.loadErrors)).ToArray());
                if (File.Exists(path)) File.Delete(path);
                renderStamp=IdStamp(core); renderPayload=service.SaveSnapshot(core); renderStates=Party.Select(Biology).ToArray(); renderInventories=Party.Select(Inventory).ToArray();
                renderOwners=Party.Select(p => Owner(p,core)).ToArray(); renderReceipts=Rations(core).Select(Receipt).ToArray(); renderSpent=Spent(core); renderCustody=History(core);
                window=new DelegatedCustodyWindow(service); window.SelectCity(ids[1]); window.SelectCaravan(caravan); Find.WindowStack.Add(window); return;
            }
            renderFrames++;
            if (renderFrames > 1800) throw new InvalidOperationException("CityLocked screen did not repaint and capture in time");
            if (!captured && window.Rendered && renderFrames > 30) { ScreenCapture.CaptureScreenshot(path); captured=true; }
            if (captured && File.Exists(path) && new FileInfo(path).Length > 1000)
            {
                Assert(IdStamp(core) == renderStamp && service.SaveSnapshot(core) == renderPayload && Party.Select(Biology).SequenceEqual(renderStates) &&
                    Party.Select(Inventory).SequenceEqual(renderInventories) && Party.Select(p => Owner(p,core)).SequenceEqual(renderOwners) &&
                    Rations(core).Select(Receipt).SequenceEqual(renderReceipts) && Spent(core).SequenceEqual(renderSpent) && History(core).SequenceEqual(renderCustody) &&
                    EscortBindingPreserved(core) && core.Ledger.HasPendingReference(ids[1]),
                    "CityLocked repaint and screenshot preserve all original IDs, food, sealed witnesses, biology, native owners and pending proof without recovery");
                done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
        }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current; Find.TickManager.Pause();
                if (Has("render") || Has("render-japanese")) { Render(service,core); return; }
                Assert(core.IsReady && service != null,"Core and Colony initialize ordinary custody city-lock validation");
                Assert(!GenCommandLine.CommandLineArgPassed("inh-delegated-custody"),"The city-lock UI probe has no development storage bypass");
                if (Has("setup-prepared") || Has("setup-applied")) Setup(service,core); else if (Has("reload")) Reload(service,core); else Locked(service,core);
            }
            catch (Exception error) { done=true; Log.Error("[INH-DELEGATED-UI-CITY] FAIL "+error); Application.Quit(1); }
        }
    }
}
