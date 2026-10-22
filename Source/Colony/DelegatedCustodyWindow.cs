using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.Colony
{
    public sealed class DelegatedCustodyCommandController
    {
        private readonly ColonyService service;
        private readonly Dictionary<string,string> causes=new Dictionary<string,string>();
        public DelegatedCustodyCommandController(ColonyService service) { this.service=service; }
        private string Cause(string key)
        {
            string cause;
            if (!causes.TryGetValue(key,out cause))
            { cause="delegated-command/"+Guid.NewGuid().ToString("N"); causes.Add(key,cause); }
            return cause;
        }
        public string Register(Caravan caravan,string name)
        {
            if (caravan == null) throw new InvalidOperationException("An original caravan is required");
            string key="register/"+caravan.GetUniqueLoadID()+"/"+name;
            string city=service.RegisterDelegatedSiteAtCaravan(Cause(key),name,caravan);
            causes.Remove(key); return city;
        }
        public string Store(string city,Caravan caravan,Pawn pawn)
        {
            if (caravan == null || pawn == null) throw new InvalidOperationException("An original caravan and pawn are required");
            string key="store/"+city+"/"+caravan.GetUniqueLoadID()+"/"+pawn.GetUniqueLoadID();
            string transfer=service.StoreDelegatedPawn(Cause(key),city,caravan,pawn);
            causes.Remove(key); return transfer;
        }
        public string Release(string city,string entity,Caravan caravan)
        {
            if (caravan == null) throw new InvalidOperationException("The original caravan is required");
            string key="release/"+city+"/"+entity+"/"+caravan.GetUniqueLoadID();
            string transfer=service.ReleaseDelegatedPawn(Cause(key),city,entity,caravan);
            causes.Remove(key); return transfer;
        }
        public void Resume(string id)
        {
            var pending=service.DelegatedCustodyPending(null).SingleOrDefault(p => p.Id == id);
            string cause=pending == null ? null : pending.Cause;
            service.ResumeDelegatedCustody(id);
            if (cause == null) return;
            foreach (string key in causes.Where(p => p.Value == cause).Select(p => p.Key).ToArray()) causes.Remove(key);
        }
    }

    public sealed class DelegatedCustodyWindow : Window
    {
        private readonly ColonyService service;
        public readonly DelegatedCustodyCommandController Commands;
        private string cityId,siteName="";
        private Caravan caravan;
        private Vector2 candidateScroll,residentScroll,pendingScroll;
        public bool Rendered { get; private set; }
        public override Vector2 InitialSize { get { return new Vector2(1080,660); } }
        public DelegatedCustodyWindow(ColonyService service)
        { this.service=service; Commands=new DelegatedCustodyCommandController(service); doCloseX=true; forcePause=true; absorbInputAroundWindow=true; }
        public void SelectCity(string id)
        { cityId=id; candidateScroll=Vector2.zero; residentScroll=Vector2.zero; pendingScroll=Vector2.zero; }
        public void SelectCaravan(Caravan original)
        { caravan=original; candidateScroll=Vector2.zero; }
        public override void DoWindowContents(Rect rect)
        {
            if (Event.current.type == EventType.Repaint) Rendered=true;
            Widgets.Label(new Rect(0,0,rect.width-226,32),"INH_DelegatedCustodyTitle".Translate().ToString());
            Widgets.Label(new Rect(0,36,rect.width,48),"INH_DelegatedCustodyHelp".Translate().ToString());
            Widgets.Label(new Rect(0,rect.height-56,rect.width,56),"INH_DelegatedFrozenWarning".Translate().ToString());
            var sites=service.Bases.Where(b => b.Mode == BaseMode.Delegated).ToArray();
            if (cityId != null && !sites.Any(b => b.Id == cityId)) SelectCity(null);
            var site=sites.SingleOrDefault(b => b.Id == cityId);
            if (site != null && site.Tile >= 0 && Widgets.ButtonText(new Rect(rect.width-216,0,206,30),"INH_DelegatedOpenMaintenance".Translate().ToString()))
                ColonyOverview.Run(delegate { var window=new DelegatedMaintenanceWindow(service); window.SelectCity(cityId); Find.WindowStack.Add(window); });
            DelegatedCaravanSnapshot[] caravans;
            DelegatedStorePawnSnapshot[] candidates;
            DelegatedResidentSnapshot[] residents;
            DelegatedPendingSnapshot[] pending;
            try
            {
                caravans=service.DelegatedCaravans(cityId);
                if (caravan != null && !caravans.Any(c => ReferenceEquals(c.Caravan,caravan))) SelectCaravan(null);
                candidates=cityId != null && caravan != null ? service.AvailableDelegatedStorePawns(cityId,caravan) : new DelegatedStorePawnSnapshot[0];
                residents=cityId != null ? service.DelegatedCustodyResidents(cityId) : new DelegatedResidentSnapshot[0];
                pending=cityId != null ? service.DelegatedCustodyPending(cityId) : new DelegatedPendingSnapshot[0];
            }
            catch (InvalidOperationException)
            { Widgets.Label(new Rect(0,96,rect.width,60),"INH_DelegatedReadUnavailable".Translate().ToString()); return; }
            float half=(rect.width-16)/2;
            Widgets.Label(new Rect(0,90,half,24),"INH_DelegatedSite".Translate().ToString());
            Widgets.Label(new Rect(half+16,90,half,24),"INH_DelegatedCaravan".Translate().ToString());
            if (Widgets.ButtonText(new Rect(0,116,half,30),site == null ? "INH_DelegatedChooseSite".Translate().ToString() : site.Name))
            {
                var options=new List<FloatMenuOption>();
                options.Add(new FloatMenuOption("INH_DelegatedNewSite".Translate().ToString(),delegate { SelectCity(null); }));
                options.AddRange(sites.Select(s => new FloatMenuOption(s.Name,delegate { SelectCity(s.Id); })));
                Find.WindowStack.Add(new FloatMenu(options));
            }
            var selected=caravans.SingleOrDefault(c => ReferenceEquals(c.Caravan,caravan));
            if (Widgets.ButtonText(new Rect(half+16,116,half,30),selected == null ? "INH_DelegatedChooseCaravan".Translate().ToString() : selected.Name))
            {
                if (caravans.Length > 0)
                    Find.WindowStack.Add(new FloatMenu(caravans.Select(c => new FloatMenuOption(c.Name,delegate { SelectCaravan(c.Caravan); })).ToList()));
                else Find.WindowStack.Add(new Dialog_MessageBox("INH_DelegatedNoCaravans".Translate().ToString()));
            }
            Widgets.Label(new Rect(0,156,110,30),"INH_DelegatedNewSiteName".Translate().ToString());
            siteName=Widgets.TextField(new Rect(116,156,rect.width-326,30),siteName);
            bool enabled=GUI.enabled;
            GUI.enabled=enabled && service.DelegatedCustodyCommandsReady && selected != null && selected.CanRegister && !string.IsNullOrEmpty(siteName.Trim());
            if (Widgets.ButtonText(new Rect(rect.width-202,156,198,30),"INH_DelegatedRegisterHere".Translate().ToString()))
                ColonyOverview.Run(delegate { string city=Commands.Register(caravan,siteName.Trim()); SelectCity(city); siteName=""; });
            GUI.enabled=enabled;
            string notice=site != null && site.Tile < 0 ? Problem("Unlocated") :
                selected != null && selected.Problem != "None" && !string.IsNullOrEmpty(selected.Problem) ? Problem(selected.Problem) :
                !service.DelegatedCustodyCommandsReady ? "INH_DelegatedDisabled".Translate().ToString() : "INH_DelegatedRegisterHelp".Translate().ToString();
            Widgets.Label(new Rect(0,192,rect.width,40),notice);
            float panelHeight=rect.height-410;
            DrawCandidates(new Rect(0,236,half,panelHeight),candidates);
            DrawResidents(new Rect(half+16,236,half,panelHeight),residents);
            DrawPending(new Rect(0,rect.height-166,rect.width,102),pending);
        }
        private void DrawCandidates(Rect panel,DelegatedStorePawnSnapshot[] candidates)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,26),"INH_DelegatedCandidates".Translate().ToString());
            var area=new Rect(panel.x,panel.y+30,panel.width,panel.height-30);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,candidates.Length*100+60));
            Widgets.BeginScrollView(area,ref candidateScroll,content);
            if (candidates.Length == 0) Widgets.Label(new Rect(0,0,content.width,58),"INH_DelegatedNoCandidates".Translate().ToString());
            float y=0;
            foreach (var candidate in candidates)
            {
                Widgets.Label(new Rect(0,y,content.width-150,26),candidate.Name);
                Widgets.Label(new Rect(0,y+28,content.width-150,26),Details(candidate.Kind,candidate.Items));
                Widgets.Label(new Rect(0,y+56,content.width,40),Problem(candidate.Problem));
                bool enabled=GUI.enabled; GUI.enabled=enabled && service.DelegatedCustodyCommandsReady && candidate.CanStore;
                if (Widgets.ButtonText(new Rect(content.width-144,y+8,140,30),"INH_DelegatedStore".Translate().ToString()))
                    ColonyOverview.Run(delegate { Commands.Store(cityId,caravan,candidate.Pawn); });
                GUI.enabled=enabled; y+=100;
            }
            Widgets.EndScrollView();
        }
        public DelegatedSupplyPreviewWindow OpenSupplyPreview(string city,string entity)
        {
            var window=new DelegatedSupplyPreviewWindow(service,city,entity);
            Find.WindowStack.Add(window);
            return window;
        }

        private void DrawResidents(Rect panel,DelegatedResidentSnapshot[] residents)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,26),"INH_DelegatedResidents".Translate().ToString());
            var area=new Rect(panel.x,panel.y+30,panel.width,panel.height-30);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,residents.Length*126+60));
            Widgets.BeginScrollView(area,ref residentScroll,content);
            if (residents.Length == 0) Widgets.Label(new Rect(0,0,content.width,58),"INH_DelegatedNoResidents".Translate().ToString());
            float y=0;
            foreach (var resident in residents)
            {
                Widgets.Label(new Rect(0,y,content.width-150,26),resident.Name);
                Widgets.Label(new Rect(0,y+28,content.width-150,26),Details(resident.Kind,resident.Items));
                string originalCaravanLabel="INH_DelegatedOriginalCaravan".Translate(CaravanName(resident.CaravanName)).ToString();
                var originalCaravanRect=new Rect(0,y+56,content.width-150,26);
                Widgets.Label(originalCaravanRect,originalCaravanLabel);
                TooltipHandler.TipRegion(originalCaravanRect,originalCaravanLabel);
                Widgets.Label(new Rect(0,y+84,content.width,38),Problem(resident.Problem));
                bool enabled=GUI.enabled; GUI.enabled=enabled && resident.CanRelease;
                if (Widgets.ButtonText(new Rect(content.width-144,y+8,140,30),"INH_DelegatedRelease".Translate().ToString()))
                    ColonyOverview.Run(delegate { Commands.Release(cityId,resident.EntityId,resident.Caravan); });
                GUI.enabled=enabled;
                if (Widgets.ButtonText(new Rect(content.width-144,y+44,140,30),"INH_DelegatedOpenSupplyPreview".Translate().ToString()))
                    ColonyOverview.Run(delegate { OpenSupplyPreview(cityId,resident.EntityId); });
                y+=126;
            }
            Widgets.EndScrollView();
        }
        private void DrawPending(Rect panel,DelegatedPendingSnapshot[] pending)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,26),"INH_DelegatedPending".Translate().ToString());
            var area=new Rect(panel.x,panel.y+30,panel.width,panel.height-30);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,pending.Length*76+32));
            Widgets.BeginScrollView(area,ref pendingScroll,content);
            if (pending.Length == 0) Widgets.Label(new Rect(0,0,content.width,30),"INH_DelegatedNoPending".Translate().ToString());
            float y=0;
            foreach (var intent in pending)
            {
                string action=(intent.IsRelease ? "INH_DelegatedRelease" : "INH_DelegatedStore").Translate().ToString();
                Widgets.Label(new Rect(0,y,content.width-176,28),"INH_DelegatedPendingRow".Translate(intent.Name,action,CaravanName(intent.CaravanName)).ToString());
                Widgets.Label(new Rect(0,y+30,content.width-176,42),Problem(intent.Problem));
                bool enabled=GUI.enabled; GUI.enabled=enabled && intent.CanResume;
                if (Widgets.ButtonText(new Rect(content.width-170,y+8,166,30),"INH_DelegatedRetry".Translate().ToString()))
                    ColonyOverview.Run(delegate { Commands.Resume(intent.Id); });
                GUI.enabled=enabled; y+=76;
            }
            Widgets.EndScrollView();
        }
        private static string CaravanName(string name)
        { return string.IsNullOrEmpty(name) ? "INH_DelegatedMissingCaravan".Translate().ToString() : name; }
        private static string Details(string kind,int items)
        { return "INH_DelegatedDetails".Translate((kind == "animal" ? "INH_DelegatedAnimal" : "INH_DelegatedPerson").Translate().ToString(),items).ToString(); }
        private static string Problem(string code)
        {
            if (string.IsNullOrEmpty(code) || code == "None") return "";
            switch (code)
            {
                case "Disabled": case "Unlocated": case "CaravanMissing": case "CaravanMoving": case "CaravanAway": case "Occupied":
                case "NoEscort": case "Ineligible": case "Reserved": case "PendingCustody": case "PendingRations": case "CityLocked":
                    return ("INH_DelegatedProblem_"+code).Translate().ToString();
                default: return "INH_DelegatedProblem_Unknown".Translate().ToString();
            }
        }
    }
}
