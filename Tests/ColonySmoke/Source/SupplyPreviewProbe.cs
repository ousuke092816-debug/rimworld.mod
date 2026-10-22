using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using Inheritance.Core;
using Inheritance.Colony;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.ColonySmoke
{
    // Staged only. Positive input: INH_MaintenanceResumed. Lock input: INH_DelegatedUiCityLock.
    // INH_MaintenanceStopped is a released-resident save, not a positive fixture.
    // No clock advancement, native creation, transfer, plan control, recovery or save.
    public sealed class SupplyPreviewProbe : GameComponent
    {
        private bool done;
        private string[] ids;
        private Caravan caravan;
        private PawnCustodyResidentSnapshot[] residents;
        private Pawn[] recipients;
        private Pawn[] originals;
        private int historyMatches;
        private DelegatedSupplyPreviewWindow window;
        private int pausedSetupFrames,renderFrames,captureFrame;
        private bool captured;
        private string renderState,renderJournal,renderNative,renderClock,renderPath;
        private static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static bool Locked { get { return GenCommandLine.CommandLineArgPassed("inh-supply-preview-city-lock"); } }
        private static bool Japanese { get { return GenCommandLine.CommandLineArgPassed("inh-supply-preview-render-japanese"); } }
        private static bool Rendering { get { return Japanese || GenCommandLine.CommandLineArgPassed("inh-supply-preview-render"); } }
        private static bool Selected { get { return Locked || Rendering || GenCommandLine.CommandLineArgPassed("inh-supply-preview"); } }
        public SupplyPreviewProbe(Game game) { }
        public override void FinalizeInit() { if (Selected) Find.TickManager.Pause(); }
        private static void Assert(bool value,string message)
        { if (!value) throw new InvalidOperationException(message); Log.Message("[INH-SUPPLY-PREVIEW] PASS "+message); }
        private static string F(float value) { return value.ToString("R",CultureInfo.InvariantCulture); }
        private static bool Same(float left,float right)
        { return !float.IsNaN(left) && !float.IsNaN(right) && left == right; }
        private static string Pack(IEnumerable<string> values)
        { return string.Concat(values.Select(s => s == null ? "-1:" : s.Length.ToString(CultureInfo.InvariantCulture)+":"+s).ToArray()); }
        private static string Identity(object value)
        { return value == null ? "null" : value.GetType().FullName+"#"+RuntimeHelpers.GetHashCode(value).ToString(CultureInfo.InvariantCulture); }
        private static string Item(Thing thing)
        {
            return Pack(new[] {Identity(thing),thing.GetUniqueLoadID(),thing.def.defName,thing.Stuff == null ? null : thing.Stuff.defName,
                thing.stackCount.ToString(CultureInfo.InvariantCulture),thing.HitPoints.ToString(CultureInfo.InvariantCulture),thing.Destroyed.ToString(),
                thing.Spawned.ToString(),Identity(thing.holdingOwner),thing.Map == null ? null : thing.Map.uniqueID.ToString(CultureInfo.InvariantCulture),
                thing.Position.ToString(),thing.Rotation.ToString(),thing.EverSeenByPlayer.ToString()});
        }
        private static string Biology(Pawn pawn)
        {
            return Pack(new[] {Item(pawn),pawn.Name == null ? null : pawn.Name.ToStringFull,pawn.LabelShort,pawn.gender.ToString(),
                pawn.Faction == null ? null : pawn.Faction.GetUniqueLoadID(),pawn.Dead.ToString(),
                pawn.ageTracker.AgeBiologicalTicks.ToString(CultureInfo.InvariantCulture),pawn.ageTracker.AgeChronologicalTicks.ToString(CultureInfo.InvariantCulture),
                pawn.skills == null ? null : Pack(pawn.skills.skills.Select(s => Pack(new[] {s.def.defName,s.Level.ToString(CultureInfo.InvariantCulture),F(s.xpSinceLastLevel)}))),
                Pack(pawn.health.hediffSet.hediffs.Select(h => Pack(new[] {h.def.defName,F(h.Severity),h.Part == null ? null : h.Part.def.defName}))),
                pawn.genes == null ? null : Pack(pawn.genes.GenesListForReading.Select(g => Pack(new[] {g.def.defName,g.Active.ToString(),g.Overridden.ToString()}))),
                pawn.Ideo == null ? null : pawn.Ideo.GetUniqueLoadID(),
                pawn.needs == null ? null : Pack(pawn.needs.AllNeeds.OrderBy(n => n.def.defName,StringComparer.Ordinal).Select(n => Pack(new[] {n.def.defName,F(n.CurLevel)}))),
                pawn.story == null ? null : Pack(pawn.story.traits.allTraits.Select(t => Pack(new[] {t.def.defName,t.Degree.ToString(CultureInfo.InvariantCulture)}))),
                pawn.apparel == null ? null : Pack(pawn.apparel.WornApparel.Select(Item)),
                pawn.equipment == null ? null : Pack(pawn.equipment.AllEquipmentListForReading.Select(Item)),
                pawn.inventory == null ? null : Pack(pawn.inventory.innerContainer.Select(Item))});
        }
        private static IEnumerable<FieldInfo> AllFields(Type type)
        {
            for (Type current=type;current != null && current != typeof(object);current=current.BaseType)
                foreach (var field in current.GetFields(Fields|BindingFlags.DeclaredOnly).OrderBy(f => f.Name,StringComparer.Ordinal)) yield return field;
        }
        private static object Field(object value,string name)
        {
            var field=AllFields(value.GetType()).SingleOrDefault(f => f.Name == name);
            if (field == null) throw new InvalidOperationException("Required original journal field is missing: "+name);
            return field.GetValue(value);
        }
        // Read fields only: do not call ExposeData, serializing getters or SynchronizeCounts.
        // Include full saved Core records (requests, stamps, digest and recovery fields), not just IDs.
        private static string Canonical(object value) { return Canonical(value,new HashSet<object>(ReferenceComparer.Instance)); }
        private static string Canonical(object value,HashSet<object> visited)
        {
            if (value == null) return "null";
            var type=value.GetType();
            if (value is string) return "string:"+Pack(new[] {(string)value});
            if (value is float) return "Single:"+((float)value).ToString("R",CultureInfo.InvariantCulture);
            if (value is double) return "Double:"+((double)value).ToString("R",CultureInfo.InvariantCulture);
            if (type.IsPrimitive || type.IsEnum || value is decimal) return type.FullName+":"+Convert.ToString(value,CultureInfo.InvariantCulture);
            var thing=value as Thing; if (thing != null) return Item(thing);
            var def=value as Def; if (def != null) return type.FullName+":"+def.defName;
            var owner=value as ThingOwner;
            if (owner != null) return Pack(new[] {Identity(owner),Pack(owner.Cast<Thing>().Select(Item))});
            var callback=value as Delegate;
            if (callback != null) return Pack(callback.GetInvocationList().Select(d => Identity(d.Target)+":"+d.Method.DeclaringType.FullName+":"+d.Method.Name));
            if (!type.IsValueType && !visited.Add(value)) return "reference:"+Identity(value);
            var sequence=value as IEnumerable;
            if (sequence != null) return type.FullName+":"+Pack(sequence.Cast<object>().Select(v => Canonical(v,visited)));
            return type.FullName+":"+Pack(AllFields(type).Select(f => f.DeclaringType.FullName+"."+f.Name+"="+Canonical(f.GetValue(value),visited)));
        }
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance=new ReferenceComparer();
            public new bool Equals(object x,object y) { return ReferenceEquals(x,y); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
        private static Pawn[] NativePawns(CoreWorldComponent core)
        {
            return core.GetDirectlyHeldThings().OfType<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns))
                .Concat(Find.WorldObjects.Caravans.SelectMany(c => c.PawnsListForReading)).Concat(Find.WorldPawns.AllPawnsAliveOrDead)
                .Distinct().OrderBy(p => p.GetUniqueLoadID(),StringComparer.Ordinal).ToArray();
        }
        private static ColonyState State(string payload)
        { return (ColonyState)new XmlSerializer(typeof(ColonyState)).Deserialize(new StringReader(payload)); }
        private static string Owners(Pawn pawn,CoreWorldComponent core)
        {
            return Pack(new[] {Identity(pawn.holdingOwner),core.GetDirectlyHeldThings().Contains(pawn).ToString(),
                Find.WorldPawns.Contains(pawn).ToString(),Pack(Find.WorldObjects.Caravans.Where(c => c.PawnsListForReading.Contains(pawn)).Select(c => c.GetUniqueLoadID())),
                Pack(Find.Maps.Where(m => m.mapPawns.AllPawns.Contains(pawn)).Select(m => m.uniqueID.ToString(CultureInfo.InvariantCulture)))});
        }
        private string NativeStamp(CoreWorldComponent core)
        {
            return Pack(new[] {Pack(NativePawns(core).Select(p => Identity(p)+":"+p.GetUniqueLoadID())),
                Pack(originals.Select(p => Pack(new[] {Biology(p),Owners(p,core),Canonical(p.inventory)}))),
                Pack(originals.Where(p => p.inventory != null).SelectMany(p => p.inventory.innerContainer).Select(t =>
                    Pack(new[] {Item(t),t is ThingWithComps ? Canonical(((ThingWithComps)t).AllComps) : null}))),
                Pack(core.Rations.GetDirectlyHeldThings().Select(t => Pack(new[] {Item(t),t is ThingWithComps ? Canonical(((ThingWithComps)t).AllComps) : null}))),
                Pack(Find.WorldObjects.Caravans.Select(c => Pack(new[] {Identity(c),c.GetUniqueLoadID(),c.Tile.ToString(),c.pather.Moving.ToString(),
                    Pack(c.PawnsListForReading.Select(p => p.GetUniqueLoadID()))}))) });
        }
        private static string Journal(CoreWorldComponent core)
        {
            return Pack(new[] {Canonical(core.Header),Canonical(Field(core.Ledger,"state")),
                Canonical(Field(core.PawnCustody,"residents")),Canonical(Field(core.PawnCustody,"transfers")),
                Canonical(Field(core.PawnCustody,"residentCount")),Canonical(Field(core.PawnCustody,"transferCount")),
                Canonical(Field(core.Rations,"debits")),Canonical(Field(core.Rations,"debitCount")),Canonical(Field(core.Rations,"spentCount"))});
        }
        private static string Clock()
        { return Find.TickManager.TicksGame.ToString(CultureInfo.InvariantCulture)+":"+Find.TickManager.TicksAbs.ToString(CultureInfo.InvariantCulture)+":"+Find.TickManager.Paused; }
        private static string RawColony(ColonyService service) { return Canonical(Field(service,"state")); }
        private void Bind(CoreWorldComponent core,ColonyService service)
        {
            ids=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"p04-maintenance-ids.txt"));
            Assert(ids.Length >= 16 && ids[0] == core.Header.SaveScopeId,"The loader restored the exact source save's maintenance identity metadata");
            var found=core.ReadPawnCustodyResidents(ColonyService.Actor).Where(r => r.CityId == ids[1]).ToArray();
            Assert(found.Length == 2 && found.Select(r => r.EntityId).Distinct().Count() == 2 && found.Select(r => r.NativeId).Distinct().Count() == 2,
                "Two distinct original residents exist; a released or mismatched fixture must fail the positive preview test");
            residents=new[] {found.Single(r => r.EntityId == ids[5]),found.Single(r => r.EntityId == ids[6])};
            Assert(residents[0].NativeId == ids[3] && residents[1].NativeId == ids[4] && residents.All(r => r.CaravanId == ids[2]),
                "Resident native identities and their original caravan derive from Core custody and match the source metadata");
            var all=NativePawns(core);
            Assert(all.Select(p => p.GetUniqueLoadID()).Distinct().Count() == all.Length,"No distinct native Pawn objects share an original native identity");
            recipients=residents.Select(r => all.Single(p => p.GetUniqueLoadID() == r.NativeId)).ToArray();
            caravan=Find.WorldObjects.Caravans.Single(c => c.GetUniqueLoadID() == residents[0].CaravanId);
            originals=all.Where(p => core.Ledger.Entities.Any(e => e.BoundThingLoadId == p.GetUniqueLoadID()) || caravan.PawnsListForReading.Contains(p)).ToArray();
            Assert(recipients[0].RaceProps.Humanlike && recipients[1].RaceProps.Animal && recipients.All(p => !p.Spawned && !p.Destroyed && !p.Dead &&
                ReferenceEquals(p.holdingOwner,core.GetDirectlyHeldThings()) && !Find.WorldPawns.Contains(p) && !Find.WorldObjects.Caravans.Any(c => c.PawnsListForReading.Contains(p))),
                "The exact human and animal remain solely in Core native custody, with names derived from these originals");
            Assert(residents.All(r => core.Ledger.Entities.Count(e => e.BoundThingLoadId == r.NativeId) == 1 && core.Ledger.GetEntity(r.EntityId).BoundThingLoadId == r.NativeId),
                "Each original native resident has exactly one permanent Core binding");
            var site=((ColonyState)Field(service,"state")).bases.Single(b => b.cityId == ids[1]);
            Assert(!caravan.pather.Moving && caravan.Tile.tileId == site.tile && site.mode == BaseMode.Delegated && caravan.PawnsListForReading.Any(p => p.IsColonist),
                "The stopped original caravan is at the existing delegated site with its native escort");
            if (Locked)
            {
                Assert(core.Ledger.HasPendingReference(ids[1]) && core.Rations.HasPending(residents[0].EntityId) && !core.Rations.HasPending(residents[1].EntityId),
                    "The clean city fixture has the human pending ration and independent animal under the same pending Core city proof");
                var humanDebit=core.Rations.Snapshots.Single(r => r.EntityId == residents[0].EntityId && r.Stage != RationDebitStage.Committed);
                var second=core.Ledger.FindOperation(ColonyService.Actor,"create","ration-applied/"+humanDebit.Cause);
                Assert(humanDebit.Stage == RationDebitStage.Applied && second != null && second.Stage == OperationStage.Prepared,
                    "City-lock preview begins with the original Applied native food debit and its exact Prepared second Core proof");
            }
            else Assert(!core.Ledger.HasPendingReference(ids[1]) && residents.All(r => !core.Rations.HasPending(r.EntityId)),
                "The positive preview fixture has available human and animal residents without a pending food or city operation");
        }
        private static string Cargo(DelegatedSupplyCargoHistorySnapshot history)
        { return Pack(new[] {history.ShipmentId,history.Stage,history.SourceNativeId,history.CargoNativeId,history.JourneySupply.ToString(),history.MatchesSourceId.ToString(),history.MatchesCargoId.ToString()}); }
        private static string[] ExpectedHistory(ColonyState state,string native)
        {
            return state.physical.SelectMany(r => r.cargo.Where(c => c.sourceNativeId == native || c.cargoNativeId == native).Select(c =>
                Pack(new[] {r.shipmentId,r.stage.ToString(),c.sourceNativeId,c.cargoNativeId,c.journeySupply.ToString(),(c.sourceNativeId == native).ToString(),(c.cargoNativeId == native).ToString()}))).ToArray();
        }
        private static bool Historical(ColonyState state,string native,bool journey)
        { return state.physical.SelectMany(r => r.cargo).Any(c => (c.sourceNativeId == native || c.cargoNativeId == native) && c.journeySupply == journey); }
        private static string PreviewStamp(DelegatedSupplyPreviewSnapshot preview)
        {
            return Pack(new[] {preview.CityId,preview.CityName,preview.EntityId,preview.RecipientName,preview.RecipientKind,preview.OriginalCaravanId,
                preview.CaravanName,Identity(preview.Caravan),Identity(preview.Recipient),F(preview.DailyDemand),preview.CanPreview.ToString(),Pack(preview.Problems),
                Pack(preview.Foods.Select(row => Pack(new[] {Identity(row.SourcePawn),Identity(row.Food),row.SourceName,row.SourceKind,row.SourceNativeId,row.SourceEntityId,
                    row.FoodName,row.NativeId,row.DefName,row.StuffDef,row.Count.ToString(CultureInfo.InvariantCulture),row.HitPoints.ToString(CultureInfo.InvariantCulture),
                    F(row.UnitNutrition),F(row.DailyDemand),row.StackNutrition.ToString("R",CultureInfo.InvariantCulture),row.CanPreview.ToString(),Pack(row.Problems),Pack(row.CargoHistory.Select(Cargo))}))) });
        }
        private void Check(CoreWorldComponent core,ColonyService service,ColonyState state,int index,DelegatedSupplyPreviewSnapshot preview,bool first)
        {
            var resident=residents[index]; var pawn=recipients[index];
            float nativeDemand=pawn.needs.food.FoodFallPerTickAssumingCategory(HungerCategory.Fed,true)*60000f;
            var ordinary=core.Rations.Quote(ColonyService.Actor,resident.EntityId,resident.CityId);
            var recipient=core.Rations.QuoteSupplyCandidate(ColonyService.Actor,resident.EntityId,resident.CityId,null,null);
            var site=state.bases.Single(b => b.cityId == resident.CityId);
            Assert(ReferenceEquals(preview.Recipient,pawn) && ReferenceEquals(recipient.Recipient,pawn) && ReferenceEquals(preview.Caravan,caravan) &&
                preview.CityId == resident.CityId && preview.CityName == site.name && preview.EntityId == resident.EntityId && preview.OriginalCaravanId == resident.CaravanId &&
                preview.RecipientName == pawn.LabelShort && preview.RecipientKind == (pawn.RaceProps.Animal ? "animal" : "person") && preview.CaravanName == caravan.Label &&
                nativeDemand > 0f && Same(preview.DailyDemand,nativeDemand) && Same(ordinary.DailyDemand,nativeDemand) && Same(recipient.DailyDemand,nativeDemand),
                "The "+(index == 0 ? "human" : "animal")+" preview quotes its original Pawn's native daily demand and existing Core quote exactly");
            bool recipientHistory=pawn.inventory.innerContainer.Any(t => t.def.IsNutritionGivingIngestible && Historical(state,t.GetUniqueLoadID(),false));
            bool recipientJourney=pawn.inventory.innerContainer.Any(t => t.def.IsNutritionGivingIngestible && Historical(state,t.GetUniqueLoadID(),true));
            Assert(preview.Problems.Contains("RecipientHistoricalCargo") == recipientHistory && preview.Problems.Contains("RecipientHistoricalJourneySupply") == recipientJourney,
                "Recipient immutable cargo and journey history reasons match all saved manifest stages");
            if (Locked)
                Assert(!preview.CanPreview && preview.Problems.Contains("CityLocked") && preview.Problems.Contains("RecipientPendingRations") == (index == 0),
                    "The existing city proof prevents both previews and retains the human's separate pending-ration reason");
            else Assert(preview.CanPreview && preview.Problems.Count == 0,"The ordinary available resident has a positive read-only preview without a transfer receipt");
            var actual=caravan.PawnsListForReading.Where(p => p != null && (p.RaceProps.Humanlike || p.RaceProps.Animal) && p.inventory != null)
                .SelectMany(p => p.inventory.innerContainer.Where(t => t != null && t.def.IsNutritionGivingIngestible).Select(t => new {Pawn=p,Food=t})).ToArray();
            Assert(actual.Length > 0 && preview.Foods.Count == actual.Length && preview.Foods.Select(r => r.NativeId).Distinct().Count() == actual.Length &&
                actual.All(a => preview.Foods.Count(r => ReferenceEquals(r.Food,a.Food) && ReferenceEquals(r.SourcePawn,a.Pawn)) == 1),
                "Every actual nutrition-giving stack in the original caravan occurs exactly once, including any rejected food");
            int suitable=0;
            foreach (var row in preview.Foods)
            {
                var source=row.SourcePawn; var food=row.Food;
                var quote=core.Rations.QuoteSupplyCandidate(ColonyService.Actor,resident.EntityId,resident.CityId,source,food);
                var binding=core.Ledger.Entities.SingleOrDefault(e => e.BoundThingLoadId == source.GetUniqueLoadID());
                Assert(food.holdingOwner == source.inventory.innerContainer && source.inventory.innerContainer.Contains(food) && !food.Destroyed &&
                    row.NativeId == food.GetUniqueLoadID() && row.SourceNativeId == source.GetUniqueLoadID() && row.SourceName == source.LabelShort && row.FoodName == food.Label &&
                    row.SourceEntityId == (binding == null ? null : binding.Id) && row.SourceKind == (source.RaceProps.Animal ? "animal" : "person") && row.DefName == food.def.defName &&
                    row.StuffDef == (food.Stuff == null ? null : food.Stuff.defName) && row.Count == food.stackCount && row.HitPoints == food.HitPoints &&
                    Same(row.DailyDemand,nativeDemand) && Same(row.UnitNutrition,quote.UnitNutrition) && row.StackNutrition == (row.UnitNutrition < 0f ? -1d : (double)row.UnitNutrition*food.stackCount),
                    "Food "+row.NativeId+" uses the original owner, native stack metadata and precise Core unit/stack nutrition");
                if (quote.FoodProblem == "None")
                {
                    suitable++;
                    Assert(Same(row.UnitNutrition,FoodUtility.NutritionForEater(pawn,food)) && FoodUtility.WillEat(pawn,food,null,true,false),
                        "Food "+row.NativeId+" uses actual FoodUtility nutrition and acceptance for this recipient");
                }
                else Assert(!row.CanPreview && row.Problems.Contains(quote.FoodProblem),"Rejected native food remains visible with the real Core food reason");
                if (quote.RecipientProblem != "None") Assert(row.Problems.Contains(quote.RecipientProblem) && !row.CanPreview,"The per-food row retains its real Core recipient rejection");
                var history=ExpectedHistory(state,row.NativeId);
                Assert(row.CargoHistory.Select(Cargo).SequenceEqual(history) && row.Problems.Contains("HistoricalCargo") == Historical(state,row.NativeId,false) &&
                    row.Problems.Contains("HistoricalJourneySupply") == Historical(state,row.NativeId,true),
                    "Food "+row.NativeId+" retains exact source/cargo ID matches, stages and journey reasons from immutable shipment history");
                if (first) historyMatches+=history.Length;
                Assert(row.CanPreview == (row.Problems.Count == 0 && quote.CanPreview) && preview.Problems.All(p => row.Problems.Contains(p)),
                    "The candidate's preview availability reflects all its reasons without authorizing any native transfer");
            }
            Assert(suitable > 0,"The existing caravan offers at least one actual accepted food for the original "+(index == 0 ? "human" : "animal"));
            if (!Locked) Assert(preview.Foods.Any(r => r.CanPreview),"At least one original food stack is a positive preview candidate");
            else
            {
                Assert(preview.Foods.All(r => !r.CanPreview && r.Problems.Contains("CityLocked")),"City-lock rejection remains on every native food candidate");
                var management=service.DelegatedCustodyResidents(ids[1]).Single(r => r.EntityId == resident.EntityId);
                Assert(!management.CanRelease && management.Problem == "CityLocked","Read-only nutrition remains observable while the original resident cannot return through the locked city");
            }
        }
        private void BeginRender(CoreWorldComponent core,ColonyService service,string rawState,string journal,string native,string clock)
        {
            // Carry the pre-query baseline into the UI; do not normalize or rebase it.
            renderState=rawState; renderJournal=journal; renderNative=native; renderClock=clock;
            string language=Japanese ? "Japanese" : "English";
            Assert(LanguageDatabase.activeLanguage.folderName.StartsWith(language,StringComparison.Ordinal),"The preview screen uses the requested native game language");
            File.WriteAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"supply-preview-translation-errors-"+language+".txt"),
                LanguageDatabase.activeLanguage.loadErrors.Concat(LanguageDatabase.activeLanguage.defInjections.SelectMany(p => p.loadErrors)).ToArray());
            foreach (string key in new[] {"INH_SupplyPreviewTitle","INH_SupplyPreviewWarning","INH_SupplyPreviewEstimateHint","INH_SupplyPreviewFoods"})
                Assert(key.Translate().ToString() != key,"The actual preview screen has translated text for "+key);
            var formatter=typeof(DelegatedSupplyPreviewWindow).GetMethod("Problem",BindingFlags.NonPublic|BindingFlags.Static);
            Assert(formatter != null,"The real window's reason formatter is available for the language boundary check");
            var preview=service.PreviewDelegatedFoodSupply(ids[1],residents[0].EntityId,caravan);
            foreach (string code in preview.Problems.Concat(preview.Foods.SelectMany(f => f.Problems)).Distinct(StringComparer.Ordinal))
            {
                string key="INH_SupplyPreviewProblem_"+code,expected=key.Translate().ToString();
                string actual=(string)formatter.Invoke(null,new object[] {code});
                Assert(expected != key && actual == expected && actual != "INH_SupplyPreviewProblem_Unknown".Translate().ToString(),
                    "The actual window renders "+code+" with its exact translated reason instead of an Unknown fallback");
            }
            if (Locked) Assert(preview.Problems.Contains("CityLocked") && preview.Problems.Contains("RecipientPendingRations"),
                "The city-lock screen has both distinct refusal reasons on the original human before painting");
            renderPath=Path.Combine(GenFilePaths.SaveDataFolderPath,"SupplyPreview-"+(Locked ? "CityLock-" : "")+language+".png");
            if (File.Exists(renderPath)) File.Delete(renderPath);
            Assert(RawColony(service) == renderState && Journal(core) == renderJournal && NativeStamp(core) == renderNative && Clock() == renderClock,
                "Translation checks and the extra real-service observation preserve the same unnormalized pre-query baseline");
            window=new DelegatedCustodyWindow(service).OpenSupplyPreview(ids[1],residents[0].EntityId); 
        }
        private void Render(CoreWorldComponent core,ColonyService service)
        {
            renderFrames++;
            if (renderFrames > 1800) throw new InvalidOperationException("Supply preview did not repaint and capture in time");
            if (!captured && window.Rendered && renderFrames > 30)
            { ScreenCapture.CaptureScreenshot(renderPath); captured=true; captureFrame=renderFrames; }
            if (!captured || renderFrames < captureFrame+3 || !File.Exists(renderPath) || new FileInfo(renderPath).Length <= 1000) return;
            Assert(RawColony(service) == renderState && Journal(core) == renderJournal && NativeStamp(core) == renderNative && Clock() == renderClock && window.Rendered,
                "Actual preview repaint, screenshot and following paused frames change no complete payload, Core IDs/allocator/proofs, native inventory/biology/needs/ownership, spent witnesses or clock");
            if (Locked) Assert(core.Ledger.HasPendingReference(ids[1]) && core.Rations.HasPending(residents[0].EntityId),
                "The actual screen preserves the original city and human ration checkpoint without recovery");
            done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
        }
        public override void GameComponentUpdate()
        {
            if (done || !Selected || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>();
            if (!core.IsReady && core.BlockReason == null) return;
            try
            {
                Find.TickManager.Pause();
                if (window != null) { Render(core,ColonyMod.Current); return; }
                // Let the ordinary paused Core update settle its existing maintenance
                // Hold on a lock fixture before measuring the multi-frame UI boundary.
                // This probe never invokes Tick, controls, native transfer or recovery.
                if (Rendering && pausedSetupFrames++ < 2) return;
                Assert(core.IsReady && ColonyMod.Current != null,"The original validated world and ordinary Colony service are ready");
                Find.TickManager.Pause(); var service=ColonyMod.Current; Bind(core,service);
                // Warm validation/count synchronization exactly once before the baseline.
                // No SaveSnapshot call is permitted after observation starts: it could
                // normalize a changed count and mask the difference under test.
                string payload=service.SaveSnapshot(core),rawState=RawColony(service),journal=Journal(core),native=NativeStamp(core),clock=Clock();
                var state=State(payload); var stamps=new string[2];
                for (int repeat=0;repeat<3;repeat++)
                {
                    for (int index=0;index<2;index++)
                    {
                        var preview=service.PreviewDelegatedFoodSupply(ids[1],residents[index].EntityId,caravan);
                        Check(core,service,state,index,preview,repeat == 0);
                        string stamp=PreviewStamp(preview); if (repeat == 0) stamps[index]=stamp;
                        else Assert(stamp == stamps[index],"Repeated real-service preview returns exactly the same original-object observations");
                    }
                    Assert(RawColony(service) == rawState && Journal(core) == journal && NativeStamp(core) == native && Clock() == clock,
                        "Repeated preview changes no Core IDs/allocator/operations, complete Colony payload, custody/ration/maintenance journal, spent witness, native biology/needs/foods/owners or clock");
                }
                if (historyMatches == 0) Log.Message("[INH-SUPPLY-PREVIEW] EVIDENCE LIMIT: the original caravan's live food IDs have no cargo-history match; all manifest stages and empty per-food histories were checked, but this fixture does not exercise a positive historical-food rejection.");
                else Log.Message("[INH-SUPPLY-PREVIEW] historical native food matches="+historyMatches);
                Log.Message("[INH-SUPPLY-PREVIEW] Read-only candidates are not transfer authorization; no transfer API, save, plan control, food creation or recovery was invoked.");
                if (Rendering) { BeginRender(core,service,rawState,journal,native,clock); return; }
                done=true; Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception error) { done=true; Log.Error("[INH-SUPPLY-PREVIEW] FAIL "+error); Application.Quit(1); }
        }
    }
}
