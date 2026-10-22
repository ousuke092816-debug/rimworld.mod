using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Core
{
    public sealed class SaveHeader : IExposable
    {
        public const int CurrentSchemaVersion = 9;
        private int schemaVersion = CurrentSchemaVersion;
        private string saveScopeId, producerVersion = "0.4.16", gameVersion, worldMode = "vanilla";
        private long nextSequence = 1;
        private List<ModuleStamp> moduleStamps = new List<ModuleStamp>();
        private List<string> migrations = new List<string>();
        private bool loaded;
        public int SchemaVersion { get { return schemaVersion; } }
        public string SaveScopeId { get { return saveScopeId; } }
        public long NextSequence { get { return nextSequence; } }
        public bool IsCompatible { get { return schemaVersion == CurrentSchemaVersion; } }
        public string WorldMode { get { return worldMode; } }
        public IEnumerable<string> Migrations { get { return migrations.ToArray(); } }
        internal List<ModuleStamp> ModuleStamps { get { return moduleStamps; } }
        public void EnsureInitialized()
        {
            if (!IsCompatible) throw new InvalidOperationException("Unsupported Inheritance save schema: " + schemaVersion);
            if (string.IsNullOrEmpty(saveScopeId) && !loaded) saveScopeId = Guid.NewGuid().ToString("N");
            ValidateIdentity();
        }
        internal void ValidateIdentity()
        {
            Guid scope;
            if ((schemaVersion < 2 || schemaVersion > CurrentSchemaVersion) || saveScopeId == null || !Guid.TryParseExact(saveScopeId, "N", out scope) ||
                scope == Guid.Empty || scope.ToString("N") != saveScopeId || nextSequence < 1)
                throw new InvalidOperationException("Invalid save identity or ID allocator");
            if (worldMode != "vanilla" && (schemaVersion < 3 || worldMode != "six-nations")) throw new InvalidOperationException("Unsupported world mode: " + worldMode);
            if (moduleStamps == null || migrations == null) throw new InvalidOperationException("Missing save header metadata");
        }
        internal void UpdateProducer(ModuleCatalogue modules)
        { producerVersion = "0.4.16"; gameVersion = VersionControl.CurrentVersionString; moduleStamps = modules.Stamps(); }
        internal void ChooseMode(string mode) { if (loaded) throw new InvalidOperationException("A loaded world cannot change mode"); worldMode = mode; ValidateIdentity(); }
        internal SaveHeader MigrateTwo(ModuleCatalogue modules)
        {
            if (schemaVersion != 2 || worldMode != "vanilla") throw new InvalidOperationException("Invalid schema two migration source");
            var h = new SaveHeader { saveScopeId = saveScopeId, nextSequence = nextSequence, loaded = true,
                migrations = new List<string>(migrations) };
            h.migrations.Add("2->3:world-bootstrap-gate"); h.migrations.Add("3->4:module-payloads"); h.migrations.Add("4->5:core-pawn-custody"); h.migrations.Add("5->6:caravan-pawn-custody"); h.migrations.Add("6->7:native-ration-debits"); h.migrations.Add("7->8:non-reserving-supply-intents"); h.migrations.Add("8->9:armed-supply-transfers"); h.UpdateProducer(modules); h.ValidateIdentity(); return h;
        }
        internal SaveHeader MigrateOne(ModuleCatalogue modules)
        {
            Guid scope;
            if (schemaVersion != 1 || saveScopeId == null || !Guid.TryParseExact(saveScopeId, "N", out scope) ||
                scope == Guid.Empty || scope.ToString("N") != saveScopeId)
                throw new InvalidOperationException("Legacy header has no valid world identity");
            var h = new SaveHeader { saveScopeId = saveScopeId, loaded = true };
            h.migrations.Add("1->2:empty-ledger"); h.migrations.Add("2->3:world-bootstrap-gate"); h.migrations.Add("3->4:module-payloads"); h.migrations.Add("4->5:core-pawn-custody"); h.migrations.Add("5->6:caravan-pawn-custody"); h.migrations.Add("6->7:native-ration-debits"); h.migrations.Add("7->8:non-reserving-supply-intents"); h.migrations.Add("8->9:armed-supply-transfers"); h.UpdateProducer(modules); h.ValidateIdentity(); return h;
        }
        internal SaveHeader MigrateThree(ModuleCatalogue modules)
        {
            if (schemaVersion != 3) throw new InvalidOperationException("Invalid schema three migration source");
            var h = new SaveHeader { saveScopeId = saveScopeId, nextSequence = nextSequence, worldMode = worldMode,
                loaded = true, migrations = new List<string>(migrations) };
            h.migrations.Add("3->4:module-payloads"); h.migrations.Add("4->5:core-pawn-custody"); h.migrations.Add("5->6:caravan-pawn-custody"); h.migrations.Add("6->7:native-ration-debits"); h.migrations.Add("7->8:non-reserving-supply-intents"); h.migrations.Add("8->9:armed-supply-transfers"); h.UpdateProducer(modules); h.ValidateIdentity(); return h;
        }
        internal SaveHeader MigrateFour(ModuleCatalogue modules)
        {
            if (schemaVersion != 4) throw new InvalidOperationException("Invalid schema four migration source");
            var h=new SaveHeader {saveScopeId=saveScopeId,nextSequence=nextSequence,worldMode=worldMode,loaded=true,migrations=new List<string>(migrations)};
            h.migrations.Add("4->5:core-pawn-custody"); h.migrations.Add("5->6:caravan-pawn-custody"); h.migrations.Add("6->7:native-ration-debits"); h.migrations.Add("7->8:non-reserving-supply-intents"); h.migrations.Add("8->9:armed-supply-transfers"); h.UpdateProducer(modules); h.ValidateIdentity(); return h;
        }
        internal SaveHeader MigrateFive(ModuleCatalogue modules)
        {
            if (schemaVersion != 5) throw new InvalidOperationException("Invalid schema five migration source");
            var h=new SaveHeader {saveScopeId=saveScopeId,nextSequence=nextSequence,worldMode=worldMode,loaded=true,migrations=new List<string>(migrations)};
            h.migrations.Add("5->6:caravan-pawn-custody"); h.migrations.Add("6->7:native-ration-debits"); h.migrations.Add("7->8:non-reserving-supply-intents"); h.migrations.Add("8->9:armed-supply-transfers"); h.UpdateProducer(modules); h.ValidateIdentity(); return h;
        }
        internal SaveHeader MigrateSix(ModuleCatalogue modules)
        {
            if (schemaVersion != 6) throw new InvalidOperationException("Invalid schema six migration source");
            var h=new SaveHeader {saveScopeId=saveScopeId,nextSequence=nextSequence,worldMode=worldMode,loaded=true,migrations=new List<string>(migrations)};
            h.migrations.Add("6->7:native-ration-debits"); h.migrations.Add("7->8:non-reserving-supply-intents"); h.migrations.Add("8->9:armed-supply-transfers"); h.UpdateProducer(modules); h.ValidateIdentity(); return h;
        }
        internal SaveHeader MigrateSeven(ModuleCatalogue modules)
        {
            if (schemaVersion != 7) throw new InvalidOperationException("Invalid schema seven migration source");
            var h=new SaveHeader {saveScopeId=saveScopeId,nextSequence=nextSequence,worldMode=worldMode,loaded=true,migrations=new List<string>(migrations)};
            h.migrations.Add("7->8:non-reserving-supply-intents"); h.migrations.Add("8->9:armed-supply-transfers"); h.UpdateProducer(modules); h.ValidateIdentity(); return h;
        }
        internal SaveHeader MigrateEight(ModuleCatalogue modules)
        {
            if (schemaVersion != 8) throw new InvalidOperationException("Invalid schema eight migration source");
            var h=new SaveHeader {saveScopeId=saveScopeId,nextSequence=nextSequence,worldMode=worldMode,loaded=true,migrations=new List<string>(migrations)};
            h.migrations.Add("8->9:armed-supply-transfers"); h.UpdateProducer(modules); h.ValidateIdentity(); return h;
        }
        internal string Issue(string kind)
        {
            ValidateIdentity();
            if (nextSequence == long.MaxValue) throw new InvalidOperationException("Permanent ID range exhausted");
            return "inh:" + saveScopeId + ":" + kind + ":" + (nextSequence++).ToString(CultureInfo.InvariantCulture);
        }
        internal long ParseId(string id, string kind)
        {
            var p = id == null ? new string[0] : id.Split(':'); long sequence;
            if (p.Length != 4 || p[0] != "inh" || p[1] != saveScopeId || p[2] != kind ||
                !long.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out sequence) || sequence < 1 ||
                sequence.ToString(CultureInfo.InvariantCulture) != p[3]) throw new InvalidOperationException("Invalid permanent ID: " + id);
            return sequence;
        }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars) loaded = true;
            // An absent version always means the historical prototype, never today's schema.
            Scribe_Values.Look(ref schemaVersion, "schemaVersion", 1, true);
            Scribe_Values.Look(ref saveScopeId, "saveScopeId");
            if (schemaVersion >= 2)
            {
                Scribe_Values.Look(ref nextSequence, "nextSequence", 0L, true);
                Scribe_Values.Look(ref producerVersion, "producerVersion"); Scribe_Values.Look(ref gameVersion, "gameVersion");
                Scribe_Values.Look(ref worldMode, "worldMode");
                Scribe_Collections.Look(ref moduleStamps, "moduleStamps", LookMode.Deep);
                Scribe_Collections.Look(ref migrations, "migrations", LookMode.Value);
                if (Scribe.mode == LoadSaveMode.PostLoadInit && migrations == null) migrations = new List<string>();
            }
        }
    }

    public sealed partial class CoreWorldComponent : WorldComponent,IThingHolder
    {
        private SaveHeader header = new SaveHeader();
        private LedgerState savedLedger = new LedgerState();
        private CoreLedger ledger;
        private bool incompatibleWorldQueued, loaded, initialized, worldWasLoaded;
        private bool worldValidated, bootstrapping;
        private static Func<string> chooseMode;
        private static Action<CoreWorldComponent,CoreLedger> bootstrapWorld;
        private static Func<CoreWorldComponent,string> saveWorld;
        private static Action<CoreWorldComponent> updateWorld;
        private string worldPayload;
        private string blockReason;
        public SaveHeader Header { get { return header; } }
        public bool IsReady { get { return ledger != null && blockReason == null && worldValidated && modulesValidated; } }
        public string BlockReason { get { return blockReason; } }
        public string WorldPayload { get { return worldPayload; } }
        public bool IsLoadedWorld { get { return loaded || worldWasLoaded; } }
        public CoreLedger Ledger
        { get { if (!IsReady) throw new InvalidOperationException("World ledger is unavailable: " + blockReason); return ledger; } }
        private PawnCustody pawnCustody;
        internal PawnCustody CustodyForValidation { get { return pawnCustody; } }
        private RationDebits rations;
        private RationSupplyIntents supplyIntents;
        private bool loadedSupplyNodePresent;
        private RationSupplyTransfers supplyTransfers;
        private bool loadedSupplyTransferNodePresent;
        internal RationSupplyTransfers SupplyTransfersForValidation { get { return supplyTransfers; } }
        public RationSupplyTransfers SupplyTransfers { get { if (!IsReady) throw new InvalidOperationException("Core supply transfers are unavailable"); return supplyTransfers; } }
        internal RationSupplyIntents SupplyIntentsForValidation { get { return supplyIntents; } }
        public RationSupplyIntents SupplyIntents { get { if (!IsReady) throw new InvalidOperationException("Core supply intents are unavailable"); return supplyIntents; } }
        internal bool RationMutation { get; set; }
        public RationDebits Rations { get { if (!IsReady) throw new InvalidOperationException("Core rations are unavailable"); return rations; } }
        public PawnCustody PawnCustody { get { if (!IsReady) throw new InvalidOperationException("Core custody is unavailable"); return pawnCustody; } }
        // Detached metadata is also readable by the module currently being restored.
        // Native mutation remains gated through the ready-world PawnCustody API.
        private void CheckCustodyRead(string actor)
        {
            if (Find.World == null || Find.World.GetComponent<CoreWorldComponent>() != this || ledger == null || blockReason != null || pawnCustody == null ||
                !(IsReady || bootstrapping && bootstrapActor == actor)) throw new InvalidOperationException("Core custody metadata is unavailable");
        }
        public PawnCustodySnapshot[] ReadPawnCustodyTransfers(string actor)
        { CheckCustodyRead(actor); return pawnCustody.Transfers.ToArray(); }
        public PawnCustodyResidentSnapshot[] ReadPawnCustodyResidents(string actor)
        { CheckCustodyRead(actor); return pawnCustody.Residents.ToArray(); }
        public RationDebitSnapshot[] ReadRationDebits(string actor)
        { CheckCustodyRead(actor); if (rations == null) throw new InvalidOperationException("Core rations are missing"); return rations.Snapshots.ToArray(); }
        public RationSupplyIntentSnapshot[] ReadRationSupplyIntents(string actor)
        { CheckCustodyRead(actor); if (supplyIntents == null) throw new InvalidOperationException("Core supply intents are missing"); return supplyIntents.ReadSnapshots(ledger); }
        public RationSupplyTransferSnapshot[] ReadRationSupplyTransfers(string actor)
        { CheckCustodyRead(actor); if (supplyTransfers == null) throw new InvalidOperationException("Core supply transfers are missing"); return supplyTransfers.ReadSnapshots(ledger); }
        public CoreWorldComponent(World world) : base(world) { pawnCustody=new PawnCustody(this); rations=new RationDebits(this); supplyIntents=new RationSupplyIntents(this); supplyTransfers=new RationSupplyTransfers(this); }
        public IThingHolder ParentHolder { get { return null; } }
        public ThingOwner GetDirectlyHeldThings() { return pawnCustody == null ? null : pawnCustody.Container; }
        public void GetChildHolders(List<IThingHolder> children) { if (pawnCustody != null) ThingOwnerUtility.AppendThingHoldersFromThings(children,pawnCustody.Container); if (rations != null) children.Add(rations); }
        public static void RegisterWorldBootstrap(Func<string> chooseNewWorldMode, Action<CoreWorldComponent,CoreLedger> bootstrap, Func<CoreWorldComponent,string> saveSnapshot, Action<CoreWorldComponent> update = null)
        {
            if (chooseNewWorldMode == null || bootstrap == null || saveSnapshot == null || bootstrapWorld != null) throw new InvalidOperationException("Invalid or duplicate World lifecycle registration");
            chooseMode = chooseNewWorldMode; bootstrapWorld = bootstrap; saveWorld = saveSnapshot; updateWorld = update;
        }
        internal bool AllowsWrite(CoreLedger candidate, string actor)
        { return ledger == candidate && blockReason == null && ((worldValidated && modulesValidated) || (bootstrapping && actor == bootstrapActor)); }
        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            worldWasLoaded = worldWasLoaded || fromLoad;
            // World finalization precedes map pawn cross-reference resolution.
            // Loaded ledgers remain inaccessible until the whole game load completes.
            if (!fromLoad && !loaded) Initialize(false);
        }
        private void Initialize(bool fromLoad)
        {
            if (initialized) return; initialized = true;
            try
            {
                bool allowNewMode = !fromLoad;
                var modules = ModuleCatalogue.Current();
                if (header == null)
                {
                    if (savedLedger != null) throw new InvalidOperationException("Ledger exists without its save header");
                    header = new SaveHeader(); savedLedger = new LedgerState(); fromLoad = false;
                }
                bool oldTransferSchema=header.SchemaVersion < 9;
                if (oldTransferSchema && (loadedSupplyTransferNodePresent || savedLedger != null && savedLedger.operations != null &&
                    savedLedger.operations.Any(op => op != null && op.cause != null &&
                        (op.cause.StartsWith("supply-transfer-arm/",StringComparison.Ordinal) || op.cause.StartsWith("supply-transfer-disarm/",StringComparison.Ordinal)))))
                    throw new InvalidOperationException("Legacy save unexpectedly contains supply transfer data");
                bool oldSupplySchema=header.SchemaVersion < 8;
                if (oldSupplySchema && (loadedSupplyNodePresent || savedLedger != null && savedLedger.operations != null &&
                    savedLedger.operations.Any(op => op != null && op.cause != null &&
                        (op.cause.StartsWith("supply-intent/",StringComparison.Ordinal) || op.cause.StartsWith("supply-cancel/",StringComparison.Ordinal)))))
                    throw new InvalidOperationException("Legacy save unexpectedly contains supply intent data");
                if (header.SchemaVersion == 1)
                {
                    if (savedLedger != null) throw new InvalidOperationException("Legacy header unexpectedly contains a ledger");
                    header = header.MigrateOne(modules); savedLedger = new LedgerState();
                }
                else if (header.SchemaVersion == 2)
                {
                    if (savedLedger == null) throw new InvalidOperationException("Schema two ledger is missing");
                    CoreLedger.Validate(header,savedLedger,modules);
                    header = header.MigrateTwo(modules);
                }
                else if (header.SchemaVersion == 3)
                {
                    if (savedLedger == null) throw new InvalidOperationException("Schema three ledger is missing");
                    CoreLedger.Validate(header,savedLedger,modules);
                    header = header.MigrateThree(modules);
                }
                else if (header.SchemaVersion == 4)
                {
                    if (savedLedger == null) throw new InvalidOperationException("Schema four ledger is missing");
                    // Check payload-only owners against the original saved stamps before
                    // migration replaces producer metadata with today's module catalogue.
                    ValidateModulePayloads(modules);
                    CoreLedger.Validate(header,savedLedger,modules);
                    header=header.MigrateFour(modules);
                }
                else if (header.SchemaVersion == 5)
                {
                    if (savedLedger == null || pawnCustody == null) throw new InvalidOperationException("Schema five Core state is missing");
                    // Validate all original certificates and module ownership before refreshing stamps.
                    ValidateModulePayloads(modules);
                    CoreLedger.Validate(header,savedLedger,modules);
                    pawnCustody.Validate(new CoreLedger(header,savedLedger,modules),header);
                    header=header.MigrateFive(modules);
                }
                else if (header.SchemaVersion == 6)
                {
                    if (savedLedger == null || pawnCustody == null || rations == null) throw new InvalidOperationException("Schema six Core state is missing");
                    ValidateModulePayloads(modules);
                    CoreLedger.Validate(header,savedLedger,modules);
                    var original=new CoreLedger(header,savedLedger,modules);
                    pawnCustody.Validate(original,header); rations.Validate(original,header);
                    header=header.MigrateSix(modules);
                }
                else if (header.SchemaVersion == 7)
                {
                    if (savedLedger == null || pawnCustody == null || rations == null) throw new InvalidOperationException("Schema seven Core state is missing");
                    ValidateModulePayloads(modules);
                    CoreLedger.Validate(header,savedLedger,modules);
                    var original=new CoreLedger(header,savedLedger,modules);
                    pawnCustody.Validate(original,header); rations.Validate(original,header);
                    header=header.MigrateSeven(modules);
                }
                else if (header.SchemaVersion == 8)
                {
                    if (savedLedger == null || pawnCustody == null || rations == null || supplyIntents == null) throw new InvalidOperationException("Schema eight Core state is missing");
                    ValidateModulePayloads(modules);
                    CoreLedger.Validate(header,savedLedger,modules);
                    var original=new CoreLedger(header,savedLedger,modules);
                    pawnCustody.Validate(original,header); rations.Validate(original,header);
                    supplyIntents.Validate(original,header);
                    header=header.MigrateEight(modules);
                }
                else if (!header.IsCompatible) throw new InvalidOperationException("Unsupported save schema " + header.SchemaVersion);
                else
                {
                    header.EnsureInitialized();
                    if (savedLedger == null) throw new InvalidOperationException("Schema two ledger is missing");
                    if (!fromLoad || !loaded) header.UpdateProducer(modules);
                }
                if (oldSupplySchema) supplyIntents=new RationSupplyIntents(this);
                if (oldTransferSchema) supplyTransfers=new RationSupplyTransfers(this);
                if (!fromLoad || !loaded) header.ChooseMode(!allowNewMode || chooseMode == null ? "vanilla" : chooseMode());
                if (header.WorldMode == "six-nations") modules.Require("inheritance.world");
                else if (!string.IsNullOrEmpty(worldPayload)) throw new InvalidOperationException("Vanilla mode unexpectedly contains World data");
                ValidateModulePayloads(modules);
                CoreLedger.Validate(header, savedLedger, modules);
                var candidate = new CoreLedger(header, savedLedger, modules);
                if (pawnCustody == null) throw new InvalidOperationException("Core pawn custody is missing");
                pawnCustody.Validate(candidate,header);
                if (rations == null) throw new InvalidOperationException("Core ration journal is missing");
                rations.Validate(candidate,header);
                if (supplyIntents == null) throw new InvalidOperationException("Core supply intent journal is missing");
                candidate.Recover();
                supplyIntents.Restore(candidate,header); supplyIntents.Validate(candidate,header);
                if (supplyTransfers == null) throw new InvalidOperationException("Core supply transfer journal is missing");
                supplyTransfers.Restore(candidate,header); supplyTransfers.Validate(candidate,header);
                header.UpdateProducer(modules);
                ledger = candidate; savedLedger = candidate.SavedState;
                worldValidated = header.WorldMode == "vanilla";
            }
            catch (Exception e) { blockReason = e.Message; ledger = null; }
        }
        public override void WorldComponentUpdate() { CheckCompatibilityAfterLoad(); }
        internal void CheckCompatibilityAfterLoad()
        {
            if (LongEventHandler.AnyEventNowOrWaiting || !GenScene.InPlayScene) return;
            if (!initialized) Initialize(loaded || worldWasLoaded);
            if (ledger != null && blockReason == null && !worldValidated)
            {
                try
                {
                    if (bootstrapWorld == null) throw new InvalidOperationException("Required World lifecycle is missing");
                    bootstrapActor = "inheritance.world"; bootstrapping = true; bootstrapWorld(this,ledger); worldValidated = true;
                }
                catch (Exception e) { blockReason = "World bootstrap failed: " + e.Message; }
                finally { bootstrapping = false; bootstrapActor = null; }
            }
            if (ledger != null && blockReason == null && worldValidated && !modulesValidated)
            {
                try { RestoreModules(); }
                catch (Exception e) { blockReason = "Module bootstrap failed: " + e.Message; }
            }
            if (blockReason != null) RejectIncompatibleWorld();
            else if (IsReady)
            {
                try { if (updateWorld != null) updateWorld(this); UpdateModules(); }
                catch (Exception e) { blockReason = "Module update failed: " + e.Message; RejectIncompatibleWorld(); }
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                loaded = true; ledger = null; initialized = false; worldValidated = false; modulesValidated = false;
                supplyIntents = null; supplyTransfers = null;
                loadedSupplyTransferNodePresent = Scribe.loader.curXmlParent.SelectSingleNode("inheritanceRationSupplyTransfers") != null;
                loadedSupplyNodePresent = Scribe.loader.curXmlParent.SelectSingleNode("inheritanceRationSupplyIntents") != null;
            }
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (!IsReady) throw new InvalidOperationException("Cannot save an incompatible Inheritance world: " + blockReason);
                if (RationMutation) throw new InvalidOperationException("Cannot save during a native ration mutation");
                savedLedger = ledger.SavedState;
                pawnCustody.SynchronizeCounts(); pawnCustody.Validate(ledger,header);
                rations.SynchronizeCounts(); rations.Validate(ledger,header);
                if (supplyIntents == null || supplyIntents.IsWriting) throw new InvalidOperationException("Cannot save while supply intent metadata is unavailable or writing");
                supplyIntents.SynchronizeCounts(); supplyIntents.Validate(ledger,header);
                if (supplyTransfers == null || supplyTransfers.IsWriting) throw new InvalidOperationException("Cannot save while supply transfer metadata is unavailable or writing");
                supplyTransfers.SynchronizeCounts(); supplyTransfers.Validate(ledger,header);
                SaveModules();
                if (header.WorldMode == "six-nations")
                {
                    if (saveWorld == null) throw new InvalidOperationException("World save adapter is missing");
                    worldPayload = saveWorld(this);
                    if (string.IsNullOrEmpty(worldPayload)) throw new InvalidOperationException("World save snapshot is missing");
                }
            }
            Scribe_Deep.Look(ref header, "inheritanceHeader");
            Scribe_Deep.Look(ref savedLedger, "inheritanceLedger");
            // A Core-owned string remains readable even when the optional World assembly is absent.
            Scribe_Values.Look(ref worldPayload,"inheritanceWorldPayload");
            ExposeModulePayloads();
            if (header != null && header.SchemaVersion >= 5) Scribe_Deep.Look(ref pawnCustody,"inheritancePawnCustody",this);
            if (header != null && header.SchemaVersion >= 7) Scribe_Deep.Look(ref rations,"inheritanceRationDebits",this);
            if (header != null && header.SchemaVersion >= 8) Scribe_Deep.Look(ref supplyIntents,"inheritanceRationSupplyIntents",this);
            if (header != null && header.SchemaVersion >= 9) Scribe_Deep.Look(ref supplyTransfers,"inheritanceRationSupplyTransfers",this);
        }
        private void RejectIncompatibleWorld()
        {
            if (incompatibleWorldQueued) return; incompatibleWorldQueued = true;
            Find.TickManager.Pause();
            LongEventHandler.QueueLongEvent(delegate
            {
                Log.Error("[Inheritance] " + blockReason + "; returning to the main menu. Original save is not modified.");
                GenScene.GoToMainMenu();
            }, null, false, null, false);
        }
    }
    public sealed class CompatibilityGuard : GameComponent
    {
        public CompatibilityGuard(Game game) { }
        public override void GameComponentUpdate()
        {
            if (Find.World == null) return;
            var core = Find.World.GetComponent<CoreWorldComponent>();
            if (core != null) core.CheckCompatibilityAfterLoad();
        }
    }
}
