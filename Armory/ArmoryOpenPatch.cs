using HarmonyLib;

namespace Armory
{
    /// <summary>
    /// The loadout panel rides on the vanilla container opening instead of replacing it.
    ///
    /// Pressing [Use] on the rack reaches Container.Interact like any chest: it checks the guard
    /// stone and the privacy setting, asks the ZDO's owner, and the owner hands the ZDO over and
    /// answers.  Only that answer calls InventoryGui.Show — by which point this client owns the
    /// ZDO, so the storage grid will draw and Container will save what goes into it.  All this
    /// patch does is notice that the container which just opened happens to be an Armory Rack.
    ///
    /// Everything the rack used to re-implement — the exchange, the ownership transfer, the
    /// in-use refusal, wards, privacy — is vanilla's again, and is deleted from this mod.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class ArmoryOpenPatch
    {
        private static void Postfix(Container container)
        {
            if (container == null) return;
            var rack = container.GetComponent<ArmoryRack>();
            if (rack == null) return;
            ArmoryUI.Open(rack);
        }
    }
}
