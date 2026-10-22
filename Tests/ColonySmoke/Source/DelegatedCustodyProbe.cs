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
    // Eight independent processes exercise both native endpoints of the custody journal.
    public sealed class DelegatedCustodyProbe : GameComponent
    {
        private bool done,travelStarted;
        private int travelTicks;
        private string[] ids;
        private Caravan caravan;
        private Pawn human,animal,escort;
        private Map origin;
        private PlanetTile destination;
        private const string StoreHuman="delegated-store-person", StoreAnimal="delegated-store-animal";
        private const string ReleaseHuman="delegated-release-person", ReleaseAnimal="delegated-release-animal";
        private const string ReservedConflict="delegated-reserved-conflict";
        private static readonly string[] Modes={"prepare","apply","commit","release-prepare","release-apply","release","final","reloadfinal"};
        public DelegatedCustodyProbe(Game game) { }
        private static bool Has(string mode) { return GenCommandLine.CommandLineArgPassed("inh-delegated-custody-"+mode); }
        private static bool Selected { get { return Modes.Any(Has); } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-delegated-"+key+".txt"); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-DELEGATED-CUSTODY] PASS "+message); }
        private static void Refuse(Action action,string message)
        { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,message); }
        private static ColonyState State(ColonyService service,CoreWorldComponent core)
        { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(service.SaveSnapshot(core))); }
        private static string Item(Thing t)
        { return t.GetUniqueLoadID()+":"+t.def.defName+":"+(t.Stuff == null ? "" : t.Stuff.defName)+":"+t.stackCount+":"+t.HitPoints; }
        private static IEnumerable<Thing> Possessions(Pawn p)
        { return p.inventory.innerContainer.Cast<Thing>().Concat(p.apparel == null ? new Thing[0] : p.apparel.WornApparel.Cast<Thing>()).Concat(p.equipment == null ? new Thing[0] : p.equipment.AllEquipmentListForReading.Cast<Thing>()); }
        private static string PawnState(Pawn p)
        {
            return string.Join("|",new[] {p.GetUniqueLoadID(),p.def.defName,p.Name == null ? "" : p.Name.ToStringFull,p.gender.ToString(),p.Faction.GetUniqueLoadID(),
                p.ageTracker.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture),p.ageTracker.AgeChronologicalTicks.ToString(CultureInfo.InvariantCulture),
                p.skills == null ? "" : string.Join(",",p.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+s.xpSinceLastLevel.ToString("R",CultureInfo.InvariantCulture)).ToArray()),
                string.Join(",",p.health.hediffSet.hediffs.Select(h => h.def.defName+":"+h.Severity.ToString("R",CultureInfo.InvariantCulture)+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                p.genes == null ? "" : "endo:"+string.Join(",",p.genes.Endogenes.Select(g => g.def.defName+":"+g.Active).ToArray())+";xeno:"+string.Join(",",p.genes.Xenogenes.Select(g => g.def.defName+":"+g.Active).ToArray()),
                p.Ideo == null ? "" : p.Ideo.GetUniqueLoadID(),string.Join(",",Possessions(p).Select(Item).OrderBy(s => s,StringComparer.Ordinal).ToArray()),
                p.needs == null ? "" : string.Join(",",p.needs.AllNeeds.OrderBy(n => n.def.defName,StringComparer.Ordinal).Select(n => n.def.defName+":"+n.CurLevel.ToString("R",CultureInfo.InvariantCulture)).ToArray())});
        }
        private static bool SameSavedPawn(Pawn pawn,string expected)
        {
            string[] actual=PawnState(pawn).Split('|'), old=expected.Split('|');
            if (actual.Length != old.Length || !actual.Where((s,i) => i != 5 && i != 6).SequenceEqual(old.Where((s,i) => i != 5 && i != 6))) return false;
            return new[] {5,6}.All(i => long.Parse(actual[i],CultureInfo.InvariantCulture)-long.Parse(old[i],CultureInfo.InvariantCulture) >= 0 &&
                long.Parse(actual[i],CultureInfo.InvariantCulture)-long.Parse(old[i],CultureInfo.InvariantCulture) <= 3);
        }
        private static string AbstractStock(CoreLedger ledger)
        { return string.Join(",",ledger.Entities.Where(e => e.Kind == "lot" && e.Authority == SimulationAuthority.Abstract).OrderBy(e => e.Id,StringComparer.Ordinal).Select(e => e.Id+":"+e.Quantity).ToArray()); }
        private static int Individuals(CoreLedger ledger) { return ledger.Entities.Count(e => e.Kind == "person" || e.Kind == "animal"); }
        private static string LocalHistory(CoreWorldComponent core)
        {
            return string.Join(";",core.ReadPawnCustodyTransfers(ColonyService.Actor).Where(t => t.CaravanId == null).Select(t =>
                string.Join(",",new[] {t.Id,t.Actor,t.Cause,t.EntityId,t.NativeId,t.CityId,t.IsRelease.ToString(),t.Stage.ToString(),t.MapId.ToString(),t.Cell.ToString()})).ToArray());
        }
        private static Pawn Original(CoreWorldComponent core,string native)
        {
            var all=core.GetDirectlyHeldThings().Cast<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns))
                .Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Concat(Find.WorldPawns.AllPawnsAliveOrDead).Distinct().ToArray();
            return all.Single(p => p.GetUniqueLoadID() == native);
        }
        private static bool InCaravan(Pawn p,Caravan c)
        { return !p.Spawned && p.holdingOwner == c.GetDirectlyHeldThings() && c.PawnsListForReading.Contains(p) && Find.WorldPawns.Contains(p) && Find.WorldObjects.Caravans.Count(v => v.PawnsListForReading.Contains(p)) == 1; }
        private static bool Held(Pawn p,CoreWorldComponent core)
        { return !p.Spawned && p.holdingOwner == core.GetDirectlyHeldThings() && core.GetDirectlyHeldThings().Contains(p) && !Find.WorldPawns.Contains(p) && !Find.WorldObjects.Caravans.Any(c => c.PawnsListForReading.Contains(p)); }
        private void CheckRegistry(ColonyService service,CoreWorldComponent core)
        {
            var state=State(service,core);
            Assert(state.people.Count(p => p.nativeId == ids[6] && p.id == ids[9]) == 1 && state.animals.Count(p => p.nativeId == ids[7] && p.id == ids[10]) == 1,
                "Registry retains one permanent person and animal identity for the original native pawns");
            Assert(core.Ledger.Entities.Count(e => e.BoundThingLoadId == ids[6]) == 1 && core.Ledger.GetEntity(ids[9]).BoundThingLoadId == ids[6] &&
                core.Ledger.Entities.Count(e => e.BoundThingLoadId == ids[7]) == 1 && core.Ledger.GetEntity(ids[10]).BoundThingLoadId == ids[7],"Permanent Core bindings still select the original native individuals");
            Assert(AbstractStock(core.Ledger) == ids[15] && Individuals(core.Ledger) == int.Parse(ids[16],CultureInfo.InvariantCulture),"Delegated custody creates no abstract inventory or replacement pawn identity");
            Assert(LocalHistory(core) == ids[17] && core.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion,"Current schema preserves the historical schema five local custody journal");
            var cargo=new[] {human,animal,escort}.SelectMany(p => p.inventory.innerContainer).Single(t => t.GetUniqueLoadID() == ids[18]);
            Assert(Item(cargo) == ids[19],"The existing native steel cargo ID and stack remain unchanged");
        }
        private void CheckFingerprint(CoreWorldComponent core,string key)
        {
            var expected=File.ReadAllLines(PathFor(key)); var pawns=new[] {human,animal,escort};
            Assert(pawns.Select((p,i) => SameSavedPawn(p,expected[i])).All(v => v),"Process restart and frozen custody preserve original names, ages, skills XP, wounds, genes, ideology, needs and every native possession");
            Assert(ReferenceEquals(Original(core,ids[6]),human) && ReferenceEquals(Original(core,ids[7]),animal) && ReferenceEquals(Original(core,ids[8]),escort),"Every native ID selects its sole original pawn object");
        }
        private PawnCustodySnapshot Transfer(CoreWorldComponent core,string id)
        { return core.ReadPawnCustodyTransfers(ColonyService.Actor).Single(t => t.Id == id); }
        private void CheckStage(CoreWorldComponent core,string id,PawnCustodyStage stage,bool release)
        {
            var t=Transfer(core,id);
            Assert(t.Stage == stage && t.IsRelease == release && t.EntityId == ids[9] && t.NativeId == ids[6] && t.CityId == ids[4] &&
                t.CaravanId == ids[5] && t.Tile.ToString(CultureInfo.InvariantCulture) == ids[3] && t.MapId == -1 && !t.Cell.IsValid,
                "Durable custody checkpoint retains the exact caravan, delegated tile, original pawn and phase");
            Assert(core.Ledger.GetEntity(id).Kind == "lease", "Each custody intent has one Core lease certificate");
        }
        private void CheckResidents(ColonyService service,CoreWorldComponent core,bool person,bool beast)
        {
            var rows=service.DelegatedResidents(ids[4]); var residents=core.ReadPawnCustodyResidents(ColonyService.Actor);
            for (int i=0;i<2;i++)
            {
                bool expected=i == 0 ? person : beast; string entity=ids[9+i],native=ids[6+i],store=ids[11+i]; Pawn p=i == 0 ? human : animal;
                Assert(rows.Count(r => r.EntityId == entity) == (expected ? 1 : 0),"Delegated resident row appears once exactly while its committed store is resident");
                if (!expected) { Assert(!residents.Any(r => r.EntityId == entity),"Returned or uncommitted original has no resident certificate"); continue; }
                var row=rows.Single(r => r.EntityId == entity); var resident=residents.Single(r => r.EntityId == entity);
                Assert(row.Name == p.LabelShort && row.Kind == (i == 0 ? "person" : "animal") && row.Items == Possessions(p).Sum(t => t.stackCount),"Resident name, species and possessions describe the original stored pawn");
                Assert(resident.NativeId == native && resident.CityId == ids[4] && resident.StoreTransferId == store && resident.CaravanId == ids[5] &&
                    resident.Tile.ToString(CultureInfo.InvariantCulture) == ids[3] && resident.SourceMapId == -1,"Resident retains the original caravan store endpoint and certificate");
            }
        }
        private void Save(CoreWorldComponent core,string name)
        {
            File.WriteAllLines(PathFor("ids"),ids); File.WriteAllText(PathFor("count"),core.Ledger.EntityCount.ToString(CultureInfo.InvariantCulture));
            GameDataSaveLoader.SaveGame(name); done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                var service=ColonyMod.Current;
                Assert(core.IsReady && service != null,"Core and Colony delegated custody initialize successfully");
                Find.TickManager.Pause();
                if (Has("prepare")) { Prepare(service,core); return; }
                if (ids == null) Restore(service,core);
                if (Has("final")) { Home(service,core); return; }
                if (Has("reloadfinal"))
                {
                    CheckFingerprint(core,"home-states"); CheckRegistry(service,core); CheckResidents(service,core,false,false);
                    Assert(new[] {human,animal,escort}.All(p => p.Spawned && p.Map == origin && !Find.WorldPawns.Contains(p)) && !Find.WorldObjects.Caravans.Any(c => c.GetUniqueLoadID() == ids[5]),"Final native reload finds the original party exclusively on its home map with its caravan gone");
                    int count=core.Ledger.EntityCount;
                    foreach (string receipt in new[] {ids[11],ids[12],ids[13],ids[14]})
                    {
                        service.ResumeDelegatedCustody(receipt); service.ResumeDelegatedCustody(receipt);
                        core.PawnCustody.ApplyNative(receipt); core.PawnCustody.Commit(receipt);
                    }
                    CheckReverseEndpoint(service,core);
                    Assert(core.Ledger.EntityCount == count && new[] {human,animal,escort}.All(p => p.Spawned && p.Map == origin && !Find.WorldPawns.Contains(p)),
                        "Completed Colony and Core receipt replay after caravan removal retains the original home party without another certificate");
                    done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); return;
                }
                CheckFingerprint(core,"states"); CheckRegistry(service,core);
                int before=core.Ledger.EntityCount;
                if (Has("apply"))
                {
                    CheckStage(core,ids[11],PawnCustodyStage.Prepared,false); Assert(InCaravan(human,caravan) && InCaravan(animal,caravan),"Prepared store reload leaves the originals in their native caravan");
                    Assert(caravan.pather.Moving && core.IsReady,"Prepared store reload remains ready even when its original caravan anchor began moving before saving");
                    Refuse(() => core.PawnCustody.ApplyNative(ids[11]),"A moving original anchor prevents applying its prepared native store");
                    Refuse(() => service.ResumeDelegatedCustody(ids[11]),"Colony recovery also refuses native storage until the original anchor stops");
                    Assert(core.Ledger.EntityCount == before && InCaravan(human,caravan) && InCaravan(animal,caravan) && Transfer(core,ids[11]).Stage == PawnCustodyStage.Prepared,
                        "Moving-anchor refusal leaves original native ownership and its existing prepared certificate unchanged");
                    caravan.pather.StopDead();
                    core.PawnCustody.ApplyNative(ids[11]); core.PawnCustody.ApplyNative(ids[11]); CheckStage(core,ids[11],PawnCustodyStage.Applying,false);
                    Assert(Held(human,core) && InCaravan(animal,caravan) && InCaravan(escort,caravan) && core.Ledger.EntityCount == before,"Applying store moves only the original person and allocates no further Core entity");
                    CheckResidents(service,core,false,false); CheckFingerprint(core,"states"); Save(core,"INH_DelegatedCustodyApplied");
                }
                else if (Has("commit"))
                {
                    CheckStage(core,ids[11],PawnCustodyStage.Applying,false); Assert(Held(human,core),"Applying store survives restart with the original exclusively held by Core");
                    service.ResumeDelegatedCustody(ids[11]); service.ResumeDelegatedCustody(ids[11]); CheckStage(core,ids[11],PawnCustodyStage.Committed,false);
                    Assert(service.DelegatedCustodyCommandsReady,"Normal delegated storage is enabled");
                    Refuse(() => service.StoreDelegatedPawn("delegated-last-escort",ids[4],caravan,escort),"The last eligible human escort cannot enter delegated storage");
                    Refuse(() => service.StoreDelegatedPawn(StoreHuman,ids[4],caravan,animal),"A completed store cause cannot be reused for another native pawn");
                    Assert(core.Ledger.EntityCount == before && InCaravan(escort,caravan),"Last-escort and changed-request refusal occur before allocation or native movement");
                    ids[12]=service.StoreDelegatedPawn(StoreAnimal,ids[4],caravan,animal);
                    Assert(service.StoreDelegatedPawn(StoreAnimal,ids[4],caravan,animal) == ids[12] && core.Ledger.EntityCount == before+2,"Animal store and replay use precisely one intent and its Core operation");
                    Assert(Held(human,core) && Held(animal,core) && InCaravan(escort,caravan),"Both original residents have exclusive Core ownership while one native escort remains");
                    CheckResidents(service,core,true,true); CheckFingerprint(core,"states"); CheckRegistry(service,core);
                    CheckFrozenInterval(core); CheckFingerprint(core,"states"); CheckRegistry(service,core); Save(core,"INH_DelegatedCustodyStored");
                }
                else if (Has("release-prepare"))
                {
                    CheckResidents(service,core,true,true); Assert(Held(human,core) && Held(animal,core),"Committed delegated storage survives restart with both original residents exclusively held");
                    Refuse(() => service.ReleaseDelegatedPawn("delegated-cross-release",ids[1],ids[9],caravan),"A direct-map city cannot release this delegated resident");
                    Refuse(() => core.PawnCustody.PrepareCaravanRelease(ColonyService.Actor,"delegated-wrong-tile-release",ids[9],ids[4],caravan,int.Parse(ids[3])+1),"Release at a different native tile is refused");
                    Assert(core.Ledger.EntityCount == before && Held(human,core),"Wrong-site and wrong-tile release refusals do not allocate or move the original");
                    var release=core.PawnCustody.PrepareCaravanRelease(ColonyService.Actor,ReleaseHuman,ids[9],ids[4],caravan,int.Parse(ids[3])); ids[13]=release.Id;
                    Assert(core.PawnCustody.PrepareCaravanRelease(ColonyService.Actor,ReleaseHuman,ids[9],ids[4],caravan,int.Parse(ids[3])).Id == ids[13] && core.Ledger.EntityCount == before+2,"Prepared release replay retains precisely one Core lease and operation");
                    Refuse(() => core.PawnCustody.PrepareCaravanRelease(ColonyService.Actor,ReleaseHuman,ids[10],ids[4],caravan,int.Parse(ids[3])),"A release cause cannot select a different original resident");
                    Assert(caravan.pather.StartPath(origin.Tile,null,true,false) && caravan.pather.Moving,"The anchor can begin its ordinary native homeward route while originals remain held");
                    Refuse(() => service.ResumeDelegatedCustody(ids[13]),"A moving caravan anchor cannot apply a pending delegated release");
                    Refuse(() => service.ReleaseDelegatedPawn("delegated-moving-release",ids[4],ids[10],caravan),"A moving caravan anchor cannot allocate a new delegated release");
                    Assert(core.Ledger.EntityCount == before+2 && Held(human,core) && Held(animal,core),"Moving-anchor refusal retains both exact originals without certificate allocation");
                    CheckStage(core,ids[13],PawnCustodyStage.Prepared,true); CheckFingerprint(core,"states"); Save(core,"INH_DelegatedCustodyReleasePrepared");
                }
                else if (Has("release-apply"))
                {
                    CheckStage(core,ids[13],PawnCustodyStage.Prepared,true); Assert(Held(human,core),"Prepared release restart retains its original in the Core holder");
                    Assert(core.IsReady && caravan.pather.Moving && caravan.Tile == new PlanetTile(int.Parse(ids[3]),0),"Prepared release reload remains ready with a moving original anchor and both assets safely held");
                    Refuse(() => service.ResumeDelegatedCustody(ids[13]),"Restarted moving-anchor recovery refuses before moving its stored original");
                    Refuse(() => service.ReleaseDelegatedPawn("delegated-moving-reload-release",ids[4],ids[10],caravan),"Restarted moving anchor cannot allocate another resident release");
                    Assert(core.Ledger.EntityCount == before && Held(human,core) && Held(animal,core) && Transfer(core,ids[13]).Stage == PawnCustodyStage.Prepared,
                        "Moving-anchor reload refusal preserves both exclusive originals and the prepared certificate");
                    caravan.pather.StopDead();
                    core.PawnCustody.ApplyNative(ids[13]); core.PawnCustody.ApplyNative(ids[13]); CheckStage(core,ids[13],PawnCustodyStage.Applying,true);
                    Assert(InCaravan(human,caravan) && Held(animal,core) && core.Ledger.EntityCount == before,"Applying release returns the exact human to its original caravan without another certificate");
                    CheckResidents(service,core,true,true); CheckFingerprint(core,"states"); Save(core,"INH_DelegatedCustodyReleaseApplied");
                }
                else if (Has("release"))
                {
                    CheckStage(core,ids[13],PawnCustodyStage.Applying,true); Assert(InCaravan(human,caravan),"Applying release restart preserves the original person's exclusive caravan ownership");
                    service.ResumeDelegatedCustody(ids[13]); service.ResumeDelegatedCustody(ids[13]);
                    ids[14]=service.ReleaseDelegatedPawn(ReleaseAnimal,ids[4],ids[10],caravan);
                    Assert(service.ReleaseDelegatedPawn(ReleaseAnimal,ids[4],ids[10],caravan) == ids[14],"Animal return cause replays its original release certificate");
                    service.ResumeDelegatedCustody(ids[14]); service.ResumeDelegatedCustody(ids[14]);
                    Assert(core.Ledger.EntityCount == before+2 && new[] {human,animal,escort}.All(p => InCaravan(p,caravan)),"Both original residents return once to their original caravan, using one animal release intent");
                    CheckStage(core,ids[13],PawnCustodyStage.Committed,true); CheckResidents(service,core,false,false); CheckFingerprint(core,"states"); CheckRegistry(service,core); Save(core,"INH_DelegatedCustodyReturned");
                }
                else throw new InvalidOperationException("No delegated custody checkpoint was selected");
            }
            catch (Exception e) { done=true; Log.Error("[INH-DELEGATED-CUSTODY] FAIL "+e); Application.Quit(1); }
        }
        private void Prepare(ColonyService service,CoreWorldComponent core)
        {
            if (!travelStarted)
            {
                Assert(service.DelegatedCustodyCommandsReady,"Delegated custody commands are enabled for the travel fixture");
                var state=State(service,core); var bases=service.CustodyBases();
                var from=bases.First(b => service.AvailableCustodyPawns(b.Id).Count(p => p.IsColonist) == 2 && service.AvailableCustodyPawns(b.Id).Any(p => p.def.defName == "Muffalo"));
                origin=Find.Maps.Single(m => m.uniqueID == from.MapId); var party=service.AvailableCustodyPawns(from.Id);
                human=party.First(p => p.IsColonist && state.people.Any(r => r.nativeId == p.GetUniqueLoadID())); escort=party.Single(p => p.IsColonist && p != human);
                animal=party.First(p => p.def.defName == "Muffalo" && state.animals.Any(r => r.nativeId == p.GetUniqueLoadID()));
                string person=state.people.Single(p => p.nativeId == human.GetUniqueLoadID()).id, beast=state.animals.Single(p => p.nativeId == animal.GetUniqueLoadID()).id;
                Assert(!state.physical.Any(r => r.stage != PhysicalStage.Arrived && r.stage != PhysicalStage.Cancelled && r.party.Concat(r.animals).Any(id => id == person || id == beast)),"Raw native caravan party is free of existing active physical manifests");
                Assert(core.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion && core.ReadPawnCustodyTransfers(ColonyService.Actor).Any(t => t.CaravanId == null) &&
                    core.ReadPawnCustodyTransfers(ColonyService.Actor).Where(t => t.CaravanId == null).All(t => t.Stage == PawnCustodyStage.Committed),"The schema five final local fixture migrates with its completed local journal intact");
                var neighbors=new List<PlanetTile>(); Find.World.grid.GetTileNeighbors(origin.Tile,neighbors);
                destination=neighbors.First(t => t == new PlanetTile(t.tileId,0) && !Find.World.grid[t].WaterCovered && !Find.World.grid[t].PrimaryBiome.impassable &&
                    Find.World.grid[t].hilliness != Hilliness.Impassable && !Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == t));
                Thing steel=origin.listerThings.ThingsOfDef(ThingDefOf.Steel).First(t => t.stackCount >= 2); Thing[] moved;
                var otherDirect=bases.First(b => b.Id != from.Id && b.MapId != from.MapId);
                int reserveBefore=core.Ledger.EntityCount;
                string reservation=service.ReservePhysicalTransport(ReservedConflict,from.Id,otherDirect.Id,new[] {human,animal},new[] {new CargoSelection(steel,1)});
                var reserved=State(service,core).physical.Single(r => r.cause == ReservedConflict);
                Assert(reserved.stage == PhysicalStage.Reserved && reserved.shipmentId == reservation && reserved.caravanId == null && reserved.departureReceipt == null &&
                    reserved.party.SequenceEqual(new[] {person}) && reserved.animals.SequenceEqual(new[] {beast}) && core.Ledger.EntityCount == reserveBefore+2,
                    "Conflict fixture reserves the already registered original person and animal in one undeparted physical manifest");
                Assert(!State(service,core).people.Any(p => p.nativeId == escort.GetUniqueLoadID()),"Conflict reservation leaves the original human escort unregistered");
                // Deliberate test fixture bypass: raw native departure leaves the existing
                // manifest Reserved so the arrived bridge must enforce its transport lock.
                caravan=PhysicalCaravanAdapter.Depart(core,origin,new[] {human,escort,animal},new[] {new CargoSelection(steel,1)},out moved);
                Assert(moved.Length == 1 && moved[0].def == ThingDefOf.Steel && moved[0].stackCount == 1 && new[] {human,animal,escort}.All(p => InCaravan(p,caravan)),"Raw standard departure moves both original humans, the original muffalo and one existing steel unit");
                ids=new[] {core.Header.SaveScopeId,from.Id,origin.uniqueID.ToString(),destination.tileId.ToString(),"",caravan.GetUniqueLoadID(),human.GetUniqueLoadID(),animal.GetUniqueLoadID(),escort.GetUniqueLoadID(),person,beast,"","","","",AbstractStock(core.Ledger),Individuals(core.Ledger).ToString(),LocalHistory(core),moved[0].GetUniqueLoadID(),Item(moved[0])};
                Assert(caravan.pather.StartPath(destination,null,true,false) && caravan.pather.Moving,"Native standard path follower starts the outbound route"); travelStarted=true;
            }
            if (!Travel()) return;
            Assert(caravan.Tile == destination && !caravan.pather.Moving && new[] {human,animal,escort}.All(p => InCaravan(p,caravan)),"Standard native travel reaches the empty delegated tile with the exact original party");
            int count=core.Ledger.EntityCount; ids[4]=service.RegisterDelegatedSite("delegated-probe-site","Delegated custody probe",destination.tileId);
            int registered=core.Ledger.EntityCount;
            Assert(service.RegisterDelegatedSite("delegated-probe-site","Delegated custody probe",destination.tileId) == ids[4] && core.Ledger.EntityCount == registered,"Delegated site registration replays one anchored city");
            Assert(registered == count+2,"One new delegated city uses precisely one Core creation operation");
            Refuse(() => service.RegisterDelegatedSite("delegated-occupied-origin","Occupied home tile",origin.Tile.tileId),"A delegated site cannot occupy the original direct map tile");
            Refuse(() => service.RegisterDelegatedSite("delegated-duplicate-site","Duplicate site tile",destination.tileId),"Another site key cannot allocate a duplicate delegated tile");
            Refuse(() => service.RegisterDelegatedSite("delegated-probe-site","Changed site name",destination.tileId),"An existing site key cannot change its registered name");
            Assert(core.Ledger.EntityCount == registered && State(service,core).bases.Count(b => b.cityId == ids[4]) == 1,
                "Occupied-tile, duplicate-tile and changed-name registration refusals allocate no Core operation or city");
            var activeReservation=State(service,core).physical.Single(r => r.cause == ReservedConflict);
            Assert(activeReservation.stage == PhysicalStage.Reserved && activeReservation.caravanId == null && activeReservation.departureReceipt == null,
                "The deliberate undeparted manifest remains active after the original party's raw native arrival");
            string personBefore=PawnState(human), animalBefore=PawnState(animal);
            Refuse(() => service.StoreDelegatedPawn("delegated-reserved-person-store",ids[4],caravan,human),"An arrived original person reserved by active physical transport cannot enter delegated storage");
            Refuse(() => service.StoreDelegatedPawn("delegated-reserved-animal-store",ids[4],caravan,animal),"An arrived original animal reserved by active physical transport cannot enter delegated storage");
            Assert(core.Ledger.EntityCount == registered && PawnState(human) == personBefore && PawnState(animal) == animalBefore &&
                InCaravan(human,caravan) && InCaravan(animal,caravan),"Active physical reservation refuses both delegated stores before allocation or native movement");
            service.CancelPhysical(ReservedConflict); service.CancelPhysical(ReservedConflict);
            var cancelled=State(service,core).physical.Single(r => r.cause == ReservedConflict);
            Assert(cancelled.stage == PhysicalStage.Cancelled && cancelled.caravanId == null && cancelled.departureReceipt == null && !string.IsNullOrEmpty(cancelled.cancelReceipt) &&
                core.Ledger.GetEntity(cancelled.cancelReceipt).Kind == "lease" && core.Ledger.EntityCount == registered+2,
                "Cancelling and replaying the undeparted conflict retains one Core cancellation receipt without adopting the raw caravan");
            CheckRegistry(service,core);
            Assert(InCaravan(human,caravan) && InCaravan(animal,caravan) && PawnState(human) == personBefore && PawnState(animal) == animalBefore,
                "Cancelling the reservation unlocks the bridge while preserving original native pawns, cargo and permanent identities");
            registered=core.Ledger.EntityCount;
            string localCause=core.ReadPawnCustodyTransfers(ColonyService.Actor).First(t => t.CaravanId == null && !t.IsRelease).Cause;
            Assert(!State(service,core).people.Any(p => p.nativeId == escort.GetUniqueLoadID()) && !core.Ledger.Entities.Any(e => e.BoundThingLoadId == escort.GetUniqueLoadID()),
                "Endpoint-collision fixture uses the original escort with no registered person identity");
            Refuse(() => service.StoreDelegatedPawn(localCause,ids[4],caravan,escort),"An existing local map custody cause cannot allocate delegated storage for an unregistered original escort");
            Assert(core.Ledger.EntityCount == registered && !State(service,core).people.Any(p => p.nativeId == escort.GetUniqueLoadID()) &&
                !core.Ledger.Entities.Any(e => e.BoundThingLoadId == escort.GetUniqueLoadID()) && InCaravan(escort,caravan),
                "Map-to-caravan cause collision refuses before person registration, Core allocation or movement");
            File.WriteAllLines(PathFor("states"),new[] {PawnState(human),PawnState(animal),PawnState(escort)});
            Refuse(() => service.StoreDelegatedPawn("delegated-cross-store",ids[1],caravan,human),"A direct city cannot store a caravan original at this delegated tile");
            Refuse(() => core.PawnCustody.PrepareCaravanStore(ColonyService.Actor,"delegated-wrong-tile-store",ids[9],ids[4],caravan,destination.tileId+1),"An incorrect requested tile cannot prepare native caravan storage");
            Assert(core.Ledger.EntityCount == registered && InCaravan(human,caravan),"Wrong-site and wrong-tile store refusals occur before allocation or movement");
            var prepared=core.PawnCustody.PrepareCaravanStore(ColonyService.Actor,StoreHuman,ids[9],ids[4],caravan,destination.tileId); ids[11]=prepared.Id;
            Assert(core.PawnCustody.PrepareCaravanStore(ColonyService.Actor,StoreHuman,ids[9],ids[4],caravan,destination.tileId).Id == ids[11] && core.Ledger.EntityCount == registered+2,"Prepared store replay uses precisely one Core lease and operation");
            Refuse(() => core.PawnCustody.PrepareCaravanStore(ColonyService.Actor,StoreHuman,ids[10],ids[4],caravan,destination.tileId),"Reusing a store cause with another pawn refuses the changed request");
            CheckStage(core,ids[11],PawnCustodyStage.Prepared,false); CheckResidents(service,core,false,false); CheckFingerprint(core,"states"); CheckRegistry(service,core);
            Assert(caravan.pather.StartPath(origin.Tile,null,true,false) && caravan.pather.Moving && caravan.Tile == destination && InCaravan(human,caravan),
                "Prepared store saves after its native anchor starts moving at the same tile without moving the pending original");
            Save(core,"INH_DelegatedCustodyPrepared");
        }
        private bool Travel()
        {
            // Full ordinary native ticks include path costs, resting and needs. No tile writes or teleportation.
            for (int i=0;i<300 && caravan.pather.Moving;i++)
            {
                int before=Find.TickManager.TicksGame; Find.TickManager.DoSingleTick();
                if (Find.TickManager.TicksGame != before+1) throw new InvalidOperationException("Travel must advance one ordinary game tick per step"); travelTicks++;
            }
            if (travelTicks > 180000) throw new InvalidOperationException("Native neighboring-tile route exceeded three ordinary game days");
            if (travelTicks > 0 && travelTicks%6000 == 0) Log.Message("[INH-DELEGATED-CUSTODY] STEP native travel ticks="+travelTicks);
            return !caravan.pather.Moving;
        }
        private void Restore(ColonyService service,CoreWorldComponent core)
        {
            ids=File.ReadAllLines(PathFor("ids")); origin=Find.Maps.Single(m => m.uniqueID.ToString(CultureInfo.InvariantCulture) == ids[2]);
            human=Original(core,ids[6]); animal=Original(core,ids[7]); escort=Original(core,ids[8]);
            Assert(core.Header.SaveScopeId == ids[0] && core.Ledger.EntityCount == int.Parse(File.ReadAllText(PathFor("count")),CultureInfo.InvariantCulture),"Separate-process restart preserves world scope and the exact checkpoint Core entity count");
            if (Has("reloadfinal")) return;
            caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == ids[5]);
            Assert(caravan.Tile == new PlanetTile(int.Parse(ids[3],CultureInfo.InvariantCulture),0) &&
                (Has("apply") || Has("release-apply") ? caravan.pather.Moving : !caravan.pather.Moving) && InCaravan(escort,caravan),
                "The original caravan and escort survive checkpoint restart at the saved delegated tile with the expected native path state");
        }
        private void Home(ColonyService service,CoreWorldComponent core)
        {
            if (!travelStarted)
            {
                CheckFingerprint(core,"states"); CheckRegistry(service,core); CheckResidents(service,core,false,false);
                Assert(new[] {human,animal,escort}.All(p => InCaravan(p,caravan)),"Returned restart retains the exact original native party in its original caravan");
                int count=core.Ledger.EntityCount; service.ResumeDelegatedCustody(ids[13]); service.ResumeDelegatedCustody(ids[14]);
                Assert(core.Ledger.EntityCount == count,"Returned recovery replay adds no new intent or certificate");
                Assert(caravan.pather.StartPath(origin.Tile,null,true,false),"The original caravan starts its ordinary native route home"); travelStarted=true;
            }
            if (!Travel()) return;
            Assert(caravan.Tile == origin.Tile,"Native path travel reaches the original home map tile");
            PhysicalCaravanAdapter.EnterDestination(core,caravan,origin);
            Assert(caravan.Destroyed && new[] {human,animal,escort}.All(p => p.Spawned && p.Map == origin && !Find.WorldPawns.Contains(p)),"Standard native map entry returns all original individuals exclusively to their original home map");
            CheckReverseEndpoint(service,core);
            CheckRegistry(service,core); CheckResidents(service,core,false,false);
            // The homeward route resumes ordinary simulation; preserve its resulting state for final reload.
            File.WriteAllLines(PathFor("home-states"),new[] {PawnState(human),PawnState(animal),PawnState(escort)});
            Save(core,"INH_DelegatedCustodyHome");
        }
        private void CheckReverseEndpoint(ColonyService service,CoreWorldComponent core)
        {
            Assert(service.CustodyCommandsReady,"Local custody is enabled so reverse endpoint collision is exercised before normal registration");
            int count=core.Ledger.EntityCount; string native=escort.GetUniqueLoadID();
            Assert(!State(service,core).people.Any(p => p.nativeId == native) && !core.Ledger.Entities.Any(e => e.BoundThingLoadId == native),
                "Home endpoint-collision fixture still has the original unregistered escort");
            Refuse(() => service.StoreLocalPawn(StoreHuman,ids[1],escort),"A delegated store cause cannot allocate local map custody for the returned unregistered escort");
            Refuse(() => service.StoreLocalPawn(StoreHuman,ids[1],escort),"Repeated reverse endpoint collision continues to refuse its immutable historical receipt");
            Refuse(() => service.ReleaseLocalPawn(ReleaseHuman,ids[1],ids[9]),"A completed delegated release receipt cannot become a local map release");
            Assert(core.Ledger.EntityCount == count && !State(service,core).people.Any(p => p.nativeId == native) &&
                !core.Ledger.Entities.Any(e => e.BoundThingLoadId == native) && escort.Spawned && escort.Map == origin,
                "Caravan-to-map cause collision and historical release refusal create no person identity, certificate or native movement");
        }
        private void CheckFrozenInterval(CoreWorldComponent core)
        {
            string[] heldBefore={PawnState(human),PawnState(animal)}; string escortBefore=PawnState(escort);
            int clock=Find.TickManager.TicksGame, count=core.Ledger.EntityCount;
            for (int i=0;i<1200;i++)
            {
                int before=Find.TickManager.TicksGame; Find.TickManager.DoSingleTick();
                if (Find.TickManager.TicksGame != before+1) throw new InvalidOperationException("Frozen interval must advance ordinary game ticks");
            }
            Assert(Find.TickManager.TicksGame == clock+1200 && Held(human,core) && Held(animal,core) && InCaravan(escort,caravan),
                "Two original residents remain exclusively Core-held throughout 1,200 ordinary world ticks while the native escort remains active");
            var expected=File.ReadAllLines(PathFor("states"));
            for (int i=0;i<2;i++)
            {
                string[] old=heldBefore[i].Split('|'), current=PawnState(i == 0 ? human : animal).Split('|');
                Assert(old.Length == current.Length && old.Where((s,j) => j != 6).SequenceEqual(current.Where((s,j) => j != 6)),
                    "Held original biological age, needs, skills XP, health, genes, ideology and native possessions remain exactly frozen during ordinary elapsed time");
                long elapsed=long.Parse(current[6],CultureInfo.InvariantCulture)-long.Parse(old[6],CultureInfo.InvariantCulture);
                Assert(elapsed == 1200,"Held original chronological age follows the global clock by exactly 1,200 ticks without biological aging");
                // Only the global-clock field changes in the original held-pawn baseline.
                string[] row=expected[i].Split('|'); row[6]=current[6]; expected[i]=string.Join("|",row);
            }
            Assert(PawnState(escort).Split('|')[12] != escortBefore.Split('|')[12],"The original caravan escort continues ordinary native needs simulation during the frozen interval");
            expected[2]=PawnState(escort); File.WriteAllLines(PathFor("states"),expected);
            Assert(core.Ledger.EntityCount == count,"Elapsed frozen storage creates no Core identity or abstract simulation record");
        }
    }
}
