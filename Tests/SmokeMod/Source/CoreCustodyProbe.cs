using System;
using System.IO;
using System.Linq;
using Inheritance.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace Inheritance.Smoke
{
    public sealed class CoreCustodyProbe : GameComponent
    {
        private bool done;
        public CoreCustodyProbe(Game game) { }
        public override void FinalizeInit() { if (GenCommandLine.CommandLineArgPassed("inh-core-custody") || GenCommandLine.CommandLineArgPassed("inh-schema-four-payload-valid")) Find.TickManager.Pause(); }
        private static string PathFor(string key) { return Path.Combine(GenFilePaths.SaveDataFolderPath,"core-custody-"+key+".txt"); }
        private static void Assert(bool value,string text) { if (!value) throw new InvalidOperationException(text); Log.Message("[INH-CORE-CUSTODY] PASS "+text); }
        private static void Refuse(Action action,string text) { bool refused=false; try { action(); } catch (InvalidOperationException) { refused=true; } Assert(refused,text); }
        private static string Create(CoreLedger ledger,EntitySpec spec,string cause)
        { string op=ledger.NewOperationId("inheritance.core"); var r=ledger.PrepareCreate(op,cause,spec); ledger.Commit(op); return r.ResultId; }
        private static string State(Pawn p)
        {
            return string.Join("|",new[] {p.GetUniqueLoadID(),p.def.defName,p.Name.ToStringFull,p.gender.ToString(),p.Faction.GetUniqueLoadID(),p.ageTracker.AgeBiologicalTicks.ToString(),
                p.skills == null ? "" : string.Join(",",p.skills.skills.Select(s => s.def.defName+":"+s.Level+":"+s.xpSinceLastLevel.ToString("R")).ToArray()),
                string.Join(",",p.health.hediffSet.hediffs.Select(h => h.def.defName+":"+h.Severity.ToString("R")+":"+(h.Part == null ? "" : h.Part.def.defName)).ToArray()),
                p.genes == null ? "" : "endo:"+string.Join(",",p.genes.Endogenes.Select(g => g.def.defName+":"+g.Active).ToArray())+";xeno:"+string.Join(",",p.genes.Xenogenes.Select(g => g.def.defName+":"+g.Active).ToArray()),p.Ideo == null ? "" : p.Ideo.GetUniqueLoadID(),
                p.apparel == null ? "" : string.Join(",",p.apparel.WornApparel.Select(t => t.GetUniqueLoadID()).ToArray()),p.equipment == null ? "" : string.Join(",",p.equipment.AllEquipmentListForReading.Select(t => t.GetUniqueLoadID()).ToArray()),
                string.Join(",",p.inventory.innerContainer.Select(t => t.GetUniqueLoadID()+":"+t.def.defName+":"+t.stackCount+":"+t.HitPoints).OrderBy(s => s,StringComparer.Ordinal).ToArray())});
        }
        private static bool Same(Pawn p,string expected)
        { var a=State(p).Split('|'); var b=expected.Split('|'); return a.Where((s,i) => i != 5).SequenceEqual(b.Where((s,i) => i != 5)) && long.Parse(a[5])-long.Parse(b[5]) >= 0 && long.Parse(a[5])-long.Parse(b[5]) <= 3; }
        public override void GameComponentUpdate()
        {
            bool schemaFourPayload=GenCommandLine.CommandLineArgPassed("inh-schema-four-payload-valid");
            if (done || !(GenCommandLine.CommandLineArgPassed("inh-core-custody") || schemaFourPayload) || Find.CurrentMap == null || LongEventHandler.AnyEventNowOrWaiting) return;
            var core=Find.World.GetComponent<CoreWorldComponent>(); if (!core.IsReady && core.BlockReason == null) return; done=true;
            try
            {
                if (schemaFourPayload)
                {
                    var expected=File.ReadAllLines(Path.Combine(GenFilePaths.SaveDataFolderPath,"schema-four-payload-expected.txt"));
                    Assert(core.IsReady && core.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion && ModsConfig.IsActive("inheritance.colony"),"Historical payload-only Colony world migrates with its saved module stamp");
                    Assert(core.Header.SaveScopeId == expected[0] && core.Header.NextSequence.ToString() == expected[1] && core.Ledger.EntityCount.ToString() == expected[2] && core.Ledger.OperationCount.ToString() == expected[3],"Payload-only migration preserves world scope, allocator and ledger counts");
                    Assert(core.Ledger.Entities.OrderBy(e => e.Id,StringComparer.Ordinal).Select(e => e.Id+"|"+(e.BoundThingLoadId ?? "")).SequenceEqual(expected.Skip(4)),"Payload-only migration preserves every permanent identity and native binding");
                    Assert(core.PawnCustody.StoredCount == 0 && !core.PawnCustody.Transfers.Any() && core.Header.Migrations.Contains("4->5:core-pawn-custody") && core.Header.Migrations.Contains("5->6:caravan-pawn-custody"),"Historical migration retains an empty Core vault without allocating custody assets");
                    GameDataSaveLoader.SaveGame("INH_SchemaFourPayloadMigrated");
                    Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0); return;
                }
                Assert(core.IsReady && core.Header.SchemaVersion == SaveHeader.CurrentSchemaVersion,"Core custody is ready at the current schema without optional Colony or World");
                Assert(!ModsConfig.IsActive("inheritance.colony") && !ModsConfig.IsActive("inheritance.world") && !ModsConfig.AnomalyActive,"Native custody requires no optional gameplay module or Anomaly");
                var vault=core.PawnCustody;
                if (GenCommandLine.CommandLineArgPassed("inh-core-custody-prepare"))
                {
                    Map map=Find.CurrentMap; Pawn human=map.mapPawns.FreeColonists.First(p => !p.Downed && !p.Drafted && !p.InMentalState && !p.DevelopmentalStage.Baby());
                    Pawn animal=PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"),Faction.OfPlayer);
                    GenSpawn.Spawn(animal,map.AllCells.First(c => c.Standable(map) && c.GetThingList(map).Count == 0),map);
                    var steel=ThingMaker.MakeThing(ThingDefOf.Steel); steel.stackCount=10; Assert(animal.inventory.innerContainer.TryAdd(steel,false),"Fixture animal holds an original steel stack");
                    human.skills.GetSkill(SkillDefOf.Crafting).xpSinceLastLevel=123.25f;
                    foreach (Pawn p in new[] {human,animal})
                    { var injury=HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Scratch"),p,p.RaceProps.body.corePart); injury.Severity=0.5f; p.health.AddHediff(injury); }
                    Assert(human.health.hediffSet.hediffs.Any(h => h.def.defName == "Scratch") && animal.health.hediffSet.hediffs.Any(h => h.def.defName == "Scratch"),"Both originals have wounds and the human has nonzero skill experience to preserve");
                    if (ModsConfig.BiotechActive)
                    {
                        human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("Robust"),false); human.genes.AddGene(DefDatabase<GeneDef>.GetNamed("DarkVision"),true);
                        Assert(human.genes.Endogenes.Any(g => g.def.defName == "Robust") && human.genes.Xenogenes.Any(g => g.def.defName == "DarkVision"),"Biotech fixture contains a functional endogene and xenogene");
                    }
                    string city=Create(core.Ledger,new EntitySpec("city","Custody test city"),"custody-city");
                    var pawns=new[] {human,animal}; var entities=pawns.Select((p,i) => Create(core.Ledger,new EntitySpec(i == 0 ? "person" : "animal",p.LabelShort,authority:SimulationAuthority.Physical,binding:p),"custody-individual-"+i)).ToArray();
                    File.WriteAllLines(PathFor("states"),pawns.Select(State).ToArray());
                    var stores=entities.Select((e,i) => vault.PrepareStore("inheritance.core","store-"+i,e,city)).ToArray();
                    File.WriteAllLines(PathFor("ids"),new[] {core.Header.SaveScopeId,city,map.uniqueID.ToString(),entities[0],entities[1],stores[0].Id,stores[1].Id,core.Ledger.EntityCount.ToString()});
                    Assert(vault.StoredCount == 0 && pawns.All(p => p.Spawned),"Preparing custody records intent before moving any original pawn");
                    int count=core.Ledger.EntityCount;
                    Assert(vault.PrepareStore("inheritance.core","store-0",entities[0],city).Id == stores[0].Id && core.Ledger.EntityCount == count,"Repeated store intent reuses one Core certificate");
                    Refuse(() => vault.PrepareStore("inheritance.core","other-store",entities[0],city),"Overlapping intents for the same original pawn are refused");
                    Refuse(() => vault.PrepareStore("inheritance.core","store-0",entities[1],city),"A cause cannot select a different original pawn");
                    Refuse(() => vault.Commit(stores[0].Id),"Intent alone cannot certify a native transfer");
                    GameDataSaveLoader.SaveGame("INH_CoreCustodyPrepared");
                }
                else
                {
                    var ids=File.ReadAllLines(PathFor("ids")); var entities=ids.Skip(3).Take(2).ToArray(); var stores=ids.Skip(5).Take(2).ToArray();
                    Assert(core.Header.SaveScopeId == ids[0],"Core custody reload keeps the original world scope");
                    Pawn[] pawns;
                    if (GenCommandLine.CommandLineArgPassed("inh-core-custody-apply"))
                    {
                        Assert(core.Ledger.EntityCount == int.Parse(ids[7]) && vault.StoredCount == 0,"Prepared checkpoint preserves the same intent and original map pawns");
                        foreach (string id in stores) { vault.ApplyNative(id); vault.ApplyNative(id); }
                        Assert(vault.StoredCount == 2 && entities.All(e => !vault.IsStored(e)),"Native move checkpoint holds exact originals before committing residents");
                        GameDataSaveLoader.SaveGame("INH_CoreCustodyMoved");
                    }
                    else if (GenCommandLine.CommandLineArgPassed("inh-core-custody-commit"))
                    {
                        Assert(vault.StoredCount == 2,"Moved checkpoint deep-loads both original individuals");
                        foreach (string id in stores) { vault.ApplyNative(id); vault.Commit(id); vault.Commit(id); }
                        Assert(entities.All(vault.IsStored) && core.Ledger.EntityCount == int.Parse(ids[7]),"Moved checkpoint completes once without new identities or certificates");
                        GameDataSaveLoader.SaveGame("INH_CoreCustodyStored");
                    }
                    else if (GenCommandLine.CommandLineArgPassed("inh-core-custody-release-prepare"))
                    {
                        Map map=Find.Maps.Single(m => m.uniqueID.ToString() == ids[2]); var releases=new string[2]; var cells=new IntVec3[2];
                        Assert(vault.StoredCount == 2 && entities.All(vault.IsStored),"Committed storage reload retains both original individuals before release intent");
                        for (int i=0;i<2;i++)
                        {
                            IntVec3 cell=map.AllCells.First(c => c.Standable(map) && c.GetThingList(map).Count == 0 && !cells.Take(i).Contains(c)); cells[i]=cell;
                            var r=vault.PrepareRelease("inheritance.core","release-"+i,entities[i],ids[1],map,cell); releases[i]=r.Id;
                            Assert(vault.PrepareRelease("inheritance.core","release-"+i,entities[i],ids[1],map,cell).Id == r.Id,"Repeated release intent reuses its original certificate");
                            Refuse(() => vault.PrepareRelease("inheritance.core","release-"+i,entities[i],ids[1],map,cell+IntVec3.North),"Changed release destination cannot reuse the same cause");
                            Refuse(() => vault.Commit(r.Id),"Release intent alone cannot certify a native transfer");
                        }
                        File.WriteAllLines(PathFor("releases"),releases);
                        File.WriteAllLines(PathFor("release-cells"),cells.Select(c => c.x+","+c.y+","+c.z).ToArray());
                        Assert(vault.StoredCount == 2 && entities.All(vault.IsStored) && core.Ledger.EntityCount == int.Parse(ids[7])+4,"Prepared release checkpoint keeps exact originals held and allocates each intent once");
                        GameDataSaveLoader.SaveGame("INH_CoreCustodyReleasePrepared");
                    }
                    else if (GenCommandLine.CommandLineArgPassed("inh-core-custody-release"))
                    {
                        Map map=Find.Maps.Single(m => m.uniqueID.ToString() == ids[2]); var releases=File.ReadAllLines(PathFor("releases"));
                        var cells=File.ReadAllLines(PathFor("release-cells")).Select(s => s.Split(',')).Select(s => new IntVec3(int.Parse(s[0]),int.Parse(s[1]),int.Parse(s[2]))).ToArray();
                        Assert(vault.StoredCount == 2 && entities.All(vault.IsStored) && core.Ledger.EntityCount == int.Parse(ids[7])+4,"Prepared release reload preserves original residents and intent certificates");
                        for (int i=0;i<2;i++)
                        {
                            var original=vault.Transfers.Single(t => t.Id == releases[i]);
                            Assert(original.Stage == PawnCustodyStage.Prepared && original.IsRelease,"Release intent remains prepared across process restart");
                            Assert(vault.PrepareRelease("inheritance.core","release-"+i,entities[i],ids[1],map,cells[i]).Id == releases[i] && core.Ledger.EntityCount == int.Parse(ids[7])+4,"Release restart reuses the same intent without allocating certificates");
                            if (i == 0)
                            {
                                Pawn heldOriginal=core.GetDirectlyHeldThings().Cast<Pawn>().Single(p => p.GetUniqueLoadID() == original.NativeId);
                                int entityCount=core.Ledger.EntityCount;
                                Thing blocker=ThingMaker.MakeThing(ThingDefOf.Steel); blocker.stackCount=1;
                                GenSpawn.Spawn(blocker,cells[i],map);
                                try
                                {
                                    Assert(blocker.Spawned && blocker.Position == cells[i],"Release failure fixture occupies the saved destination");
                                    Refuse(() => vault.ApplyNative(releases[i]),"An occupied destination refuses the native release before moving its original");
                                    Assert(vault.StoredCount == 2 && entities.All(vault.IsStored) && !heldOriginal.Spawned && heldOriginal.holdingOwner == core.GetDirectlyHeldThings() &&
                                        core.GetDirectlyHeldThings().Cast<Pawn>().Any(p => ReferenceEquals(p,heldOriginal)),"Refused release keeps the exact original safely in Core storage");
                                    Assert(vault.Transfers.Single(t => t.Id == releases[i]).Stage == PawnCustodyStage.Prepared && core.Ledger.EntityCount == entityCount,
                                        "Refused release preserves its prepared stage and Core certificate count");
                                }
                                finally { blocker.Destroy(); }
                                Assert(cells[i].GetThingList(map).Count == 0,"Removing the test blocker restores the saved release destination");
                            }
                            vault.ApplyNative(releases[i]); vault.ApplyNative(releases[i]);
                            Assert(map.mapPawns.AllPawns.Single(p => p.GetUniqueLoadID() == core.Ledger.GetEntity(entities[i]).BoundThingLoadId).Position == cells[i],"Original individual appears at the saved release destination");
                        }
                        Assert(vault.StoredCount == 0 && entities.All(vault.IsStored),"Native release checkpoint retains resident records until verified commit");
                        GameDataSaveLoader.SaveGame("INH_CoreCustodyReleased");
                    }
                    else
                    {
                        foreach (string id in File.ReadAllLines(PathFor("releases"))) { vault.ApplyNative(id); vault.Commit(id); vault.Commit(id); }
                        Assert(vault.StoredCount == 0 && entities.All(e => !vault.IsStored(e)) && core.Ledger.EntityCount == int.Parse(ids[7])+4,"Released checkpoint certifies once without duplicating people, cargo or certificates");
                        GameDataSaveLoader.SaveGame("INH_CoreCustodyFinal");
                    }
                    pawns=entities.Select(e => core.GetDirectlyHeldThings().Cast<Pawn>().Concat(Find.Maps.SelectMany(m => m.mapPawns.AllPawns)).Single(p => p.GetUniqueLoadID() == core.Ledger.GetEntity(e).BoundThingLoadId)).ToArray();
                    Assert(pawns.Select((p,i) => Same(p,File.ReadAllLines(PathFor("states"))[i])).All(v => v),"Original names, skills, wounds, genes, ideology, equipment and cargo are unchanged");
                    Assert(pawns.All(p => !Find.WorldPawns.Contains(p)),"Stored or returned originals have no world-pawn duplicate");
                }
                Log.Message("[INH-SMOKE] ALL PASS"); Application.Quit(0);
            }
            catch (Exception e) { Log.Error("[INH-CORE-CUSTODY] FAIL "+e); Application.Quit(1); }
        }
    }
}
