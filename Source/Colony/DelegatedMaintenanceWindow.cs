using System;
using System.Collections.Generic;
using System.Linq;
using Inheritance.Core;
using UnityEngine;
using Verse;

namespace Inheritance.Colony
{
    public sealed class DelegatedMaintenanceWindow : Window
    {
        private readonly ColonyService service;
        private readonly Dictionary<string,string> causes=new Dictionary<string,string>();
        private string cityId;
        private Vector2 residentScroll,pendingScroll;
        public bool Rendered { get; private set; }
        public override Vector2 InitialSize { get { return new Vector2(1060,760); } }
        public DelegatedMaintenanceWindow(ColonyService service)
        { this.service=service; doCloseX=true; forcePause=true; absorbInputAroundWindow=true; }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0,0,rect.width-30,32),"INH_MaintenanceTitle".Translate().ToString());
            Widgets.Label(new Rect(0,36,rect.width,58),"INH_MaintenanceHelp".Translate().ToString());
            Widgets.Label(new Rect(0,rect.height-54,rect.width,54),"INH_MaintenanceFrozenWarning".Translate().ToString());
            if (Event.current.type == EventType.Repaint) Rendered=true;
            var bases=service.Bases.Where(b => b.Mode == BaseMode.Delegated).ToArray();
            if (bases.Length == 0)
            { Widgets.Label(new Rect(0,108,rect.width,58),"INH_MaintenanceNoSites".Translate().ToString()); return; }
            if (!bases.Any(b => b.Id == cityId)) SelectCity(bases.OrderBy(b => b.Tile < 0).First().Id);
            Widgets.Label(new Rect(0,104,110,30),"INH_MaintenanceSite".Translate().ToString());
            var site=bases.Single(b => b.Id == cityId);
            if (Widgets.ButtonText(new Rect(120,102,rect.width-120,32),site.Name))
                Find.WindowStack.Add(new FloatMenu(bases.Select(b => new FloatMenuOption(b.Name,delegate { SelectCity(b.Id); })).ToList()));
            Widgets.Label(new Rect(0,140,rect.width,30),site.Tile < 0 ? "INH_MaintenanceProblem_Unlocated".Translate().ToString() : "INH_MaintenanceSitePosition".Translate(site.Tile).ToString());
            if (!service.MaintenanceCommandsReady)
                Widgets.Label(new Rect(0,174,rect.width,48),"INH_MaintenanceDisabled".Translate().ToString());
            else Widgets.Label(new Rect(0,174,rect.width,48),"INH_MaintenanceFoodHelp".Translate().ToString());
            DelegatedMaintenanceResidentSnapshot[] residents;
            DelegatedMaintenanceSnapshot[] schedules;
            RationDebitSnapshot[] pending;
            try
            {
                residents=service.DelegatedResidentMaintenance(cityId);
                schedules=service.DelegatedMaintenance.Where(m => m.CityId == cityId).ToArray();
                pending=service.DelegatedRations.Where(r => r.CityId == cityId && r.Stage != RationDebitStage.Committed).ToArray();
            }
            catch (InvalidOperationException)
            { Widgets.Label(new Rect(0,232,rect.width,58),"INH_MaintenanceReadUnavailable".Translate().ToString()); return; }
            DrawResidents(new Rect(0,232,rect.width,rect.height-426),site,residents,schedules);
            DrawPending(new Rect(0,rect.height-184,rect.width,122),residents,schedules,pending);
        }
        public void SelectCity(string id)
        { cityId=id; residentScroll=Vector2.zero; pendingScroll=Vector2.zero; }
        private void Control(string key,Action<string> command)
        {
            string cause;
            if (!causes.TryGetValue(key,out cause))
            { cause="maintenance-command/"+Guid.NewGuid().ToString("N"); causes.Add(key,cause); }
            command(cause); causes.Remove(key);
        }
        private void DrawResidents(Rect panel,BaseSnapshot site,DelegatedMaintenanceResidentSnapshot[] residents,DelegatedMaintenanceSnapshot[] schedules)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,28),"INH_MaintenanceResidents".Translate().ToString());
            var candidates=residents.Where(r => !schedules.Any(m => m.EntityId == r.EntityId && m.Stage.ToString() != "Released")).ToArray();
            var area=new Rect(panel.x,panel.y+32,panel.width,panel.height-32);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,(candidates.Length+schedules.Length)*144+44));
            Widgets.BeginScrollView(area,ref residentScroll,content);
            if (candidates.Length == 0 && schedules.Length == 0)
                Widgets.Label(new Rect(0,0,content.width,56),"INH_MaintenanceNoResidents".Translate().ToString());
            float y=0;
            foreach (var resident in candidates)
            {
                string kind=(resident.Kind == "animal" ? "INH_MaintenanceAnimal" : "INH_MaintenancePerson").Translate().ToString();
                Widgets.Label(new Rect(0,y,content.width-210,28),"INH_MaintenanceResidentRow".Translate(resident.Name,kind).ToString());
                Widgets.Label(new Rect(0,y+30,content.width-210,28),Nutrition(resident.AvailableNutrition,resident.DailyDemand));
                Widgets.Label(new Rect(0,y+60,content.width,28),"INH_MaintenanceNotStarted".Translate().ToString());
                Widgets.Label(new Rect(0,y+90,content.width,46),Problem(resident.Problem));
                bool enabled=GUI.enabled; GUI.enabled=enabled && service.MaintenanceCommandsReady && site.Tile >= 0;
                if (Widgets.ButtonText(new Rect(content.width-202,y+2,198,30),"INH_MaintenanceStart".Translate().ToString()))
                    ColonyOverview.Run(delegate { Control("start/"+cityId+"/"+resident.EntityId,delegate(string cause) { service.StartDelegatedMaintenance(cause,cityId,resident.EntityId); }); });
                GUI.enabled=enabled; y+=144;
            }
            foreach (var schedule in schedules)
            {
                string stage=schedule.Stage.ToString();
                var resident=residents.FirstOrDefault(r => r.EntityId == schedule.EntityId);
                string kind=resident == null ? "INH_MaintenanceUnknownResident".Translate().ToString() :
                    (resident.Kind == "animal" ? "INH_MaintenanceAnimal" : "INH_MaintenancePerson").Translate().ToString();
                string name=string.IsNullOrEmpty(schedule.Name) ? "INH_MaintenanceUnknownResident".Translate().ToString() : schedule.Name;
                Widgets.Label(new Rect(0,y,content.width-210,28),"INH_MaintenanceScheduleRow".Translate(name,kind,("INH_MaintenanceStage_"+stage).Translate().ToString()).ToString());
                Widgets.Label(new Rect(0,y+30,content.width-210,28),Nutrition(schedule.AvailableNutrition,schedule.DailyDemand));
                Widgets.Label(new Rect(0,y+60,content.width-210,28),"INH_MaintenanceDates".Translate(Day(schedule.StartDay),Day(schedule.LastPaidDay),Day(schedule.NextDay)).ToString());
                string problem=Problem(schedule.Problem);
                if (resident != null && !string.IsNullOrEmpty(resident.Problem) && resident.Problem != "None" && resident.Problem != schedule.Problem)
                    problem+=(problem.Length == 0 ? "" : " ")+"INH_MaintenanceCurrentCondition".Translate(Problem(resident.Problem)).ToString();
                Widgets.Label(new Rect(0,y+90,content.width,46),problem);
                if (stage == "Active" || stage == "Held")
                {
                    if (Widgets.ButtonText(new Rect(content.width-202,y+2,198,30),"INH_MaintenanceStop".Translate().ToString()))
                        ColonyOverview.Run(delegate { Control("stop/"+schedule.Id,delegate(string cause) { service.StopDelegatedMaintenance(cause,schedule.Id); }); });
                }
                if (stage == "Held" || stage == "Stopped")
                {
                    if (Widgets.ButtonText(new Rect(content.width-202,y+38,198,30),"INH_MaintenanceResume".Translate().ToString()))
                        ColonyOverview.Run(delegate { Control("resume/"+schedule.Id,delegate(string cause) { service.ResumeDelegatedMaintenance(cause,schedule.Id); }); });
                }
                y+=144;
            }
            Widgets.EndScrollView();
        }
        private void DrawPending(Rect panel,DelegatedMaintenanceResidentSnapshot[] residents,DelegatedMaintenanceSnapshot[] schedules,RationDebitSnapshot[] pending)
        {
            Widgets.Label(new Rect(panel.x,panel.y,panel.width,28),"INH_MaintenancePending".Translate().ToString());
            var area=new Rect(panel.x,panel.y+32,panel.width,panel.height-32);
            var content=new Rect(0,0,panel.width-20,Math.Max(area.height,pending.Length*48+36));
            Widgets.BeginScrollView(area,ref pendingScroll,content);
            if (pending.Length == 0) Widgets.Label(new Rect(0,0,content.width,30),"INH_MaintenanceNoPending".Translate().ToString());
            float y=0;
            foreach (var debit in pending)
            {
                string name=residents.Where(r => r.EntityId == debit.EntityId).Select(r => r.Name).FirstOrDefault() ??
                    schedules.Where(s => s.EntityId == debit.EntityId).Select(s => s.Name).FirstOrDefault() ?? "INH_MaintenanceUnknownResident".Translate().ToString();
                Widgets.Label(new Rect(0,y,content.width-210,44),"INH_MaintenancePendingRow".Translate(name,Day(debit.Day)).ToString());
                if (Widgets.ButtonText(new Rect(content.width-202,y+4,198,30),"INH_MaintenanceRecover".Translate().ToString()))
                    ColonyOverview.Run(delegate { service.ResumeDelegatedRations(debit.Id); causes.Clear(); });
                y+=48;
            }
            Widgets.EndScrollView();
        }
        private static string Nutrition(float available,float demand)
        {
            string a=float.IsNaN(available) || float.IsInfinity(available) || available < 0 ? "INH_MaintenanceUnknown".Translate().ToString() : available.ToString("0.00");
            string d=float.IsNaN(demand) || float.IsInfinity(demand) || demand < 0 ? "INH_MaintenanceUnknown".Translate().ToString() : demand.ToString("0.00");
            return "INH_MaintenanceNutrition".Translate(a,d).ToString();
        }
        private static string Day(int day)
        { return day < 0 ? "INH_MaintenanceNoDay".Translate().ToString() : "INH_MaintenanceDay".Translate(day+1).ToString(); }
        private static string Problem(string code)
        {
            if (string.IsNullOrEmpty(code) || code == "None") return "";
            switch (code)
            {
                case "Unlocated": case "AnchorMissing": case "ResidentUnavailable": case "CargoReserved":
                case "Unsupported": case "Pending": case "Shortage": case "DayGap": case "ResidenceChanged":
                    return ("INH_MaintenanceProblem_"+code).Translate().ToString();
                default: return "INH_MaintenanceProblem_Unknown".Translate().ToString();
            }
        }
    }
}
