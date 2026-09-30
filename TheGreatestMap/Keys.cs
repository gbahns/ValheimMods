using BepInEx.Configuration;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>Keybind polling through Valheim's own ZInput, plus the usual "is a GUI eating input" check.</summary>
    internal static class Keys
    {
        private static readonly KeyCode[] Modifiers =
        {
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.LeftAlt, KeyCode.RightAlt,
        };

        /// <summary>
        /// A shortcut is down when its key has just been pressed with exactly the modifiers it asks
        /// for -- no more. Checking only that the listed ones are held, as this used to, makes a
        /// plain key fire while any modifier is down: Shift+Y for the legend also took the map out
        /// on plain Y, and a plain key of ours answered another mod's chord on the same letter.
        /// This is what BepInEx's own KeyboardShortcut.IsDown does, which these hand-rolled copies
        /// had left out.
        /// </summary>
        internal static bool IsDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return false;
            if (!ZInput.GetKeyDown(shortcut.MainKey)) return false;
            foreach (var modifier in Modifiers)
            {
                if (modifier == shortcut.MainKey) continue;
                bool wanted = false;
                foreach (var asked in shortcut.Modifiers)
                    if (asked == modifier) { wanted = true; break; }
                if (wanted != ZInput.GetKey(modifier)) return false;
            }
            return true;
        }

        internal static bool CanTakeInput()
        {
            if (Console.IsVisible() || TextInput.IsVisible() || Menu.IsActive() || InventoryGui.IsVisible() || StoreGui.IsVisible())
                return false;
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Minimap.instance != null && Minimap.InTextInput()) return false;
            return true;
        }
    }
}
