using Verse;

namespace Inheritance.Core
{
    // Read-only observations. Native references are identities, not mutation permission.
    public sealed class RationSupplyCandidateQuote
    {
        public readonly Pawn Recipient;
        public readonly float DailyDemand, UnitNutrition;
        public readonly string RecipientProblem, FoodProblem;
        public readonly bool CanPreview;

        internal RationSupplyCandidateQuote(Pawn recipient,float demand,float nutrition,string recipientProblem,string foodProblem)
        {
            Recipient=recipient; DailyDemand=demand; UnitNutrition=nutrition;
            RecipientProblem=recipientProblem; FoodProblem=foodProblem;
            CanPreview=recipientProblem == "None" && foodProblem == "None";
        }
    }
}
