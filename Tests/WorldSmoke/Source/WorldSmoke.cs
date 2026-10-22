using System;
using System.IO;
using System.Linq;
using Inheritance.Core;
using Inheritance.World;
using UnityEngine;
using Verse;

namespace Inheritance.WorldSmoke
{
    public sealed class SmokeMod : Mod
    {
        public SmokeMod(ModContentPack content) : base(content)
        {
            if (GenCommandLine.CommandLineArgPassed("inh-world-prototype")) Application.logMessageReceived += delegate(string message, string stack, LogType type)
            {
                if (message.StartsWith("[Inheritance]") && message.Contains("returning to the main menu"))
                { Log.Error("[INH-P03] FAIL unexpected load rejection: " + message); Application.Quit(1); }
            };
            string save = GenCommandLine.CommandLineArgPassed("inh-prototype-load") ? "INH_PrototypeSmoke" :
                GenCommandLine.CommandLineArgPassed("inh-prototype-completed-load") ? "INH_PrototypeCompletedSmoke" :
                GenCommandLine.CommandLineArgPassed("inh-plan-resume") ? "INH_PlanCheckpointSmoke" :
                GenCommandLine.CommandLineArgPassed("inh-vanilla-attach") ? "INH_VanillaAttachSmoke" : null;
            if (save != null) LongEventHandler.ExecuteWhenFinished(delegate
            {
                // Added-mod warning dialogs require the interactive menu. This fixture tests the accepted-load path.
                if (GenCommandLine.CommandLineArgPassed("inh-vanilla-attach")) GameDataSaveLoader.LoadGame(save);
                else GameDataSaveLoader.CheckVersionAndLoadGame(save);
            });
        }
    }
    public sealed class SmokeComponent : GameComponent
    {
        private bool done;
        public SmokeComponent(Game game) { }
        private static string Expected(string name) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"p03-" + name + ".txt"); }
        private static void Assert(bool value, string text)
        { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-P03] PASS " + text); }
        private static void Refuses(Action action, string text)
        { bool refused = false; try { action(); } catch (InvalidOperationException) { refused = true; } Assert(refused,text); }
        public override void GameComponentUpdate()
        {
            if (done || LongEventHandler.AnyEventNowOrWaiting || Find.CurrentMap == null) return;
            bool save = GenCommandLine.CommandLineArgPassed("inh-prototype-save"), load = GenCommandLine.CommandLineArgPassed("inh-prototype-load"),
                completed = GenCommandLine.CommandLineArgPassed("inh-prototype-completed-load"), resume = GenCommandLine.CommandLineArgPassed("inh-plan-resume"), attach = GenCommandLine.CommandLineArgPassed("inh-vanilla-attach");
            if (!save && !load && !completed && !resume && !attach) return;
            var core = Find.World.GetComponent<CoreWorldComponent>();
            if (!core.IsReady && core.BlockReason == null) return;
            done = true;
            try
            {
                Assert(core.IsReady,"Core validation and World bootstrap gates completed");
                var world = WorldMod.CurrentWorld;
                if (attach)
                {
                    Assert(core.Header.WorldMode == "vanilla" && core.Ledger.EntityCount == 0 && world == null,"Adding Core and enabled World to an existing vanilla save does not activate six nations");
                    GameDataSaveLoader.SaveGame("INH_VanillaAttachedSmoke");
                }
                else
                {
                    Assert(core.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion && core.Header.WorldMode == "six-nations" && world.IsReady,"New world explicitly activates six-nation mode");
                    var nations = world.Nations.ToArray(); var cities = world.Cities.ToArray();
                    Assert(nations.Length == 6 && cities.Length == 24 && cities.Select(c => c.Tile).Distinct().Count() == 24,"Six nations have 24 distinct land cities");
                    Assert(cities.All(c => !Find.World.grid.Surface[c.Tile].WaterCovered && Find.World.grid.Surface[c.Tile].PrimaryBiome.canBuildBase),"All cities use buildable surface land");
                    Assert(world.PlanFingerprint == world.RecomputePlanFingerprint(),"Same seed and terrain reproduce the complete placement plan");
                    string identifiers = string.Join("\n",nations.Select(n => n.Id).Concat(cities.Select(c => c.Id)).OrderBy(x => x,StringComparer.Ordinal).ToArray());
                    if (save)
                    {
                        Assert(nations.Select(n => n.InitialBudget).Distinct().Single() == 100 && nations.All(n => n.CityIds.Length == 4),"Six nations begin with equal budget and one city of each tier");
                        Assert(nations.All(n => cities.Single(c => c.Id == n.CapitalCityId).Level == 4) && nations.All(n => !n.TemporaryCapital),"Each initial capital is a separate L4 political role");
                        foreach (var n in nations) Assert(cities.Where(c => c.ControllerId == n.Id).Select(c => c.Level).OrderBy(x => x).SequenceEqual(new[] {1,2,3,4}),"Four tiers present for " + n.CultureKey);
                        File.WriteAllText(Expected("ids"),identifiers); File.WriteAllText(Expected("plan"),world.PlanFingerprint);
                        File.WriteAllText(Expected("scope"),core.Header.SaveScopeId); File.WriteAllText(Expected("capital"),nations[0].CapitalCityId);
                        GameDataSaveLoader.SaveGame("INH_PlanReadySmoke");
                        string op = world.PrepareCapture(nations[0].CapitalCityId,nations[1].Id,world.StartTick+60000,"p03-capture-test"); core.Ledger.BeginApplying(op);
                        File.WriteAllText(Expected("capture"),op);
                        Assert(world.Income.Count() == 24 && nations[0].Id == cities.Single(c => c.Id == nations[0].CapitalCityId).ControllerId,"Capture checkpoint saves before changing administration");
                        Refuses(delegate { world.SettleIncomeTo(world.StartTick+120000); },"Pending capture holds income settlement");
                        GameDataSaveLoader.SaveGame("INH_PrototypeSmoke");
                    }
                    else
                    {
                        Assert(identifiers == File.ReadAllText(Expected("ids")) && world.PlanFingerprint == File.ReadAllText(Expected("plan")) && core.Header.SaveScopeId == File.ReadAllText(Expected("scope")),"Nation/city IDs and placement survive loading or generation resumption");
                        if (resume) Assert(world.GenerationStage == WorldGenerationStage.Ready && core.Ledger.Entities.Count(e => e.Provider == "inheritance.world" && e.Status == EntityStatus.Active && (e.Kind == "city" || e.Kind == "polity")) == 30,"Prepared final generation operation resumes once with the original result ID");
                        else
                        {
                            string capital = File.ReadAllText(Expected("capital")); var captured = cities.Single(c => c.Id == capital);
                            Assert(captured.ControllerId == nations[1].Id && captured.OwnerId == nations[0].Id && captured.CultureKey == nations[0].CultureKey,"Captured city changes manager while ownership and culture persist");
                            Assert(nations[0].TemporaryCapital && cities.Single(c => c.Id == nations[0].CapitalCityId).Level == 3 && nations[1].CapitalCityId != captured.Id,"Defeated state relocates to L3 and conqueror keeps its own capital");
                            world.Capture(capital,nations[1].Id,world.StartTick+60000,"p03-capture-test");
                            Refuses(delegate { world.Capture(capital,nations[2].Id,world.StartTick+60000,"p03-capture-test"); },"Capture cause cannot be reused for a different conqueror");
                            world.SettleIncomeTo(world.StartTick+120000); int receipts = world.Income.Count(); world.SettleIncomeTo(world.StartTick+120000);
                            var after = world.Nations.ToArray();
                            Assert(after[0].AbstractIncomeTicks == 44*60000L && after[1].AbstractIncomeTicks == 76*60000L && after.Skip(2).All(n => n.AbstractIncomeTicks == 60*60000L),"Two-day income is split at capture boundary between the actual managers");
                            Assert(world.Income.Count() == receipts && after.Sum(n => n.AbstractIncomeTicks) == 360*60000L && core.Ledger.GetEntity(capital).ControllerChangeCount == 1,"Repeated capture and settlement do not duplicate administration or income");
                            if (!completed) GameDataSaveLoader.SaveGame("INH_PrototypeCompletedSmoke");
                        }
                    }
                }
                Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception e) { Log.Error("[INH-P03] FAIL " + e); Application.Quit(1); }
        }
    }
}
