using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Core
{
    public enum PawnCustodyStage { Prepared, Applying, Committed }
    public sealed class PawnCustodySnapshot
    {
        public readonly string Id, Actor, Cause, EntityId, NativeId, CityId, CaravanId;
        public readonly bool IsRelease;
        public readonly PawnCustodyStage Stage;
        public readonly int MapId, Tile;
        public readonly IntVec3 Cell;
        internal PawnCustodySnapshot(PawnCustodyTransfer t)
        { Id=t.id; Actor=t.actor; Cause=t.cause; EntityId=t.entity; NativeId=t.native; CityId=t.city; IsRelease=t.release; Stage=t.stage; MapId=t.mapId; Cell=t.cell; CaravanId=t.caravanId; Tile=t.tile; }
    }
    public sealed class PawnCustodyResidentSnapshot
    {
        public readonly string EntityId, NativeId, CityId, StoreTransferId, CaravanId;
        public readonly int SourceMapId, Tile;
        internal PawnCustodyResidentSnapshot(PawnCustodyResident r,PawnCustodyTransfer t)
        { EntityId=r.entity; NativeId=r.native; CityId=r.city; StoreTransferId=r.transfer; SourceMapId=t.mapId; CaravanId=t.caravanId; Tile=t.tile; }
    }
    internal sealed class PawnCustodyResident : IExposable
    {
        internal string entity,city,native,transfer;
        public PawnCustodyResident() { }
        public void ExposeData()
        { Scribe_Values.Look(ref entity,"entity"); Scribe_Values.Look(ref city,"city"); Scribe_Values.Look(ref native,"native"); Scribe_Values.Look(ref transfer,"transfer"); }
    }
    internal sealed class PawnCustodyTransfer : IExposable
    {
        internal string id,actor,cause,entity,native,city,digest,caravanId;
        internal bool release;
        internal int mapId,rotation;
        internal int tile=-1;
        internal IntVec3 cell;
        internal PawnCustodyStage stage;
        public PawnCustodyTransfer() { }
        public void ExposeData()
        {
            Scribe_Values.Look(ref id,"id"); Scribe_Values.Look(ref actor,"actor"); Scribe_Values.Look(ref cause,"cause");
            Scribe_Values.Look(ref entity,"entity"); Scribe_Values.Look(ref native,"native"); Scribe_Values.Look(ref city,"city"); Scribe_Values.Look(ref digest,"digest");
            Scribe_Values.Look(ref release,"release",false,true); Scribe_Values.Look(ref mapId,"mapId",-1,true);
            Scribe_Values.Look(ref rotation,"rotation",-1,true);
            Scribe_Values.Look(ref caravanId,"caravanId"); Scribe_Values.Look(ref tile,"tile",-1,true);
            Scribe_Values.Look(ref cell,"cell",IntVec3.Invalid,true); Scribe_Values.Look(ref stage,"stage",(PawnCustodyStage)(-1),true);
        }
        internal string Hash()
        {
            var b=new StringBuilder();
            // Keep historical map certificates byte-for-byte unchanged.
            if (caravanId != null)
            {
                b.Append("pawn-custody/caravan-v1:");
                foreach (string s in new[] {caravanId,tile.ToString(CultureInfo.InvariantCulture)})
                    b.Append(s.Length.ToString(CultureInfo.InvariantCulture)+":"+s);
            }
            foreach (string s in new[] {actor,cause,entity,native,city,release.ToString(),mapId.ToString(CultureInfo.InvariantCulture),cell.ToString(),rotation.ToString(CultureInfo.InvariantCulture)})
                b.Append(s == null ? "-1:" : s.Length.ToString(CultureInfo.InvariantCulture)+":"+s);
            using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-","").ToLowerInvariant();
        }
    }
    // Core-owned native storage only. No abstract inventory conversion or needs simulation.
    public sealed class PawnCustody : IExposable
    {
        private ThingOwner<Pawn> held;
        private List<PawnCustodyResident> residents=new List<PawnCustodyResident>();
        private List<PawnCustodyTransfer> transfers=new List<PawnCustodyTransfer>();
        private int residentCount,transferCount;
        private readonly IThingHolder owner;
        public PawnCustody(IThingHolder owner) { this.owner=owner; held=new ThingOwner<Pawn>(owner); }
        internal ThingOwner Container { get { return held; } }
        public int StoredCount { get { return held.Count; } }
        public IEnumerable<PawnCustodySnapshot> Transfers { get { return transfers.Select(t => new PawnCustodySnapshot(t)).ToArray(); } }
        public IEnumerable<PawnCustodyResidentSnapshot> Residents { get { return residents.Select(r => new PawnCustodyResidentSnapshot(r,FindTransfer(r.transfer))).ToArray(); } }
        public bool IsStored(string entityId) { return residents.Any(r => r.entity == entityId); }
        private CoreWorldComponent Core
        {
            get
            {
                var core=owner as CoreWorldComponent;
                if (core == null || Find.World == null || Find.World.GetComponent<CoreWorldComponent>() != core || !core.IsReady)
                    throw new InvalidOperationException("Pawn custody belongs to an unavailable world");
                return core;
            }
        }
        private Pawn PawnFor(CoreLedger ledger,string entityId)
        {
            var e=ledger.GetEntity(entityId);
            var pawn=ledger.SavedState.entities.Single(r => r.id == entityId).binding as Pawn;
            if (e.Status != EntityStatus.Active || (e.Kind != "person" && e.Kind != "animal") || e.Authority != SimulationAuthority.Physical || pawn == null || pawn.Destroyed || pawn.Dead)
                throw new InvalidOperationException("Stored individual is unavailable");
            return pawn;
        }
        private static void City(CoreLedger ledger,string cityId)
        { var city=ledger.GetEntity(cityId); if (city.Kind != "city" || city.Status != EntityStatus.Active) throw new InvalidOperationException("Storage requires an active city"); }
        private static Map MapFor(int id)
        { var map=Find.Maps.SingleOrDefault(m => m.uniqueID == id); if (map == null || !map.IsPlayerHome) throw new InvalidOperationException("Custody transfer requires an available player map"); return map; }
        private static void Eligible(Pawn p)
        {
            if (p.Faction != Faction.OfPlayer || p.Downed || p.Drafted || p.InMentalState ||
                !(p.IsColonist && !p.DevelopmentalStage.Baby() || p.RaceProps.Animal)) throw new InvalidOperationException("Individual cannot enter storage now");
        }
        private static bool OnMap(Pawn pawn,Map map)
        { return pawn.Spawned && pawn.Map == map && pawn.holdingOwner == map.spawnedThings && !Find.WorldPawns.Contains(pawn); }
        private bool HeldExclusively(Pawn pawn)
        { return !pawn.Spawned && pawn.holdingOwner == held && held.Contains(pawn) && !Find.WorldPawns.Contains(pawn) &&
            !Find.WorldObjects.Caravans.Any(c => c.PawnsListForReading.Contains(pawn)); }
        private static Caravan CaravanFor(string id,int tile)
        {
            var caravan=Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == id);
            CheckCaravan(caravan,tile); return caravan;
        }
        private static void CheckCaravan(Caravan caravan,int tile)
        {
            if (caravan == null || caravan.Destroyed || !caravan.Spawned || !Find.WorldObjects.Contains(caravan) ||
                !caravan.IsPlayerControlled || caravan.Faction != Faction.OfPlayer || tile < 0 || !caravan.Tile.Valid ||
                caravan.Tile != new PlanetTile(tile,0) || caravan.pather == null || caravan.pather.Moving)
                throw new InvalidOperationException("Custody requires the same stopped player caravan at its saved surface tile");
        }
        private static bool InCaravan(Pawn pawn,Caravan caravan)
        {
            return !pawn.Spawned && pawn.holdingOwner == caravan.GetDirectlyHeldThings() &&
                caravan.PawnsListForReading.Contains(pawn) && Find.WorldPawns.Contains(pawn) &&
                Find.WorldObjects.Caravans.Count(c => c.PawnsListForReading.Contains(pawn)) == 1;
        }
        private static bool HumanEscort(Pawn pawn)
        {
            return !pawn.Destroyed && !pawn.Dead && pawn.Faction == Faction.OfPlayer && pawn.IsColonist &&
                pawn.RaceProps.Humanlike && !pawn.DevelopmentalStage.Baby() && !pawn.Downed && !pawn.Drafted && !pawn.InMentalState;
        }
        private static void CheckEscort(Caravan caravan,Pawn stored)
        {
            if (!caravan.PawnsListForReading.Any(p => p != stored && HumanEscort(p) && InCaravan(p,caravan)))
                throw new InvalidOperationException("Caravan storage must leave another eligible human escort");
        }
        private PawnCustodyTransfer FindTransfer(string id)
        { var t=transfers.SingleOrDefault(x => x.id == id); if (t == null) throw new InvalidOperationException("Unknown custody transfer"); return t; }
        public PawnCustodySnapshot PrepareStore(string actor,string cause,string entityId,string cityId)
        {
            var core=Core; core.Ledger.AssertWriteAccess(actor);
            var previous=transfers.SingleOrDefault(t => t.actor == actor && t.cause == cause);
            if (previous != null) return Repeat(previous,entityId,cityId,false,-1,IntVec3.Invalid);
            Pawn pawn=PawnFor(core.Ledger,entityId); City(core.Ledger,cityId); Eligible(pawn);
            if (!pawn.Spawned || !pawn.Map.IsPlayerHome || !OnMap(pawn,pawn.Map) || IsStored(entityId)) throw new InvalidOperationException("Individual is not on a direct player map");
            return Prepare(new PawnCustodyTransfer {actor=actor,cause=cause,entity=entityId,native=pawn.GetUniqueLoadID(),city=cityId,mapId=pawn.Map.uniqueID,cell=pawn.Position,rotation=pawn.Rotation.AsInt});
        }
        public PawnCustodySnapshot PrepareRelease(string actor,string cause,string entityId,string cityId,Map map,IntVec3 cell)
        {
            var core=Core; core.Ledger.AssertWriteAccess(actor);
            if (map == null) throw new ArgumentNullException("map");
            var previous=transfers.SingleOrDefault(t => t.actor == actor && t.cause == cause);
            if (previous != null) return Repeat(previous,entityId,cityId,true,map.uniqueID,cell);
            Pawn pawn=PawnFor(core.Ledger,entityId); City(core.Ledger,cityId); Eligible(pawn);
            var resident=residents.SingleOrDefault(r => r.entity == entityId);
            if (resident == null || resident.city != cityId || !HeldExclusively(pawn)) throw new InvalidOperationException("Individual is not held by the requested city");
            if (FindTransfer(resident.transfer).caravanId != null) throw new InvalidOperationException("Caravan residents must return to their original caravan");
            CheckCell(map,cell,pawn,pawn.Rotation);
            return Prepare(new PawnCustodyTransfer {actor=actor,cause=cause,entity=entityId,native=pawn.GetUniqueLoadID(),city=cityId,release=true,mapId=map.uniqueID,cell=cell,rotation=pawn.Rotation.AsInt});
        }
        public PawnCustodySnapshot PrepareCaravanStore(string actor,string cause,string entityId,string cityId,Caravan caravan,int tile)
        {
            var core=Core; core.Ledger.AssertWriteAccess(actor);
            if (caravan == null) throw new ArgumentNullException("caravan");
            var previous=transfers.SingleOrDefault(t => t.actor == actor && t.cause == cause);
            if (previous != null) return RepeatCaravan(previous,entityId,cityId,false,caravan.GetUniqueLoadID(),tile);
            CheckCaravan(caravan,tile); Pawn pawn=PawnFor(core.Ledger,entityId); City(core.Ledger,cityId); Eligible(pawn);
            CheckCarried(pawn);
            if (IsStored(entityId) || !InCaravan(pawn,caravan)) throw new InvalidOperationException("Store requires the original pawn exclusively in the requested caravan");
            CheckEscort(caravan,pawn);
            return Prepare(new PawnCustodyTransfer {actor=actor,cause=cause,entity=entityId,native=pawn.GetUniqueLoadID(),city=cityId,
                caravanId=caravan.GetUniqueLoadID(),tile=tile,mapId=-1,cell=IntVec3.Invalid,rotation=pawn.Rotation.AsInt});
        }
        public PawnCustodySnapshot PrepareCaravanRelease(string actor,string cause,string entityId,string cityId,Caravan caravan,int tile)
        {
            var core=Core; core.Ledger.AssertWriteAccess(actor);
            if (caravan == null) throw new ArgumentNullException("caravan");
            var previous=transfers.SingleOrDefault(t => t.actor == actor && t.cause == cause);
            if (previous != null) return RepeatCaravan(previous,entityId,cityId,true,caravan.GetUniqueLoadID(),tile);
            CheckCaravan(caravan,tile); Pawn pawn=PawnFor(core.Ledger,entityId); City(core.Ledger,cityId); Eligible(pawn);
            CheckCarried(pawn);
            CheckResident(entityId,cityId,pawn,caravan.GetUniqueLoadID(),tile);
            if (!HeldExclusively(pawn)) throw new InvalidOperationException("Release original pawn is not exclusively held");
            return Prepare(new PawnCustodyTransfer {actor=actor,cause=cause,entity=entityId,native=pawn.GetUniqueLoadID(),city=cityId,release=true,
                caravanId=caravan.GetUniqueLoadID(),tile=tile,mapId=-1,cell=IntVec3.Invalid,rotation=pawn.Rotation.AsInt});
        }
        private void CheckResident(string entity,string city,Pawn pawn,string caravanId,int tile)
        {
            var resident=residents.SingleOrDefault(r => r.entity == entity);
            if (resident == null || resident.city != city || resident.native != pawn.GetUniqueLoadID())
                throw new InvalidOperationException("Release has no matching original resident");
            var store=FindTransfer(resident.transfer);
            if (store.release || store.stage != PawnCustodyStage.Committed || store.caravanId != caravanId ||
                caravanId != null && store.tile != tile) throw new InvalidOperationException("Release destination disagrees with its originating custody store");
        }
        private static void CheckCarried(Pawn pawn)
        {
            if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
                throw new InvalidOperationException("Caravan custody requires an individual carrying no other thing");
        }
        private static void CheckCell(Map map,IntVec3 cell,Pawn pawn,Rot4 rotation)
        {
            if (!map.IsPlayerHome || !Find.Maps.Contains(map) || !cell.IsValid ||
                GenAdj.OccupiedRect(cell,rotation,pawn.def.size).Cells.Any(c => !c.InBounds(map) || !c.Standable(map) || c.GetThingList(map).Count != 0))
                throw new InvalidOperationException("Release cells are unavailable");
        }
        private PawnCustodySnapshot Repeat(PawnCustodyTransfer t,string entity,string city,bool release,int map,IntVec3 cell)
        {
            if (t.caravanId != null || t.entity != entity || t.city != city || t.release != release || release && (t.mapId != map || t.cell != cell)) throw new InvalidOperationException("Custody cause was reused for a different request");
            return new PawnCustodySnapshot(t);
        }
        private PawnCustodySnapshot RepeatCaravan(PawnCustodyTransfer t,string entity,string city,bool release,string caravanId,int tile)
        {
            if (t.entity != entity || t.city != city || t.release != release || t.caravanId != caravanId || t.tile != tile)
                throw new InvalidOperationException("Custody cause was reused for a different request");
            return new PawnCustodySnapshot(t);
        }
        private PawnCustodySnapshot Prepare(PawnCustodyTransfer t)
        {
            if (t.release && Core.Rations.HasPending(t.entity)) throw new InvalidOperationException("Release is locked by an unfinished ration debit");
            if (string.IsNullOrEmpty(t.cause) || transfers.Any(x => x.entity == t.entity && x.stage != PawnCustodyStage.Committed)) throw new InvalidOperationException("Invalid cause or overlapping custody transfer");
            Core.AssertNoActiveSupplyTransfer(t.city,t.entity,t.native); t.digest=t.Hash(); var ledger=Core.Ledger;
            var existing=ledger.FindOperation(t.actor,"create","pawn-custody/"+t.cause);
            if (existing != null) throw new InvalidOperationException("Orphan custody intent exists; refusing to allocate another");
            if (ledger.HasPendingReference(t.city)) throw new InvalidOperationException("Custody city is locked before intent allocation");
            string op=ledger.NewOperationId(t.actor);
            var created=ledger.PrepareCreate(op,"pawn-custody/"+t.cause,new EntitySpec("lease",identityKey:t.digest,custodyId:t.city));
            ledger.Commit(op); t.id=created.ResultId; transfers.Add(t);
            return new PawnCustodySnapshot(t);
        }
        public PawnCustodySnapshot ApplyNative(string id)
        {
            var core=Core; var t=FindTransfer(id); core.Ledger.AssertWriteAccess(t.actor); CheckIntent(core.Ledger,t);
            if (t.stage == PawnCustodyStage.Committed) return new PawnCustodySnapshot(t);
            core.AssertNoActiveSupplyTransfer(t.city,t.entity,t.native); if (core.Ledger.HasPendingReference(t.city)) throw new InvalidOperationException("Custody city is locked before native transfer");
            if (t.release && core.Rations.HasPending(t.entity)) throw new InvalidOperationException("Release is locked by an unfinished ration debit");
            Pawn pawn=PawnFor(core.Ledger,t.entity); Eligible(pawn);
            if (t.caravanId != null) return ApplyCaravan(t,pawn);
            if (t.release)
            {
                CheckResident(t.entity,t.city,pawn,null,-1);
                Map map=MapFor(t.mapId);
                if (pawn.Spawned)
                { if (t.stage != PawnCustodyStage.Applying || !OnMap(pawn,map) || pawn.Position != t.cell) throw new InvalidOperationException("Release location does not match its intent"); }
                else
                {
                    if (!HeldExclusively(pawn)) throw new InvalidOperationException("Release original pawn is missing or multiply owned");
                    CheckCell(map,t.cell,pawn,new Rot4(t.rotation)); t.stage=PawnCustodyStage.Applying;
                    try
                    {
                        if (!held.Remove(pawn)) throw new InvalidOperationException("Cannot remove original stored pawn");
                        if (GenSpawn.Spawn(pawn,t.cell,map,new Rot4(t.rotation)) != pawn || !OnMap(pawn,map) || pawn.Position != t.cell)
                            throw new InvalidOperationException("Native spawn did not release the original pawn");
                    }
                    catch
                    {
                        if (!pawn.Spawned && pawn.holdingOwner == null && !Find.WorldPawns.Contains(pawn) && !held.TryAdd(pawn,false))
                            throw new InvalidOperationException("Original release pawn could not return to custody");
                        throw;
                    }
                }
            }
            else if (pawn.holdingOwner == held)
            { if (t.stage != PawnCustodyStage.Applying || !HeldExclusively(pawn)) throw new InvalidOperationException("Storage lacks an exclusive applying original"); }
            else
            {
                Map map=MapFor(t.mapId);
                if (!OnMap(pawn,map)) throw new InvalidOperationException("Store original pawn is missing from its source map");
                t.stage=PawnCustodyStage.Applying; IntVec3 originalCell=pawn.Position; Rot4 originalRotation=pawn.Rotation;
                try
                {
                    pawn.DeSpawn();
                    if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                    if (!held.TryAdd(pawn,false)) throw new InvalidOperationException("Cannot hold original pawn");
                }
                catch
                {
                    if (!pawn.Spawned && pawn.holdingOwner == null && !Find.WorldPawns.Contains(pawn))
                    {
                        try { GenSpawn.Spawn(pawn,originalCell,map,originalRotation); }
                        finally { if (!pawn.Spawned && pawn.holdingOwner == null) held.TryAdd(pawn,false); }
                    }
                    throw;
                }
            }
            return new PawnCustodySnapshot(t);
        }
        private PawnCustodySnapshot ApplyCaravan(PawnCustodyTransfer t,Pawn pawn)
        {
            var caravan=CaravanFor(t.caravanId,t.tile); CheckCarried(pawn);
            if (t.release)
            {
                CheckResident(t.entity,t.city,pawn,t.caravanId,t.tile);
                if (InCaravan(pawn,caravan))
                {
                    if (t.stage != PawnCustodyStage.Applying) throw new InvalidOperationException("Caravan release is not an applying original");
                }
                else
                {
                    if (!HeldExclusively(pawn)) throw new InvalidOperationException("Release original pawn is missing or multiply owned");
                    t.stage=PawnCustodyStage.Applying;
                    try
                    {
                        if (!held.Remove(pawn)) throw new InvalidOperationException("Cannot remove original stored pawn");
                        AddOriginal(caravan,pawn);
                    }
                    catch
                    {
                        // Roll back only our exact original, never remove a foreign owner.
                        if (pawn.holdingOwner == caravan.GetDirectlyHeldThings()) caravan.RemovePawn(pawn);
                        if (!pawn.Spawned && pawn.holdingOwner == null)
                        {
                            if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                            if (!held.TryAdd(pawn,false) || !HeldExclusively(pawn))
                                throw new InvalidOperationException("Original caravan release pawn could not return to custody");
                        }
                        throw;
                    }
                }
            }
            else
            {
                CheckEscort(caravan,pawn);
                if (HeldExclusively(pawn))
                {
                    if (t.stage != PawnCustodyStage.Applying) throw new InvalidOperationException("Caravan store is not an applying original");
                }
                else
                {
                    if (!InCaravan(pawn,caravan) || IsStored(t.entity)) throw new InvalidOperationException("Store original pawn is missing from its caravan");
                    t.stage=PawnCustodyStage.Applying;
                    try
                    {
                        caravan.RemovePawn(pawn);
                        if (pawn.holdingOwner != null) throw new InvalidOperationException("Caravan did not release original ownership");
                        Find.WorldPawns.RemovePawn(pawn);
                        if (!held.TryAdd(pawn,false) || !HeldExclusively(pawn)) throw new InvalidOperationException("Cannot hold original caravan pawn");
                    }
                    catch
                    {
                        if (!pawn.Spawned && pawn.holdingOwner == null)
                        {
                            try { AddOriginal(caravan,pawn); }
                            finally
                            {
                                if (!pawn.Spawned && pawn.holdingOwner == null)
                                {
                                    if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                                    held.TryAdd(pawn,false);
                                }
                            }
                        }
                        throw;
                    }
                }
            }
            return new PawnCustodySnapshot(t);
        }
        private static void AddOriginal(Caravan caravan,Pawn pawn)
        {
            caravan.AddPawn(pawn,false);
            Find.WorldPawns.PassToWorld(pawn,PawnDiscardDecideMode.Decide);
            if (!InCaravan(pawn,caravan)) throw new InvalidOperationException("Native caravan did not receive the exclusive original pawn");
        }
        public PawnCustodySnapshot Commit(string id)
        {
            var core=Core; var t=FindTransfer(id); core.Ledger.AssertWriteAccess(t.actor); CheckIntent(core.Ledger,t);
            if (t.stage == PawnCustodyStage.Committed) return new PawnCustodySnapshot(t);
            core.AssertNoActiveSupplyTransfer(t.city,t.entity,t.native); if (core.Ledger.HasPendingReference(t.city)) throw new InvalidOperationException("Custody city is locked before transfer commit");
            if (t.stage != PawnCustodyStage.Applying) throw new InvalidOperationException("Custody has not applied its native transfer");
            Pawn pawn=PawnFor(core.Ledger,t.entity);
            if (t.caravanId != null)
            {
                Eligible(pawn); CheckCarried(pawn); var caravan=CaravanFor(t.caravanId,t.tile);
                if (t.release) CheckResident(t.entity,t.city,pawn,t.caravanId,t.tile);
                else CheckEscort(caravan,pawn);
            }
            if (t.release)
            {
                CheckResident(t.entity,t.city,pawn,t.caravanId,t.tile);
                if (!AtDestination(t,pawn)) throw new InvalidOperationException("Release has no original pawn at its destination");
                residents.RemoveAll(r => r.entity == t.entity);
            }
            else
            {
                if (!HeldExclusively(pawn)) throw new InvalidOperationException("Storage has no exclusive original pawn");
                if (IsStored(t.entity)) throw new InvalidOperationException("Duplicate storage resident");
                residents.Add(new PawnCustodyResident {entity=t.entity,city=t.city,native=t.native,transfer=t.id});
            }
            t.stage=PawnCustodyStage.Committed; return new PawnCustodySnapshot(t);
        }
        private static bool AtDestination(PawnCustodyTransfer t,Pawn pawn)
        {
            if (t.caravanId != null) return InCaravan(pawn,CaravanFor(t.caravanId,t.tile));
            return pawn.Spawned && OnMap(pawn,pawn.Map) && pawn.Map.uniqueID == t.mapId && pawn.Position == t.cell;
        }
        private static bool AtSavedEndpoint(PawnCustodyTransfer t,Pawn pawn)
        {
            if (t.caravanId == null) return AtDestination(t,pawn);
            var caravan=Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == t.caravanId);
            return caravan != null && !caravan.Destroyed && caravan.Spawned && InCaravan(pawn,caravan);
        }
        private static void CheckIntent(CoreLedger ledger,PawnCustodyTransfer t)
        {
            CheckShape(t);
            var lease=ledger.GetEntity(t.id);
            var op=ledger.FindOperation(t.actor,"create","pawn-custody/"+t.cause);
            if (t.digest != t.Hash() || lease.Kind != "lease" || lease.Status != EntityStatus.Active || lease.Provider != t.actor || lease.IdentityKey != t.digest || lease.CustodyId != t.city ||
                op == null || op.Stage != OperationStage.Committed || op.ResultId != t.id || !Enum.IsDefined(typeof(PawnCustodyStage),t.stage)) throw new InvalidOperationException("Custody intent disagrees with its Core certificate");
            if (ledger.GetEntity(t.entity).BoundThingLoadId != t.native) throw new InvalidOperationException("Custody native binding changed");
            City(ledger,t.city);
        }
        private static void CheckShape(PawnCustodyTransfer t)
        {
            if (t.rotation < 0 || t.rotation > 3 ||
                (t.caravanId == null ? t.tile != -1 || t.mapId < 0 || !t.cell.IsValid :
                string.IsNullOrEmpty(t.caravanId) || t.tile < 0 || t.mapId != -1 || t.cell != IntVec3.Invalid))
                throw new InvalidOperationException("Custody intent has an invalid or ambiguous native endpoint");
        }
        internal void Validate(CoreLedger ledger,SaveHeader header)
        {
            if (held == null || residents == null || transfers == null || residentCount != residents.Count || transferCount != transfers.Count) throw new InvalidOperationException("Pawn custody tables are missing or incomplete");
            var ids=new HashSet<string>(StringComparer.Ordinal); var causes=new HashSet<string>(StringComparer.Ordinal); var pending=new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in transfers)
            {
                if (t == null || !ids.Add(t.id) || string.IsNullOrEmpty(t.cause) || !causes.Add(t.actor+"/"+t.cause) || header.SchemaVersion < 6 && t.caravanId != null) throw new InvalidOperationException("Invalid custody transfer journal");
                CheckIntent(ledger,t);
                if (t.stage != PawnCustodyStage.Committed && !pending.Add(t.entity)) throw new InvalidOperationException("Overlapping custody intents");
            }
            foreach (var op in ledger.SavedState.operations.Where(o => o.kind == "create" && o.cause.StartsWith("pawn-custody/",StringComparison.Ordinal)))
                if (!transfers.Any(t => t.id == op.result && t.actor == op.actor && "pawn-custody/"+t.cause == op.cause)) throw new InvalidOperationException("Missing custody journal for a Core intent");
            // Reconstruct current custody from permanent certificate order, never XML row order.
            var expected=new Dictionary<string,PawnCustodyTransfer>(StringComparer.Ordinal);
            foreach (var group in transfers.GroupBy(t => t.entity,StringComparer.Ordinal))
            {
                PawnCustodyTransfer resident=null; bool unfinished=false;
                foreach (var t in group.OrderBy(t => header.ParseId(t.id,"lease")))
                {
                    if (unfinished || t.release && (resident == null || resident.city != t.city || resident.native != t.native ||
                        resident.caravanId != t.caravanId || t.caravanId != null && resident.tile != t.tile) || !t.release && resident != null)
                        throw new InvalidOperationException("Custody transfer history is inconsistent");
                    if (t.stage == PawnCustodyStage.Committed) resident=t.release ? null : t;
                    else unfinished=true;
                }
                if (resident != null) expected.Add(group.Key,resident);
            }
            if (residents.Count != expected.Count) throw new InvalidOperationException("Resident table disagrees with committed custody history");
            var residentsSeen=new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in residents)
            {
                if (r == null || !residentsSeen.Add(r.entity)) throw new InvalidOperationException("Duplicate custody resident");
                var t=FindTransfer(r.transfer); Pawn pawn=PawnFor(ledger,r.entity);
                PawnCustodyTransfer current;
                if (!expected.TryGetValue(r.entity,out current) || current != t || t.release || t.stage != PawnCustodyStage.Committed || t.entity != r.entity || t.city != r.city || t.native != r.native) throw new InvalidOperationException("Custody resident disagrees with its committed store");
                var release=transfers.SingleOrDefault(x => x.entity == r.entity && x.release && x.stage == PawnCustodyStage.Applying);
                if (!HeldExclusively(pawn) && !(release != null && AtSavedEndpoint(release,pawn))) throw new InvalidOperationException("Stored original pawn is missing");
            }
            foreach (Pawn pawn in held.Cast<Pawn>())
            {
                string entity=ledger.SavedState.entities.Single(e => e.binding == pawn).id;
                if (!HeldExclusively(pawn) || !residentsSeen.Contains(entity) &&
                    !transfers.Any(t => t.entity == entity && !t.release && t.stage == PawnCustodyStage.Applying)) throw new InvalidOperationException("Unaccounted or multiply owned stored pawn");
            }
            foreach (var t in transfers.Where(x => x.stage != PawnCustodyStage.Committed))
            {
                Pawn pawn=PawnFor(ledger,t.entity);
                bool inStore=HeldExclusively(pawn);
                bool atEndpoint;
                if (t.caravanId != null)
                {
                    // A moved or missing anchor blocks execution, not the safe saving of
                    // an original already in the vault (including a rolled-back release).
                    atEndpoint=false;
                    if (!inStore) atEndpoint=AtSavedEndpoint(t,pawn);
                }
                else atEndpoint=pawn.Spawned && OnMap(pawn,pawn.Map) && pawn.Map.uniqueID == t.mapId && (!t.release || pawn.Position == t.cell);
                if (atEndpoint == inStore || t.stage == PawnCustodyStage.Prepared && (t.release ? !inStore : !atEndpoint)) throw new InvalidOperationException("Pending custody original has an invalid location");
            }
        }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) { residentCount=residents.Count; transferCount=transfers.Count; }
            Scribe_Deep.Look(ref held,"pawns",owner);
            Scribe_Collections.Look(ref residents,"residents",LookMode.Deep); Scribe_Collections.Look(ref transfers,"transfers",LookMode.Deep);
            Scribe_Values.Look(ref residentCount,"residentCount",-1,true); Scribe_Values.Look(ref transferCount,"transferCount",-1,true);
        }
        internal void SynchronizeCounts() { residentCount=residents.Count; transferCount=transfers.Count; }
    }
}
