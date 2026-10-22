using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    // Staged only. Armed is metadata responsibility, never native food delivery.
    // No food/Pawn creation, movement, splitting, consumption or clock fixture.
    public sealed class SupplyTransferProbe : GameComponent
    {
        private static readonly string[] Modes={"arm","load","final-load","stale","animal","stale-armed","stale-armed-load"};
        private const string PreparedSave="INH_SupplyIntentPrepared",StaleInput="INH_SupplyIntentStalePending";
        private const string ArmedSave="INH_SupplyTransferArmed",DisarmedSave="INH_SupplyTransferDisarmed";
        private const string ArmCause="supply-transfer-probe-arm",DisarmCause="supply-transfer-probe-disarm";
        private static readonly BindingFlags All=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly BindingFlags PrivateStatic=BindingFlags.NonPublic|BindingFlags.Static;
        private static readonly BindingFlags PrivateInstance=BindingFlags.NonPublic|BindingFlags.Instance;
        private bool done;
        private int pausedFrames;
        private CoreWorldComponent core;
        private ColonyService service;
        private SupplyIntentProbe forensic;
        private string[] ids;
        private Caravan caravan;
        private PawnCustodyResidentSnapshot[] residents;
        private string intentId;
        private string routeSource;
        private Pawn[] routeParty;
        private CargoSelection[] routeCargo;

        public SupplyTransferProbe(Game game) { }
        private static bool Has(string mode) { return GenCommandLine.CommandLineArgPassed("inh-supply-transfer-"+mode); }
        private static bool Selected { get { return Modes.Any(Has); } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-SUPPLY-TRANSFER] PASS "+message); }
        private static string Pack(IEnumerable<string> values)
        { return string.Concat(values.Select(v => v == null ? "-1:" : v.Length.ToString(CultureInfo.InvariantCulture)+":"+v).ToArray()); }
        private static string[] Unpack(string packed)
        {
            var values=new List<string>(); int position=0;
            while (position < packed.Length)
            {
                int colon=packed.IndexOf(':',position);
                if (colon < position) throw new InvalidOperationException("Invalid durable evidence field framing");
                int length=int.Parse(packed.Substring(position,colon-position),CultureInfo.InvariantCulture); position=colon+1;
                if (length == -1) { values.Add(null); continue; }
                if (length < 0 || position+length > packed.Length) throw new InvalidOperationException("Invalid durable evidence field length");
                values.Add(packed.Substring(position,length)); position+=length;
            }
            return values.ToArray();
        }
        private static object Call(string name,object target,params object[] args)
        {
            var method=typeof(SupplyIntentProbe).GetMethods(target == null ? PrivateStatic : PrivateInstance)
                .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
            try { return method.Invoke(target,args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        private static object Field(object target,string name) { return Call("Field",null,target,name); }
        private static string Canonical(object value) { return (string)Call("Canonical",null,value); }
        private static string Except(object target,params string[] names) { return (string)Call("Except",null,target,names); }
        private string DurableExcept(object target,params string[] names) { return (string)Call("DurableExcept",forensic,target,names); }
        private static Dictionary<string,string> Records(object target,string name) { return (Dictionary<string,string>)Call("Records",null,target,name); }
        private object Capture() { return Call("Capture",forensic); }
        private string TransferRaw() { return Except(core.SupplyTransfers,"owner"); }
        private Dictionary<string,string> TransferRows() { return Records(core.SupplyTransfers,"records"); }
        private string RawColony() { return (string)Call("RawColony",forensic); }
        private string Clock() { return (string)Call("Clock",null); }
        private string NativePersistent() { return (string)Call("PersistentNative",forensic); }
        private string FixedIntent() { return (string)Call("FixedIntent",forensic,intentId); }
        private RationSupplyIntentSnapshot Intent { get { return service.DelegatedSupplyIntents.Single(i => i.Id == intentId); } }
        private RationSupplyTransferSnapshot Transfer(string id) { return service.DelegatedSupplyTransfers.Single(t => t.Id == id); }
        private object RawTransfer(string id)
        { return ((IEnumerable)Field(core.SupplyTransfers,"records")).Cast<object>().Single(r => (string)Field(r,"id") == id); }
        private static void SetProbeField(object target,string name,object value)
        {
            var field=target.GetType().GetField(name,All);
            if (field == null) throw new InvalidOperationException("Required forensic probe field is missing: "+name);
            field.SetValue(target,value); // Only this test helper's fields, never product state.
        }
        private void Bind()
        {
            forensic=new SupplyIntentProbe(Current.Game); SetProbeField(forensic,"core",core); SetProbeField(forensic,"service",service);
            Call("Bind",forensic);
            ids=(string[])Field(forensic,"ids"); caravan=(Caravan)Field(forensic,"caravan"); residents=(PawnCustodyResidentSnapshot[])Field(forensic,"residents");
            Assert(core.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion && core.Header.SchemaVersion == 9,
                "The validated actual Core schema is the metadata-only transfer schema 9");
        }
        private void NoChange(object before,string transferBefore,string message)
        { Call("Unchanged",forensic,before,message,true); Assert(TransferRaw() == transferBefore,message+": the full new transfer journal is exact too"); }
        private void ReadOnly(Action action,string message)
        { var before=Capture(); string transfer=TransferRaw(); action(); NoChange(before,transfer,message); }
        private static void Refuse(Action action,string message,bool transferReason)
        {
            InvalidOperationException refusal=null; try { action(); } catch (InvalidOperationException error) { refusal=error; }
            Assert(refusal != null,message);
            if (transferReason) Assert(refusal.Message.IndexOf("transfer",StringComparison.OrdinalIgnoreCase) >= 0,
                message+": refusal names the supply-transfer guard rather than an unrelated invalid fixture");
            Log.Message("[INH-SUPPLY-TRANSFER] REFUSAL "+(refusal == null ? "missing" : refusal.Message));
        }
        private void RefuseExact(Action action,string message,bool transferReason)
        { ReadOnly(delegate { Refuse(action,message,transferReason); },message+": +0 allocation, raw Core/intent/transfer/Colony/native/clock exact"); }
        private object RawCity()
        { return ((IEnumerable)Field(Field(core.Ledger,"state"),"entities")).Cast<object>().Single(e => (string)Field(e,"id") == Intent.CityId); }
        private void Delta(object before,string cityBefore,long revisionBefore,bool arm,string message)
        {
            var after=Capture(); var oldEntities=(Dictionary<string,string>)Field(before,"EntityRows"); var oldOperations=(Dictionary<string,string>)Field(before,"OperationRows");
            var newEntities=(Dictionary<string,string>)Field(after,"EntityRows"); var newOperations=(Dictionary<string,string>)Field(after,"OperationRows");
            Assert((string)Field(before,"Colony") == (string)Field(after,"Colony") && (string)Field(before,"Native") == (string)Field(after,"Native") &&
                (string)Field(before,"Clock") == (string)Field(after,"Clock") && (string)Field(before,"Old") == (string)Field(after,"Old") &&
                (string)Field(before,"Supply") == (string)Field(after,"Supply") && (string)Field(before,"Reservations") == (string)Field(after,"Reservations") &&
                (string)Field(before,"LedgerOther") == (string)Field(after,"LedgerOther"),message+": every old intent/journal, raw Colony, identity/biology/cache/food/owner and clock is exact");
            Assert(oldEntities.Where(e => !arm || e.Key != Intent.CityId).All(e => newEntities.ContainsKey(e.Key) && newEntities[e.Key] == e.Value) &&
                oldOperations.All(o => newOperations.ContainsKey(o.Key) && newOperations[o.Key] == o.Value),message+": all other full old Core entities and all old operations are exact");
            Assert(Except(RawCity(),"revision") == cityBefore && core.Ledger.GetEntity(Intent.CityId).Revision == revisionBefore+(arm ? 1 : 0),
                message+(arm ? ": only the named city revision increases by one" : ": the city and every old revision remain exact"));
            var added=core.Ledger.Entities.Where(e => !oldEntities.ContainsKey(e.Id)).ToArray(); var proofs=core.Ledger.Operations.Where(o => !oldOperations.ContainsKey(o.Id)).ToArray();
            Assert(core.Header.NextSequence == (long)Field(before,"Sequence")+2 && core.Ledger.EntityCount == (int)Field(before,"Entities")+2 &&
                core.Ledger.OperationCount == (int)Field(before,"Operations")+1 && added.Length == 2 && added.All(e => e.Provider == ColonyService.Actor) &&
                added.Count(e => e.Kind == "operation") == 1 && added.Count(e => e.Kind == "lease") == 1 && proofs.Length == 1 &&
                proofs[0].Actor == ColonyService.Actor && proofs[0].Kind == "create" && proofs[0].Stage == OperationStage.Committed &&
                added.Any(e => e.Id == proofs[0].Id && e.Kind == "operation") && added.Any(e => e.Id == proofs[0].ResultId && e.Kind == "lease" && e.Quantity == 0 && e.BoundThingLoadId == null),
                message+": exactly +2 actor IDs and one metadata proof, no food or Pawn entity");
            var ledger=Field(core.Ledger,"state");
            Assert((int)Field(ledger,"entityCount") == 0 && (int)Field(ledger,"operationCount") == 0 && (int)Field(ledger,"reservationCount") == 0,
                message+": ordinary raw Core copy counters are observed before any serialization");
        }
        private void Active(string id,bool expected)
        {
            var intent=Intent; var transfers=core.SupplyTransfers;
            ReadOnly(delegate {
                Assert(transfers.HasActiveForIntent(intent.Id) == expected && transfers.HasActiveForCity(intent.CityId) == expected &&
                    transfers.HasActiveForEntity(intent.EntityId) == expected && transfers.HasActiveForNative(intent.NativeId) == expected &&
                    transfers.HasActiveForNative(intent.SourceNativeId) == expected && transfers.HasActiveForFood(intent.FoodNativeId) == expected,
                    expected ? "Armed responsibility covers the exact intent, city, recipient, source native and original food" : "An unarmed or disarmed intention has no active transfer responsibility");
                if (intent.SourceEntityId != null) Assert(transfers.HasActiveForEntity(intent.SourceEntityId) == expected,"The registered source binding has the matching active responsibility");
                if (id != null) Assert(!transfers.HasActiveForIntent(intent.Id,id) && !transfers.HasActiveForCity(intent.CityId,id) &&
                    !transfers.HasActiveForEntity(intent.EntityId,id) && !transfers.HasActiveForNative(intent.NativeId,id) && !transfers.HasActiveForNative(intent.SourceNativeId,id) &&
                    !transfers.HasActiveForFood(intent.FoodNativeId,id),"Excepting only this transfer does not invent another active owner");
                string other=residents.Single(r => r.EntityId != intent.EntityId).NativeId;
                Assert(!transfers.HasActiveForNative(other),"The other stored original native individual is not falsely claimed by this transfer");
            },"Active responsibility queries preserve every raw old/new journal and original");
        }
        private void MatchManifest(RationSupplyTransferSnapshot transfer)
        {
            var intent=Intent; var raw=Call("RawIntent",forensic,intent.Id);
            Assert(transfer.Actor == ColonyService.Actor && transfer.ScopeId == core.Header.SaveScopeId && transfer.IntentId == intent.Id && transfer.IntentDigest == (string)Field(raw,"digest") &&
                transfer.EntityId == intent.EntityId && transfer.NativeId == intent.NativeId && transfer.CityId == intent.CityId && transfer.StoreTransferId == intent.StoreTransferId &&
                transfer.CaravanId == intent.CaravanId && transfer.SourceNativeId == intent.SourceNativeId && transfer.SourceEntityId == intent.SourceEntityId && transfer.SourceKind == intent.SourceKind &&
                transfer.FoodNativeId == intent.FoodNativeId && transfer.DefName == intent.DefName && transfer.StuffDef == intent.StuffDef && transfer.Tile == intent.Tile &&
                transfer.WholeCount == intent.WholeCount && transfer.HitPoints == intent.HitPoints && transfer.UnitNutrition == intent.UnitNutrition && transfer.DailyDemand == intent.DailyDemand,
                "The transfer fixes the entire prepared intention digest and original human/animal/native whole-food manifest");
        }
        private string Arm(string cause)
        {
            var before=Capture(); var oldRows=TransferRows(); string city=Except(RawCity(),"revision"); long revision=core.Ledger.GetEntity(Intent.CityId).Revision;
            string id=service.BeginDelegatedSupplyTransfer(cause,intentId); Delta(before,city,revision,true,"Arm metadata responsibility");
            Assert(TransferRows().Count == oldRows.Count+1 && oldRows.All(r => TransferRows()[r.Key] == r.Value),"Arm adds one transfer row and changes no prior transfer history");
            var transfer=Transfer(id); MatchManifest(transfer);
            Assert(transfer.Cause == cause && transfer.ArmedTick == Find.TickManager.TicksGame && transfer.Stage == RationSupplyTransferStage.Armed &&
                transfer.ArmProofStage == OperationStage.Committed && transfer.DisarmCause == null && transfer.DisarmReceiptId == null,"Arm has exactly its committed first proof and no disarm fields");
            Active(id,true);
            ReadOnly(delegate {
                var inspect=service.InspectDelegatedSupplyTransfer(id); var original=service.InspectDelegatedSupplyIntent(intentId);
                Assert(inspect.Transfer.Id == id && inspect.IsCurrent && inspect.CanDisarm && !inspect.CanResumeProof && inspect.Problems.Count == 0 && original.IsCurrent,
                    "Normal transfer inspect does not reject its own Armed responsibility or rewrite the original intent");
                Assert(service.BeginDelegatedSupplyTransfer(cause,intentId) == id,"Same-cause normal Begin returns the same historical transfer"); service.ResumeDelegatedSupplyTransferProof(id);
            },"Inspect, Begin replay and committed proof resume allocate +0 and change nothing");
            RefuseExact(delegate { service.BeginDelegatedSupplyTransfer(cause+"-duplicate",intentId); },"Another cause cannot create a second active transfer",false);
            string different=service.DelegatedSupplyIntents.First(i => i.Id != intentId).Id;
            RefuseExact(delegate { service.BeginDelegatedSupplyTransfer(cause,different); },"The arm cause cannot be reused for a different intention",false);
            return id;
        }
        private void Disarm(string id,string cause)
        {
            var before=Capture(); var oldRows=TransferRows(); string city=Except(RawCity(),"revision"); long revision=core.Ledger.GetEntity(Intent.CityId).Revision;
            string fixedIntent=FixedIntent(),fixedTransfer=Except(RawTransfer(id),"stage","disarmCause","disarmOp","disarmReceipt");
            service.DisarmDelegatedSupplyTransfer(cause,id); Delta(before,city,revision,false,"Disarm before any native move");
            var transfer=Transfer(id); MatchManifest(transfer);
            Assert(transfer.Stage == RationSupplyTransferStage.Cancelled && transfer.DisarmCause == cause && transfer.DisarmProofStage == OperationStage.Committed &&
                transfer.DisarmReceiptId != null && FixedIntent() == fixedIntent && Intent.Stage == RationSupplyIntentStage.Prepared &&
                Except(RawTransfer(id),"stage","disarmCause","disarmOp","disarmReceipt") == fixedTransfer &&
                TransferRows().Count == oldRows.Count && oldRows.Where(r => r.Key != id).All(r => TransferRows()[r.Key] == r.Value),
                "Disarm appends only its proof, preserves the original intent Prepared and changes only this transfer's disarm state");
            Active(id,false);
            ReadOnly(delegate { service.DisarmDelegatedSupplyTransfer(cause,id); service.ResumeDelegatedSupplyTransferProof(id);
                var status=service.InspectDelegatedSupplyTransfer(id); Assert(!status.CanDisarm && !status.CanResumeProof,"Completed disarm needs no additional proof recovery");
            },"Disarm replay, inspect and resume allocate +0 and do not touch original food/Pawn/cache/clock");
            RefuseExact(delegate { service.DisarmDelegatedSupplyTransfer(cause+"-conflict",id); },"A different disarm cause cannot rewrite the completed certificate",false);
        }
        private void FindRouteCandidate()
        {
            foreach (var source in service.Bases.Where(b => b.Mode == BaseMode.Direct))
            {
                var map=Find.Maps.SingleOrDefault(m => m.uniqueID == source.MapId);
                if (map == null || map.Tile.tileId == Intent.Tile) continue;
                var pawn=service.AvailableTransportPawns(source.Id).FirstOrDefault(); var cargo=service.AvailableTransportCargo(source.Id).FirstOrDefault(c => c.Available > 0);
                if (pawn == null || cargo == null) continue;
                var party=new[] {pawn}; var selections=new[] {new CargoSelection(cargo.Thing,1)};
                try { PhysicalCaravanAdapter.ValidateDeparture(core,map,party,selections); }
                catch (InvalidOperationException) { continue; }
                routeSource=source.Id; routeParty=party; routeCargo=selections; break;
            }
            if (routeSource == null) Log.Message("[INH-SUPPLY-TRANSFER] EVIDENCE LIMIT: no existing valid direct-map departure candidate; source-caravan Pawn is not a valid map departure, so no transport-guard pass is claimed.");
        }
        private void Blocks()
        {
            var intent=Intent;
            bool pendingArm=GenCommandLine.CommandLineArgPassed("inh-supply-transfer-checkpoint") &&
                service.DelegatedSupplyTransfers.Any(t => t.IntentId == intentId && t.Stage == RationSupplyTransferStage.Armed &&
                    (t.ArmProofStage == OperationStage.Prepared || t.ArmProofStage == OperationStage.NeedsReconciliation));
            if (pendingArm) ReadOnly(delegate { Assert(core.Ledger.HasPendingReference(intent.CityId),
                "The original Prepared arm proof itself also holds the ordinary city precondition lock"); },"The pending arm city precondition is observed without repair");
            RefuseExact(delegate { core.AssertNoActiveSupplyTransfer(nativeId:intent.SourceNativeId); },"The original source native has its exact preflight responsibility lock",true);
            RefuseExact(delegate { core.AssertNoActiveSupplyTransfer(foodId:intent.FoodNativeId); },"The original food has its exact preflight responsibility lock",true);
            RefuseExact(delegate { service.CancelDelegatedSupplyIntent("supply-transfer-probe-block-colony-cancel",intentId); },"Colony cannot cancel the original intent while Armed",true);
            RefuseExact(delegate { core.SupplyIntents.Cancel(ColonyService.Actor,"supply-transfer-probe-block-core-cancel",intentId); },"Core cannot cancel the original intent while Armed",true);
            RefuseExact(delegate { core.Rations.Prepare(ColonyService.Actor,"supply-transfer-probe-block-ration",intent.EntityId,intent.CityId,Find.TickManager.TicksGame/60000); },
                "Core refuses a new current-day ration debit before allocation while Armed",true);
            RefuseExact(delegate { core.PawnCustody.PrepareCaravanRelease(ColonyService.Actor,"supply-transfer-probe-block-core-return",intent.EntityId,intent.CityId,caravan,intent.Tile); },
                "Core refuses original recipient return to its saved caravan before allocation while Armed",true);
            RefuseExact(delegate { service.ReleaseDelegatedPawn("supply-transfer-probe-block-colony-return",intent.CityId,intent.EntityId,caravan); },
                "Colony refuses ordinary original return while Armed",!pendingArm);
            if (routeSource != null) RefuseExact(delegate { service.ReservePhysicalTransport("supply-transfer-probe-block-route",routeSource,intent.CityId,routeParty,routeCargo); },
                "Colony rejects an otherwise real map departure to the Armed city before reservation/registration",true);
        }
        private static Dictionary<string,string> ReadEvidence(string path)
        { return File.ReadAllLines(path).ToDictionary(line => line.Substring(0,line.IndexOf('=')),line => Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(line.IndexOf('=')+1))),StringComparer.Ordinal); }
        private static Dictionary<string,string> HeaderFields(string value)
        { return Unpack(value).ToDictionary(entry => entry.Substring(0,entry.IndexOf('=')),entry => entry.Substring(entry.IndexOf('=')+1),StringComparer.Ordinal); }
        private static string[] CanonicalList(string value) { return Unpack(value.Substring(value.IndexOf(':')+1)); }
        private void CheckSourceEvidence(string name)
        {
            var before=Capture(); string raw=TransferRaw(); var evidence=ReadEvidence(Path.Combine(GenFilePaths.SaveDataFolderPath,name+"-supply-intent-evidence.txt"));
            intentId=evidence["intent"]; var oldCore=Unpack(evidence["core"]); var currentCore=Unpack((string)Call("DurableCore",forensic));
            Assert(oldCore.Length == 6 && currentCore.Length == 6 && oldCore.Skip(1).SequenceEqual(currentCore.Skip(1)),"Core8 input retains every complete old ledger/custody/ration/intent durable record under Core9");
            var oldHeader=HeaderFields(oldCore[0]); var header=HeaderFields(currentCore[0]);
            Assert(oldHeader.Count == header.Count && oldHeader.Keys.All(header.ContainsKey) &&
                oldHeader.Where(p => p.Key != "schemaVersion" && p.Key != "producerVersion" && p.Key != "migrations" && p.Key != "moduleStamps").All(p => header[p.Key] == p.Value),
                "Only the formal schema/producer/migration header fields may differ from the old input; scope, allocator and all other metadata are exact");
            if (oldHeader["schemaVersion"] == "System.Int32:8")
            {
                string expectedStamps=oldHeader["moduleStamps"].Replace("string:16:inheritance.core57:Inheritance.Core.ModuleStamp.schemaVersion=System.Int32:8",
                    "string:16:inheritance.core57:Inheritance.Core.ModuleStamp.schemaVersion=System.Int32:9");
                Assert(expectedStamps != oldHeader["moduleStamps"] && header["moduleStamps"] == expectedStamps,
                    "Only the existing Core module stamp advances from 8 to 9; all other saved module stamps remain exact");
                Assert(header["schemaVersion"] == "System.Int32:9" && CanonicalList(header["migrations"]).SequenceEqual(CanonicalList(oldHeader["migrations"]).Concat(new[] {"string:"+Pack(new[] {"8->9:armed-supply-transfers"})})),
                    "The input records exactly the declared Core8-to-Core9 migration, without another hidden rewrite");
            }
            else Assert(oldHeader["schemaVersion"] == header["schemaVersion"] && oldHeader["migrations"] == header["migrations"] && oldHeader["moduleStamps"] == header["moduleStamps"],"Already-current input has no additional migration");
            Log.Message("[INH-SUPPLY-TRANSFER] INPUT HEADER oldProducer="+oldHeader["producerVersion"]+" currentProducer="+header["producerVersion"]+" actualSchema="+core.Header.SchemaVersion);
            Assert(evidence["scope"] == core.Header.SaveScopeId && evidence["colony"] == RawColony() && evidence["native"] == NativePersistent() &&
                evidence["clock"] == Clock() && evidence["fixed"] == FixedIntent(),"The actual prepared input matches its original food/Pawn/owner/Colony/clock and fixed intention evidence");
            Call("CheckSavedBiologicalAges",forensic,evidence["ages"]);
            Assert(service.DelegatedSupplyTransfers.Length == 0,"The old prepared input contains no transfer proof or journal");
            NoChange(before,raw,"Reading original evidence and migration differences does not normalize or alter the live state");
        }
        private string BaselineDurableCore()
        { return Pack(new[] {(string)Call("DurableCore",forensic),DurableExcept(core.SupplyTransfers,"owner","recordCount","invalidXml"),SaveHeader.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}); }
        private bool SavedCoreMatches(string saved)
        {
            string current=BaselineDurableCore();
            if (saved == current) return true;
            // Explicit test-only 15->16 producer transition: compare, never rewrite evidence or product state.
            if (!GenCommandLine.CommandLineArgPassed("inh-supply-transfer-producer16")) return false;
            var old=Unpack(saved); var next=Unpack(current);
            if (old.Length != 3 || next.Length != 3 || old[2] != "9" || next[2] != "9" || !old.Skip(1).SequenceEqual(next.Skip(1))) return false;
            var oldCore=Unpack(old[0]); var newCore=Unpack(next[0]);
            if (oldCore.Length != 6 || newCore.Length != 6 || !oldCore.Skip(1).SequenceEqual(newCore.Skip(1))) return false;
            var before=HeaderFields(oldCore[0]); var after=HeaderFields(newCore[0]);
            bool exact=before.Count == after.Count && before.Keys.All(after.ContainsKey) &&
                before["producerVersion"] == "string:6:0.4.15" && after["producerVersion"] == "string:6:0.4.16" &&
                before.Where(p => p.Key != "producerVersion").All(p => after[p.Key] == p.Value);
            if (exact) Log.Message("[INH-SUPPLY-TRANSFER] PASS Explicit 0.4.15 to 0.4.16 producer metadata transition; all other complete headers, records, tables and migration history are exact");
            return exact;
        }
        private static string EvidencePath(string name) { return Path.Combine(GenFilePaths.SaveDataFolderPath,name+"-supply-transfer-evidence.txt"); }
        private Dictionary<string,string> Evidence(string transferId)
        { return new Dictionary<string,string> {{"scope",core.Header.SaveScopeId},{"intent",intentId},{"transfer",transferId},{"core",BaselineDurableCore()},{"colony",RawColony()},
            {"native",NativePersistent()},{"ages",(string)Call("BiologicalAges",forensic)},{"clock",Clock()},{"fixed",FixedIntent()}}; }
        private void Save(string name,string transferId)
        {
            var before=Capture(); string transfer=TransferRaw(); var data=Evidence(transferId);
            File.WriteAllLines(EvidencePath(name),data.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+Convert.ToBase64String(Encoding.UTF8.GetBytes(p.Value))).ToArray());
            NoChange(before,transfer,"Stable external transfer evidence includes actual schema and new journal without runtime hashes or normalization");
            GameDataSaveLoader.SaveGame(name); done=true; Log.Message("[INH-SUPPLY-TRANSFER] Saved "+name); Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private string Restore(string name)
        {
            var before=Capture(); string raw=TransferRaw(); var data=ReadEvidence(EvidencePath(name)); intentId=data["intent"]; string id=data["transfer"];
            Assert(data["scope"] == core.Header.SaveScopeId && SavedCoreMatches(data["core"]) && data["colony"] == RawColony() && data["native"] == NativePersistent() &&
                data["clock"] == Clock() && data["fixed"] == FixedIntent(),"Separate process reload retains exact transfer/intent proofs, old metadata, all original food/Pawn/owners, Colony and clock");
            Call("CheckSavedBiologicalAges",forensic,data["ages"]);
            var ledger=Field(core.Ledger,"state");
            Assert((int)Field(ledger,"entityCount") == 0 && (int)Field(ledger,"operationCount") == 0 && (int)Field(ledger,"reservationCount") == 0 &&
                (int)Field(core.SupplyIntents,"recordCount") == service.DelegatedSupplyIntents.Length && (int)Field(core.SupplyTransfers,"recordCount") == TransferRows().Count,
                "Normal Core recovery leaves copied ledger counters at zero while validated intent/transfer counts remain complete, without probe normalization");
            NoChange(before,raw,"External durable transfer reload comparisons are read-only"); MatchManifest(Transfer(id)); return id;
        }
        private void Animal()
        {
            string previous=Restore(DisarmedSave); Active(previous,false);
            // Original human Prepared still overlaps the source; explicitly cancel it.
            Call("Cancel",forensic,intentId,"supply-transfer-probe-cancel-human-intent");
            string animal=(string)Call("Prepare",forensic,1,"supply-transfer-probe-animal-intent",false); intentId=animal;
            Active(null,false); string transfer=Arm("supply-transfer-probe-animal-arm"); Blocks(); Disarm(transfer,"supply-transfer-probe-animal-disarm");
            Assert(Intent.EntityId == residents[1].EntityId,"The positive animal path uses the same original Core-held animal and existing caravan food"); Finish();
        }
        private void Finish() { done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                Find.TickManager.Pause(); service=ColonyMod.Current;
                if (core.IsReady && service != null && pausedFrames++ < 2) return;
                Assert(Modes.Count(Has) == 1 && core.IsReady && service != null,"One transfer stage runs against the validated actual Core/Colony");
                Bind(); service.SaveSnapshot(core); // Exactly once, before the measured baseline.
                if (Has("animal")) { Animal(); return; }
                if (Has("stale-armed") || Has("stale-armed-load"))
                {
                    string active=Restore(Has("stale-armed") ? ArmedSave : "INH_SupplyTransferArmedStale"); Active(active,true);
                    if (Has("stale-armed"))
                    {
                        var intent=Intent; var source=caravan.PawnsListForReading.Single(p => p.GetUniqueLoadID() == intent.SourceNativeId);
                        var food=source.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == intent.FoodNativeId);
                        Assert(FoodUtility.WillEat(source,food,null,true,false),"The Armed original food remains normally edible by its real source owner");
                        var before=Capture(); string raw=TransferRaw(),fixedIntent=FixedIntent(); int oldCount=food.stackCount;
                        float nutrition=food.Ingested(source,0.1f);
                        Assert(food.Destroyed || food.stackCount < oldCount,"Ordinary Ingested changes the original Armed whole food without any replacement or clock fixture");
                        Call("Unchanged",forensic,before,"Normal eating changes no Core/Colony/intent journal or clock while Armed",false);
                        Assert(raw == TransferRaw() && fixedIntent == FixedIntent(),"Normal eating preserves the complete transfer journal and historical manifest");
                        Log.Message("[INH-SUPPLY-TRANSFER] NATIVE CONSUMPTION food="+intent.FoodNativeId+" before="+oldCount+" after="+food.stackCount+" destroyed="+food.Destroyed+" nutrition="+nutrition);
                    }
                    ReadOnly(delegate { var status=service.InspectDelegatedSupplyTransfer(active);
                        Assert(!status.IsCurrent && status.CanDisarm && (status.Problems.Contains("SourceChanged") || status.Problems.Contains("SourceUnavailable")),
                            "Stale Armed originals keep their responsibility and can be explicitly disarmed");
                        Assert(service.BeginDelegatedSupplyTransfer(Transfer(active).Cause,intentId) == active,"Historical Armed Begin replay keeps its original manifest even after ordinary source consumption");
                    },"Stale Armed inspect and replay preserve exact raw state and allocate +0");
                    if (Has("stale-armed")) { Save("INH_SupplyTransferArmedStale",active); return; }
                    Disarm(active,"supply-transfer-probe-stale-disarm"); Finish(); return;
                }
                if (Has("arm") || Has("stale"))
                {
                    CheckSourceEvidence(Has("stale") ? StaleInput : PreparedSave); Active(null,false);
                    if (Has("stale"))
                    {
                        ReadOnly(delegate { var status=service.InspectDelegatedSupplyIntent(intentId); Assert(!status.IsCurrent && Intent.Stage == RationSupplyIntentStage.Prepared &&
                            (status.Problems.Contains("SourceChanged") || status.Problems.Contains("SourceUnavailable")),"The real input is a stale yet uncancelled original whole-stack intention"); },"Stale original intent inspect is raw-state read-only");
                        RefuseExact(delegate { service.BeginDelegatedSupplyTransfer("supply-transfer-probe-stale-arm",intentId); },"Stale whole-stack intent cannot be Armed",false); Finish(); return;
                    }
                    FindRouteCandidate(); string id=Arm(ArmCause); Blocks(); Save(ArmedSave,id); return;
                }
                string restored=Restore(Has("load") ? ArmedSave : DisarmedSave);
                if (Has("load")) { Active(restored,true); FindRouteCandidate(); Blocks(); Disarm(restored,DisarmCause); Save(DisarmedSave,restored); return; }
                Active(restored,false);
                ReadOnly(delegate { var transfer=Transfer(restored); Assert(transfer.Stage == RationSupplyTransferStage.Cancelled && transfer.ArmProofStage == OperationStage.Committed && transfer.DisarmProofStage == OperationStage.Committed,
                    "Final separate reload retains both committed metadata proofs and no active Armed responsibility");
                    Assert(service.BeginDelegatedSupplyTransfer(transfer.Cause,intentId) == restored,"Historical Begin after disarm returns its existing certificate");
                    service.DisarmDelegatedSupplyTransfer(transfer.DisarmCause,restored); service.ResumeDelegatedSupplyTransferProof(restored);
                },"Final disarmed Begin/Disarm/Resume replay allocates +0 and preserves every raw state and original"); Finish();
            }
            catch (Exception error) { done=true; Log.Error("[INH-SUPPLY-TRANSFER] FAIL "+error); Application.Quit(1); }
        }
    }
}
