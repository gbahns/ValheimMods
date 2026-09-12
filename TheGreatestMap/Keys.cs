using BepInEx.Configuration;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>Keybind polling through Valheim's own ZInput, plus the usual "is a GUI eating input" check.</summary>
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
