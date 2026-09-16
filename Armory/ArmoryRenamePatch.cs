using HarmonyLib;

namespace Armory
{
    /// <summary>
    /// Shift+[Use] renames the rack instead of opening it, matching the modifier vanilla uses for
    /// portals and signs.
    ///
    /// It has to be caught here rather than on ArmoryRack, because the rack deliberately stopped
    /// being the Interactable in 1.2.0 — the Container answers [Use] so storage behaves exactly
    /// like a chest. Container.Interact ignores its own `alt` argument, so intercepting it costs
    /// vanilla nothing: without the modifier this prefix stands aside entirely.
    /// </summary>
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class ArmoryRenamePatch
    {
        private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            // Plain [Use], or held for vanilla's take-all: not ours.
            if (!alt || hold) return true;

            var rack = __instance.GetComponent<ArmoryRack>();
            if (rack == null) return true;

            // Renaming is a modification, so it answers to a ward the way editing a sign does —
            // unconditionally, rather than following the container's own m_checkGuardStone.
            if (!PrivateArea.CheckAccess(__instance.transform.position))
            {
                character.Message(MessageHud.MessageType.Center, "$piece_noaccess");
                __result = true;
                return false;
            }

            // A personal rack is the builder's to name, for the same reason it is theirs to open.
            if (rack.IsPrivate && !rack.IsBuilder)
            {
                character.Message(MessageHud.MessageType.Center, "$msg_cantopen");
                __result = true;
                return false;
            }

            TextInput.instance.RequestText(rack, "$armory_rename_topic", 32);
            __result = true;
            return false;
        }
    }
}
