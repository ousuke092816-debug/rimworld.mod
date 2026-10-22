using System;

namespace Inheritance.Core
{
    public sealed partial class CoreWorldComponent
    {
        // The public preflight never accepts a caller-selected lock exemption.
        public void AssertNoActiveSupplyTransfer(string cityId=null,string entityId=null,string nativeId=null,string foodId=null,string intentId=null)
        {
            if (!IsReady || supplyTransfers == null) throw new InvalidOperationException("Core supply transfer responsibility is unavailable");
            if (supplyTransfers.HasActiveForCity(cityId) || supplyTransfers.HasActiveForEntity(entityId) ||
                supplyTransfers.HasActiveForNative(nativeId) || supplyTransfers.HasActiveForFood(foodId) || supplyTransfers.HasActiveForIntent(intentId))
                throw new InvalidOperationException("Original is locked by an active supply transfer preparation");
        }
    }
}
