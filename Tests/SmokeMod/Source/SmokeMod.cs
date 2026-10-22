using System;
using System.IO;
using System.Linq;
using Inheritance.Core;
using UnityEngine;
using Verse;
using System.Xml;
using System.Reflection;

namespace Inheritance.Smoke
{
    public sealed class SmokeMod : Mod
    {
        public SmokeMod(ModContentPack content) : base(content)
        {
            if (GenCommandLine.CommandLineArgPassed("inh-core-custody"))
            {
                Application.logMessageReceived += delegate(string message,string stack,LogType type)
                { if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu")) { Log.Error("[INH-CORE-CUSTODY] FAIL unexpected load rejection: "+message); Application.Quit(1); } };
                if (!GenCommandLine.CommandLineArgPassed("inh-core-custody-prepare")) LongEventHandler.ExecuteWhenFinished(delegate
                {
                    string save=GenCommandLine.CommandLineArgPassed("inh-core-custody-apply") ? "INH_CoreCustodyPrepared" :
                        GenCommandLine.CommandLineArgPassed("inh-core-custody-commit") ? "INH_CoreCustodyMoved" :
                        GenCommandLine.CommandLineArgPassed("inh-core-custody-release-prepare") ? "INH_CoreCustodyStored" :
                        GenCommandLine.CommandLineArgPassed("inh-core-custody-release") ? "INH_CoreCustodyReleasePrepared" :
                        GenCommandLine.CommandLineArgPassed("inh-core-custody-reload") ? "INH_CoreCustodyFinal" : "INH_CoreCustodyReleased";
                    GameDataSaveLoader.CheckVersionAndLoadGame(save);
                });
            }
            if (GenCommandLine.CommandLineArgPassed("inh-smoke"))
                LongEventHandler.ExecuteWhenFinished(Run);
            if (GenCommandLine.CommandLineArgPassed("inh-world-load") ||
                GenCommandLine.CommandLineArgPassed("inh-future-load") || GenCommandLine.CommandLineArgPassed("inh-completed-load") ||
                GenCommandLine.CommandLineArgPassed("inh-legacy-load") || GenCommandLine.CommandLineArgPassed("inh-reject-load") ||
                GenCommandLine.CommandLineArgPassed("inh-legacy-reload") || GenCommandLine.CommandLineArgPassed("inh-reconcile-load") ||
                GenCommandLine.CommandLineArgPassed("inh-schema-two-load") || GenCommandLine.CommandLineArgPassed("inh-required-world-load") ||
                GenCommandLine.CommandLineArgPassed("inh-schema-four-payload-valid"))
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    bool future = GenCommandLine.CommandLineArgPassed("inh-future-load");
                    bool reject = GenCommandLine.CommandLineArgPassed("inh-reject-load");
                    bool required = GenCommandLine.CommandLineArgPassed("inh-required-world-load");
                    if (future || reject || required)
                    {
                        Application.logMessageReceived += MenuProbe.ObserveLog;
                        var probe = new GameObject("InheritanceFutureSaveProbe");
                        UnityEngine.Object.DontDestroyOnLoad(probe);
                        probe.AddComponent<MenuProbe>();
                    }
                    string save = future ? "INH_FutureSmoke" : reject && GenCommandLine.CommandLineArgPassed("inh-supply-transfer-reject") ? "INH_SupplyTransferReject" : reject && GenCommandLine.CommandLineArgPassed("inh-supply-intent-reject") ? "INH_SupplyIntentReject" : reject || required ? "INH_RejectSmoke" :
                        GenCommandLine.CommandLineArgPassed("inh-schema-four-payload-valid") ? "INH_SchemaFourPayloadSmoke" :
                        GenCommandLine.CommandLineArgPassed("inh-schema-two-load") ? "INH_SchemaTwoSmoke" :
                        GenCommandLine.CommandLineArgPassed("inh-reconcile-load") ? "INH_ReconcileSmoke" :
                        GenCommandLine.CommandLineArgPassed("inh-legacy-reload") ? "INH_LegacyMigratedSmoke" :
                        GenCommandLine.CommandLineArgPassed("inh-legacy-load") ? "INH_LegacySmoke" :
                        GenCommandLine.CommandLineArgPassed("inh-completed-load") ? "INH_CompletedSmoke" : "INH_WorldSmoke";
                    // Controlled synthetic fixtures add an active optional module after
                    // the original game metadata was written; skip only that native dialog.
                    if (required || GenCommandLine.CommandLineArgPassed("inh-direct-test-load")) GameDataSaveLoader.LoadGame(save);
                    else GameDataSaveLoader.CheckVersionAndLoadGame(save);
                });
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Log.Message("[INH-SMOKE] PASS " + message);
        }
        private static void Run()
        {
            try
            {
                bool fourDlc = GenCommandLine.CommandLineArgPassed("inh-four-dlc");
                Require(ModsConfig.RoyaltyActive == fourDlc && ModsConfig.IdeologyActive == fourDlc &&
                    ModsConfig.BiotechActive == fourDlc && ModsConfig.OdysseyActive == fourDlc,
                    fourDlc ? "All four target DLC are active" : "Core works with no DLC active");
                Require(!ModsConfig.AnomalyActive, "Anomaly is inactive");
                var module = DefDatabase<ModuleDef>.GetNamed("INH_Core");
                Require(module.packageId == "inheritance.core" && module.protocolVersion == 2 && module.moduleSchemaVersion == SaveHeader.CurrentSchemaVersion, "Core XML resolved");
                Require(LoadedModManager.GetMod<CoreMod>() != null, "Core Mod class initialized");
                if (GenCommandLine.CommandLineArgPassed("inh-har-check"))
                {
                    Require(ModsConfig.IsActive("erdelf.HumanoidAlienRaces"), "HAR package is active in the isolated compatibility configuration");
                    var alienType=Type.GetType("AlienRace.ThingDef_AlienRace, AlienRace",false);
                    Require(alienType != null && typeof(ThingDef).IsAssignableFrom(alienType), "The actual loaded HAR assembly exposes its alien race ThingDef type");
                    Log.Message("[INH-HAR] Loaded assembly="+alienType.Assembly.FullName+"; framework initialization only, six species are not implemented");
                }
                var path = Path.Combine(GenFilePaths.SaveDataFolderPath, "InheritanceHeaderTest.xml");
                var first = new SaveHeader(); first.EnsureInitialized();
                string id = first.SaveScopeId;
                Scribe.saver.InitSaving(path, "inheritanceSmoke");
                Scribe_Deep.Look(ref first, "header");
                Scribe.saver.FinalizeSaving();
                var savedXml = new XmlDocument(); savedXml.Load(path);
                Require(savedXml.SelectSingleNode("/inheritanceSmoke/header/schemaVersion").InnerText == SaveHeader.CurrentSchemaVersion.ToString(), "Schema version written explicitly");
                SaveHeader second = null;
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref second, "header");
                Scribe.loader.FinalizeLoading();
                Require(second != null && second.SaveScopeId == id && second.SchemaVersion == SaveHeader.CurrentSchemaVersion, "Header Scribe round trip");
                second.EnsureInitialized();
                Require(second.SaveScopeId == id, "Header initialization is idempotent");
                // Historical fixture contains only fields that existed in P01.
                savedXml.LoadXml("<inheritanceSmoke><header><saveScopeId>" + id + "</saveScopeId></header></inheritanceSmoke>");
                savedXml.Save(path);
                SaveHeader legacy = null;
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref legacy, "header");
                Scribe.loader.FinalizeLoading();
                Require(legacy.SchemaVersion == 1 && legacy.SaveScopeId == id, "Unversioned prototype stays schema one");
                var versionNode = savedXml.CreateElement("schemaVersion"); versionNode.InnerText = "99";
                savedXml.SelectSingleNode("/inheritanceSmoke/header").AppendChild(versionNode);
                savedXml.Save(path);
                SaveHeader future = null;
                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref future, "header");
                Scribe.loader.FinalizeLoading();
                Require(!future.IsCompatible, "Future schema recognized as incompatible");
                bool refused = false;
                try { future.EnsureInitialized(); } catch (InvalidOperationException) { refused = true; }
                Require(refused, "Future header cannot be initialized for writing");
                Log.Message("[INH-SMOKE] ALL PASS");
                Application.Quit(0);
            }
            catch (Exception e)
            {
                Scribe.ForceStop();
                Log.Error("[INH-SMOKE] FAIL " + e);
                Application.Quit(1);
            }
        }
    }

    public sealed class MenuProbe : MonoBehaviour
    {
        private static bool rejectionObserved;
        private float lastDiagnostic;
        public static void ObserveLog(string message, string stackTrace, LogType type)
        {
            if (message.Contains("[Inheritance]") && message.Contains("returning to the main menu. Original save is not modified.")) rejectionObserved = true;
        }
        public void Update()
        {
            if (Time.realtimeSinceStartup - lastDiagnostic > 15f)
            {
                lastDiagnostic = Time.realtimeSinceStartup;
                var core = Find.World == null ? null : Find.World.GetComponent<CoreWorldComponent>();
                Log.Message("[INH-FUTURE] STATE play=" + GenScene.InPlayScene + ", entry=" + GenScene.InEntryScene +
                    ", longEvent=" + LongEventHandler.AnyEventNowOrWaiting + ", game=" + (Current.Game != null) +
                    ", schema=" + (core == null ? -1 : core.Header.SchemaVersion) +
                    ", guard=" + (Current.Game != null && Current.Game.GetComponent<CompatibilityGuard>() != null));
                var eventField = typeof(LongEventHandler).GetField("currentEvent", BindingFlags.NonPublic | BindingFlags.Static);
                var active = eventField.GetValue(null);
                if (active != null)
                    foreach (var f in active.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        Log.Message("[INH-FUTURE] EVENT " + f.Name + "=" + f.GetValue(active));
            }
            if (!rejectionObserved || !GenScene.InEntryScene || LongEventHandler.AnyEventNowOrWaiting || Current.Game != null) return;
            Log.Message("[INH-FUTURE] MAIN MENU");
            Log.Message("[INH-SMOKE] ALL PASS");
            Application.Quit(0);
        }
    }

    public sealed class WorldSmokeComponent : GameComponent
    {
        private bool done;
        public WorldSmokeComponent(Game game) { }
        public override void GameComponentUpdate()
        {
            if (done || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return;
            bool save = GenCommandLine.CommandLineArgPassed("inh-world-save");
            bool load = GenCommandLine.CommandLineArgPassed("inh-world-load");
            bool completed = GenCommandLine.CommandLineArgPassed("inh-completed-load");
            bool legacy = GenCommandLine.CommandLineArgPassed("inh-legacy-load") || GenCommandLine.CommandLineArgPassed("inh-legacy-reload");
            bool reconcile = GenCommandLine.CommandLineArgPassed("inh-reconcile-load");
            bool schemaTwo = GenCommandLine.CommandLineArgPassed("inh-schema-two-load");
            if (!save && !load && !completed && !legacy && !reconcile && !schemaTwo) return;
            var pendingCore = Find.World.GetComponent<CoreWorldComponent>();
            if (pendingCore != null && !pendingCore.IsReady && pendingCore.BlockReason == null) return;
            done = true;
            try
            {
                var component = Find.World.GetComponent<CoreWorldComponent>();
                if (component == null || !component.IsReady)
                    throw new InvalidOperationException("World component was not initialized: " + (component == null ? "absent" : component.BlockReason));
                string idPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "expected-world-id.txt");
                if (save)
                {
                    File.WriteAllText(idPath, component.Header.SaveScopeId);
                    LedgerChecks.Populate(component);
                    GameDataSaveLoader.SaveGame("INH_WorldSmoke");
                    var xml = new XmlDocument(); xml.Load(GenFilePaths.FilePathForSavedGame("INH_WorldSmoke"));
                    var node = xml.SelectSingleNode("//li[@Class='Inheritance.Core.CoreWorldComponent']/inheritanceHeader/saveScopeId");
                    if (node == null || node.InnerText != component.Header.SaveScopeId)
                        throw new InvalidOperationException("World header missing from the game save");
                    Log.Message("[INH-WORLD] PASS new world component and full game save");
                }
                else if (legacy)
                {
                    LedgerChecks.Require(component.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion && component.Header.SaveScopeId == File.ReadAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"expected-legacy-id.txt")) &&
                        component.Ledger.EntityCount == 0 && component.Header.NextSequence == 1 && component.Header.Migrations.SequenceEqual(new[] {"1->2:empty-ledger","2->3:world-bootstrap-gate","3->4:module-payloads","4->5:core-pawn-custody","5->6:caravan-pawn-custody","6->7:native-ration-debits","7->8:non-reserving-supply-intents","8->9:armed-supply-transfers"}), "P01 migration preserves scope and creates no assets");
                    GameDataSaveLoader.SaveGame("INH_LegacyMigratedSmoke");
                    Log.Message("[INH-WORLD] PASS schema one to two actual game migration");
                }
                else if (reconcile) LedgerChecks.VerifyReconciliation(component);
                else
                {
                    if (File.ReadAllText(idPath) != component.Header.SaveScopeId)
                        throw new InvalidOperationException("World identifier changed after loading");
                    Log.Message("[INH-WORLD] PASS full game reload preserved world identifier");
                    if (completed || schemaTwo) LedgerChecks.VerifyCompleted(component);
                    else { LedgerChecks.Resume(component); GameDataSaveLoader.SaveGame("INH_CompletedSmoke"); }
                    if (schemaTwo)
                    {
                        LedgerChecks.Require(component.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion && component.Header.WorldMode == "vanilla" && component.Header.Migrations.Contains("2->3:world-bootstrap-gate"),"Schema two ledger migrates intact and stays vanilla");
                        GameDataSaveLoader.SaveGame("INH_SchemaTwoMigratedSmoke");
                    }
                }
                Log.Message("[INH-SMOKE] ALL PASS");
                Application.Quit(0);
            }
            catch (Exception e) { Log.Error("[INH-WORLD] FAIL " + e); Application.Quit(1); }
        }
    }
}
