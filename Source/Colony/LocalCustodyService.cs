using System;
using System.Collections.Generic;
using System.Linq;
using Inheritance.Core;
using RimWorld;
using Verse;

namespace Inheritance.Colony
{
    public sealed class CustodyResidentSnapshot
    {
        public readonly string EntityId, Name, Kind;
        public readonly int Items;
        internal CustodyResidentSnapshot(string entity,Pawn pawn)
        { EntityId=entity; Name=pawn.LabelShort; Kind=pawn.RaceProps.Animal ? "animal" : "person";
            Items=(pawn.inventory == null ? 0 : pawn.inventory.innerContainer.Sum(t => t.stackCount))+
                (pawn.apparel == null ? 0 : pawn.apparel.WornApparel.Sum(t => t.stackCount))+
                (pawn.equipment == null ? 0 : pawn.equipment.AllEquipmentListForReading.Sum(t => t.stackCount)); }
    }
    public sealed class CustodyPendingSnapshot
    {
        public readonly string Id, Name, BaseId, Stage;
        public readonly bool IsRelease;
        internal CustodyPendingSnapshot(PawnCustodySnapshot t,Pawn pawn)
        { Id=t.Id; Name=pawn.LabelShort; BaseId=t.CityId; Stage=t.Stage.ToString(); IsRelease=t.IsRelease; }
    }
    // Local frozen storage only; it does not move people between towns or simulate a delegated life.
    public sealed partial class ColonyService
    {
        private PawnCustodySnapshot[] LocalTransfers()
        { return core.ReadPawnCustodyTransfers(Actor).Where(t => t.Actor == Actor && t.CaravanId == null).ToArray(); }
        private PawnCustodyResidentSnapshot[] LocalResidents()
        {
            var ids=new HashSet<string>(LocalTransfers().Select(t => t.Id));
            return core.ReadPawnCustodyResidents(Actor).Where(r => ids.Contains(r.StoreTransferId)).ToArray();
        }
        public bool HasLocalCustody { get { return LocalResidents().Length > 0 || LocalTransfers().Any(t => t.Stage != PawnCustodyStage.Committed); } }
        public bool CustodyCommandsReady { get { return core.IsReady && ColonyMod.CustodyAllowed; } }
        public BaseSnapshot[] CustodyBases()
        { return state.bases.Where(b => b.mode == BaseMode.Direct && Find.Maps.Any(m => m.uniqueID == b.mapId && m.IsPlayerHome)).Select(b => new BaseSnapshot(b)).ToArray(); }
        private bool PendingCustody(string entity)
        { return LocalTransfers().Any(t => t.EntityId == entity && t.Stage != PawnCustodyStage.Committed); }
        private bool NativeCustodyReserved(Pawn pawn)
        { return pawn != null && core.ReadPawnCustodyTransfers(Actor).Any(t => t.NativeId == pawn.GetUniqueLoadID() && t.Stage != PawnCustodyStage.Committed); }
        private bool TransportReserved(string entity)
        { return state.physical.Any(r => ActivePhysical(r) && r.party.Concat(r.animals).Contains(entity)); }
        private string RegisteredPawnId(Pawn pawn)
        { return pawn.RaceProps.Animal ? state.animals.Where(a => a.nativeId == pawn.GetUniqueLoadID()).Select(a => a.id).SingleOrDefault() : state.people.Where(p => p.nativeId == pawn.GetUniqueLoadID()).Select(p => p.id).SingleOrDefault(); }
        private static bool LocalEligible(Pawn pawn,Map map)
        { return map.IsPlayerHome && PhysicalCaravanAdapter.Eligible(pawn,map) && pawn.holdingOwner == map.spawnedThings && !Find.WorldPawns.Contains(pawn); }
        public Pawn[] AvailableCustodyPawns(string baseId)
        {
            var map=DirectMap(baseId);
            return map.mapPawns.AllPawnsSpawned.Where(p => LocalEligible(p,map) && !NativeCustodyReserved(p) &&
                !ledger.Entities.Any(e => e.BoundThingLoadId == p.GetUniqueLoadID() && e.Provider != Actor) &&
                (RegisteredPawnId(p) == null || !TransportReserved(RegisteredPawnId(p))))
                .OrderBy(p => p.LabelShort,StringComparer.Ordinal).ThenBy(p => p.GetUniqueLoadID(),StringComparer.Ordinal).ToArray();
        }
        private Pawn Original(string entity)
        {
            var e=ledger.GetEntity(entity); var pawn=NativePawn(e.BoundThingLoadId);
            if (e.Provider != Actor || (e.Kind != "person" && e.Kind != "animal") || pawn == null || pawn.Dead || pawn.Destroyed)
                throw new InvalidOperationException("Local custody original is unavailable");
            return pawn;
        }
        public CustodyResidentSnapshot[] CustodyResidents(string baseId)
        { RequireBase(baseId); return LocalResidents().Where(r => r.CityId == baseId).Select(r => new CustodyResidentSnapshot(r.EntityId,Original(r.EntityId))).ToArray(); }
        public CustodyPendingSnapshot[] CustodyPending()
        { return LocalTransfers().Where(t => t.Stage != PawnCustodyStage.Committed).Select(t => new CustodyPendingSnapshot(t,Original(t.EntityId))).ToArray(); }
        private string RegisterCustodyPawn(Pawn pawn)
        {
            string prior=RegisteredPawnId(pawn); if (prior != null) return prior;
            core.AssertNoActiveSupplyTransfer(nativeId:pawn.GetUniqueLoadID()); EnsureCommunity(); string native=pawn.GetUniqueLoadID(); bool animal=pawn.RaceProps.Animal;
            string prefix=animal ? "native-animal/" : "native-person/";
            string id=Create(prefix+native,new EntitySpec(animal ? "animal" : "person",pawn.LabelShort,ownerId:state.communityId,communityId:state.communityId,affiliationId:state.communityId,
                authority:SimulationAuthority.Physical,identityKey:prefix+native+(animal ? "/"+pawn.def.defName : ""),binding:pawn));
            if (animal) state.animals.Add(new AnimalRecord {id=id,nativeId=native,defName=pawn.def.defName});
            else state.people.Add(new PersonRecord {id=id,nativeId=native});
            return id;
        }
        private void StoreSafety(Pawn pawn,Map map,string entity)
        {
            if (!LocalEligible(pawn,map) || entity != null && TransportReserved(entity)) throw new InvalidOperationException("Local storage requires an available unreserved original");
            if (pawn.RaceProps.Humanlike && map.mapPawns.FreeColonistsSpawned.Count(p => p != pawn && LocalEligible(p,map)) == 0)
                throw new InvalidOperationException("The last available colonist must remain on the map");
        }
        public string StoreLocalPawn(string cause,string baseId,Pawn pawn)
        {
            RejectReturnCause(cause);
            Gate(); if (!CustodyCommandsReady) throw new InvalidOperationException("Frozen storage is disabled");
            if (string.IsNullOrEmpty(cause) || pawn == null) throw new InvalidOperationException("A storage cause and original pawn are required");
            Map map=DirectMap(baseId);
            if (core.ReadPawnCustodyTransfers(Actor).Any(t => t.Actor == Actor && t.Cause == cause && t.CaravanId != null))
                throw new InvalidOperationException("Custody cause already belongs to delegated storage");
            var prior=LocalTransfers().SingleOrDefault(t => t.Cause == cause);
            if (prior != null)
            {
                if (prior.IsRelease || prior.CityId != baseId || prior.NativeId != pawn.GetUniqueLoadID() || prior.MapId != map.uniqueID) throw new InvalidOperationException("Storage cause was reused");
                ResumeLocalCustody(prior.Id); return prior.Id;
            }
            string entity=RegisteredPawnId(pawn);
            StoreSafety(pawn,map,entity);
            if (NativeCustodyReserved(pawn) || entity != null && core.PawnCustody.IsStored(entity) || ledger.Entities.Any(e => e.BoundThingLoadId == pawn.GetUniqueLoadID() && e.Provider != Actor))
                throw new InvalidOperationException("Original is reserved or owned by another provider");
            core.AssertNoActiveSupplyTransfer(baseId,entity,pawn.GetUniqueLoadID()); entity=RegisterCustodyPawn(pawn);
            var intent=core.PawnCustody.PrepareStore(Actor,cause,entity,baseId);
            ResumeLocalCustody(intent.Id); return intent.Id;
        }
        public string ReleaseLocalPawn(string cause,string baseId,string entityId)
        {
            RejectReturnCause(cause);
            Gate(); if (!core.IsReady || string.IsNullOrEmpty(cause)) throw new InvalidOperationException("Local release is unavailable");
            Map map=DirectMap(baseId); var prior=LocalTransfers().SingleOrDefault(t => t.Cause == cause);
            if (prior != null)
            {
                if (!prior.IsRelease || prior.CityId != baseId || prior.EntityId != entityId || prior.MapId != map.uniqueID) throw new InvalidOperationException("Release cause was reused");
                ResumeLocalCustody(prior.Id); return prior.Id;
            }
            var resident=LocalResidents().SingleOrDefault(r => r.EntityId == entityId && r.CityId == baseId);
            if (resident == null || resident.SourceMapId != map.uniqueID || PendingCustody(entityId) || TransportReserved(entityId)) throw new InvalidOperationException("Original must return to its local storage map");
            Pawn pawn=Original(entityId); var stored=LocalTransfers().Single(t => t.Id == resident.StoreTransferId);
            IntVec3 cell=ReleaseCell(map,pawn,stored.Cell);
            var intent=core.PawnCustody.PrepareRelease(Actor,cause,entityId,baseId,map,cell);
            ResumeLocalCustody(intent.Id); return intent.Id;
        }
        private static IntVec3 ReleaseCell(Map map,Pawn pawn,IntVec3 anchor)
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(anchor,40f,true))
                if (GenAdj.OccupiedRect(c,pawn.Rotation,pawn.def.size).Cells.All(x => x.InBounds(map) && x.Standable(map) && x.GetThingList(map).Count == 0)) return c;
            throw new InvalidOperationException("No free local return cell; clear space near the original position");
        }
        public void ResumeLocalCustody(string transferId)
        {
            Gate(); var intent=LocalTransfers().SingleOrDefault(t => t.Id == transferId);
            if (intent == null) throw new InvalidOperationException("Unknown local custody transfer");
            var map=DirectMap(intent.CityId);
            if (map.uniqueID != intent.MapId || TransportReserved(intent.EntityId)) throw new InvalidOperationException("Custody conflicts with a local map or transport");
            if (intent.Stage == PawnCustodyStage.Committed) return;
            Pawn pawn=Original(intent.EntityId);
            if (!intent.IsRelease && pawn.Spawned) StoreSafety(pawn,map,intent.EntityId);
            core.PawnCustody.ApplyNative(intent.Id); core.PawnCustody.Commit(intent.Id); Validate();
        }
        private void ValidateLocalCustody(HashSet<string> known)
        {
            foreach (var t in LocalTransfers())
            {
                var b=RequireBase(t.CityId); var e=ledger.GetEntity(t.EntityId);
                if (!known.Add(t.Id) || b.mode != BaseMode.Direct || b.mapId != t.MapId || e.Provider != Actor ||
                    !(state.people.Any(p => p.id == t.EntityId) || state.animals.Any(a => a.id == t.EntityId)) ||
                    t.Stage != PawnCustodyStage.Committed && TransportReserved(t.EntityId)) throw new InvalidOperationException("Local custody journal conflicts with Colony assets");
            }
            foreach (var r in LocalResidents())
                if (RequireBase(r.CityId).mapId != r.SourceMapId || TransportReserved(r.EntityId)) throw new InvalidOperationException("Stored local original conflicts with its map or transport");
        }
    }
}
