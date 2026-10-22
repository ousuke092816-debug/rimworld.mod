using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace Inheritance.Colony
{
    public sealed class PhysicalLossWindow : Window
    {
        private readonly TransportCommandController controller;
        public readonly PhysicalLossPreview Preview;
        private Vector2 scroll;
        public bool Rendered { get; private set; }
        public override Vector2 InitialSize { get { return new Vector2(780,520); } }
        public PhysicalLossWindow(ColonyService service,string cause)
        { controller=new TransportCommandController(service); Preview=service.PreviewPhysicalLoss(cause); forcePause=true; doCloseX=true; absorbInputAroundWindow=true; }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0,0,rect.width-30,32),"INH_ColonyInspectLoss".Translate().ToString());
            Widgets.Label(new Rect(0,40,rect.width,72),"INH_ColonyLossHelp".Translate().ToString());
            var items=Preview.Items.ToArray(); var area=new Rect(0,122,rect.width,rect.height-214);
            var content=new Rect(0,0,area.width-24,Math.Max(area.height,items.Length*36));
            Widgets.BeginScrollView(area,ref scroll,content); float y=0;
            foreach (var item in items) { Widgets.Label(new Rect(0,y,content.width,32),"INH_ColonyLossItem".Translate(item.Label,item.Expected,item.Remaining).ToString()); y+=36; }
            Widgets.EndScrollView();
            Widgets.Label(new Rect(0,rect.height-76,rect.width,32),"INH_ColonyLossTotal".Translate(Preview.Lost).ToString());
            if (Widgets.ButtonText(new Rect(0,rect.height-36,210,32),"INH_ColonyAcceptLoss".Translate().ToString())) ColonyOverview.Run(delegate { controller.AcceptLoss(Preview); Close(); });
            if (Widgets.ButtonText(new Rect(rect.width-170,rect.height-36,160,32),"INH_ColonyKeepHeld".Translate().ToString())) Close();
            if (Event.current.type == EventType.Repaint) Rendered=true;
        }
    }
}
