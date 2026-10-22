using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Inheritance.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Colony
{
    // Optional ordinary management of frozen originals at an arrived native caravan.
    // No map is removed, caravan generated or inventory converted into abstract stock.
    public sealed partial class ColonyService
    {
        public bool DelegatedCustodyCommandsReady
        { get { return core.IsReady && ColonyMod.DelegatedAllowed; } }
        private PawnCustodySnapshot[] DelegatedTransfers()
        { return core.ReadPawnCustodyTransfers(Actor).Where(t => t.Actor == Actor && t.CaravanId != null).ToArray(); }
        private PawnCustodyResidentSnapshot[] DelegatedStored()
        {
            var ids=new HashSet<string>(DelegatedTransfers().Select(t => t.Id));
            return core.ReadPawnCustodyResidents(Actor).Where(r => ids.Contains(r.StoreTransferId)).ToArray();
        }
        private static string SitePrefix(int tile)
        { return "delegated-site/"+tile.ToString(CultureInfo.InvariantCulture)+"/"; }
        public string RegisterDelegatedSite(string key,string name,int tile)
        {
            Gate();
            if (!DelegatedCustodyCommandsReady || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name) ||
                tile < 0 || tile >= Find.WorldGrid.Surface.TilesCount)
                throw new InvalidOperationException("An enabled delegated site requires a valid surface tile and identity");
            string identity=SitePrefix(tile)+key;
            var prior=state.bases.SingleOrDefault(b => b.key == identity);
            if (prior != null) return RegisterBase(identity,name,BaseMode.Delegated,-1,tile);
            var target=new PlanetTile(tile,0);
            if (state.bases.Any(b => b.tile == tile) || Find.Maps.Any(m => m.Tile == target) ||
                Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == target && !(o is Caravan)))
                throw new InvalidOperationException("Delegated site tile is already occupied");
            return RegisterBase(identity,name,BaseMode.Delegated,-1,tile);
        }
        private BaseRecord DelegatedSite(string city)
        {
            var b=RequireBase(city);
            if (b.mode != BaseMode.Delegated || b.mapId != -1 || b.tile < 0 || b.tile >= Find.WorldGrid.Surface.TilesCount ||
                !b.key.StartsWith(SitePrefix(b.tile),StringComparison.Ordinal))
                throw new InvalidOperationException("Delegated custody needs a located surface site");
            return b;
        }
        private static void CheckSiteCaravan(BaseRecord site,Caravan caravan)
        {
            if (caravan == null || caravan.Destroyed || !caravan.Spawned || !Find.WorldObjects.Contains(caravan) ||
                caravan.Faction != Faction.OfPlayer || !caravan.IsPlayerControlled || caravan.Tile != new PlanetTile(site.tile,0) ||
                caravan.pather == null || caravan.pather.Moving)
                throw new InvalidOperationException("The original caravan must be stopped at the delegated site");
        }
        private static bool CaravanOriginal(Caravan caravan,Pawn pawn)
        {
            return pawn != null && !pawn.Destroyed && !pawn.Dead && !pawn.Spawned && pawn.Faction == Faction.OfPlayer &&
                !pawn.Downed && !pawn.Drafted && !pawn.InMentalState &&
                (pawn.IsColonist && !pawn.DevelopmentalStage.Baby() || pawn.RaceProps.Animal) &&
                (pawn.carryTracker == null || pawn.carryTracker.CarriedThing == null) &&
                pawn.holdingOwner == caravan.GetDirectlyHeldThings() && caravan.PawnsListForReading.Contains(pawn) &&
                Find.WorldPawns.Contains(pawn) && Find.WorldObjects.Caravans.Count(c => c.PawnsListForReading.Contains(pawn)) == 1;
        }
        private void CheckDelegatedOriginal(Pawn pawn,string entity)
        {
            string native=pawn.GetUniqueLoadID();
            if (ledger.Entities.Any(e => e.BoundThingLoadId == native && e.Provider != Actor) ||
                entity != null && TransportReserved(entity))
                throw new InvalidOperationException("Delegated original belongs to another provider or transport");
            if (entity != null)
            {
                var e=ledger.GetEntity(entity);
                if (e.Provider != Actor || e.Status != EntityStatus.Active || e.Authority != SimulationAuthority.Physical ||
                    e.BoundThingLoadId != native || e.Kind != (pawn.RaceProps.Animal ? "animal" : "person"))
                    throw new InvalidOperationException("Delegated original disagrees with its permanent identity");
                if (pawn.RaceProps.Animal && !state.animals.Any(a => a.id == entity && a.nativeId == native && a.defName == pawn.def.defName))
                    throw new InvalidOperationException("Delegated animal species changed");
            }
        }
        public CustodyResidentSnapshot[] DelegatedResidents(string city)
        { DelegatedSite(city); return DelegatedStored().Where(r => r.CityId == city).Select(r => new CustodyResidentSnapshot(r.EntityId,Original(r.EntityId))).ToArray(); }
        public string StoreDelegatedPawn(string cause,string city,Caravan caravan,Pawn pawn)
        {
            Gate();
            RejectReturnCause(cause);
            if (!DelegatedCustodyCommandsReady || string.IsNullOrEmpty(cause) || pawn == null || caravan == null)
                throw new InvalidOperationException("New delegated storage requires enabled management and original individuals");
            var site=DelegatedSite(city);
            if (core.ReadPawnCustodyTransfers(Actor).Any(t => t.Actor == Actor && t.Cause == cause && t.CaravanId == null))
                throw new InvalidOperationException("Custody cause already belongs to local storage");
            var prior=DelegatedTransfers().SingleOrDefault(t => t.Cause == cause);
            if (prior != null)
            {
                if (prior.IsRelease || prior.CityId != city || prior.NativeId != pawn.GetUniqueLoadID() ||
                    prior.CaravanId != caravan.GetUniqueLoadID() || prior.Tile != site.tile) throw new InvalidOperationException("Delegated storage cause was reused");
                ResumeDelegatedCustody(prior.Id); return prior.Id;
            }
            CheckSiteCaravan(site,caravan);
            if (ledger.HasPendingReference(city)) throw new InvalidOperationException("Delegated city is locked before individual registration");
            string entity=RegisteredPawnId(pawn);
            CheckDelegatedOriginal(pawn,entity);
            if (!CaravanOriginal(caravan,pawn) || NativeCustodyReserved(pawn) || entity != null && core.PawnCustody.IsStored(entity) ||
                !caravan.PawnsListForReading.Any(p => p != pawn && p.RaceProps.Humanlike && p.IsColonist && CaravanOriginal(caravan,p)))
                throw new InvalidOperationException("Delegated storage requires an unreserved original and another human escort");
            core.AssertNoActiveSupplyTransfer(city,entity,pawn.GetUniqueLoadID()); entity=RegisterCustodyPawn(pawn);
            var intent=core.PawnCustody.PrepareCaravanStore(Actor,cause,entity,city,caravan,site.tile);
            ResumeDelegatedCustody(intent.Id); return intent.Id;
        }
        public string ReleaseDelegatedPawn(string cause,string city,string entity,Caravan caravan)
        {
            Gate();
            RejectReturnCause(cause);
            if (!core.IsReady || string.IsNullOrEmpty(cause) || caravan == null) throw new InvalidOperationException("Delegated release is unavailable");
            var site=DelegatedSite(city);
            var prior=DelegatedTransfers().SingleOrDefault(t => t.Cause == cause);
            if (prior != null)
            {
                if (!prior.IsRelease || prior.CityId != city || prior.EntityId != entity ||
                    prior.CaravanId != caravan.GetUniqueLoadID() || prior.Tile != site.tile) throw new InvalidOperationException("Delegated release cause was reused");
                ResumeDelegatedCustody(prior.Id); return prior.Id;
            }
            CheckSiteCaravan(site,caravan);
            if (ledger.HasPendingReference(city)) throw new InvalidOperationException("Delegated city is locked before release preparation");
            var resident=DelegatedStored().SingleOrDefault(r => r.EntityId == entity && r.CityId == city);
            if (resident == null || resident.CaravanId != caravan.GetUniqueLoadID() || resident.Tile != site.tile ||
                core.ReadPawnCustodyTransfers(Actor).Any(t => t.EntityId == entity && t.Stage != PawnCustodyStage.Committed))
                throw new InvalidOperationException("Delegated original must return to its saved caravan anchor");
            CheckDelegatedOriginal(Original(entity),entity);
            var intent=core.PawnCustody.PrepareCaravanRelease(Actor,cause,entity,city,caravan,site.tile);
            ResumeDelegatedCustody(intent.Id); return intent.Id;
        }
        public void ResumeDelegatedCustody(string id)
        {
            Gate();
            var t=DelegatedTransfers().SingleOrDefault(x => x.Id == id);
            if (t == null) throw new InvalidOperationException("Unknown delegated custody intent");
            if (t.Stage == PawnCustodyStage.Committed) return;
            var site=DelegatedSite(t.CityId);
            if (t.Tile != site.tile || TransportReserved(t.EntityId)) throw new InvalidOperationException("Delegated custody conflicts with site or transport");
            CheckDelegatedOriginal(Original(t.EntityId),t.EntityId);
            core.PawnCustody.ApplyNative(id); core.PawnCustody.Commit(id); Validate();
        }
        private void ValidateDelegatedCustody(HashSet<string> known)
        {
            var located=state.bases.Where(b => b.tile >= 0).ToArray();
            if (state.bases.Any(b => b.tile < -1 || b.mode == BaseMode.Direct && b.tile != -1) ||
                located.Select(b => b.tile).Distinct().Count() != located.Length)
                throw new InvalidOperationException("Invalid or duplicate delegated site coordinates");
            foreach (var b in located) DelegatedSite(b.cityId);
            foreach (var t in DelegatedTransfers())
            {
                var b=DelegatedSite(t.CityId); var e=ledger.GetEntity(t.EntityId);
                bool registered=state.people.Any(p => p.id == t.EntityId && p.nativeId == t.NativeId) || state.animals.Any(a => a.id == t.EntityId && a.nativeId == t.NativeId);
                if (!known.Add(t.Id) || t.MapId != -1 || t.Tile != b.tile || e.Provider != Actor || e.BoundThingLoadId != t.NativeId ||
                    !registered || t.Stage != PawnCustodyStage.Committed && TransportReserved(t.EntityId))
                    throw new InvalidOperationException("Delegated custody journal disagrees with its Colony site or individual");
            }
            foreach (var r in DelegatedStored())
                if (r.SourceMapId != -1 || r.Tile != DelegatedSite(r.CityId).tile || TransportReserved(r.EntityId))
                    throw new InvalidOperationException("Delegated resident conflicts with site or active transport");
        }
    }
}
