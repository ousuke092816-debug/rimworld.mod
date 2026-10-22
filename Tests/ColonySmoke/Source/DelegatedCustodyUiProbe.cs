using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    // Staged only: root integrates this after the frozen 0.4.11 regression finishes.
    // Input start is INH_DelegatedCustodyReturned: the original party is at its old site.
    public sealed class DelegatedCustodyUiProbe : GameComponent
    {
        private const string PendingStore="delegated-ui-pending-store",PendingRelease="delegated-ui-pending-release",SiteName="UI delegated outpost";
        private static readonly string[] Modes={"start","store","prepare-release","release","reload","register","registered-reload","render","render-japanese"};
        private string[] ids;
        private Pawn human,animal,escort;
        private Caravan caravan;
        private Map origin;
        private bool done,restored,travelStarted,captured;
        private int travelTicks,travelCount,frames;
        private PlanetTile destination;
        private string[] frozen;
        private DelegatedCustodyWindow window;
        public DelegatedCustodyUiProbe(Game game) { }
        private static bool Has(string mode) { return GenCommandLine.CommandLineArgPassed("inh-delegated-ui-"+mode); }
        private static bool Selected { get { return GenCommandLine.CommandLineArgPassed("inh-delegated-ui-probe") && Modes.Any(Has); } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private Pawn[] Party { get { return new[] {human,animal,escort}; } }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-delegated-ui-"+key+".txt"); }
        private static string Backup(string name,string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,name+"-delegated-ui-"+key+".txt"); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-DELEGATED-UI] PASS "+message); }
        private static void Refuse(Action action,string message)
        { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,message); }
        private static ColonyState State(ColonyService service,CoreWorldComponent core)
        { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core))); }
        private static string Number(float value) { return value.ToString("R",CultureInfo.InvariantCulture); }
        private static string Item(Thing item)
        { return item.GetUniqueLoadID()+":"+item.def.defName+":"+(item.Stuff == null ? "" : item.Stuff.defName)+":"+item.stackCount+":"+item.HitPoints; }
        private static IEnumerable<Thing> Possessions(Pawn pawn)
        { return pawn.inventory.innerContainer.Cast<Thing>().Concat(pawn.apparel == null ? new Thing[0] : pawn.apparel.WornApparel.Cast<Thing>()).Concat(pawn.equipment == null ? new Thing[0] : pawn.equipment.AllEquipmentListForReading.Cast<Thing>()); }
        private static string PawnState(Pawn pawn)
        {
            return string.Join("|",new[] {pawn.GetUniqueLoadID(),pawn.def.defName,pawn.Name == null ? "" : pawn.Name.ToStringFull,pawn.gender.ToString(),pawn.Faction.GetUniqueLoadID(),
                pawn.ageTracker.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture),pawn.ageTracker.AgeChronologicalTicks.ToString(CultureInfo.InvariantCulture),
                pawn.skills == null ? "" : string.Join(",",pawn.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+Number(s.xpSinceLastLevel)).ToArray()),
                string.Join(",",pawn.health.hediffSet.hediffs.Select(h => h.def.defName+":"+Number(h.Severity)+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                pawn.genes == null ? "" : "endo:"+string.Join(",",pawn.genes.Endogenes.Select(g => g.def.defName+":"+g.Active).ToArray())+";xeno:"+string.Join(",",pawn.genes.Xenogenes.Select(g => g.def.defName+":"+g.Active).ToArray()),
                pawn.Ideo == null ? "" : pawn.Ideo.GetUniqueLoadID(),string.Join(",",Possessions(pawn).Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray()),
                pawn.needs == null ? "" : string.Join(",",pawn.needs.AllNeeds.OrderBy(n => n.def.defName,StringComparer.Ordinal).Select(n => n.def.defName+":"+Number(n.CurLevel)).ToArray()),
                pawn.story == null ? "" : string.Join(",",pawn.story.traits.allTraits.Select(t => t.def.defName+":"+t.Degree).ToArray())});
        }
        private static bool SameSavedPawn(Pawn pawn,string expected)
        {
            string[] current=PawnState(pawn).Split('|'),old=expected.Split('|');
            return current.Length == old.Length && current.Where((s,i) => i != 5 && i != 6).SequenceEqual(old.Where((s,i) => i != 5 && i != 6)) &&
                new[] {5,6}.All(i => long.Parse(current[i],CultureInfo.InvariantCulture)-long.Parse(old[i],CultureInfo.InvariantCulture) >= 0 &&
                    long.Parse(current[i],CultureInfo.InvariantCulture)-long.Parse(old[i],CultureInfo.InvariantCulture) <= 3);
        }
        private static Pawn Original(CoreWorldComponent core,string id)
        { return core.GetDirectlyHeldThings().Cast<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns)).Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().Single(p => p.GetUniqueLoadID() == id); }
        private static bool InCaravan(Pawn pawn,Caravan caravan)
        { return !pawn.Spawned && pawn.holdingOwner == caravan.GetDirectlyHeldThings() && caravan.PawnsListForReading.Contains(pawn) && Find.WorldPawns.Contains(pawn) && Find.WorldObjects.Caravans.Count(c => c.PawnsListForReading.Contains(pawn)) == 1; }
        private static bool Held(Pawn pawn,CoreWorldComponent core)
        { return !pawn.Spawned && pawn.holdingOwner == core.GetDirectlyHeldThings() && core.GetDirectlyHeldThings().Contains(pawn) && !Find.WorldPawns.Contains(pawn) && !Find.WorldObjects.Caravans.Any(c => c.PawnsListForReading.Contains(pawn)); }
        private static string Stocks(CoreWorldComponent core)
        { return string.Join(";",core.Ledger.Entities.Where(e => e.Kind == "lot" && e.Authority == SimulationAuthority.Abstract).OrderBy(e => e.Id,StringComparer.Ordinal).Select(e => e.Id+":"+e.Quantity).ToArray()); }
        private static int Individuals(CoreWorldComponent core) { return core.Ledger.Entities.Count(e => e.Kind == "person" || e.Kind == "animal"); }
        private string Owner(Pawn pawn,CoreWorldComponent core)
        { return Held(pawn,core) ? "Core" : InCaravan(pawn,caravan) ? caravan.GetUniqueLoadID() : "Unexpected"; }
        private static void Enabled(bool value)
        {
            var settingsField=typeof(ColonyMod).GetField("settings",BindingFlags.NonPublic|BindingFlags.Static);
            var settings=settingsField.GetValue(null); var enabledField=settings.GetType().GetField("delegatedEnabled",BindingFlags.Public|BindingFlags.Instance);
            Assert(enabledField != null,"The normal delegated-management setting is present without a development flag");
            enabledField.SetValue(settings,value); // Fixture-only, never writes user settings to disk.
            Assert(ColonyMod.DelegatedEnabled == value && ColonyMod.DelegatedAllowed == value,"The normal setting alone controls new delegated management commands");
        }
        private PawnCustodySnapshot Transfer(CoreWorldComponent core,string id)
        { return core.ReadPawnCustodyTransfers(ColonyService.Actor).Single(t => t.Id == id); }
        private string[] History(CoreWorldComponent core)
        { return core.ReadPawnCustodyTransfers(ColonyService.Actor).OrderBy(t => t.Id,StringComparer.Ordinal).Select(t =>
            string.Join("|",new[] {t.Id,t.Actor,t.Cause,t.EntityId,t.NativeId,t.CityId,t.CaravanId,t.Tile.ToString(CultureInfo.InvariantCulture),t.IsRelease.ToString(),t.Stage.ToString()})).ToArray(); }
        private void Bind(CoreWorldComponent core)
        {
            human=Original(core,ids[3]); animal=Original(core,ids[4]); escort=Original(core,ids[5]);
            caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[2]); origin=Find.Maps.Single(m => m.uniqueID.ToString(CultureInfo.InvariantCulture) == ids[8]);
        }
        private void Originals(CoreWorldComponent core)
        {
            Assert(core.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion && core.Header.SaveScopeId == ids[0] && Individuals(core) == int.Parse(ids[15],CultureInfo.InvariantCulture) && Stocks(core) == ids[16],
                "Ordinary management preserves Core7, the original world scope and every individual and abstract stock identity");
            Assert(Party.Select((p,i) => ReferenceEquals(Original(core,ids[3+i]),p) && Owner(p,core) != "Unexpected").All(v => v) &&
                core.Ledger.GetEntity(ids[6]).BoundThingLoadId == ids[3] && core.Ledger.GetEntity(ids[7]).BoundThingLoadId == ids[4] &&
                core.Ledger.Entities.Count(e => e.BoundThingLoadId == ids[3]) == 1 && core.Ledger.Entities.Count(e => e.BoundThingLoadId == ids[4]) == 1,
                "The original humans, animal, native possessions and sole permanent bindings remain at exactly one native endpoint");
        }
        private void Unchanged(ColonyService service,CoreWorldComponent core,Action action,string message)
        {
            int count=core.Ledger.EntityCount; string payload=service.SaveSnapshot(core); var states=Party.Select(PawnState).ToArray(); var owners=Party.Select(p => Owner(p,core)).ToArray(); var history=History(core);
            action();
            Assert(core.Ledger.EntityCount == count && service.SaveSnapshot(core) == payload && Party.Select(PawnState).SequenceEqual(states) &&
                Party.Select(p => Owner(p,core)).SequenceEqual(owners) && History(core).SequenceEqual(history),message);
        }
        private void Save(ColonyService service,CoreWorldComponent core,string name)
        {
            Originals(core);
            var data=new Dictionary<string,string[]> {{"ids",ids},{"count",new[] {core.Ledger.EntityCount.ToString(CultureInfo.InvariantCulture)}},
                {"states",Party.Select(PawnState).ToArray()},{"owners",Party.Select(p => Owner(p,core)).ToArray()},{"colony",new[] {service.SaveSnapshot(core)}},{"custody",History(core)}};
            foreach (var entry in data) { File.WriteAllLines(PathFor(entry.Key),entry.Value); File.Copy(PathFor(entry.Key),Backup(name,entry.Key),true); }
            GameDataSaveLoader.SaveGame(name); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private void Restore(ColonyService service,CoreWorldComponent core)
        {
            ids=File.ReadAllLines(PathFor("ids")); Bind(core);
            var states=File.ReadAllLines(PathFor("states"));
            Assert(core.Ledger.EntityCount == int.Parse(File.ReadAllLines(PathFor("count"))[0],CultureInfo.InvariantCulture) &&
                Party.Select((p,i) => SameSavedPawn(p,states[i])).All(v => v) && Party.Select(p => Owner(p,core)).SequenceEqual(File.ReadAllLines(PathFor("owners"))) &&
                History(core).SequenceEqual(File.ReadAllLines(PathFor("custody"))) && service.SaveSnapshot(core) == string.Join(Environment.NewLine,File.ReadAllLines(PathFor("colony"))),
                "Separate-process reload preserves all receipts, original native IDs, food and possessions, needs, ages, skills and ownership without allocation");
            Originals(core); restored=true;
        }
        private void Start(ColonyService service,CoreWorldComponent core)
        {
            var legacy=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-delegated-ids.txt"));
            ids=new[] {legacy[0],legacy[4],legacy[5],legacy[6],legacy[7],legacy[8],legacy[9],legacy[10],legacy[2],legacy[3],"","","","","",Individuals(core).ToString(CultureInfo.InvariantCulture),Stocks(core)};
            Bind(core); Assert(!caravan.pather.Moving && caravan.Tile == new PlanetTile(int.Parse(ids[9],CultureInfo.InvariantCulture),0) && Party.All(p => InCaravan(p,caravan)),
                "INH_DelegatedCustodyReturned starts with the exact original stopped caravan, two humans and animal at their old site");
            var controller=new DelegatedCustodyCommandController(service); Enabled(false);
            Unchanged(service,core,delegate {
                Assert(service.DelegatedCaravans(ids[1]).Single(c => c.Caravan == caravan).Problem == "Disabled" && service.AvailableDelegatedStorePawns(ids[1],caravan).All(p => !p.CanStore),
                    "Disabled ordinary management still returns readable caravan and original member rows");
                Refuse(() => service.RegisterDelegatedSiteAtCaravan("ui-disabled-site","Disabled site",caravan),"A disabled normal setting refuses new site registration without a development flag");
                Refuse(() => controller.Register(caravan,"Disabled controller site"),"The normal registration controller also obeys the disabled setting");
                Refuse(() => controller.Store(ids[1],caravan,human),"The normal Store controller refuses new storage while disabled");
            },"Disabled registration and storage change no ID, food, original pawn, journal or native holder");
            Enabled(true); var rows=service.AvailableDelegatedStorePawns(ids[1],caravan);
            Assert(rows.Length == 3 && rows.Single(r => r.Pawn == human).CanStore && rows.Single(r => r.Pawn == animal).CanStore &&
                rows.All(r => r.Items == Possessions(r.Pawn).Sum(t => t.stackCount) && r.Name == r.Pawn.LabelShort),
                "Normal enabled candidates describe the original person, animal, escort and their exact native possessions");
            int count=core.Ledger.EntityCount; var state=Party.Select(PawnState).ToArray();
            string firstStore=controller.Store(ids[1],caravan,human);
            Assert(Held(human,core) && InCaravan(escort,caravan) && core.Ledger.EntityCount == count+2 && Transfer(core,firstStore).Stage == PawnCustodyStage.Committed,
                "The ordinary human Store button uses one existing native original and one intent, retaining the original human escort");
            string firstRelease=controller.Release(ids[1],ids[6],caravan);
            Assert(InCaravan(human,caravan) && core.Ledger.EntityCount == count+4 && Transfer(core,firstRelease).Stage == PawnCustodyStage.Committed && Party.Select(PawnState).SequenceEqual(state),
                "The ordinary Release button restores that same human and every food item without simulating native biology");
            ids[10]=core.PawnCustody.PrepareCaravanStore(ColonyService.Actor,PendingStore,ids[6],ids[1],caravan,int.Parse(ids[9],CultureInfo.InvariantCulture)).Id;
            Assert(core.Ledger.EntityCount == count+6 && InCaravan(human,caravan) && service.AvailableDelegatedStorePawns(ids[1],caravan).Single(r => r.Pawn == human).Problem == "PendingCustody",
                "A sole Prepared native store reserves the original human while leaving it in its original caravan");
            Unchanged(service,core,delegate { Refuse(() => controller.Store(ids[1],caravan,human),"A second normal Store request cannot compete with the original's pending custody reservation"); },
                "Pending custody reservation refusal creates no extra person, intent, food or native move");
            Enabled(false); Assert(service.HasDelegatedCustody && service.DelegatedCustodyPending(ids[1]).Single(r => r.Id == ids[10]).CanResume,
                "Disabling new commands keeps the existing Prepared store visible and recoverable");
            Save(service,core,"INH_DelegatedUiStorePrepared");
        }
        private void Store(ColonyService service,CoreWorldComponent core)
        {
            Enabled(false); var controller=new DelegatedCustodyCommandController(service); int count=core.Ledger.EntityCount; var states=Party.Select(PawnState).ToArray();
            controller.Resume(ids[10]); controller.Resume(ids[10]);
            Assert(core.Ledger.EntityCount == count && Held(human,core) && service.DelegatedCustodyResidents(ids[1]).Single(r => r.EntityId == ids[6]).CanRelease &&
                service.DelegatedCustodyPending(ids[1]).Length == 0,"A disabled setting allows recovery of the same existing store and exposes the exact held resident");
            Enabled(true);
            Assert(service.AvailableDelegatedStorePawns(ids[1],caravan).Single(r => r.Pawn == escort).Problem == "NoEscort","The last original human escort is visible with its specific unavailable reason");
            Unchanged(service,core,delegate { Refuse(() => controller.Store(ids[1],caravan,escort),"The last original human escort cannot be stored by the ordinary controller"); },
                "Last-escort refusal occurs before registering another individual or moving any possession");
            Assert(caravan.pather.StartPath(origin.Tile,null,true,false) && caravan.pather.Moving,"The original escort starts its ordinary native route for the moving boundary");
            Unchanged(service,core,delegate {
                Assert(service.AvailableDelegatedStorePawns(ids[1],caravan).All(r => r.Problem == "CaravanMoving" && !r.CanStore) &&
                    service.DelegatedCustodyResidents(ids[1]).Single(r => r.EntityId == ids[6]).Problem == "CaravanMoving","Moving original anchor rows disable Store and Release consistently");
                Refuse(() => controller.Store(ids[1],caravan,animal),"Moving native anchor refuses a new ordinary animal store");
                Refuse(() => controller.Release(ids[1],ids[6],caravan),"Moving native anchor refuses an ordinary resident release");
                Refuse(() => controller.Register(caravan,"Moving site"),"Moving native anchor refuses ordinary registration");
            },"Moving-route validation allocates no proof and changes no original food or native ownership");
            caravan.pather.StopDead(); ids[11]=controller.Store(ids[1],caravan,animal);
            Assert(core.Ledger.EntityCount == count+2 && Held(human,core) && Held(animal,core) && InCaravan(escort,caravan) && Party.Select(PawnState).SequenceEqual(states),
                "The ordinary animal Store button preserves the native person, animal, all items and biology while retaining the last escort");
            Enabled(false); var residents=service.DelegatedCustodyResidents(ids[1]);
            Assert(residents.Length == 2 && residents.All(r => r.CanRelease && r.Caravan == caravan && r.CaravanName == caravan.Label) &&
                residents.All(r => r.Items == Possessions(r.EntityId == ids[6] ? human : animal).Sum(t => t.stackCount)),
                "Existing resident rows and return actions stay available with the normal setting disabled");
            Save(service,core,"INH_DelegatedUiStored");
        }
        private void PrepareRelease(ColonyService service,CoreWorldComponent core)
        {
            Enabled(false); int count=core.Ledger.EntityCount; var states=Party.Select(PawnState).ToArray();
            ids[12]=core.PawnCustody.PrepareCaravanRelease(ColonyService.Actor,PendingRelease,ids[6],ids[1],caravan,int.Parse(ids[9],CultureInfo.InvariantCulture)).Id;
            var pending=service.DelegatedCustodyPending(ids[1]).Single(r => r.Id == ids[12]); var residents=service.DelegatedCustodyResidents(ids[1]);
            Assert(core.Ledger.EntityCount == count+2 && pending.CanResume && pending.IsRelease && pending.Stage == "Prepared" && pending.Cause == PendingRelease &&
                residents.Length == 2 && !residents.Single(r => r.EntityId == ids[6]).CanRelease && residents.Single(r => r.EntityId == ids[7]).CanRelease &&
                Party.Select(PawnState).SequenceEqual(states) && Held(human,core) && Held(animal,core),
                "The disabled screen keeps its exact Prepared release, original resident rows and independent animal return action readable");
            Save(service,core,"INH_DelegatedUiReleasePrepared");
        }
        private void Release(ColonyService service,CoreWorldComponent core)
        {
            Enabled(false); var controller=new DelegatedCustodyCommandController(service); int count=core.Ledger.EntityCount; var states=Party.Select(PawnState).ToArray();
            ids[13]=controller.Release(ids[1],ids[7],caravan); controller.Resume(ids[12]); controller.Resume(ids[12]); controller.Resume(ids[13]);
            Assert(core.Ledger.EntityCount == count+2 && Party.All(p => InCaravan(p,caravan)) && Party.Select(PawnState).SequenceEqual(states) &&
                service.DelegatedCustodyResidents(ids[1]).Length == 0 && service.DelegatedCustodyPending(ids[1]).Length == 0,
                "Disabled ordinary controls return both exact originals once to their saved original caravan using only the animal's new release intent");
            Unchanged(service,core,delegate { foreach (string id in new[] {ids[10],ids[11],ids[12],ids[13]}) controller.Resume(id); },
                "Historical Store and Release recovery allocates nothing and never reverses a completed native endpoint");
            Save(service,core,"INH_DelegatedUiReleased");
        }
        private bool Travel()
        {
            for (int i=0;i<300 && caravan.pather.Moving;i++)
            { int tick=Find.TickManager.TicksGame; Find.TickManager.DoSingleTick(); if (Find.TickManager.TicksGame != tick+1) throw new InvalidOperationException("Native route must advance one ordinary tick"); travelTicks++; }
            if (travelTicks > 180000) throw new InvalidOperationException("Ordinary registration route exceeded three native days");
            return !caravan.pather.Moving;
        }
        private void Register(ColonyService service,CoreWorldComponent core)
        {
            if (!travelStarted)
            {
                Enabled(true); var controller=new DelegatedCustodyCommandController(service);
                Unchanged(service,core,delegate {
                    Assert(service.DelegatedCaravans(ids[1]).Single(c => c.Caravan == caravan).Problem == "Occupied","The existing delegated site is already occupied for new registration");
                    Refuse(() => controller.Register(caravan,"Duplicate occupied site"),"A new UI registration cannot allocate a second city at the old site");
                    Refuse(() => service.RegisterDelegatedSiteAtCaravan("another-site-key","Another occupied site",caravan),"A separate backend cause also refuses the already occupied old site");
                },"Old-site occupancy refusals preserve all native originals and allocate no city");
                var neighbors=new List<PlanetTile>(); Find.World.grid.GetTileNeighbors(caravan.Tile,neighbors);
                destination=neighbors.First(t => t == new PlanetTile(t.tileId,0) && !Find.World.grid[t].WaterCovered && !Find.World.grid[t].PrimaryBiome.impassable &&
                    Find.World.grid[t].hilliness != Hilliness.Impassable && !Find.Maps.Any(m => m.Tile == t) && !Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == t) && !State(service,core).bases.Any(b => b.tile == t.tileId));
                travelCount=core.Ledger.EntityCount;
                Assert(caravan.pather.StartPath(destination,null,true,false) && caravan.pather.Moving,"The original caravan travels normally to an empty neighboring surface tile, without any tile writes");
                travelStarted=true;
            }
            if (!Travel()) return;
            Assert(caravan.Tile == destination && Party.All(p => InCaravan(p,caravan)) && core.Ledger.EntityCount == travelCount,"Normal native travel reaches the new tile with the same caravan and individual identities");
            // Ordinary travel advances native biology and can eat food. Only the registration operation below must freeze its input.
            Originals(core); Enabled(false);
            Unchanged(service,core,delegate { Refuse(() => service.RegisterDelegatedSiteAtCaravan("disabled-empty-site",SiteName,caravan),"Disabled registration also refuses a genuinely empty stopped caravan tile"); },
                "Disabled empty-tile refusal has no hidden city or native mutation");
            Enabled(true); var commands=new DelegatedCustodyCommandController(service);
            Unchanged(service,core,delegate {
                foreach (string name in new[] {"","   ",new string('x',61)," padded","padded ","bad\nname"})
                    Refuse(() => service.RegisterDelegatedSiteAtCaravan("invalid-name",name,caravan),"Invalid, padded, controlled or overlong visible site names are refused before allocation");
                foreach (string key in new[] {"","   ",new string('x',129)})
                    Refuse(() => service.RegisterDelegatedSiteAtCaravan(key,SiteName,caravan),"An empty or overlong registration cause is refused before allocation");
            },"Every invalid registration leaves the exact native food, biology, Core counters and base table unchanged");
            Assert(service.DelegatedCaravans(null).Single(c => c.Caravan == caravan).CanRegister,"The normal screen offers registration only for the stopped current empty surface tile");
            int count=core.Ledger.EntityCount; var before=Party.Select(PawnState).ToArray(); ids[14]=commands.Register(caravan,SiteName);
            var site=State(service,core).bases.Single(b => b.cityId == ids[14]); string prefix="delegated-site/"+destination.tileId.ToString(CultureInfo.InvariantCulture)+"/";
            Assert(core.Ledger.EntityCount == count+2 && site.tile == caravan.Tile.tileId && site.mode == BaseMode.Delegated && site.mapId == -1 && site.name == SiteName &&
                Party.Select(PawnState).SequenceEqual(before),"One ordinary Register button creates exactly one city at the original caravan's stopped current tile and preserves its native input");
            string keyUsed=site.key.Substring(prefix.Length);
            Unchanged(service,core,delegate {
                Assert(service.RegisterDelegatedSiteAtCaravan(keyUsed,SiteName,caravan) == ids[14],"Exact registration replay returns the same city before occupancy validation");
                Refuse(() => service.RegisterDelegatedSiteAtCaravan(keyUsed,"Changed city name",caravan),"The same registration identity cannot rename its existing city");
                Refuse(() => service.RegisterDelegatedSiteAtCaravan("different-city-cause",SiteName,caravan),"A different registration cause cannot duplicate the current city tile");
                Refuse(() => new DelegatedCustodyCommandController(service).Register(caravan,SiteName),"A fresh UI command cannot register a second city after the first command succeeds");
            },"All registration replay and duplicate refusal paths retain one exact city without new IDs or native mutation");
            Assert(service.AvailableDelegatedStorePawns(ids[1],caravan).All(r => r.Problem == "CaravanAway" && !r.CanStore),"Stopped original caravan at another tile is shown as Away at the old site");
            Unchanged(service,core,delegate { Refuse(() => commands.Store(ids[1],caravan,human),"An away original caravan cannot store a person at its old site"); },
                "Away-site refusal allocates no custody intent and does not move the original person");
            Enabled(false); Save(service,core,"INH_DelegatedUiRegistered");
        }
        private void Render(ColonyService service,CoreWorldComponent core)
        {
            Enabled(false); string language=Has("render-japanese") || Has("japanese") ? "Japanese" : "English";
            string path=Path.Combine(GenFilePaths.SaveDataFolderPath,"DelegatedCustodyUi-"+language+".png");
            if (window == null)
            {
                Assert(LanguageDatabase.activeLanguage.folderName.StartsWith(language,StringComparison.Ordinal),"The delegated custody screen uses the requested native game language");
                Assert(service.HasDelegatedCustody && service.DelegatedCustodyResidents(ids[1]).Length == 2 && service.DelegatedCustodyPending(ids[1]).Any(r => r.Id == ids[12] && r.CanResume),
                    "Disabled rendering contains existing original residents, native possessions and its exact pending recovery action");
                File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"delegated-ui-translation-errors-"+language+".txt"),LanguageDatabase.activeLanguage.loadErrors.Concat(LanguageDatabase.activeLanguage.defInjections.SelectMany(p => p.loadErrors)).ToArray());
                if (File.Exists(path)) File.Delete(path); frozen=Party.Select(PawnState).ToArray(); window=new DelegatedCustodyWindow(service); window.SelectCity(ids[1]); window.SelectCaravan(caravan); Find.WindowStack.Add(window); return;
            }
            frames++; if (frames > 1800) throw new InvalidOperationException("Delegated custody screen did not render and capture in time");
            if (!captured && window.Rendered && frames > 30) { ScreenCapture.CaptureScreenshot(path); captured=true; }
            if (captured && File.Exists(path) && new FileInfo(path).Length > 1000)
            { Assert(Party.Select(PawnState).SequenceEqual(frozen),"English/Japanese repaint preserves every original native food, age, skill and need"); Originals(core); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
        }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current; Find.TickManager.Pause();
                Assert(core.IsReady && service != null,"Core and Colony initialize ordinary delegated UI management");
                Assert(!GenCommandLine.CommandLineArgPassed("inh-delegated-custody"),"The ordinary management probe has no development delegated-custody bypass");
                if (Has("start")) { Start(service,core); return; }
                if (!restored) Restore(service,core);
                if (Has("store")) Store(service,core);
                else if (Has("prepare-release")) PrepareRelease(service,core);
                else if (Has("release")) Release(service,core);
                else if (Has("register")) Register(service,core);
                else if (Has("render") || Has("render-japanese")) Render(service,core);
                else if (Has("reload") || Has("registered-reload"))
                {
                    Enabled(false); Assert(Party.All(p => InCaravan(p,caravan)),"Final reload keeps all original residents exclusively in the same surviving native caravan");
                    if (Has("registered-reload")) Assert(State(service,core).bases.Single(b => b.cityId == ids[14]).tile == caravan.Tile.tileId,"The ordinary registered city survives process reload at its exact native endpoint");
                    Unchanged(service,core,delegate { service.Tick(); },"Disabled paused update creates no native simulation or new UI journal");
                    done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
                }
            }
            catch (Exception error) { done=true; Log.Error("[INH-DELEGATED-UI] FAIL "+error); Application.Quit(1); }
        }
    }
}
