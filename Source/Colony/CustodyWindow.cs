using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Inheritance.Colony
{
    public sealed class CustodyCommandController
    {
        private readonly ColonyService service;
        private readonly Dictionary<string,string> causes=new Dictionary<string,string>();
        public CustodyCommandController(ColonyService service) { this.service=service; }
        private string Cause(string key)
        {
            string cause;
            if (!causes.TryGetValue(key,out cause)) { cause="custody-ui/"+Guid.NewGuid().ToString("N"); causes.Add(key,cause); }
            return cause;
        }
        public string Store(string baseId,Pawn pawn)
        {
            if (pawn == null) throw new InvalidOperationException("A pawn is required");
            string key="store/"+baseId+"/"+pawn.GetUniqueLoadID();
            string result=service.StoreLocalPawn(Cause(key),baseId,pawn);
            causes.Remove(key); return result;
        }
        public string Release(string baseId,string entityId)
        {
            string key="release/"+baseId+"/"+entityId;
            string result=service.ReleaseLocalPawn(Cause(key),baseId,entityId);
            causes.Remove(key); return result;
        }
        public void Resume(string transferId) { service.ResumeLocalCustody(transferId); causes.Clear(); }
    }

    public sealed class CustodyWindow : Window
    {
        private readonly ColonyService service;
        private readonly CustodyCommandController controller;
        private string baseId;
        private Vector2 pawnScroll,residentScroll,pendingScroll;
        public bool Rendered { get; private set; }
        public override Vector2 InitialSize { get { return new Vector2(1040,740); } }
        public CustodyWindow(ColonyService service)
        { this.service=service; controller=new CustodyCommandController(service); doCloseX=true; forcePause=true; absorbInputAroundWindow=true; }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0,0,rect.width-30,32),"INH_ColonyCustody".Translate().ToString());
            Widgets.Label(new Rect(0,36,rect.width,54),"INH_ColonyCustodyHelp".Translate().ToString());
            Widgets.Label(new Rect(0,rect.height-56,rect.width,56),"INH_ColonyCustodyFrozenWarning".Translate().ToString());
            if (!ColonyMod.CustodyAllowed && !service.HasLocalCustody)
            { Widgets.Label(new Rect(0,100,rect.width,50),"INH_ColonyCustodyUnavailable".Translate().ToString()); return; }
            var bases=service.CustodyBases();
            if (bases.Length == 0)
            { Widgets.Label(new Rect(0,100,rect.width,50),"INH_ColonyCustodyNoBases".Translate().ToString()); return; }
            if (!bases.Any(b => b.Id == baseId)) SelectBase(bases[0].Id);
            Widgets.Label(new Rect(0,96,130,30),"INH_ColonyCustodyBase".Translate().ToString());
            if (Widgets.ButtonText(new Rect(138,94,rect.width-138,32),bases.Single(b => b.Id == baseId).Name))
                Find.WindowStack.Add(new FloatMenu(bases.Select(b => new FloatMenuOption(b.Name,delegate { SelectBase(b.Id); })).ToList()));
            try
            {
                float half=(rect.width-16)/2, panelHeight=rect.height-350;
                DrawCandidates(new Rect(0,138,half,panelHeight),service.CustodyCommandsReady ? service.AvailableCustodyPawns(baseId) : new Pawn[0]);
                DrawResidents(new Rect(half+16,138,half,panelHeight),service.CustodyResidents(baseId));
                DrawPending(new Rect(0,rect.height-196,rect.width,132),service.CustodyPending().Where(p => p.BaseId == baseId).ToArray());
            }
            catch (InvalidOperationException)
            { Widgets.Label(new Rect(0,138,rect.width,52),"INH_ColonyCustodyChanged".Translate().ToString()); }
            if (Event.current.type == EventType.Repaint) Rendered=true;
        }
        private void SelectBase(string id)
        { baseId=id; pawnScroll=Vector2.zero; residentScroll=Vector2.zero; pendingScroll=Vector2.zero; }
        private void DrawCandidates(Rect panel,Pawn[] pawns)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,28),"INH_ColonyCustodyCandidates".Translate().ToString());
            var area=new Rect(panel.x,panel.y+32,panel.width,panel.height-32);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,pawns.Length*64+50));
            Widgets.BeginScrollView(area,ref pawnScroll,content);
            if (pawns.Length == 0) Widgets.Label(new Rect(0,0,content.width,68),(service.CustodyCommandsReady ? "INH_ColonyCustodyNoCandidates" : "INH_ColonyCustodyStoreDisabled").Translate().ToString());
            float y=0;
            foreach (var pawn in pawns)
            {
                string kind=(pawn.RaceProps.Animal ? "INH_ColonyCustodyAnimal" : "INH_ColonyCustodyPerson").Translate().ToString();
                int items=PawnItems(pawn);
                Widgets.Label(new Rect(0,y,content.width-130,28),pawn.LabelShortCap);
                Widgets.Label(new Rect(0,y+28,content.width-130,28),"INH_ColonyCustodyDetails".Translate(kind,items).ToString());
                bool enabled=GUI.enabled; GUI.enabled=enabled && service.CustodyCommandsReady;
                if (Widgets.ButtonText(new Rect(content.width-124,y+10,120,32),"INH_ColonyCustodyStore".Translate().ToString()))
                    Run(delegate { controller.Store(baseId,pawn); });
                GUI.enabled=enabled;
                y+=64;
            }
            Widgets.EndScrollView();
        }
        private void DrawResidents(Rect panel,CustodyResidentSnapshot[] residents)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,28),"INH_ColonyCustodyStored".Translate().ToString());
            var area=new Rect(panel.x,panel.y+32,panel.width,panel.height-32);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,residents.Length*64+50));
            Widgets.BeginScrollView(area,ref residentScroll,content);
            if (residents.Length == 0) Widgets.Label(new Rect(0,0,content.width,48),"INH_ColonyCustodyNoStored".Translate().ToString());
            float y=0;
            foreach (var resident in residents)
            {
                string kind=(resident.Kind == "animal" ? "INH_ColonyCustodyAnimal" : "INH_ColonyCustodyPerson").Translate().ToString();
                Widgets.Label(new Rect(0,y,content.width-130,28),resident.Name);
                Widgets.Label(new Rect(0,y+28,content.width-130,28),"INH_ColonyCustodyDetails".Translate(kind,resident.Items).ToString());
                if (Widgets.ButtonText(new Rect(content.width-124,y+10,120,32),"INH_ColonyCustodyRelease".Translate().ToString()))
                    Run(delegate { controller.Release(baseId,resident.EntityId); });
                y+=64;
            }
            Widgets.EndScrollView();
        }
        private void DrawPending(Rect panel,CustodyPendingSnapshot[] pending)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,28),"INH_ColonyCustodyPending".Translate().ToString());
            var area=new Rect(panel.x,panel.y+32,panel.width,panel.height-32);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,pending.Length*48+36));
            Widgets.BeginScrollView(area,ref pendingScroll,content);
            if (pending.Length == 0) Widgets.Label(new Rect(0,0,content.width,30),"INH_ColonyCustodyNoPending".Translate().ToString());
            float y=0;
            foreach (var job in pending)
            {
                string action=(job.IsRelease ? "INH_ColonyCustodyRelease" : "INH_ColonyCustodyStore").Translate().ToString();
                Widgets.Label(new Rect(0,y,content.width-148,44),"INH_ColonyCustodyPendingRow".Translate(job.Name,action).ToString());
                if (Widgets.ButtonText(new Rect(content.width-140,y+4,136,32),"INH_ColonyCustodyRetry".Translate().ToString()))
                    Run(delegate { controller.Resume(job.Id); });
                y+=48;
            }
            Widgets.EndScrollView();
        }
        private static int PawnItems(Pawn pawn)
        {
            int items=0;
            if (pawn.equipment != null) items+=pawn.equipment.AllEquipmentListForReading.Sum(t => t.stackCount);
            if (pawn.apparel != null) items+=pawn.apparel.WornApparel.Sum(t => t.stackCount);
            if (pawn.inventory != null) items+=pawn.inventory.innerContainer.Sum(t => t.stackCount);
            return items;
        }
        private static void Run(Action action)
        {
            try { action(); }
            catch (InvalidOperationException e)
            { Log.Message("[Inheritance-Colony] Storage action refused: "+e.Message); Find.WindowStack.Add(new Dialog_MessageBox("INH_ColonyCustodyRefused".Translate().ToString())); }
        }
    }
}
