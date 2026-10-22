using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Inheritance.Core;
using Verse;

namespace Inheritance.Colony
{
    // Coordinates separate abstract warehouses and native transport journals. Never converts or manufactures map items.
    public sealed partial class ColonyService
    {
        public const string Actor = "inheritance.colony";
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ColonyState));
        private ColonyState state;
        private readonly CoreWorldComponent core;
        private readonly CoreLedger ledger;
        public ColonyService(CoreWorldComponent core, CoreLedger ledger, string payload)
        {
            this.core = core; this.ledger = ledger;
            if (payload == null)
            {
                if (ledger.Entities.Any(e => e.Provider == Actor)) throw new InvalidOperationException("Colony payload is missing for registered assets");
                state = new ColonyState { scope = core.Header.SaveScopeId };
            }
            else
            {
                using (var text = new StringReader(payload))
                using (var reader = XmlReader.Create(text,new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    state = (ColonyState)Serializer.Deserialize(reader);
                if (state == null || state.bases == null || state.stocks == null || state.transports == null || state.people == null || state.animals == null || state.physical == null || state.returns == null || state.maintenance == null || state.schema != 1 || state.scope != core.Header.SaveScopeId ||
                    state.baseCount != state.bases.Count || state.stockCount != state.stocks.Count || state.transportCount != state.transports.Count || state.personCount != state.people.Count || state.animalCount != state.animals.Count || state.physicalCount != state.physical.Count || state.returnCount != state.returns.Count || state.maintenanceCount != state.maintenance.Count || state.maintenance.Any(p => p == null || p.controls == null || p.controlCount != p.controls.Count))
                    throw new InvalidOperationException("Colony save header or record count mismatch");
            }
            ValidateStructure();
            ValidateCheckpoints();
            foreach (var r in state.transports)
            {
                if (r.stage == TransportStage.Planning) CompletePlanning(r);
                else if (r.stage == TransportStage.Departing) CompleteDeparture(r);
                else if (r.stage == TransportStage.Arriving) CompleteArrival(r);
                else if (r.stage == TransportStage.Cancelling) CompleteCancellation(r);
            }
            Validate();
            RestorePhysical();
            RestoreDelegatedReturns();
            Validate();
            Log.Message("[Inheritance-Colony] Ready; bases=" + state.bases.Count + "; transports=" + state.transports.Count);
        }
        public IEnumerable<BaseSnapshot> Bases { get { return state.bases.Select(b => new BaseSnapshot(b)).ToArray(); } }
        public IEnumerable<TransportSnapshot> Transports { get { return state.transports.Select(r => new TransportSnapshot(r)).ToArray(); } }
        private void Gate() { ledger.AssertWriteAccess(Actor); }
        private string Create(string cause, EntitySpec spec)
        {
            var op = ledger.FindOperation(Actor,"create",cause);
            string id = op == null ? ledger.NewOperationId(Actor) : op.Id;
            ledger.PrepareCreate(id,cause,spec); return ledger.Commit(id).ResultId;
        }
        private void EnsureCommunity()
        { if (state.communityId == null) state.communityId = Create("colony-community",new EntitySpec("community","連絡局共同体",identityKey:"colony-community")); }
        public string RegisterDirectMap(Map map)
        {
            if (map == null || !Find.Maps.Contains(map) || map.ParentFaction != RimWorld.Faction.OfPlayer) throw new InvalidOperationException("Only active player maps can be registered");
            if (state.bases.Any(b => b.tile >= 0 && map.Tile == new RimWorld.Planet.PlanetTile(b.tile,0))) throw new InvalidOperationException("Map tile already belongs to a delegated site");
            var prior = state.bases.SingleOrDefault(b => b.mode == BaseMode.Direct && b.mapId == map.uniqueID);
            if (prior != null) { Gate(); return prior.cityId; }
            return RegisterBase("map/" + map.uniqueID,map.Parent.LabelCap.ToString(),BaseMode.Direct,map.uniqueID);
        }
        public string RegisterDelegatedBase(string key, string name)
        { return RegisterBase(key,name,BaseMode.Delegated,-1); }
        private string RegisterBase(string key, string name, BaseMode mode, int mapId, int tile = -1)
        {
            Gate();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name)) throw new InvalidOperationException("Base name and key are required");
            var prior = state.bases.SingleOrDefault(b => b.key == key);
            if (prior != null)
            {
                if (prior.name != name || prior.mode != mode || prior.mapId != mapId || prior.tile != tile) throw new InvalidOperationException("Base key was reused for a different base");
                return prior.cityId;
            }
            if (mode == BaseMode.Direct && state.bases.Any(b => b.mapId == mapId)) throw new InvalidOperationException("Map already belongs to a base");
            EnsureCommunity();
            string city = Create("base/" + key,new EntitySpec("city",name,controllerId:state.communityId,ownerId:state.communityId,communityId:state.communityId,identityKey:key));
            state.bases.Add(new BaseRecord { key = key, name = name, mode = mode, mapId = mapId, cityId = city, tile = tile }); Validate(); return city;
        }
        // This is an explicit abstract-stock import API, not a physical-item conversion API.
        // The first prototype invokes it only from the development test mod.
        public string RegisterAbstractStock(string key, string baseId, string itemKey, long quantity)
        {
            Gate(); RequireBase(baseId);
            if (string.IsNullOrEmpty(key) || quantity <= 0) throw new InvalidOperationException("Positive stock and an import key are required");
            var prior = state.stocks.SingleOrDefault(s => s.key == key);
            if (prior != null)
            {
                if (prior.baseId != baseId || prior.itemKey != itemKey || prior.initialQuantity != quantity) throw new InvalidOperationException("Stock import key was reused");
                return prior.lotId;
            }
            core.AssertNoActiveSupplyTransfer(cityId:baseId); string lot = Create("stock/" + key,new EntitySpec("lot",ownerId:state.communityId,custodyId:baseId,quantity:quantity,itemKey:itemKey,identityKey:key));
            state.stocks.Add(new StockRecord { key = key, baseId = baseId, lotId = lot, itemKey = itemKey, initialQuantity = quantity }); Validate(); return lot;
        }
        public long Inventory(string baseId, string itemKey) { RequireBase(baseId); return ledger.Inventory(baseId,itemKey); }
        public long Reserved(string lotId) { return ledger.Reserved(lotId); }
        public string ReserveTransport(string cause, string lotId, string destinationId, long amount, long dueTick)
        {
            Gate();
            RejectReturnCause(cause);
            var prior = state.transports.SingleOrDefault(x => x.cause == cause);
            if (prior != null)
            {
                if (prior.sourceLotId != lotId || prior.destinationId != destinationId || prior.amount != amount || prior.dueTick != dueTick) throw new InvalidOperationException("Transport cause was reused for a different request");
                if (prior.stage == TransportStage.Planning) CompletePlanning(prior);
                return prior.shipmentId;
            }
            if (string.IsNullOrEmpty(cause) || amount <= 0 || dueTick < Find.TickManager.TicksGame) throw new InvalidOperationException("Invalid transport cause, quantity or time");
            var lot = ledger.GetEntity(lotId); RequireBase(lot.CustodyId); RequireBase(destinationId);
            if (lot.Provider != Actor || lot.Authority != SimulationAuthority.Abstract || lot.CustodyId == destinationId || ledger.Available(lotId) < amount)
                throw new InvalidOperationException("Transport stock is unavailable");
            core.AssertNoActiveSupplyTransfer(cityId:lot.CustodyId); core.AssertNoActiveSupplyTransfer(cityId:destinationId);
            var r = new TransportRecord { cause = cause, sourceId = lot.CustodyId, destinationId = destinationId, sourceLotId = lotId,
                amount = amount, dueTick = dueTick, sourceRevision = lot.Revision, stage = TransportStage.Planning };
            state.transports.Add(r); CompletePlanning(r); Validate(); return r.shipmentId;
        }
        private void CompletePlanning(TransportRecord r)
        {
            Gate();
            if (NeedsAbstractProof("create","shipment/"+r.cause) || NeedsAbstractProof("reserve","reserve/"+r.cause)) AssertAbstractSupplyFree(r);
            string shipment = Create("shipment/" + r.cause,new EntitySpec("shipment",r.cause,ownerId:state.communityId,identityKey:ManifestIdentity(r)));
            if (r.shipmentId != null && r.shipmentId != shipment) throw new InvalidOperationException("Shipment ID changed"); r.shipmentId = shipment;
            var prior = ledger.FindOperation(Actor,"reserve","reserve/" + r.cause);
            string id = prior == null ? r.reserveOperation ?? ledger.NewOperationId(Actor) : prior.Id;
            if (r.reserveOperation != null && r.reserveOperation != id) throw new InvalidOperationException("Reservation operation changed");
            r.reserveOperation = id;
            ledger.PrepareReservation(id,"reserve/" + r.cause,r.sourceLotId,r.amount,r.sourceRevision);
            string reservation = ledger.Commit(id).ResultId;
            if (r.reservationId != null && r.reservationId != reservation) throw new InvalidOperationException("Reservation identity changed");
            r.reservationId = reservation; r.stage = TransportStage.Reserved;
        }
        private TransportRecord Job(string cause)
        { var r = state.transports.SingleOrDefault(x => x.cause == cause); if (r == null) throw new InvalidOperationException("Unknown transport"); return r; }
        public void Depart(string cause)
        {
            Gate(); var r = Job(cause);
            if (r.stage == TransportStage.InTransit || r.stage == TransportStage.Arrived) return;
            if (r.stage != TransportStage.Reserved && r.stage != TransportStage.Departing) throw new InvalidOperationException("Transport cannot depart");
            if (NeedsAbstractProof("depart","depart/"+r.cause)) AssertAbstractSupplyFree(r); r.stage = TransportStage.Departing; CompleteDeparture(r); Validate();
        }
        private void CompleteDeparture(TransportRecord r)
        {
            Gate(); var prior = ledger.FindOperation(Actor,"depart","depart/" + r.cause); if (prior == null || prior.Stage != OperationStage.Committed) AssertAbstractSupplyFree(r);
            string id = prior == null ? r.departOperation ?? ledger.NewOperationId(Actor) : prior.Id;
            if (r.departOperation != null && r.departOperation != id) throw new InvalidOperationException("Departure operation changed"); r.departOperation = id;
            ledger.PrepareDeparture(id,"depart/" + r.cause,r.reservationId,r.shipmentId);
            string cargo = ledger.Commit(id).ResultId;
            if (r.cargoId != null && r.cargoId != cargo) throw new InvalidOperationException("Cargo identity changed"); r.cargoId = cargo;
            r.stage = TransportStage.InTransit;
        }
        public void Arrive(string cause)
        {
            Gate(); var r = Job(cause);
            if (r.stage == TransportStage.Arrived) return;
            if (r.stage != TransportStage.InTransit && r.stage != TransportStage.Arriving) throw new InvalidOperationException("Transport is not in transit");
            if (Find.TickManager.TicksGame < r.dueTick) throw new InvalidOperationException("Transport has not reached its arrival time");
            if (NeedsAbstractProof("move","arrive/"+r.cause)) AssertAbstractSupplyFree(r); if (r.stage == TransportStage.InTransit) r.cargoRevision = ledger.GetEntity(r.cargoId).Revision;
            r.stage = TransportStage.Arriving; CompleteArrival(r); Validate();
        }
        private void CompleteArrival(TransportRecord r)
        {
            Gate(); if (NeedsAbstractProof("move","arrive/"+r.cause)) AssertAbstractSupplyFree(r); if (r.cargoRevision < 0) throw new InvalidOperationException("Arrival checkpoint lacks its expected revision");
            var prior = ledger.FindOperation(Actor,"move","arrive/" + r.cause);
            string id = prior == null ? r.arriveOperation ?? ledger.NewOperationId(Actor) : prior.Id;
            if (r.arriveOperation != null && r.arriveOperation != id) throw new InvalidOperationException("Arrival operation changed"); r.arriveOperation = id;
            ledger.PrepareMove(id,"arrive/" + r.cause,r.cargoId,r.destinationId,r.cargoRevision); ledger.Commit(id); r.stage = TransportStage.Arrived;
        }
        public void Cancel(string cause)
        {
            Gate(); var r = Job(cause);
            if (r.stage == TransportStage.Cancelled) return;
            if (r.stage != TransportStage.Reserved && r.stage != TransportStage.Cancelling) throw new InvalidOperationException("Only a shipment that has not departed can be cancelled");
            if (NeedsAbstractProof("release","cancel/"+r.cause)) AssertAbstractCancellationSupplyFree(r); r.stage = TransportStage.Cancelling; CompleteCancellation(r); Validate();
        }
        private void CompleteCancellation(TransportRecord r)
        {
            Gate(); var prior = ledger.FindOperation(Actor,"release","cancel/" + r.cause); if (prior == null || prior.Stage != OperationStage.Committed) AssertAbstractCancellationSupplyFree(r);
            string id = prior == null ? r.cancelOperation ?? ledger.NewOperationId(Actor) : prior.Id;
            if (r.cancelOperation != null && r.cancelOperation != id) throw new InvalidOperationException("Cancellation operation changed"); r.cancelOperation = id;
            ledger.PrepareRelease(id,"cancel/" + r.cause,r.reservationId); ledger.Commit(id); r.stage = TransportStage.Cancelled;
        }
        public void Tick()
        {
            foreach (var r in state.transports.Where(r => r.stage == TransportStage.InTransit && r.dueTick <= Find.TickManager.TicksGame).ToArray())
                if (!AbstractSupplyBusy(r)) Arrive(r.cause);
            TickPhysical();
            TickDelegatedReturns();
            TickDelegatedMaintenance();
        }
        public string SaveSnapshot(CoreWorldComponent caller)
        {
            if (caller != core || Find.World.GetComponent<CoreWorldComponent>() != core) throw new InvalidOperationException("Colony snapshot belongs to another world");
            if (state.bases.Count == 0 && state.communityId == null) return null;
            if (state.transports.Any(r => r.stage == TransportStage.Planning || r.stage == TransportStage.Departing || r.stage == TransportStage.Arriving || r.stage == TransportStage.Cancelling)) ValidateStructure();
            else Validate();
            state.UpdateCounts();
            using (var writer = new StringWriter(CultureInfo.InvariantCulture)) { Serializer.Serialize(writer,state); return writer.ToString(); }
        }
        private void AssertAbstractSupplyFree(TransportRecord transport)
        {
            // Validated actor read works during module bootstrap as well as normal play.
            if (core.ReadRationSupplyTransfers(Actor).Any(t => t.Stage == RationSupplyTransferStage.Armed &&
                (t.CityId == transport.sourceId || t.CityId == transport.destinationId)))
                throw new InvalidOperationException("Original is locked by an active supply transfer preparation");
        }
        private void AssertAbstractCancellationSupplyFree(TransportRecord transport)
        {
            // Releasing an unspent abstract reservation changes its source lot only.
            // A responsibility at the unvisited destination does not prohibit safe cancellation.
            if (core.ReadRationSupplyTransfers(Actor).Any(t => t.Stage == RationSupplyTransferStage.Armed && t.CityId == transport.sourceId))
                throw new InvalidOperationException("Original is locked by an active supply transfer preparation");
        }
        private bool NeedsAbstractProof(string kind,string cause)
        {
            var proof=ledger.FindOperation(Actor,kind,cause);
            return proof == null || proof.Stage != OperationStage.Committed;
        }
        private bool AbstractSupplyBusy(TransportRecord transport)
        {
            var journal=core.SupplyTransfers;
            return journal.HasActiveForCity(transport.sourceId) || journal.HasActiveForCity(transport.destinationId);
        }
        private BaseRecord RequireBase(string id)
        { var b = state.bases.SingleOrDefault(x => x.cityId == id); if (b == null) throw new InvalidOperationException("Unknown Colony base"); return b; }
        private static string ManifestIdentity(TransportRecord r)
        {
            var text = new StringBuilder();
            foreach (var value in new object[] { r.cause,r.sourceId,r.destinationId,r.sourceLotId,r.amount,r.dueTick,r.sourceRevision })
            { string part = Convert.ToString(value,CultureInfo.InvariantCulture); text.Append(part.Length).Append(':').Append(part); }
            using (var sha = SHA256.Create()) return "transport:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
        }
        private void ValidateStructure()
        {
            if (state.bases.Any(x => x == null || string.IsNullOrEmpty(x.key) || string.IsNullOrEmpty(x.name) || !Enum.IsDefined(typeof(BaseMode),x.mode)) ||
                state.bases.Select(x => x.key).Distinct().Count() != state.bases.Count || state.bases.Select(x => x.cityId).Distinct().Count() != state.bases.Count ||
                state.stocks.Any(x => x == null || string.IsNullOrEmpty(x.key) || x.initialQuantity <= 0) || state.stocks.Select(x => x.key).Distinct().Count() != state.stocks.Count ||
                state.transports.Any(x => x == null || string.IsNullOrEmpty(x.cause) || x.amount <= 0 || x.dueTick < 0 || x.sourceRevision < 0 || !Enum.IsDefined(typeof(TransportStage),x.stage)) ||
                state.transports.Select(x => x.cause).Distinct().Count() != state.transports.Count)
                throw new InvalidOperationException("Invalid or duplicate Colony records");
            foreach (var r in state.transports)
            {
                RequireBase(r.sourceId); RequireBase(r.destinationId);
                var source = ledger.GetEntity(r.sourceLotId);
                if (r.sourceId == r.destinationId || source.Provider != Actor || source.Kind != "lot" || source.Authority != SimulationAuthority.Abstract)
                    throw new InvalidOperationException("Transport source is invalid");
            }
        }
        private void ValidateCheckpoints()
        {
            // Check immutable intent before resuming any operation. A corrupted request must not move stock first.
            foreach (var r in state.transports)
            {
                var created = ledger.FindOperation(Actor,"create","shipment/" + r.cause);
                if (created != null && created.Stage == OperationStage.Committed)
                {
                    var shipment = ledger.GetEntity(created.ResultId);
                    if ((r.shipmentId != null && r.shipmentId != shipment.Id) || shipment.IdentityKey != ManifestIdentity(r) || shipment.OwnerId != state.communityId)
                        throw new InvalidOperationException("Transport checkpoint does not match its original manifest");
                }
                var reserve = ledger.FindOperation(Actor,"reserve","reserve/" + r.cause);
                if (reserve != null && (reserve.SubjectId != r.sourceLotId || reserve.Quantity != r.amount || reserve.ExpectedRevision != r.sourceRevision ||
                    (r.reserveOperation != null && r.reserveOperation != reserve.Id) || (r.reservationId != null && r.reservationId != reserve.ResultId)))
                    throw new InvalidOperationException("Transport checkpoint does not match its reservation");
                if (r.stage == TransportStage.Arriving && Find.TickManager.TicksGame < r.dueTick)
                    throw new InvalidOperationException("Arrival intent is earlier than its scheduled time");
            }
        }
        private void Validate()
        {
            ValidateStructure();
            var known = new HashSet<string>(StringComparer.Ordinal);
            ValidatePhysical(known);
            ValidateDelegatedReturns(known);
            ValidateLocalCustody(known);
            ValidateDelegatedCustody(known);
            ValidateDelegatedRations(known);
            ValidateDelegatedMaintenance(known);
            ValidateDelegatedSupplyIntents(known);
            ValidateDelegatedSupplyTransfers(known);
            if (state.communityId != null)
            {
                var community = ledger.GetEntity(state.communityId);
                if (community.Kind != "community" || community.Provider != Actor || community.IdentityKey != "colony-community") throw new InvalidOperationException("Colony community mismatch");
                known.Add(community.Id);
            }
            foreach (var b in state.bases)
            {
                var city = ledger.GetEntity(b.cityId);
                if (!known.Add(city.Id) || city.Provider != Actor || city.Kind != "city" || city.Name != b.name || city.IdentityKey != b.key || city.ControllerId != state.communityId || city.OwnerId != state.communityId ||
                    (b.mode == BaseMode.Delegated && b.mapId != -1) || (b.mode == BaseMode.Direct && !Find.Maps.Any(m => m.uniqueID == b.mapId && m.ParentFaction == RimWorld.Faction.OfPlayer)))
                    throw new InvalidOperationException("Colony base or direct map mismatch");
            }
            foreach (var stock in state.stocks)
            {
                RequireBase(stock.baseId); var lot = ledger.GetEntity(stock.lotId);
                if (!known.Add(lot.Id) || lot.Provider != Actor || lot.Kind != "lot" || lot.ItemKey != stock.itemKey || lot.IdentityKey != stock.key || lot.OwnerId != state.communityId || lot.Authority != SimulationAuthority.Abstract || lot.CustodyId != stock.baseId)
                    throw new InvalidOperationException("Colony stock mismatch");
            }
            foreach (var r in state.transports)
            {
                if (r.stage == TransportStage.Planning || r.stage == TransportStage.Departing || r.stage == TransportStage.Arriving || r.stage == TransportStage.Cancelling)
                    throw new InvalidOperationException("Unfinished Colony transition must be resumed before use");
                var shipment = ledger.GetEntity(r.shipmentId);
                if (!known.Add(shipment.Id) || shipment.Kind != "shipment" || shipment.Provider != Actor || shipment.IdentityKey != ManifestIdentity(r) || shipment.OwnerId != state.communityId)
                    throw new InvalidOperationException("Shipment identity mismatch");
                var reserve = ledger.GetOperation(r.reserveOperation);
                if (reserve == null || reserve.Stage != OperationStage.Committed || reserve.Kind != "reserve" || reserve.Actor != Actor || reserve.CauseId != "reserve/" + r.cause || reserve.ResultId != r.reservationId ||
                    reserve.SubjectId != r.sourceLotId || reserve.Quantity != r.amount || reserve.ExpectedRevision != r.sourceRevision) throw new InvalidOperationException("Reservation journal mismatch");
                var reservation = ledger.GetReservation(r.reservationId);
                if (reservation.Quantity != r.amount || reservation.LotId != r.sourceLotId || reservation.CustodyId != r.sourceId || reservation.Status !=
                    (r.stage == TransportStage.Cancelled ? ReservationStatus.Released : r.stage == TransportStage.Reserved ? ReservationStatus.Active : ReservationStatus.Consumed))
                    throw new InvalidOperationException("Transport reservation status mismatch");
                if (r.stage == TransportStage.InTransit || r.stage == TransportStage.Arrived)
                {
                    var depart = ledger.GetOperation(r.departOperation); var cargo = ledger.GetEntity(r.cargoId);
                    if (!known.Add(cargo.Id) || depart == null || depart.Stage != OperationStage.Committed || depart.Actor != Actor || depart.Kind != "depart" || depart.CauseId != "depart/" + r.cause || depart.ResultId != r.cargoId || depart.SubjectId != r.reservationId || depart.DestinationId != r.shipmentId ||
                        cargo.Provider != Actor || cargo.LineageId != r.sourceLotId || cargo.Quantity < 0 || cargo.Quantity > r.amount || (r.stage == TransportStage.InTransit && cargo.Quantity != r.amount) || cargo.OwnerId != state.communityId || cargo.Authority != SimulationAuthority.Abstract ||
                        cargo.CustodyId != (r.stage == TransportStage.InTransit ? r.shipmentId : r.destinationId))
                        throw new InvalidOperationException("Transport cargo mismatch");
                    if (r.stage == TransportStage.Arrived)
                    {
                        var arrive = ledger.GetOperation(r.arriveOperation);
                        if (arrive == null || arrive.Stage != OperationStage.Committed || arrive.Actor != Actor || arrive.Kind != "move" || arrive.CauseId != "arrive/" + r.cause || arrive.ResultId != r.cargoId || arrive.SubjectId != r.cargoId || arrive.DestinationId != r.destinationId || arrive.ExpectedRevision != r.cargoRevision) throw new InvalidOperationException("Arrival journal mismatch");
                    }
                }
                else if (r.cargoId != null || r.departOperation != null || r.arriveOperation != null) throw new InvalidOperationException("Undeparted shipment contains cargo");
                if (r.stage == TransportStage.Cancelled)
                {
                    var cancel = ledger.GetOperation(r.cancelOperation);
                    if (cancel == null || cancel.Stage != OperationStage.Committed || cancel.Actor != Actor || cancel.Kind != "release" || cancel.CauseId != "cancel/" + r.cause || cancel.ResultId != r.reservationId || cancel.SubjectId != r.reservationId) throw new InvalidOperationException("Cancellation journal mismatch");
                }
            }
            foreach (var group in state.stocks.GroupBy(s => s.itemKey))
            {
                long imported = group.Aggregate(0L,(n,s) => checked(n+s.initialQuantity));
                long current = ledger.Entities.Where(e => e.Provider == Actor && e.Kind == "lot" && e.ItemKey == group.Key && e.Authority == SimulationAuthority.Abstract).Aggregate(0L,(n,e) => checked(n+e.Quantity));
                if (current != imported) throw new InvalidOperationException("Colony abstract stock conservation failed");
            }
            if (ledger.Entities.Any(e => e.Provider == Actor && e.Status == EntityStatus.Active && e.Kind != "operation" && e.Kind != "reservation" && !known.Contains(e.Id)))
                throw new InvalidOperationException("Orphan Colony asset or missing checkpoint");
        }
    }
}
