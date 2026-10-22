using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.Linq;
using Verse;

namespace Inheritance.Colony
{
    public enum BaseMode { Direct, Delegated }
    public enum TransportStage { Planning, Reserved, Departing, InTransit, Arriving, Arrived, Cancelling, Cancelled }
    public enum PhysicalStage { Reserved, Departing, InTransit, Arriving, Arrived, Cancelled, NeedsAttention }
    public enum DelegatedReturnStage { InTransit, Arriving, Returned, NeedsAttention }
    public enum DelegatedMaintenanceStage { Active, Held, Stopped, Released }
    public enum MaintenanceControlKind { Start, Stop, Resume, Hold }
    public sealed class MaintenanceControlRecord
    {
        public string cause, receipt, previousReceipt, problem;
        public int day = -1;
        public MaintenanceControlKind kind;
    }
    public sealed class DelegatedMaintenanceRecord
    {
        public string id, cause, cityId, entityId, storeTransferId;
        public int startDay = -1, controlCount;
        [XmlArrayItem("li")] public List<MaintenanceControlRecord> controls = new List<MaintenanceControlRecord>();
    }
    public sealed class DelegatedReturnRecord
    {
        public string cause, outwardCause, shipmentId, departureReceipt, arrivalReceipt, problem;
        public DelegatedReturnStage stage;
    }
    public sealed class PersonRecord
    { public string id, nativeId; }
    public sealed class AnimalRecord
    { public string id, nativeId, defName; }
    public sealed class PhysicalCargoRecord
    { public string sourceNativeId, defName, cargoNativeId; public int count; public bool journeySupply; }
    public sealed class PhysicalLossRecord
    {
        public string cause, receipt, previousReceipt;
        [XmlArrayItem("li")] public List<int> remaining = new List<int>();
    }
    public sealed class PhysicalTransportRecord
    {
        public int destinationTile = -1;
        public string cause, sourceId, destinationId, shipmentId, caravanId, departureReceipt, arrivalReceipt, cancelReceipt, problem;
        public PhysicalStage stage;
        [XmlArrayItem("li")] public List<string> party = new List<string>();
        [XmlArrayItem("li")] public List<string> animals = new List<string>();
        [XmlArrayItem("li")] public List<PhysicalCargoRecord> cargo = new List<PhysicalCargoRecord>();
        public int partyCount, cargoCount, animalCount;
        public int lossCount;
        [XmlArrayItem("li")] public List<PhysicalLossRecord> losses = new List<PhysicalLossRecord>();
    }
    public sealed class BaseRecord
    { public string key, name, cityId; public int mapId = -1, tile = -1; public BaseMode mode; }
    public sealed class StockRecord
    { public string key, lotId, baseId, itemKey; public long initialQuantity; }
    public sealed class TransportRecord
    {
        public string cause, sourceId, destinationId, sourceLotId, shipmentId, reservationId, cargoId;
        public string reserveOperation, departOperation, arriveOperation, cancelOperation;
        public long amount, dueTick, sourceRevision, cargoRevision = -1;
        public TransportStage stage;
    }
    public sealed class ColonyState
    {
        public int schema = 1, baseCount, stockCount, transportCount;
        public string scope, communityId;
        [XmlArrayItem("li")] public List<BaseRecord> bases = new List<BaseRecord>();
        [XmlArrayItem("li")] public List<StockRecord> stocks = new List<StockRecord>();
        [XmlArrayItem("li")] public List<TransportRecord> transports = new List<TransportRecord>();
        // Additive schema-1 extension. Older saves have no physical records and default to empty tables.
        public int personCount, physicalCount;
        [XmlArrayItem("li")] public List<PersonRecord> people = new List<PersonRecord>();
        [XmlArrayItem("li")] public List<PhysicalTransportRecord> physical = new List<PhysicalTransportRecord>();
        public int animalCount;
        [XmlArrayItem("li")] public List<AnimalRecord> animals = new List<AnimalRecord>();
        public int returnCount;
        [XmlArrayItem("li")] public List<DelegatedReturnRecord> returns = new List<DelegatedReturnRecord>();
        public int maintenanceCount;
        [XmlArrayItem("li")] public List<DelegatedMaintenanceRecord> maintenance = new List<DelegatedMaintenanceRecord>();
        public void UpdateCounts() { baseCount = bases.Count; stockCount = stocks.Count; transportCount = transports.Count; personCount = people.Count; physicalCount = physical.Count; animalCount = animals.Count; returnCount = returns.Count; maintenanceCount = maintenance.Count; foreach (var plan in maintenance) plan.controlCount = plan.controls.Count; }
    }
    public sealed class BaseSnapshot
    {
        public readonly string Id, Name;
        public readonly int MapId, Tile;
        public readonly long PhysicalSteel;
        public readonly int Colonists;
        public readonly BaseMode Mode;
        internal BaseSnapshot(BaseRecord b)
        {
            Id = b.cityId; Name = b.name; MapId = b.mapId; Tile = b.tile; Mode = b.mode;
            var map = b.mode == BaseMode.Direct ? Find.Maps.SingleOrDefault(m => m.uniqueID == b.mapId) : null;
            if (map != null) { Colonists = map.mapPawns.FreeColonistsSpawned.Count; PhysicalSteel = map.listerThings.ThingsOfDef(RimWorld.ThingDefOf.Steel).Aggregate(0L,(n,t) => checked(n+t.stackCount)); }
        }
    }
    public sealed class TransportSnapshot
    {
        public readonly string Cause, SourceId, DestinationId, SourceLotId, ShipmentId, CargoId;
        public readonly long Amount, DueTick;
        public readonly TransportStage Stage;
        internal TransportSnapshot(TransportRecord r)
        { Cause = r.cause; SourceId = r.sourceId; DestinationId = r.destinationId; SourceLotId = r.sourceLotId; ShipmentId = r.shipmentId; CargoId = r.cargoId; Amount = r.amount; DueTick = r.dueTick; Stage = r.stage; }
    }
    public sealed class PhysicalTransportSnapshot
    {
        public readonly string Cause, SourceId, DestinationId, ShipmentId, CaravanId, Problem;
        public readonly PhysicalStage Stage;
        public readonly int People, Animals;
        public readonly int DestinationTile;
        public readonly long Cargo, Lost;
        internal PhysicalTransportSnapshot(PhysicalTransportRecord r)
        { Cause=r.cause; SourceId=r.sourceId; DestinationId=r.destinationId; DestinationTile=r.destinationTile; ShipmentId=r.shipmentId; CaravanId=r.caravanId; Problem=r.problem; Stage=r.stage; People=r.party.Count; Animals=r.animals.Count; Cargo=r.cargo.Sum(c => (long)c.count); if (r.losses.Count > 0) Lost=r.cargo.Select((c,i) => (long)c.count-r.losses.Last().remaining[i]).Sum(); }
    }
}
