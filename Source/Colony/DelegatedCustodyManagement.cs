using System;
using System.Linq;
using Inheritance.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Colony
{
    public sealed class DelegatedCaravanSnapshot
    {
        public readonly Caravan Caravan;
        public readonly string Name,Problem;
        public readonly bool CanRegister;
        internal DelegatedCaravanSnapshot(Caravan caravan,string problem)
        { Caravan=caravan; Name=caravan.Label; Problem=problem; CanRegister=problem == "None"; }
    }
    public sealed class DelegatedStorePawnSnapshot
    {
        public readonly Pawn Pawn;
        public readonly string Name,Kind,Problem;
        public readonly int Items;
        public readonly bool CanStore;
        internal DelegatedStorePawnSnapshot(Pawn pawn,string problem)
        { Pawn=pawn; Name=pawn.LabelShort; Kind=pawn.RaceProps.Animal ? "animal" : "person"; Items=DelegatedPossessions.Count(pawn); Problem=problem; CanStore=problem == "None"; }
    }
    public sealed class DelegatedResidentSnapshot
    {
        public readonly string EntityId,Name,Kind,CaravanName,Problem;
        public readonly int Items;
        public readonly Caravan Caravan;
        public readonly bool CanRelease;
        internal DelegatedResidentSnapshot(string entity,Pawn pawn,Caravan caravan,string problem)
        { EntityId=entity; Name=pawn.LabelShort; Kind=pawn.RaceProps.Animal ? "animal" : "person"; Items=DelegatedPossessions.Count(pawn); Caravan=caravan; CaravanName=caravan == null ? null : caravan.Label; Problem=problem; CanRelease=problem == "None"; }
    }
    public sealed class DelegatedPendingSnapshot
    {
        public readonly string Id,Name,CaravanName,Problem,Stage,Cause;
        public readonly bool IsRelease,CanResume;
        internal DelegatedPendingSnapshot(PawnCustodySnapshot transfer,Pawn pawn,Caravan caravan,string problem)
        { Id=transfer.Id; Cause=transfer.Cause; Name=pawn.LabelShort; CaravanName=caravan == null ? null : caravan.Label; Problem=problem; Stage=transfer.Stage.ToString(); IsRelease=transfer.IsRelease; CanResume=problem == "None"; }
    }
    internal static class DelegatedPossessions
    {
        internal static int Count(Pawn pawn)
        { return (pawn.inventory == null ? 0 : pawn.inventory.innerContainer.Sum(t => t.stackCount))+
            (pawn.apparel == null ? 0 : pawn.apparel.WornApparel.Sum(t => t.stackCount))+
            (pawn.equipment == null ? 0 : pawn.equipment.AllEquipmentListForReading.Sum(t => t.stackCount)); }
    }
    public sealed partial class ColonyService
    {
        public bool HasDelegatedCustody
        { get { return state.bases.Any(b => b.mode == BaseMode.Delegated && b.tile >= 0) || DelegatedStored().Length > 0 || DelegatedTransfers().Any(t => t.Stage != PawnCustodyStage.Committed); } }
        private static bool ManagedCaravan(Caravan caravan)
        { return caravan != null && !caravan.Destroyed && caravan.Spawned && Find.WorldObjects.Contains(caravan) &&
            caravan.Faction == Faction.OfPlayer && caravan.IsPlayerControlled && caravan.Tile == new PlanetTile(caravan.Tile.tileId,0) &&
            caravan.Tile.tileId >= 0 && caravan.Tile.tileId < Find.WorldGrid.Surface.TilesCount && caravan.pather != null; }
        private string CaravanRegistrationProblem(Caravan caravan)
        {
            if (!ColonyMod.DelegatedAllowed) return "Disabled";
            if (!ManagedCaravan(caravan)) return "CaravanMissing";
            if (caravan.pather.Moving) return "CaravanMoving";
            var tile=caravan.Tile;
            if (state.bases.Any(b => b.tile == tile.tileId) || Find.Maps.Any(m => m.Tile == tile) ||
                Find.WorldObjects.AllWorldObjects.Any(o => o.Tile == tile && !(o is Caravan))) return "Occupied";
            return "None";
        }
        private static string CaravanSiteProblem(BaseRecord site,Caravan caravan)
        {
            if (site == null || site.mode != BaseMode.Delegated || site.tile < 0) return "Unlocated";
            if (!ManagedCaravan(caravan)) return "CaravanMissing";
            if (caravan.pather.Moving) return "CaravanMoving";
            if (caravan.Tile != new PlanetTile(site.tile,0)) return "CaravanAway";
            return "None";
        }
        private Caravan DelegatedAnchor(string native)
        { return Find.WorldObjects.Caravans.SingleOrDefault(c => c.GetUniqueLoadID() == native); }
        public DelegatedCaravanSnapshot[] DelegatedCaravans(string city)
        {
            if (!core.IsReady) throw new InvalidOperationException("Delegated management is unavailable");
            if (city != null) RequireBase(city);
            return Find.WorldObjects.Caravans.Where(c => ManagedCaravan(c)).OrderBy(c => c.Label,StringComparer.Ordinal)
                .ThenBy(c => c.GetUniqueLoadID(),StringComparer.Ordinal).Select(c => new DelegatedCaravanSnapshot(c,CaravanRegistrationProblem(c))).ToArray();
        }
        public string RegisterDelegatedSiteAtCaravan(string key,string name,Caravan caravan)
        {
            Gate();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || string.IsNullOrWhiteSpace(name) || name.Length > 60 ||
                name != name.Trim() || name.Any(char.IsControl)) throw new InvalidOperationException("A delegated site needs a short visible name and a stable command identity");
            if (!ColonyMod.DelegatedAllowed || !ManagedCaravan(caravan) || caravan.pather.Moving)
                throw new InvalidOperationException("A new delegated site requires enabled management and a stopped original player caravan");
            // Replay an existing exact request before the ordinary occupancy check.
            // RegisterBase verifies that its immutable name and mode still match.
            string identity=SitePrefix(caravan.Tile.tileId)+key;
            if (state.bases.Any(b => b.mode == BaseMode.Delegated && b.tile >= 0 && b.tile != caravan.Tile.tileId &&
                b.key == SitePrefix(b.tile)+key)) throw new InvalidOperationException("A delegated site command cannot be reused at another tile");
            if (state.bases.Any(b => b.key == identity)) return RegisterDelegatedSite(key,name,caravan.Tile.tileId);
            if (CaravanRegistrationProblem(caravan) != "None") throw new InvalidOperationException("The caravan's current tile is already occupied");
            return RegisterDelegatedSite(key,name,caravan.Tile.tileId);
        }
        private string StoreCandidateProblem(BaseRecord site,Caravan caravan,Pawn pawn)
        {
            if (!DelegatedCustodyCommandsReady) return "Disabled";
            string problem=CaravanSiteProblem(site,caravan); if (problem != "None") return problem;
            if (ledger.HasPendingReference(site.cityId)) return "CityLocked";
            if (!CaravanOriginal(caravan,pawn)) return "Ineligible";
            string entity=RegisteredPawnId(pawn);
            if (NativeCustodyReserved(pawn)) return "PendingCustody";
            if (entity != null && TransportReserved(entity)) return "Reserved";
            if (entity != null && core.Rations.HasPending(entity)) return "PendingRations";
            try { CheckDelegatedOriginal(pawn,entity); } catch (InvalidOperationException) { return "Ineligible"; }
            if (!caravan.PawnsListForReading.Any(p => p != pawn && p.RaceProps.Humanlike && p.IsColonist && CaravanOriginal(caravan,p))) return "NoEscort";
            return "None";
        }
        public DelegatedStorePawnSnapshot[] AvailableDelegatedStorePawns(string city,Caravan caravan)
        {
            if (!core.IsReady) throw new InvalidOperationException("Delegated management is unavailable");
            var site=RequireBase(city);
            if (caravan == null || caravan.Destroyed || !Find.WorldObjects.Contains(caravan)) return new DelegatedStorePawnSnapshot[0];
            return caravan.PawnsListForReading.OrderBy(p => p.LabelShort,StringComparer.Ordinal).ThenBy(p => p.GetUniqueLoadID(),StringComparer.Ordinal)
                .Select(p => new DelegatedStorePawnSnapshot(p,StoreCandidateProblem(site,caravan,p))).ToArray();
        }
        private string ResidentReturnProblem(string city,string entity,Caravan caravan)
        {
            string problem=CaravanSiteProblem(RequireBase(city),caravan); if (problem != "None") return problem;
            if (ledger.HasPendingReference(city)) return "CityLocked";
            if (DelegatedTransfers().Any(t => t.EntityId == entity && t.Stage != PawnCustodyStage.Committed)) return "PendingCustody";
            if (TransportReserved(entity)) return "Reserved";
            if (core.Rations.HasPending(entity)) return "PendingRations";
            return "None";
        }
        public DelegatedResidentSnapshot[] DelegatedCustodyResidents(string city)
        {
            if (!core.IsReady) throw new InvalidOperationException("Delegated management is unavailable");
            RequireBase(city);
            return DelegatedStored().Where(r => r.CityId == city).Select(r =>
            {
                var caravan=DelegatedAnchor(r.CaravanId);
                return new DelegatedResidentSnapshot(r.EntityId,Original(r.EntityId),caravan,ResidentReturnProblem(city,r.EntityId,caravan));
            }).OrderBy(r => r.Name,StringComparer.Ordinal).ToArray();
        }
        public DelegatedPendingSnapshot[] DelegatedCustodyPending(string city)
        {
            if (!core.IsReady) throw new InvalidOperationException("Delegated management is unavailable");
            if (city != null) RequireBase(city);
            return DelegatedTransfers().Where(t => t.Stage != PawnCustodyStage.Committed && (city == null || t.CityId == city)).Select(t =>
            {
                var caravan=DelegatedAnchor(t.CaravanId); string problem=CaravanSiteProblem(RequireBase(t.CityId),caravan);
                if (problem == "None" && ledger.HasPendingReference(t.CityId)) problem="CityLocked";
                if (problem == "None" && TransportReserved(t.EntityId)) problem="Reserved";
                if (problem == "None" && core.Rations.HasPending(t.EntityId)) problem="PendingRations";
                if (problem == "None" && !t.IsRelease && !caravan.PawnsListForReading.Any(p => p.GetUniqueLoadID() != t.NativeId &&
                    p.RaceProps.Humanlike && p.IsColonist && CaravanOriginal(caravan,p))) problem="NoEscort";
                return new DelegatedPendingSnapshot(t,Original(t.EntityId),caravan,problem);
            }).ToArray();
        }
    }
}
