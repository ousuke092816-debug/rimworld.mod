using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Inheritance.Colony
{
    public sealed partial class ColonyService
    {
        public float EstimateTransportFood(string sourceId,Pawn[] party,CargoSelection[] selections)
        {
            if (party == null || selections == null || party.Length == 0 || party.Any(p => p == null) || selections.Any(c => c == null || c.Source == null || c.Count < 0)) return float.NaN;
            return DaysWorthOfFoodCalculator.ApproxDaysWorthOfFood(party.ToList(),selections.Select(c => new ThingCount(c.Source,c.Count)).ToList(),
                DirectMap(sourceId).Tile,IgnorePawnsInventoryMode.DontIgnore,Faction.OfPlayer);
        }
    }
}
