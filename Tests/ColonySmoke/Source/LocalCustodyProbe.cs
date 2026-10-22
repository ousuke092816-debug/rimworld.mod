using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    public sealed class LocalCustodyProbe : GameComponent
    {
        private bool done,captured;
        private int frames;
        private CustodyWindow window;
        public LocalCustodyProbe(Game game) { }
        private static bool Selected
        { get { return GenCommandLine.CommandLineArgPassed("inh-local-custody-save") || GenCommandLine.CommandLineArgPassed("inh-local-custody-load") || GenCommandLine.CommandLineArgPassed("inh-local-custody-final") || GenCommandLine.CommandLineArgPassed("inh-local-custody-render") ||
            GenCommandLine.CommandLineArgPassed("inh-local-custody-pending-save") || GenCommandLine.CommandLineArgPassed("inh-local-custody-pending-load") || GenCommandLine.CommandLineArgPassed("inh-local-custody-pending-final"); } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-local-custody-"+key+".txt"); }
        private static void Assert(bool value,string text)
        { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-LOCAL-CUSTODY] PASS "+text); }
        private static void Refuse(Action action,string text)
        { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,text); }
        private static ColonyState State(ColonyService service,CoreWorldComponent core)
        { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core))); }
        private static string NativeItem(Thing thing)
        { return thing.GetUniqueLoadID()+":"+thing.def.defName+":"+(thing.Stuff == null ? "" : thing.Stuff.defName)+":"+thing.stackCount+":"+thing.HitPoints; }
        private static string Items(Pawn pawn)
        {
            return string.Join(",",pawn.inventory.innerContainer.Cast<Thing>().Concat(pawn.apparel == null ? new Thing[0] : pawn.apparel.WornApparel.Cast<Thing>())
                .Concat(pawn.equipment == null ? new Thing[0] : pawn.equipment.AllEquipmentListForReading.Cast<Thing>()).Select(NativeItem).OrderBy(s => s,StringComparer.Ordinal).ToArray());
        }
        private static string PawnState(Pawn pawn)
        {
            return string.Join("|",new[] {pawn.GetUniqueLoadID(),pawn.def.defName,pawn.Name == null ? "" : pawn.Name.ToStringFull,pawn.gender.ToString(),pawn.Faction.GetUniqueLoadID(),
                pawn.ageTracker.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture),pawn.ageTracker.AgeChronologicalTicks.ToString(CultureInfo.InvariantCulture),
                pawn.skills == null ? "" : string.Join(",",pawn.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+s.xpSinceLastLevel.ToString("R",CultureInfo.InvariantCulture)).ToArray()),
                string.Join(",",pawn.health.hediffSet.hediffs.Select(h => h.def.defName+":"+h.Severity.ToString("R",CultureInfo.InvariantCulture)+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                pawn.genes == null ? "" : "endo:"+string.Join(",",pawn.genes.Endogenes.Select(g => g.def.defName+":"+g.Active).ToArray())+";xeno:"+string.Join(",",pawn.genes.Xenogenes.Select(g => g.def.defName+":"+g.Active).ToArray()),
                pawn.Ideo == null ? "" : pawn.Ideo.GetUniqueLoadID(),Items(pawn)});
        }
        private static bool SamePawn(Pawn pawn,string expected)
        {
            var current=PawnState(pawn).Split('|'); var old=expected.Split('|');
            if (current.Length != old.Length || !current.Where((s,i) => i != 5 && i != 6).SequenceEqual(old.Where((s,i) => i != 5 && i != 6))) return false;
            return new[] {5,6}.All(i => long.Parse(current[i],CultureInfo.InvariantCulture)-long.Parse(old[i],CultureInfo.InvariantCulture) >= 0 &&
                long.Parse(current[i],CultureInfo.InvariantCulture)-long.Parse(old[i],CultureInfo.InvariantCulture) <= 3);
        }
        private static string AbstractStock(CoreLedger ledger)
        { return string.Join(",",ledger.Entities.Where(e => e.Kind == "lot" && e.Authority == SimulationAuthority.Abstract).OrderBy(e => e.Id,StringComparer.Ordinal).Select(e => e.Id+":"+e.Quantity).ToArray()); }
        private static int Individuals(CoreLedger ledger)
        { return ledger.Entities.Count(e => e.Kind == "person" || e.Kind == "animal"); }
        private static Pawn Original(CoreWorldComponent core,string nativeId)
        { return core.GetDirectlyHeldThings().Cast<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns)).Single(p => p.GetUniqueLoadID() == nativeId); }
        private static void CheckRegistry(ColonyService service,CoreWorldComponent core,string[] ids)
        {
            var state=State(service,core);
            Assert(state.people.Count(p => p.nativeId == ids[4] && p.id == ids[6]) == 1 && state.animals.Count(a => a.nativeId == ids[5] && a.id == ids[7]) == 1,
                "Local custody reuses one existing Colony person and animal registry entry");
            Assert(core.Ledger.Entities.Count(e => e.BoundThingLoadId == ids[4]) == 1 && core.Ledger.Entities.Count(e => e.BoundThingLoadId == ids[5]) == 1 &&
                core.Ledger.GetEntity(ids[6]).BoundThingLoadId == ids[4] && core.Ledger.GetEntity(ids[7]).BoundThingLoadId == ids[5],"Both permanent Core bindings still select the exact original individuals");
            Assert(Individuals(core.Ledger) == int.Parse(ids[12]) && AbstractStock(core.Ledger) == ids[11],"Local native storage creates no new individual identities or abstract inventory");
        }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current;
                bool pendingSave=GenCommandLine.CommandLineArgPassed("inh-local-custody-pending-save"), pendingLoad=GenCommandLine.CommandLineArgPassed("inh-local-custody-pending-load"), pendingFinal=GenCommandLine.CommandLineArgPassed("inh-local-custody-pending-final");
                if (pendingLoad || pendingFinal)
                    Assert(core.IsReady && service != null && !ColonyMod.CustodyAllowed && !service.CustodyCommandsReady,"Existing custody recovery is tested with new storage disabled");
                else if (window == null) Assert(core.IsReady && service != null && service.CustodyCommandsReady,"Normal Colony local custody commands are available");
                if (GenCommandLine.CommandLineArgPassed("inh-local-custody-render")) { Render(service); return; }
                done=true; Find.TickManager.Pause();
                if (pendingSave || pendingLoad || pendingFinal) PendingCheckpoint(service,core,pendingSave,pendingLoad);
                else if (GenCommandLine.CommandLineArgPassed("inh-local-custody-save")) SaveStored(service,core);
                else RestoreOrVerify(service,core);
                Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception e) { done=true; Log.Error("[INH-LOCAL-CUSTODY] FAIL "+e); Application.Quit(1); }
        }
        private static void SaveStored(ColonyService service,CoreWorldComponent core)
        {
            var before=State(service,core); var bases=service.CustodyBases();
            var origin=bases.First(b => service.AvailableCustodyPawns(b.Id).Count(p => p.IsColonist) == 2 && service.AvailableCustodyPawns(b.Id).Any(p => p.IsColonist && before.people.Any(r => r.nativeId == p.GetUniqueLoadID())) &&
                service.AvailableCustodyPawns(b.Id).Any(p => p.RaceProps.Animal && p.def.defName == "Muffalo"));
            var remote=bases.First(b => b.Id != origin.Id && b.MapId != origin.MapId);
            var choices=service.AvailableCustodyPawns(origin.Id); var humans=choices.Where(p => p.IsColonist).ToArray();
            Pawn human=humans.First(p => before.people.Any(r => r.nativeId == p.GetUniqueLoadID()));
            Pawn animal=choices.First(p => p.RaceProps.Animal && p.def.defName == "Muffalo");
            string personId=before.people.Single(p => p.nativeId == human.GetUniqueLoadID()).id;
            Assert(human.Map.uniqueID == origin.MapId && animal.Map == human.Map && human.inventory != null && animal.inventory != null,"Original person and animal share their original direct map");
            int count=core.Ledger.EntityCount;
            Refuse(() => service.StoreLocalPawn("local-cross-store",remote.Id,human),"A different direct base cannot store an original pawn from this map");
            Assert(core.Ledger.EntityCount == count && human.Spawned && animal.Spawned,"Cross-map refusal leaves native individuals and Core records intact");
            // One existing unit is sufficient to exercise person and animal transport locks;
            // the reservation is cancelled without dispatch, splitting or moving any cargo.
            Assert(service.CommandsReady,"Transport reservation fixture uses the existing enabled workflow");
            Thing cargo=service.AvailableTransportCargo(origin.Id).First(c => c.Thing.def == ThingDefOf.Steel && c.Available >= 1).Thing;
            string cargoBefore=NativeItem(cargo);
            service.ReservePhysicalTransport("local-custody-lock",origin.Id,remote.Id,new[] {human,animal},new[] {new CargoSelection(cargo,1)});
            int lockedCount=core.Ledger.EntityCount;
            Refuse(() => service.StoreLocalPawn("local-locked-person",origin.Id,human),"An active transport reservation prevents storing its original person");
            Refuse(() => service.StoreLocalPawn("local-locked-animal",origin.Id,animal),"An active transport reservation prevents storing its original animal");
            Assert(!service.AvailableCustodyPawns(origin.Id).Contains(human) && !service.AvailableCustodyPawns(origin.Id).Contains(animal) &&
                core.Ledger.EntityCount == lockedCount && human.Spawned && animal.Spawned && NativeItem(cargo) == cargoBefore,"Transport-locked custody refusals preserve original pawns and untouched cargo");
            service.BeginPhysicalDeparture("local-custody-lock");
            IntVec3 originalCell=human.Position; Rot4 originalRotation=human.Rotation; Map originalMap=human.Map;
            try
            {
                human.DeSpawn();
                Refuse(() => service.DispatchPhysical("local-custody-lock"),"A missing member on an already-departing transport is safely refused without a null exception");
                Assert(core.Ledger.EntityCount == lockedCount && NativeItem(cargo) == cargoBefore &&
                    service.PhysicalTransports.Single(t => t.Cause == "local-custody-lock").CaravanId == null,"Missing-member refusal creates no caravan, cargo split or Core certificate");
            }
            finally { if (!human.Spawned) GenSpawn.Spawn(human,originalCell,originalMap,originalRotation); }
            service.CancelPhysical("local-custody-lock"); service.CancelPhysical("local-custody-lock");
            Assert(NativeItem(cargo) == cargoBefore && service.AvailableCustodyPawns(origin.Id).Contains(human) && service.AvailableCustodyPawns(origin.Id).Contains(animal),"Cancelling the test reservation unlocks originals without consuming or splitting cargo");
            var registered=State(service,core);
            string animalId=registered.animals.Single(a => a.nativeId == animal.GetUniqueLoadID()).id;
            Assert(registered.people.Single(p => p.nativeId == human.GetUniqueLoadID()).id == personId && core.Ledger.GetEntity(animalId).BoundThingLoadId == animal.GetUniqueLoadID(),"Custody fixture reuses the original person ID and transport-registered animal ID");
            File.WriteAllLines(PathFor("states"),new[] {PawnState(human),PawnState(animal)});
            string stock=AbstractStock(core.Ledger); int individuals=Individuals(core.Ledger); count=core.Ledger.EntityCount;
            string personStore=service.StoreLocalPawn("local-store-person",origin.Id,human);
            Assert(service.StoreLocalPawn("local-store-person",origin.Id,human) == personStore && core.Ledger.EntityCount == count+2,"Repeated person storage reuses one intent and its original Core certificate");
            var remaining=humans.Where(p => p != human && p.Spawned).ToArray();
            Assert(remaining.Length >= 1,"Local storage leaves an eligible human on the original direct map");
            if (remaining.Length == 1)
            {
                int protectedCount=core.Ledger.EntityCount;
                Refuse(() => service.StoreLocalPawn("local-last-person",origin.Id,remaining[0]),"Storing the last eligible human on the direct map is refused");
                Assert(remaining[0].Spawned && core.Ledger.EntityCount == protectedCount,"Last-human protection refuses before allocation or native movement");
            }
            else throw new InvalidOperationException("Fixture must leave exactly one eligible human to exercise last-human protection");
            string animalStore=service.StoreLocalPawn("local-store-animal",origin.Id,animal);
            Assert(service.StoreLocalPawn("local-store-animal",origin.Id,animal) == animalStore && core.Ledger.EntityCount == count+4,"Repeated animal storage allocates no replacement individual or second certificate");
            var ids=new[] {core.Header.SaveScopeId,origin.Id,remote.Id,origin.MapId.ToString(),human.GetUniqueLoadID(),animal.GetUniqueLoadID(),personId,animalId,personStore,animalStore,core.Ledger.EntityCount.ToString(),stock,individuals.ToString()};
            File.WriteAllLines(PathFor("ids"),ids);
            Assert(core.PawnCustody.IsStored(personId) && core.PawnCustody.IsStored(animalId) && !human.Spawned && !animal.Spawned &&
                ReferenceEquals(Original(core,ids[4]),human) && ReferenceEquals(Original(core,ids[5]),animal),"Normal storage holds the exact original person and animal in Core");
            CheckResidents(service,core,ids); CheckRegistry(service,core,ids);
            Assert(PawnState(human) == File.ReadAllLines(PathFor("states"))[0] && PawnState(animal) == File.ReadAllLines(PathFor("states"))[1],"Normal storage preserves all original possessions and pawn state without simulation changes");
            GameDataSaveLoader.SaveGame("INH_LocalCustodyStored");
        }
        private static void CheckResidents(ColonyService service,CoreWorldComponent core,string[] ids)
        {
            var residents=service.CustodyResidents(ids[1]);
            Assert(residents.Count(r => r.EntityId == ids[6] && r.Kind == "person" && !string.IsNullOrEmpty(r.Name)) == 1 &&
                residents.Count(r => r.EntityId == ids[7] && r.Kind == "animal" && !string.IsNullOrEmpty(r.Name)) == 1 && !service.CustodyResidents(ids[2]).Any(r => r.EntityId == ids[6] || r.EntityId == ids[7]),
                "Residents appear once at their original city's storage and never at a remote base");
            var coreResidents=core.ReadPawnCustodyResidents(ColonyService.Actor);
            Assert(coreResidents.Count(r => r.EntityId == ids[6] && r.NativeId == ids[4] && r.CityId == ids[1] && r.SourceMapId.ToString() == ids[3] && r.StoreTransferId == ids[8]) == 1 &&
                coreResidents.Count(r => r.EntityId == ids[7] && r.NativeId == ids[5] && r.CityId == ids[1] && r.SourceMapId.ToString() == ids[3] && r.StoreTransferId == ids[9]) == 1,"Core residents retain exact native bindings, original map, custody city and store certificates");
        }
        private static void RestoreOrVerify(ColonyService service,CoreWorldComponent core)
        {
            var ids=File.ReadAllLines(PathFor("ids")); var expected=File.ReadAllLines(PathFor("states"));
            Assert(core.Header.SaveScopeId == ids[0],"Local custody reload preserves the original world scope");
            var originals=new[] {Original(core,ids[4]),Original(core,ids[5])};
            Assert(originals.Select((p,i) => SamePawn(p,expected[i])).All(v => v),"Native reload retains original names, wounds, skills, genes, equipment and cargo");
            Assert(originals.All(p => !Find.WorldPawns.Contains(p)),"Local originals have no duplicate world-pawn ownership");
            CheckRegistry(service,core,ids);
            if (GenCommandLine.CommandLineArgPassed("inh-local-custody-load"))
            {
                Assert(core.Ledger.EntityCount == int.Parse(ids[10]) && originals.All(p => !p.Spawned && p.holdingOwner == core.GetDirectlyHeldThings()),"Stored process restart finds both safe original individuals in the Core holder");
                CheckResidents(service,core,ids);
                int count=core.Ledger.EntityCount;
                Refuse(() => service.ReleaseLocalPawn("local-cross-release",ids[2],ids[6]),"Stored originals cannot be returned through a remote direct base");
                Assert(core.Ledger.EntityCount == count && originals.All(p => !p.Spawned),"Cross-map release refuses before allocating or moving originals");
                var releases=new string[2];
                for (int i=0;i<2;i++)
                {
                    string entity=ids[6+i], cause=i == 0 ? "local-release-person" : "local-release-animal";
                    releases[i]=service.ReleaseLocalPawn(cause,ids[1],entity);
                    Assert(service.ReleaseLocalPawn(cause,ids[1],entity) == releases[i],"Repeated local release reuses the same completed transfer");
                    service.ResumeLocalCustody(releases[i]); service.ResumeLocalCustody(releases[i]);
                    Assert(originals[i].Spawned && originals[i].Map.uniqueID.ToString() == ids[3] && ReferenceEquals(Original(core,ids[4+i]),originals[i]),"Release returns each exact original only to its original map");
                }
                File.WriteAllLines(PathFor("releases"),releases);
                Assert(core.Ledger.EntityCount == count+4 && !core.PawnCustody.IsStored(ids[6]) && !core.PawnCustody.IsStored(ids[7]) &&
                    !service.CustodyResidents(ids[1]).Any(r => r.EntityId == ids[6] || r.EntityId == ids[7]) && !service.CustodyPending().Any(),"Completed local return removes resident rows without duplicating identities or intents");
                CheckRegistry(service,core,ids);
                Assert(originals.Select((p,i) => SamePawn(p,expected[i])).All(v => v),"Returned originals retain all stored native possessions and personal state");
                GameDataSaveLoader.SaveGame("INH_LocalCustodyReturned");
            }
            else
            {
                int count=core.Ledger.EntityCount; var releases=File.ReadAllLines(PathFor("releases"));
                for (int i=0;i<2;i++)
                {
                    Assert(service.ReleaseLocalPawn(i == 0 ? "local-release-person" : "local-release-animal",ids[1],ids[6+i]) == releases[i],"Returned restart reuses the original local release certificate");
                    service.ResumeLocalCustody(releases[i]); service.ResumeLocalCustody(releases[i]);
                }
                Assert(count == int.Parse(ids[10])+4 && core.Ledger.EntityCount == count && originals.All(p => p.Spawned && p.Map.uniqueID.ToString() == ids[3]) &&
                    !service.CustodyResidents(ids[1]).Any(r => r.EntityId == ids[6] || r.EntityId == ids[7]) && !service.CustodyPending().Any(),"Returned restart leaves no stored rows, pending moves or duplicate Core certificates");
                Assert(originals.Select((p,i) => SamePawn(p,expected[i])).All(v => v),"Idempotent local return keeps original individuals and inventories unchanged");
            }
        }
        private static void PendingCheckpoint(ColonyService service,CoreWorldComponent core,bool save,bool load)
        {
            var ids=File.ReadAllLines(PathFor("ids"));
            Assert(core.Header.SaveScopeId == ids[0],"Pending local custody keeps the original world scope");
            CheckRegistry(service,core,ids);
            Pawn human=Original(core,ids[4]), animal=Original(core,ids[5]);
            if (save)
            {
                Assert(human.Spawned && animal.Spawned && human.Map.uniqueID.ToString() == ids[3] && !core.PawnCustody.IsStored(ids[6]) &&
                    service.AvailableCustodyPawns(ids[1]).Count(p => p.IsColonist) >= 2,"Pending fixture starts with returned originals and another eligible human on their original map");
                File.WriteAllLines(PathFor("pending-states"),new[] {PawnState(human),PawnState(animal)});
                int before=core.Ledger.EntityCount;
                var intent=core.PawnCustody.PrepareStore(ColonyService.Actor,"local-pending-store",ids[6],ids[1]);
                Assert(intent.Stage == PawnCustodyStage.Prepared && core.Ledger.EntityCount == before+2 && human.Spawned && !core.PawnCustody.IsStored(ids[6]),
                    "Pending checkpoint records one known original's Core intent before native movement");
                Assert(core.PawnCustody.PrepareStore(ColonyService.Actor,"local-pending-store",ids[6],ids[1]).Id == intent.Id && core.Ledger.EntityCount == before+2,
                    "Repeated prepared local intent keeps the same existing certificate");
                var pending=service.CustodyPending().Single(p => p.Id == intent.Id);
                Assert(pending.BaseId == ids[1] && !pending.IsRelease && pending.Stage == "Prepared" && !string.IsNullOrEmpty(pending.Name),"Normal custody query exposes the real pending original and prepared stage");
                Assert(!service.AvailableCustodyPawns(ids[1]).Contains(human) && !service.AvailableTransportPawns(ids[1]).Contains(human),"Pending custody excludes its original from fresh storage and transport candidates");
                Thing cargo=service.AvailableTransportCargo(ids[1]).First(c => c.Thing.def == ThingDefOf.Steel && c.Available >= 1).Thing;
                string untouched=NativeItem(cargo); int pendingCount=core.Ledger.EntityCount;
                Refuse(() => service.ReservePhysicalTransport("local-pending-transport",ids[1],ids[2],new[] {human},new[] {new CargoSelection(cargo,1)}),
                    "Direct transport reservation cannot steal an original with pending Core custody");
                Assert(core.Ledger.EntityCount == pendingCount && human.Spawned && NativeItem(cargo) == untouched,"Pending transport refusal leaves the original, cargo and Core count unchanged");
                File.WriteAllLines(PathFor("pending"),new[] {intent.Id,before.ToString(),pendingCount.ToString(),ids[6],ids[3]});
                GameDataSaveLoader.SaveGame("INH_LocalCustodyPending");
                return;
            }
            var checkpoint=File.ReadAllLines(PathFor("pending")); var expected=File.ReadAllLines(PathFor("pending-states"));
            Assert(checkpoint[3] == ids[6] && checkpoint[4] == ids[3] && core.Ledger.EntityCount == int.Parse(checkpoint[2]) &&
                SamePawn(human,expected[0]) && SamePawn(animal,expected[1]),"Pending process restart preserves the known identities, original state and intent count");
            int disabledCount=core.Ledger.EntityCount;
            Refuse(() => service.StoreLocalPawn("local-disabled-store",ids[1],animal),"Disabled storage prevents a new store while recovery and release remain available");
            Assert(core.Ledger.EntityCount == disabledCount && animal.Spawned,"Disabled new-store refusal preserves the original animal and allocates nothing");
            if (load)
            {
                var pending=service.CustodyPending().Single(p => p.Id == checkpoint[0]);
                Assert(pending.Stage == "Prepared" && !pending.IsRelease && pending.BaseId == ids[1] && human.Spawned,
                    "Pending reload shows the real prepared intent with its original still on the source map");
                service.ResumeLocalCustody(checkpoint[0]); service.ResumeLocalCustody(checkpoint[0]);
                Assert(core.Ledger.EntityCount == disabledCount && !human.Spawned && human.holdingOwner == core.GetDirectlyHeldThings() &&
                    ReferenceEquals(Original(core,ids[4]),human) && core.PawnCustody.IsStored(ids[6]) && !service.CustodyPending().Any(),
                    "Disabled-setting recovery moves the exact original once without another intent or certificate");
                var resident=core.ReadPawnCustodyResidents(ColonyService.Actor).Single(r => r.EntityId == ids[6]);
                Assert(resident.NativeId == ids[4] && resident.CityId == ids[1] && resident.StoreTransferId == checkpoint[0] && resident.SourceMapId.ToString() == ids[3],
                    "Recovered storage keeps the original pending certificate, local map and city");
                Assert(SamePawn(human,expected[0]) && SamePawn(animal,expected[1]) && !Find.WorldPawns.Contains(human),"Recovery preserves all native personal state and possessions with one storage owner");
                CheckRegistry(service,core,ids);
                GameDataSaveLoader.SaveGame("INH_LocalCustodyRecovered");
            }
            else
            {
                Assert(!human.Spawned && human.holdingOwner == core.GetDirectlyHeldThings() && core.PawnCustody.IsStored(ids[6]) && !service.CustodyPending().Any(),
                    "Recovered reload retains the exact original safely held with no unfinished intent");
                string release=service.ReleaseLocalPawn("local-pending-release",ids[1],ids[6]);
                Assert(service.ReleaseLocalPawn("local-pending-release",ids[1],ids[6]) == release,"Disabled-setting release reuses its completed local certificate");
                service.ResumeLocalCustody(release); service.ResumeLocalCustody(release);
                Assert(core.Ledger.EntityCount == disabledCount+2 && human.Spawned && human.Map.uniqueID.ToString() == ids[3] && ReferenceEquals(Original(core,ids[4]),human) &&
                    !core.PawnCustody.IsStored(ids[6]) && !service.CustodyResidents(ids[1]).Any(r => r.EntityId == ids[6]) && !service.CustodyPending().Any(),
                    "Disabled-setting return restores the original only to its source map and removes its stored row");
                Assert(SamePawn(human,expected[0]) && SamePawn(animal,expected[1]) && !Find.WorldPawns.Contains(human),"Recovered final return preserves original identity, personal state and inventory");
                CheckRegistry(service,core,ids);
                File.WriteAllText(PathFor("pending-release"),release);
                GameDataSaveLoader.SaveGame("INH_LocalCustodyRecoveredFinal");
            }
        }
        private void Render(ColonyService service)
        {
            string language=GenCommandLine.CommandLineArgPassed("inh-local-custody-japanese") ? "Japanese" : "English";
            string image=Path.Combine(GenFilePaths.SaveDataFolderPath,"LocalCustody-"+language+".png");
            if (window == null)
            {
                Assert(LanguageDatabase.activeLanguage.folderName.StartsWith(language,StringComparison.Ordinal),"Local custody render uses the requested language");
                File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"translation-errors-"+language+".txt"),LanguageDatabase.activeLanguage.loadErrors.Concat(LanguageDatabase.activeLanguage.defInjections.SelectMany(p => p.loadErrors)).ToArray());
                if (File.Exists(image)) File.Delete(image);
                window=new CustodyWindow(service); Find.WindowStack.Add(window); return;
            }
            frames++;
            if (!captured && window.Rendered && frames > 30) { ScreenCapture.CaptureScreenshot(image); captured=true; }
            if (captured && File.Exists(image) && new FileInfo(image).Length > 1000) { done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
        }
    }
}
