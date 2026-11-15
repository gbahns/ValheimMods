using HarmonyLib;

namespace Armory
{
    /// <summary>
    /// Shift+[Use] renames the rack instead of opening it, matching the modifier vanilla uses for
    /// portals and signs.  Plain [Use] on another player's personal rack opens its loadouts to
    /// read instead of vanilla's refusal (#85).
    ///
    /// Both have to be caught here rather than on ArmoryRack, because the rack deliberately
    /// stopped being the Interactable in 1.2.0 — the Container answers [Use] so storage behaves
    /// exactly like a chest. Container.Interact ignores its own `alt` argument, so intercepting
    /// it costs vanilla nothing: for anything else this prefix stands aside entirely.
    /// </summary>
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class ArmoryRenamePatch
    {
        private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            // Held for vanilla's take-all: not ours.
            if (hold) return true;

            var rack = __instance.GetComponent<ArmoryRack>();
            if (rack == null) return true;

            if (!alt)
            {
                // Plain [Use].  Vanilla would refuse someone else's personal rack with
                // "$msg_cantopen"; the loadouts are not what Personal keeps, so show them.  A
                // ward still has the last word, in vanilla's own voice.
                if (!rack.IsPrivate || rack.IsBuilder) return true;
                if (__instance.m_checkGuardStone && !PrivateArea.CheckAccess(__instance.transform.position)) return true;
                if (!ArmoryUI.OpenReadOnly(rack))
                    character.Message(MessageHud.MessageType.Center, $"{rack.RackName}: no loadouts saved to read");
                __result = true;
                return false;
            }

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
