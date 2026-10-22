using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Inheritance.Colony
{
    // Staged preview only. No selection of another caravan and no supply command.
    public sealed class DelegatedSupplyPreviewWindow : Window
    {
        private readonly ColonyService service;
        private readonly string cityId,entityId;
        private Vector2 scroll;
        public bool Rendered { get; private set; }
        public override Vector2 InitialSize { get { return new Vector2(1080,680); } }

        public DelegatedSupplyPreviewWindow(ColonyService service,string city,string entity)
        {
            this.service=service; cityId=city; entityId=entity;
            doCloseX=true; forcePause=true; absorbInputAroundWindow=true;
        }

        public override void DoWindowContents(Rect rect)
        {
            if (Event.current.type == EventType.Repaint) Rendered=true;
            GameFont previousFont=Text.Font; bool previousWrap=Text.WordWrap;
            Text.Font=GameFont.Small; Text.WordWrap=true;
            try
            {
                string warning=T("INH_SupplyPreviewWarning");
                float titleHeight=Height(T("INH_SupplyPreviewTitle"),rect.width-36);
                Widgets.Label(new Rect(0,0,rect.width-36,titleHeight),T("INH_SupplyPreviewTitle"));
                float footerHeight=Height(warning,rect.width);
                Widgets.Label(new Rect(0,rect.height-footerHeight,rect.width,footerHeight),warning);
                var area=new Rect(0,titleHeight+8,rect.width,Math.Max(24,rect.height-titleHeight-footerHeight-24));
                float width=Math.Max(80,area.width-22);
                var blocks=new List<PreviewBlock>();
                DelegatedSupplyPreviewSnapshot preview;
                DelegatedResidentSnapshot resident=null;
                try
                {
                    // The first observation discovers the original store anchor.
                    // Only an exact match among current caravan snapshots is used.
                    var anchorPreview=service.PreviewDelegatedFoodSupply(cityId,entityId,null);
                    Caravan original=null;
                    if (!string.IsNullOrEmpty(anchorPreview.OriginalCaravanId))
                        original=service.DelegatedCaravans(cityId)
                            .Where(c => c.Caravan != null && c.Caravan.GetUniqueLoadID() == anchorPreview.OriginalCaravanId)
                            .Select(c => c.Caravan).SingleOrDefault();
                    preview=original == null ? anchorPreview : service.PreviewDelegatedFoodSupply(cityId,entityId,original);
                    if (string.IsNullOrEmpty(preview.RecipientName))
                    {
                        try { resident=service.DelegatedCustodyResidents(cityId).SingleOrDefault(r => r.EntityId == entityId); }
                        catch (InvalidOperationException) { } // Use the localized unavailable name below.
                    }
                }
                catch (InvalidOperationException)
                {
                    blocks.Add(new PreviewBlock(new[] {T("INH_SupplyPreviewReadUnavailable")},null));
                    DrawBlocks(area,width,blocks); return;
                }

                string recipientName=Name(preview.RecipientName ?? (resident == null ? null : resident.Name));
                string recipientKind=preview.RecipientKind ?? (resident == null ? null : resident.Kind);
                var summary=new List<string>();
                summary.Add(F("INH_SupplyPreviewSite",Name(preview.CityName)));
                summary.Add(F("INH_SupplyPreviewRecipient",recipientName,Kind(recipientKind)));
                summary.Add(F("INH_SupplyPreviewCaravan",string.IsNullOrEmpty(preview.CaravanName) ? T("INH_SupplyPreviewMissingCaravan") : preview.CaravanName));
                summary.Add(F("INH_SupplyPreviewDemand",Number(preview.DailyDemand)));
                summary.Add(T("INH_SupplyPreviewEstimateHint"));
                AddProblems(summary,preview.Problems);
                blocks.Add(new PreviewBlock(summary,null));
                blocks.Add(new PreviewBlock(new[] {T("INH_SupplyPreviewFoods")},null));
                if (preview.Foods.Count == 0)
                    blocks.Add(new PreviewBlock(new[] {T("INH_SupplyPreviewEmpty")},null));
                foreach (var food in preview.Foods)
                {
                    var lines=new List<string>();
                    lines.Add(Name(food.FoodName));
                    lines.Add(F("INH_SupplyPreviewOwner",Name(food.SourceName),Kind(food.SourceKind)));
                    lines.Add(F("INH_SupplyPreviewNutrition",food.Count,Number(food.UnitNutrition),Number(food.StackNutrition)));
                    lines.Add(F("INH_SupplyPreviewDemand",Number(food.DailyDemand)));
                    AddProblems(lines,food.Problems);
                    if (food.Problems.Count == 0) lines.Add(T("INH_SupplyPreviewNoListedProblems"));
                    string historyTip=null;
                    if (food.CargoHistory.Count > 0)
                    {
                        lines.Add(F("INH_SupplyPreviewHistoryCount",food.CargoHistory.Count));
                        // Explain historical matches without shipment, pawn or item IDs.
                        historyTip=string.Join("\n",food.CargoHistory.Select(h =>
                            F("INH_SupplyPreviewHistoryDetail",
                                T(h.JourneySupply ? "INH_SupplyPreviewTravelFood" : "INH_SupplyPreviewFreight"),
                                T(h.MatchesSourceId && h.MatchesCargoId ? "INH_SupplyPreviewHistoryBoth" :
                                    h.MatchesSourceId ? "INH_SupplyPreviewHistorySource" : "INH_SupplyPreviewHistoryCargo")))
                            .Distinct(StringComparer.Ordinal).ToArray());
                    }
                    blocks.Add(new PreviewBlock(lines,historyTip));
                }
                DrawBlocks(area,width,blocks);
            }
            finally { Text.Font=previousFont; Text.WordWrap=previousWrap; }
        }

        private sealed class PreviewBlock
        {
            internal readonly string[] Lines;
            internal readonly string Tooltip;
            internal PreviewBlock(IEnumerable<string> lines,string tooltip)
            { Lines=lines.ToArray(); Tooltip=tooltip; }
        }

        private void DrawBlocks(Rect area,float width,List<PreviewBlock> blocks)
        {
            // Measure each translated line at the same width/font used for drawing.
            // Long names and any number of refusal reasons contribute to scroll height.
            float total=0;
            foreach (var block in blocks)
            {
                foreach (string line in block.Lines) total+=Height(line,width)+4;
                total+=12;
            }
            Widgets.BeginScrollView(area,ref scroll,new Rect(0,0,width,Math.Max(area.height,total)));
            try
            {
                float y=0;
                foreach (var block in blocks)
                {
                    for (int index=0;index<block.Lines.Length;index++)
                    {
                        string line=block.Lines[index];
                        float height=Height(line,width);
                        var lineRect=new Rect(0,y,width,height);
                        Widgets.Label(lineRect,line);
                        TooltipHandler.TipRegion(lineRect,index == block.Lines.Length-1 && !string.IsNullOrEmpty(block.Tooltip) ? block.Tooltip : line);
                        y+=height+4;
                    }
                    y+=12;
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private static float Height(string text,float width)
        { return Math.Max(24,Text.CalcHeight(text,width)+2); }
        private static string T(string key) { return key.Translate().ToString(); }
        private static string F(string key,params object[] values)
        { return string.Format(CultureInfo.CurrentCulture,T(key),values); }
        private static string Name(string name)
        { return string.IsNullOrEmpty(name) ? T("INH_SupplyPreviewUnavailable") : name; }
        private static string Kind(string kind)
        { return T(kind == "animal" ? "INH_SupplyPreviewAnimal" : kind == "person" ? "INH_SupplyPreviewPerson" : "INH_SupplyPreviewUnknownKind"); }
        private static string Number(double value)
        { return value < 0d || double.IsNaN(value) || double.IsInfinity(value) ? T("INH_SupplyPreviewUnknownNumber") : value.ToString("0.###",CultureInfo.CurrentCulture); }
        private static void AddProblems(List<string> lines,IEnumerable<string> problems)
        {
            var translated=problems.Where(p => !string.IsNullOrEmpty(p) && p != "None").Select(Problem).Distinct(StringComparer.Ordinal).ToArray();
            if (translated.Length == 0) return;
            lines.Add(T("INH_SupplyPreviewProblems"));
            foreach (string problem in translated) lines.Add("• "+problem);
        }
        private static string Problem(string code)
        {
            switch (code)
            {
                case "WorldUnavailable": case "Unlocated": case "CityLocked":
                case "CaravanMissing": case "CaravanMoving": case "CaravanAway": case "NotOriginalCaravan":
                case "ResidentUnavailable": case "RecipientTransportReserved": case "RecipientPendingCustody":
                case "RecipientPendingRations": case "UnsupportedRecipient": case "RationMutation":
                case "RecipientHistoricalCargo": case "RecipientHistoricalJourneySupply":
                case "SourceIneligible": case "SourceNativeCustodyReserved": case "SourceTransportReserved": case "SourcePendingRations":
                case "SourceInventoryMismatch": case "UnsupportedFood": case "InvalidNutrition": case "NotQuoted":
                case "HistoricalCargo": case "HistoricalJourneySupply":
                    return T("INH_SupplyPreviewProblem_"+code);
                default: return T("INH_SupplyPreviewProblem_Unknown");
            }
        }
    }
}
