using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SmartSilencer
{
    /// <summary>Hotkey polling through Valheim's own ZInput, gated so it never fires off a letter being typed.</summary>
    internal static class Keys
    {
        internal static bool IsDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return false;
            if (!ZInput.GetKeyDown(shortcut.MainKey)) return false;
            foreach (var modifier in shortcut.Modifiers)
                if (!ZInput.GetKey(modifier)) return false;
            return true;
        }

        /// <summary>
        /// Nothing else has the keyboard: no console, chat, prompt or menu, and no text box of any
        /// kind, whoever it belongs to.
        /// </summary>
        internal static bool CanTakeInput()
        {
            if (Console.IsVisible() || TextInput.IsVisible() || Menu.IsActive()) return false;
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Minimap.instance != null && Minimap.InTextInput()) return false;
            // The hammer's build menu search box, new in Valheim 1.0, which none of the checks
            // above know about.
            if (BuildSearchFocused()) return false;
            // Any other text box: a sign, a filter, another mod's field. Ask Unity which object is
            // selected rather than trying to know them all.
            if (AnyFieldFocused()) return false;
            return true;
        }

        private static bool BuildSearchFocused()
        {
            var hud = Hud.instance;
            if (hud == null || hud.m_buildUi == null) return false;
            try { return hud.m_buildUi.SearchFieldFocused; }
            catch { return false; }   // a build menu torn down under us mid-frame
        }

        private static bool AnyFieldFocused()
        {
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null) return false;
            // Explicit null checks: destroyed Unity objects compare equal to null, which ?. misses.
            var tmp = selected.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            var legacy = selected.GetComponent<InputField>();
            return legacy != null && legacy.isFocused;
        }
    }
}
