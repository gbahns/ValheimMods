using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Giving a marker a label, in the game's own name box.
    ///
    /// <para>Valheim already has a box for naming a pin: you place one by hand and type into it.
    /// Ours was a second box of its own, which looked like this mod rather than like the game,
    /// and would have had to learn the gamepad and the on-screen keyboard separately. So this
    /// opens the game's: it points the box at the marker's own pin, shows it with the current
    /// label in it, and lets vanilla do the typing.</para>
    ///
    /// <para>What comes back is caught where this mod already watches that box close -- the hook
    /// that takes on a pin the player placed by hand -- and written to the marker, so the label
    /// travels like any other change and everybody sees it.</para>
    ///
    /// <para><b>There is no clearing it from in there.</b> Vanilla reads an empty box as a
    /// cancel: pressing Escape empties the box and submits it, and a submitted empty box changes
    /// nothing. That is the game's rule and a good one, so removing a label is its own item in
    /// the marker's menu rather than a thing you do by deleting the text.</para>
    /// </summary>
    internal static class LabelEditor
    {
        private static PinKey _editing;

        /// <summary>Point the game's name box at this marker and show it. False if it cannot be found.</summary>
        internal static bool Begin(SharedPin shared)
        {
            var map = Minimap.instance;
            if (map == null || shared == null) return false;
            var pin = ClientPins.PinDataFor(shared.Key);
            if (pin == null) return false;
            // Under the marker, which is where vanilla's box appears when you place a pin -- it
            // opens under the pointer, and the pointer is on the pin you have just put down. A
            // rename is asked for from a menu beside the marker instead, so the pointer is a few
            // rows away and the marker itself is the thing to be under. Without this the box did
            // not move at all and sat in the middle of the screen, where the game had left it.
            if (!Access.ShowNameInput(map, shared.Name ?? "", Spot(pin))) return false;
            // The box writes to whatever pin this names when it is accepted, which is how vanilla
            // drives it: it sets this to the pin it has just placed.
            Access.NamePin(map) = pin;
            _editing = shared.Key;
            return true;
        }

        /// <summary>
        /// The name box has closed. If this started it, write what the game put on the pin onto
        /// the marker behind it and say so, so the pin is not adopted as a new one.
        /// </summary>
        internal static bool Took(Minimap.PinData pin)
        {
            if (_editing.IsNone) return false;
            var id = _editing;
            _editing = default(PinKey);
            if (pin == null) return true;
            if (!ClientPins.TryGet(id, out var shared)) return true;
            // Vanilla has already put the text on the pin, or left it alone if the box was
            // cancelled, so the pin is the answer and there is nothing to decide here.
            string name = pin.m_name ?? "";
            if (string.Equals(shared.Name ?? "", name, System.StringComparison.Ordinal)) return true;
            ClientPins.RenameMarker(id, name);
            ClientPins.Restyle();
            TheGreatestMapMod.Message($"Marker labelled \"{name}\".");
            return true;
        }

        /// <summary>
        /// Where this marker is drawn, or the pointer if it is not drawn at all -- a marker hidden
        /// by a filter has no element on the map to be under, and the pointer is at least
        /// somewhere the player is looking.
        /// </summary>
        private static Vector2 Spot(Minimap.PinData pin)
        {
            return pin.m_uiElement != null ? Access.ScreenSpotOf(pin.m_uiElement) : (Vector2)ZInput.pointerPosition;
        }

        /// <summary>Nothing is being named; used when the map or the marker goes away.</summary>
        internal static void Forget() => _editing = default(PinKey);
    }
}
