using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    // Staged only. No native creation, splitting, transfer, plan control or clock fixture.
    // Prepare input: INH_MaintenanceResumed; city-lock input: INH_DelegatedUiCityLock.
    // Load/stale input: INH_SupplyIntentPrepared; final loads use their named saves.
    public sealed class SupplyIntentProbe : GameComponent
    {
        private static readonly string[] Modes={"prepare","load","cancelled-load","stale","stale-load","city-lock","stale-pending","stale-pending-load"};
        private const string PreparedSave="INH_SupplyIntentPrepared",CancelledSave="INH_SupplyIntentCancelled",StaleSave="INH_SupplyIntentStale";
        private const string StalePendingSave="INH_SupplyIntentStalePending";
        private const string PendingCause="supply-intent-probe-pending",CancelCause="supply-intent-probe-cancel-pending";
        private static readonly BindingFlags PrivateStatic=BindingFlags.NonPublic|BindingFlags.Static;
        private static readonly BindingFlags PrivateInstance=BindingFlags.NonPublic|BindingFlags.Instance;
        private static readonly BindingFlags AllInstance=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        private bool done;
        private int pausedSetupFrames;
        private SupplyPreviewProbe forensic;
        private CoreWorldComponent core;
        private ColonyService service;
        private string[] ids;
        private Caravan caravan;
        private PawnCustodyResidentSnapshot[] residents;
        private Pawn[] originals;

        public SupplyIntentProbe(Game game) { }
        private static bool Has(string mode) { return GenCommandLine.CommandLineArgPassed("inh-supply-intent-"+mode); }
        private static bool Selected { get { return Modes.Any(Has); } }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-SUPPLY-INTENT] PASS "+message); }
        private static void Refuse(Action action,string message)
        { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,message); }
        private static string Number(float value) { return value.ToString("R",CultureInfo.InvariantCulture); }
        private static string Pack(IEnumerable<string> values)
        { return string.Concat(values.Select(s => s == null ? "-1:" : s.Length.ToString(CultureInfo.InvariantCulture)+":"+s).ToArray()); }
        private static object PreviewCall(string name,object target,params object[] args)
        {
            var method=typeof(SupplyPreviewProbe).GetMethods(target == null ? PrivateStatic : PrivateInstance)
                .Single(m => m.Name == name && m.GetParameters().Length == args.Length);
            try { return method.Invoke(target,args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        private static object Field(object target,string name) { return PreviewCall("Field",null,target,name); }
        private static string Canonical(object value) { return (string)PreviewCall("Canonical",null,value); }
        private string Native() { return (string)PreviewCall("NativeStamp",forensic,core); }
        private string RawColony() { return (string)PreviewCall("RawColony",null,service); }
        private static string Clock() { return (string)PreviewCall("Clock",null); }
        private static IEnumerable<FieldInfo> Fields(Type type)
        {
            for (var current=type;current != null && current != typeof(object);current=current.BaseType)
                foreach (var field in current.GetFields(AllInstance|BindingFlags.DeclaredOnly).OrderBy(f => f.Name,StringComparer.Ordinal)) yield return field;
        }
        private static string Except(object target,params string[] excluded)
        { return Pack(Fields(target.GetType()).Where(f => !excluded.Contains(f.Name)).Select(f => f.Name+"="+Canonical(f.GetValue(target)))); }
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance=new ReferenceComparer();
            public new bool Equals(object left,object right) { return ReferenceEquals(left,right); }
            // Hash is used only to walk the object graph, never emitted as evidence.
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
        private string DurableOwner(ThingOwner owner)
        {
            if (owner == null) return null;
            if (ReferenceEquals(owner,core.GetDirectlyHeldThings())) return "CorePawnCustody";
            if (ReferenceEquals(owner,core.Rations.GetDirectlyHeldThings())) return "CoreRationWitnesses";
            var pawn=originals.SingleOrDefault(p => p.inventory != null && ReferenceEquals(owner,p.inventory.innerContainer));
            if (pawn != null) return "PawnInventory:"+pawn.GetUniqueLoadID();
            var caravanOwner=Find.WorldObjects.Caravans.SingleOrDefault(c => ReferenceEquals(owner,c.GetDirectlyHeldThings()));
            if (caravanOwner != null) return "Caravan:"+caravanOwner.GetUniqueLoadID();
            var mapOwner=Find.Maps.SingleOrDefault(m => ReferenceEquals(owner,m.spawnedThings));
            if (mapOwner != null) return "Map:"+mapOwner.uniqueID.ToString(CultureInfo.InvariantCulture);
            throw new InvalidOperationException("Durable evidence encountered an unclassified native owner; do not substitute a runtime identity");
        }
        private string DurableCanonical(object value)
        { return DurableCanonical(value,new Dictionary<object,string>(ReferenceComparer.Instance),"root"); }
        private string DurableCanonical(object value,Dictionary<object,string> visited,string path)
        {
            if (value == null) return "null";
            var type=value.GetType();
            if (value is string) return "string:"+Pack(new[] {(string)value});
            if (value is float) return "Single:"+((float)value).ToString("R",CultureInfo.InvariantCulture);
            if (value is double) return "Double:"+((double)value).ToString("R",CultureInfo.InvariantCulture);
            if (type.IsPrimitive || type.IsEnum || value is decimal) return type.FullName+":"+Convert.ToString(value,CultureInfo.InvariantCulture);
            var thing=value as Thing;
            if (thing != null) return Pack(new[] {"native:"+thing.GetUniqueLoadID(),Item(thing),DurableOwner(thing.holdingOwner),
                thing.Map == null ? null : thing.Map.uniqueID.ToString(CultureInfo.InvariantCulture),thing.Position.ToString(),thing.Rotation.ToString(),thing.EverSeenByPlayer.ToString()});
            var def=value as Def; if (def != null) return type.FullName+":"+def.defName;
            var owner=value as ThingOwner;
            if (owner != null) return Pack(new[] {DurableOwner(owner),Pack(owner.Cast<Thing>().Select(t => DurableCanonical(t,visited,path+"/native:"+t.GetUniqueLoadID())))});
            string earlier;
            if (!type.IsValueType)
            {
                if (visited.TryGetValue(value,out earlier)) return "reference-path:"+earlier;
                visited.Add(value,path);
            }
            var callback=value as Delegate;
            if (callback != null) return Pack(callback.GetInvocationList().Select((d,i) => Pack(new[] {
                d.Method.DeclaringType.FullName,d.Method.ToString(),DurableCanonical(d.Target,visited,path+"/callback:"+i)})));
            var sequence=value as IEnumerable;
            if (sequence != null) return type.FullName+":"+Pack(sequence.Cast<object>().Select((v,i) => DurableCanonical(v,visited,path+"/index:"+i)));
            return type.FullName+":"+Pack(Fields(type).Select(f => f.DeclaringType.FullName+"."+f.Name+"="+
                DurableCanonical(f.GetValue(value),visited,path+"/field:"+f.DeclaringType.FullName+"."+f.Name)));
        }
        private string DurableExcept(object target,params string[] excluded)
        { return Pack(Fields(target.GetType()).Where(f => !excluded.Contains(f.Name)).Select(f => f.Name+"="+DurableCanonical(f.GetValue(target)))); }
        private static Dictionary<string,string> Records(object target,string name)
        { return ((IEnumerable)Field(target,name)).Cast<object>().ToDictionary(r => (string)Field(r,"id"),Canonical,StringComparer.Ordinal); }
        private Dictionary<string,string> SupplyRecords() { return Records(core.SupplyIntents,"records"); }
        private string SupplyRaw() { return Except(core.SupplyIntents,"owner"); }
        private string OldJournals()
        {
            // Complete historical metadata fields; no serializer/count synchronization.
            return Pack(new[] {Except(core.Header,"nextSequence"),
                Canonical(Field(core.PawnCustody,"residents")),Canonical(Field(core.PawnCustody,"transfers")),
                Canonical(Field(core.PawnCustody,"residentCount")),Canonical(Field(core.PawnCustody,"transferCount")),
                Canonical(Field(core.Rations,"debits")),Canonical(Field(core.Rations,"debitCount")),Canonical(Field(core.Rations,"spentCount"))});
        }
        private sealed class Boundary
        {
            internal string Colony,Native,Clock,Old,Supply,Header,Ledger,Reservations,LedgerOther,CityOther;
            internal long CityRevision;
            internal long Sequence;
            internal int Entities,Operations;
            internal Dictionary<string,string> EntityRows,OperationRows,SupplyRows;
        }
        private Boundary Capture()
        {
            var ledger=Field(core.Ledger,"state");
            var city=((IEnumerable)Field(ledger,"entities")).Cast<object>().Single(e => (string)Field(e,"id") == ids[1]);
            return new Boundary {Colony=RawColony(),Native=Native(),Clock=Clock(),Old=OldJournals(),Supply=SupplyRaw(),
                CityOther=Except(city,"revision"),CityRevision=(long)Field(city,"revision"),
                Header=Canonical(core.Header),Ledger=Canonical(ledger),Reservations=Canonical(Field(ledger,"reservations")),
                LedgerOther=Except(ledger,"entities","operations","entityCount","operationCount","reservationCount"),
                Sequence=core.Header.NextSequence,Entities=core.Ledger.EntityCount,Operations=core.Ledger.OperationCount,
                EntityRows=Records(ledger,"entities"),OperationRows=Records(ledger,"operations"),SupplyRows=SupplyRecords()};
        }
        private void Unchanged(Boundary before,string message,bool native=true)
        {
            var after=Capture();
            Assert(after.Colony == before.Colony && (!native || after.Native == before.Native) && after.Clock == before.Clock && after.Old == before.Old &&
                after.Supply == before.Supply && after.Header == before.Header && after.Ledger == before.Ledger,message);
        }
        private void ReadOnly(Action action,string message)
        { var before=Capture(); action(); Unchanged(before,message); }
        private void ProofDelta(Boundary before,string message)
        {
            var after=Capture(); var ledger=Field(core.Ledger,"state");
            Assert(after.Colony == before.Colony && after.Native == before.Native && after.Clock == before.Clock && after.Old == before.Old &&
                after.Reservations == before.Reservations && after.LedgerOther == before.LedgerOther,
                message+": complete Colony, old custody/ration journals, cache/biology/food/owners, reservations and clock remain unchanged");
            var newEntities=core.Ledger.Entities.Where(e => !before.EntityRows.ContainsKey(e.Id)).ToArray();
            var newOperations=core.Ledger.Operations.Where(o => !before.OperationRows.ContainsKey(o.Id)).ToArray();
            bool prepareProof=newOperations.Length == 1 && newOperations[0].CauseId.StartsWith("supply-intent/",StringComparison.Ordinal);
            Assert(before.EntityRows.All(row => after.EntityRows.ContainsKey(row.Key) &&
                    (prepareProof && row.Key == ids[1] ? after.CityOther == before.CityOther && after.CityRevision == before.CityRevision+1 : after.EntityRows[row.Key] == row.Value)) &&
                before.OperationRows.All(row => after.OperationRows.ContainsKey(row.Key) && after.OperationRows[row.Key] == row.Value),
                message+": every previous Core field is exact except the prepare proof's city revision increment of exactly one; cancel changes no existing entity");
            Assert(after.Sequence == before.Sequence+2 && after.Entities == before.Entities+2 && after.Operations == before.Operations+1 &&
                newEntities.Length == 2 && newEntities.All(e => e.Provider == ColonyService.Actor) &&
                newEntities.Count(e => e.Kind == "operation") == 1 && newEntities.Count(e => e.Kind == "lease") == 1 &&
                newOperations.Length == 1 && newOperations[0].Actor == ColonyService.Actor && newOperations[0].Kind == "create" && newOperations[0].Stage == OperationStage.Committed &&
                newEntities.Any(e => e.Id == newOperations[0].Id && e.Kind == "operation") &&
                newEntities.Any(e => e.Id == newOperations[0].ResultId && e.Kind == "lease" && e.Status == EntityStatus.Active && e.Quantity == 0 && e.BoundThingLoadId == null),
                message+": the only allocation is the actor's one committed operation and one metadata lease (+2 IDs, +1 proof)");
            // CoreLedger.Copy resets its private save counters on each ordinary write.
            // Observe that raw behavior explicitly; never call SaveSnapshot to conceal it.
            Assert((int)Field(ledger,"entityCount") == 0 && (int)Field(ledger,"operationCount") == 0 && (int)Field(ledger,"reservationCount") == 0,
                message+": raw Core save counters have the ordinary copy state, observed before serialization");
        }
        private void Bind()
        {
            forensic=new SupplyPreviewProbe(Current.Game);
            try { PreviewCall("Bind",forensic,core,service); }
            catch (InvalidOperationException error)
            {
                // Preview's own lock flag is different. Only its final positive-fixture
                // assertion may be expected here; all preceding identity checks still run.
                bool transferCheckpoint=(GenCommandLine.CommandLineArgPassed("inh-supply-transfer-checkpoint") || GenCommandLine.CommandLineArgPassed("inh-supply-transfer-checkpoint-reload")) &&
                    core.ReadRationSupplyTransfers(ColonyService.Actor).Any(t => t.Stage == RationSupplyTransferStage.Armed &&
                        (t.ArmProofStage == OperationStage.Prepared || t.ArmProofStage == OperationStage.NeedsReconciliation) && core.Ledger.HasPendingReference(t.CityId));
                if ((!Has("city-lock") && !transferCheckpoint) || error.Message != "The positive preview fixture has available human and animal residents without a pending food or city operation") throw;
                if (transferCheckpoint) Assert(core.ReadPawnCustodyResidents(ColonyService.Actor).All(r => !core.Rations.HasPending(r.EntityId)),
                    "Only the validated transfer arm checkpoint accounts for this fixture's pending city; no ration checkpoint is hidden");
            }
            ids=(string[])Field(forensic,"ids"); caravan=(Caravan)Field(forensic,"caravan");
            residents=(PawnCustodyResidentSnapshot[])Field(forensic,"residents"); originals=(Pawn[])Field(forensic,"originals");
            Assert(ids[0] == core.Header.SaveScopeId && residents.Length == 2 && originals.Length >= 3,"Read-only binding uses the original human, animal and stopped source caravan");
            if (Has("city-lock"))
                Assert(core.Ledger.HasPendingReference(ids[1]) && core.Rations.HasPending(residents[0].EntityId) && !core.Rations.HasPending(residents[1].EntityId),
                    "The existing human ration checkpoint locks the city for the independent animal");
        }
        private DelegatedSupplyFoodSnapshot Candidate(int recipient,bool requireSourceCanEat)
        {
            var preview=service.PreviewDelegatedFoodSupply(ids[1],residents[recipient].EntityId,caravan);
            var row=preview.Foods.FirstOrDefault(r => r.CanPreview && (!requireSourceCanEat || FoodUtility.WillEat(r.SourcePawn,r.Food,null,true,false)));
            Assert(preview.CanPreview && row != null,"An existing unreserved whole native stack is an eligible candidate for the original "+(recipient == 0 ? "human" : "animal"));
            Assert(row.Count == row.Food.stackCount && row.Food.holdingOwner == row.SourcePawn.inventory.innerContainer && !row.Food.Destroyed && row.Count > 0,
                "The selected whole stack remains solely in its original caravan member's inventory");
            return row;
        }
        private RationSupplyIntentSnapshot Intent(string id) { return service.DelegatedSupplyIntents.Single(r => r.Id == id); }
        private object RawIntent(string id)
        { return ((IEnumerable)Field(core.SupplyIntents,"records")).Cast<object>().Single(r => (string)Field(r,"id") == id); }
        private string FixedIntent(string id)
        { return Except(RawIntent(id),"stage","cancelCause","cancelOp","cancelReceipt"); }
        private void CheckIntent(string id,int recipient,DelegatedSupplyFoodSnapshot row)
        {
            var intent=Intent(id); var resident=residents[recipient];
            Assert(intent.Actor == ColonyService.Actor && intent.ScopeId == core.Header.SaveScopeId && intent.EntityId == resident.EntityId && intent.NativeId == resident.NativeId &&
                intent.CityId == resident.CityId && intent.StoreTransferId == resident.StoreTransferId && intent.CaravanId == caravan.GetUniqueLoadID() && intent.Tile == resident.Tile &&
                intent.SourceNativeId == row.SourceNativeId && intent.SourceEntityId == row.SourceEntityId && intent.SourceKind == row.SourceKind && intent.FoodNativeId == row.NativeId &&
                intent.DefName == row.DefName && intent.StuffDef == row.StuffDef && intent.WholeCount == row.Count && intent.HitPoints == row.HitPoints &&
                intent.PreparedTick == Find.TickManager.TicksGame && intent.UnitNutrition == row.UnitNutrition && intent.DailyDemand == row.DailyDemand &&
                intent.Stage == RationSupplyIntentStage.Prepared && intent.PrepareProofStage == OperationStage.Committed && intent.CancelCause == null,
                "The committed intention fixes the exact original recipient, anchor, source, whole stack and read-only nutrition observation");
            ReadOnly(delegate { var status=service.InspectDelegatedSupplyIntent(id); Assert(status.IsCurrent && status.Problems.Count == 0 && status.CoreStatus.CanCancel && !status.CoreStatus.CanResumeProof,
                "Current means matching metadata only, with no reservation, food movement or delivery"); },"Inspect is a complete raw-state no-op");
        }
        private string Prepare(int recipient,string cause,bool sourceCanEat)
        {
            var row=Candidate(recipient,sourceCanEat); var before=Capture();
            string id=service.PrepareDelegatedSupplyIntent(cause,ids[1],residents[recipient].EntityId,caravan,row.SourcePawn,row.Food,row.Count);
            ProofDelta(before,"Prepare original "+(recipient == 0 ? "human" : "animal"));
            Assert(SupplyRecords().Count == before.SupplyRows.Count+1 && before.SupplyRows.All(r => SupplyRecords()[r.Key] == r.Value),"Preparing adds only its one intent row; earlier supply history is exact");
            CheckIntent(id,recipient,row);
            ReadOnly(delegate { Assert(service.PrepareDelegatedSupplyIntent(cause,ids[1],residents[recipient].EntityId,caravan,row.SourcePawn,row.Food,row.Count) == id,
                "Same-cause whole-stack replay returns the same intent"); service.ResumeDelegatedSupplyIntentProof(id); },"Committed prepare replay and proof resume allocate +0 and mutate no raw state");
            ReadOnly(delegate { Refuse(delegate { service.PrepareDelegatedSupplyIntent(cause,ids[1],residents[recipient].EntityId,caravan,row.SourcePawn,row.Food,row.Count+1); },"A same-cause contradictory whole count is refused"); },"Contradictory count refusal allocates +0 and changes nothing");
            ReadOnly(delegate { Refuse(delegate { service.PrepareDelegatedSupplyIntent(cause,ids[1],residents[1-recipient].EntityId,caravan,row.SourcePawn,row.Food,row.Count); },"A same-cause contradictory recipient is refused"); },"Contradictory recipient refusal allocates +0 and changes nothing");
            ReadOnly(delegate { Refuse(delegate { service.PrepareDelegatedSupplyIntent(cause+"-overlap",ids[1],residents[1-recipient].EntityId,caravan,row.SourcePawn,row.Food,row.Count); },"A different cause cannot overlap the prepared original source or stack"); },"Overlapping-source refusal allocates +0 and changes nothing");
            return id;
        }
        private void Cancel(string id,string cause)
        {
            var before=Capture(); string fixedMetadata=FixedIntent(id);
            service.CancelDelegatedSupplyIntent(cause,id); ProofDelta(before,"Cancel original intention");
            var intent=Intent(id);
            Assert(intent.Stage == RationSupplyIntentStage.Cancelled && intent.CancelCause == cause && intent.CancelProofStage == OperationStage.Committed &&
                intent.CancelReceiptId != null && FixedIntent(id) == fixedMetadata && SupplyRecords().Count == before.SupplyRows.Count &&
                before.SupplyRows.Where(r => r.Key != id).All(r => SupplyRecords()[r.Key] == r.Value),"Cancel appends only its metadata proof, preserves the prepare digest and changes only that intent's cancel fields/stage");
            ReadOnly(delegate { service.CancelDelegatedSupplyIntent(cause,id); service.ResumeDelegatedSupplyIntentProof(id);
                var status=service.InspectDelegatedSupplyIntent(id); Assert(!status.IsCurrent && status.Problems.Contains("Cancelled") && !status.CoreStatus.CanCancel && !status.CoreStatus.CanResumeProof,
                    "Cancelled history remains a historical intention rather than delivered food"); },"Same-cause cancelled replay, resume and inspect allocate +0 and do not recreate or move any original");
            ReadOnly(delegate { Refuse(delegate { service.CancelDelegatedSupplyIntent(cause+"-contradiction",id); },"A different cancel cause is refused"); },"Contradictory cancellation changes no raw state");
        }
        private static string Item(Thing item)
        { return Pack(new[] {item.GetUniqueLoadID(),item.def.defName,item.Stuff == null ? null : item.Stuff.defName,item.stackCount.ToString(CultureInfo.InvariantCulture),item.HitPoints.ToString(CultureInfo.InvariantCulture),item.Destroyed.ToString(),item.Spawned.ToString()}); }
        private static string DurableBiology(Pawn pawn,MethodInfo biology)
        {
            var parts=((string)biology.Invoke(null,new object[] {pawn})).Split('|');
            if (parts.Length != 14) throw new InvalidOperationException("Maintenance biology evidence layout changed");
            // Only biological age has the already observed load-time +0..3 exception.
            // Save it separately so no other native field is normalized or tolerated.
            parts[5]="biological-age-recorded-separately"; return Pack(parts);
        }
        private string BiologicalAges()
        { return string.Join("\n",originals.OrderBy(p => p.GetUniqueLoadID(),StringComparer.Ordinal).Select(p => p.GetUniqueLoadID()+"|"+p.ageTracker.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture)).ToArray()); }
        private void CheckSavedBiologicalAges(string saved)
        {
            var ages=saved.Split('\n').ToDictionary(line => line.Substring(0,line.IndexOf('|')),line => long.Parse(line.Substring(line.IndexOf('|')+1),CultureInfo.InvariantCulture),StringComparer.Ordinal);
            Assert(ages.Count == originals.Length && originals.All(p => ages.ContainsKey(p.GetUniqueLoadID())),"Reload age evidence names exactly the same original individuals");
            foreach (var pawn in originals)
            {
                long delta=pawn.ageTracker.AgeBiologicalTicks-ages[pawn.GetUniqueLoadID()];
                Log.Message("[INH-SUPPLY-INTENT] RELOAD AGE native="+pawn.GetUniqueLoadID()+" biologicalTickDelta="+delta+"; allowance 0..3 is restricted to this persisted field only.");
                Assert(delta >= 0 && delta <= 3,"Only the previously observed biological-age reload delta is permitted; every in-process age boundary remains exact");
            }
        }
        private string PersistentNative()
        {
            // Runtime identities/caches are compared by NativeStamp within each process.
            // This separate save/reload stamp uses durable native identities and fields.
            var biology=typeof(DelegatedMaintenanceProbe).GetMethod("Biology",PrivateStatic);
            return Pack(new[] {Pack(originals.OrderBy(p => p.GetUniqueLoadID(),StringComparer.Ordinal).Select(p => Pack(new[] {
                DurableBiology(p,biology),p.ageTracker.AgeChronologicalTicks.ToString(CultureInfo.InvariantCulture),p.Dead.ToString(),p.Destroyed.ToString(),p.Spawned.ToString(),
                core.GetDirectlyHeldThings().Contains(p).ToString(),Find.WorldPawns.Contains(p).ToString(),
                Pack(Find.WorldObjects.Caravans.Where(c => c.PawnsListForReading.Contains(p)).Select(c => c.GetUniqueLoadID())),
                Pack(Find.Maps.Where(m => m.mapPawns.AllPawns.Contains(p)).Select(m => m.uniqueID.ToString(CultureInfo.InvariantCulture))),
                p.inventory == null ? null : Pack(p.inventory.innerContainer.OrderBy(t => t.GetUniqueLoadID(),StringComparer.Ordinal).Select(Item))}))),
                Pack(core.Rations.GetDirectlyHeldThings().OrderBy(t => t.GetUniqueLoadID(),StringComparer.Ordinal).Select(Item)),
                Pack(Find.WorldObjects.Caravans.OrderBy(c => c.GetUniqueLoadID(),StringComparer.Ordinal).Select(c => Pack(new[] {c.GetUniqueLoadID(),c.Tile.ToString(),c.pather.Moving.ToString(),Pack(c.PawnsListForReading.Select(p => p.GetUniqueLoadID()))}))) });
        }
        private string DurableCore()
        {
            // Saving legitimately synchronizes counters; loading clears validation flags.
            // Full durable records are exact. In-process boundaries above compare raw state.
            var ledger=Field(core.Ledger,"state");
            return Pack(new[] {DurableExcept(core.Header,"loaded"),DurableExcept(ledger,"entityCount","operationCount","reservationCount","uncheckedLoad"),
                DurableCanonical(Field(core.PawnCustody,"residents")),DurableCanonical(Field(core.PawnCustody,"transfers")),DurableCanonical(Field(core.Rations,"debits")),
                DurableExcept(core.SupplyIntents,"owner","recordCount","invalidXml")});
        }
        private static string EvidencePath(string name) { return Path.Combine(GenFilePaths.SaveDataFolderPath,name+"-supply-intent-evidence.txt"); }
        private Dictionary<string,string> Evidence(string id)
        { return new Dictionary<string,string>(StringComparer.Ordinal) {{"scope",ids[0]},{"intent",id},{"core",DurableCore()},{"colony",RawColony()},{"native",PersistentNative()},{"ages",BiologicalAges()},{"clock",Clock()},{"fixed",FixedIntent(id)}}; }
        private void Save(string name,string id)
        {
            var before=Capture(); var evidence=Evidence(id);
            File.WriteAllLines(EvidencePath(name),evidence.OrderBy(p => p.Key,StringComparer.Ordinal).Select(p => p.Key+"="+Convert.ToBase64String(Encoding.UTF8.GetBytes(p.Value))).ToArray());
            Unchanged(before,"Writing external evidence reads raw state without normalization or native changes");
            GameDataSaveLoader.SaveGame(name); done=true; Log.Message("[INH-SUPPLY-INTENT] Saved "+name); Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        private string Restore(string name)
        {
            var before=Capture(); var evidence=File.ReadAllLines(EvidencePath(name)).ToDictionary(line => line.Substring(0,line.IndexOf('=')),
                line => Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(line.IndexOf('=')+1))),StringComparer.Ordinal);
            string id=evidence["intent"];
            Assert(evidence["scope"] == core.Header.SaveScopeId && evidence["core"] == DurableCore() && evidence["colony"] == RawColony() &&
                evidence["native"] == PersistentNative() && evidence["clock"] == Clock() && evidence["fixed"] == FixedIntent(id),
                "A separate process restores the exact intent/proofs/digest, old journals, original food/Pawn/owners, Colony payload and clock");
            CheckSavedBiologicalAges(evidence["ages"]);
            var ledger=Field(core.Ledger,"state");
            // FinalizeInit validates serialized counts before Recover. Recover uses
            // LedgerState.Copy, whose serialization counters start at zero, even
            // when every operation was already committed. Observe that raw state.
            Assert((int)Field(ledger,"entityCount") == 0 && (int)Field(ledger,"operationCount") == 0 &&
                (int)Field(ledger,"reservationCount") == 0 &&
                (int)Field(core.SupplyIntents,"recordCount") == SupplyRecords().Count,"Normal Core recovery leaves copied ledger save counters at zero and retains the validated supply count, without probe normalization");
            Unchanged(before,"External evidence and durable reload comparisons are read-only"); return id;
        }
        private void PrepareMode()
        {
            Assert(service.DelegatedSupplyIntents.Length == 0,"The original maintenance save contains no supply-intent history");
            var initial=Capture();
            string human=Prepare(0,"supply-intent-probe-human",false); Cancel(human,"supply-intent-probe-cancel-human");
            string animal=Prepare(1,"supply-intent-probe-animal",false); Cancel(animal,"supply-intent-probe-cancel-animal");
            string pending=Prepare(0,PendingCause,true);
            Assert(core.Header.NextSequence == initial.Sequence+10 && core.Ledger.EntityCount == initial.Entities+10 && core.Ledger.OperationCount == initial.Operations+5 &&
                service.DelegatedSupplyIntents.Length == 3 && service.DelegatedSupplyIntents.Count(i => i.Stage == RationSupplyIntentStage.Cancelled) == 2,
                "Human and animal positive cycles plus the saved human prepare produce exactly five metadata proofs and three intention rows");
            Save(PreparedSave,pending);
        }
        private void StaleMode()
        {
            string id=Restore(PreparedSave); var intent=Intent(id);
            var source=caravan.PawnsListForReading.Single(p => p.GetUniqueLoadID() == intent.SourceNativeId);
            var food=source.inventory.innerContainer.Single(t => t.GetUniqueLoadID() == intent.FoodNativeId);
            Assert(intent.Stage == RationSupplyIntentStage.Prepared && FoodUtility.WillEat(source,food,null,true,false),"The pending intention still names an existing whole stack its original owner can consume normally");
            var before=Capture(); string rawIntent=Canonical(RawIntent(id)); int count=food.stackCount;
            float eaten=food.Ingested(source,0.1f);
            Assert(food.Destroyed || food.stackCount < count,"The actual game's Ingested consumed part or all of the original selected food; no generated replacement, split fixture or clock change");
            Assert(rawIntent == Canonical(RawIntent(id)),"Normal source consumption does not rewrite the intention's digest, original count, nutrition or recipient metadata");
            Unchanged(before,"Native consumption changes no full Core/Colony/supply journal or clock",false);
            Log.Message("[INH-SUPPLY-INTENT] NATIVE CONSUMPTION source="+source.GetUniqueLoadID()+" food="+intent.FoodNativeId+" before="+count+" after="+food.stackCount+" destroyed="+food.Destroyed+" nutritionReturned="+Number(eaten)+"; only ordinary Ingested native effects are permitted.");
            ReadOnly(delegate { var status=service.InspectDelegatedSupplyIntent(id); Assert(!status.IsCurrent && (status.Problems.Contains("SourceChanged") || status.Problems.Contains("SourceUnavailable")) && status.CoreStatus.CanCancel,
                "The historical whole-stack observation becomes stale while cancellation remains available"); },"Stale inspect changes no old or new journal, native cache, food or clock");
            ReadOnly(delegate { Assert(service.PrepareDelegatedSupplyIntent(intent.Cause,intent.CityId,intent.EntityId,caravan,source,food,intent.WholeCount) == id,
                "Stale same-cause replay returns the old fixed intention without treating the changed food as a fresh candidate"); },"Historical stale prepare replay allocates +0 and changes no metadata or original");
            if (Has("stale-pending")) { Save(StalePendingSave,id); return; }
            Cancel(id,"supply-intent-probe-cancel-stale"); Save(StaleSave,id);
        }
        private void CityLockMode()
        {
            var preview=service.PreviewDelegatedFoodSupply(ids[1],residents[1].EntityId,caravan);
            var row=preview.Foods.FirstOrDefault(r => core.Rations.QuoteSupplyCandidate(ColonyService.Actor,residents[1].EntityId,ids[1],r.SourcePawn,r.Food).CanPreview);
            Assert(row != null && !preview.CanPreview && preview.Problems.Contains("CityLocked"),"The existing city lock blocks an otherwise real animal food candidate");
            ReadOnly(delegate { Refuse(delegate { service.PrepareDelegatedSupplyIntent("supply-intent-probe-city-lock",ids[1],residents[1].EntityId,caravan,row.SourcePawn,row.Food,row.Count); },
                "A new animal supply intention is refused by the existing city proof"); },"City-lock refusal preserves all Core IDs/proofs, supply history, Colony journals, native originals/cache/food and clock");
            Finish();
        }
        private void Finish() { done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            core=Find.World.GetComponent<CoreWorldComponent>();
            if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                Find.TickManager.Pause(); service=ColonyMod.Current;
                // Ordinary paused updates settle first. No probe tick or clock mutation.
                if (core.IsReady && service != null && pausedSetupFrames++ < 2) return;
                Assert(Modes.Count(Has) == 1 && core.IsReady && service != null,"Exactly one supply-intent stage runs against a validated ordinary Colony service");
                Bind();
                // The only warm SaveSnapshot is BEFORE the boundary. Never repeat it
                // after an API call, including refusal/replay/inspect/save evidence.
                service.SaveSnapshot(core);
                if (Has("city-lock")) { CityLockMode(); return; }
                if (Has("prepare")) { PrepareMode(); return; }
                if (Has("stale") || Has("stale-pending")) { StaleMode(); return; }
                if (Has("stale-pending-load"))
                {
                    string pendingId=Restore(StalePendingSave);
                    ReadOnly(delegate { var status=service.InspectDelegatedSupplyIntent(pendingId);
                        Assert(Intent(pendingId).Stage == RationSupplyIntentStage.Prepared && !status.IsCurrent &&
                            (status.Problems.Contains("SourceChanged") || status.Problems.Contains("SourceUnavailable")) && status.CoreStatus.CanCancel,
                            "A separate process preserves an uncancelled stale intention without regenerating food, updating the manifest or automatically cancelling it");
                    },"Uncancelled stale reload inspect is an exact raw-state no-op");
                    Cancel(pendingId,"supply-intent-probe-cancel-stale-pending"); Finish(); return;
                }
                string name=Has("load") ? PreparedSave : Has("stale-load") ? StaleSave : CancelledSave;
                string id=Restore(name);
                if (Has("load")) { Cancel(id,CancelCause); Save(CancelledSave,id); return; }
                ReadOnly(delegate { var intent=Intent(id); Assert(intent.Stage == RationSupplyIntentStage.Cancelled && intent.PrepareProofStage == OperationStage.Committed && intent.CancelProofStage == OperationStage.Committed,
                    "The separate final reload retains its committed prepare and cancel metadata certificates"); service.CancelDelegatedSupplyIntent(intent.CancelCause,id); service.ResumeDelegatedSupplyIntentProof(id); },
                    "Cancelled final reload/replay allocates +0 and changes no Core/supply/Colony/native original or clock");
                Finish();
            }
            catch (Exception error) { done=true; Log.Error("[INH-SUPPLY-INTENT] FAIL "+error); Application.Quit(1); }
        }
    }
}
