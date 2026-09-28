using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// One key on the map screen that answers "where is everybody?". A map carrying gold plants,
    /// orange portals and every ruin anyone has searched is a poor place to look for three moving
    /// dots, so this puts the rest of it out: the other players' markers grow, take a color of
    /// their own and pulse, and nothing else is drawn while it is on.
    ///
    /// It is a way of looking, not a change to the map. Nothing is written, nothing is shared, and
    /// putting it away brings the map back exactly as it was, including whatever the player had
    /// hidden themselves. It also ends by itself when the map closes, so it cannot be left on.
    ///
    /// Only players sharing their position can be shown, because that is all the game tells a
    /// client: vanilla's own player markers come from ZNet.GetOtherPublicPlayers, and this lights
    /// up those very markers rather than drawing any of its own.
    /// </summary>
    internal static class PlayerSpotlight
    {
        private static bool _on;

        /// <summary>True while every other marker is standing down. StylePins reads this.</summary>
        internal static bool On => _on;

        internal static void Reset() => _on = false;

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large)
            {
                if (_on) Stop(map);
                return;
            }
            var key = TgmConfig.HighlightPlayersKey;
            if (key != null && Keys.IsDown(key.Value) && Keys.CanTakeInput())
            {
                if (_on) Stop(map);
                else Start(map);
            }
            if (_on) Paint(map);
        }

        private static void Start(Minimap map)
        {
            int players = Count(map);
            if (players == 0)
            {
                // Worth saying rather than looking broken: a player who has not turned position
                // sharing on is invisible to everyone, and no key can conjure them.
                TheGreatestMapMod.Message("No other players are sharing their position.");
                return;
            }
            _on = true;
            ClientPins.Restyle();
            TheGreatestMapMod.Message(players == 1 ? "Showing 1 player; everything else is hidden." : $"Showing {players} players; everything else is hidden.");
        }

        private static void Stop(Minimap map)
        {
            _on = false;
            if (map == null) return;
            // Give back every marker we put out and every player marker we grew. Our own markers
            // are then sorted out properly by the styling pass, which knows what the player has
            // chosen to hide; this only undoes the spotlight.
            foreach (var pin in Access.Pins(map))
            {
                if (pin == null || pin.m_uiElement == null) continue;
                Show(pin, true);
                if (pin.m_type == Minimap.PinType.Player) pin.m_uiElement.localScale = Vector3.one;
            }
            ClientPins.Restyle();
        }

        /// <summary>
        /// Vanilla repaints every marker's color as it lays the map out, so the color and the size
        /// have to be put back each frame rather than set once. Portal markers are painted the same
        /// way and for the same reason.
        /// </summary>
        private static void Paint(Minimap map)
        {
            var color = Color(out float pulse);
            foreach (var pin in Access.Pins(map))
            {
                if (pin == null || pin.m_uiElement == null) continue;
                if (pin.m_type != Minimap.PinType.Player)
                {
                    if (Hiding()) Show(pin, false);
                    continue;
                }
                Show(pin, true);
                pin.m_uiElement.localScale = new Vector3(pulse, pulse, 1f);
                if (pin.m_iconElement != null) pin.m_iconElement.color = color;
                var text = pin.m_NamePinData != null ? pin.m_NamePinData.PinNameText : null;
                if (text != null) text.color = color;
            }
        }

        private static bool Hiding()
        {
            return TgmConfig.HighlightPlayersHidesOthers == null || TgmConfig.HighlightPlayersHidesOthers.Value;
        }

        private static int Count(Minimap map)
        {
            int n = 0;
            foreach (var pin in Access.Pins(map))
                if (pin != null && pin.m_type == Minimap.PinType.Player) n++;
            return n;
        }

        private static void Show(Minimap.PinData pin, bool visible)
        {
            var icon = pin.m_uiElement.gameObject;
            if (icon.activeSelf != visible) icon.SetActive(visible);
            var label = pin.m_NamePinData != null ? pin.m_NamePinData.PinNameGameObject : null;
            if (label != null && label.activeSelf != visible) label.SetActive(visible);
        }

        private static UnityEngine.Color _parsed = new UnityEngine.Color(0.4f, 0.88f, 1f, 1f);
        private static string _colorText;

        /// <summary>The color to paint the player markers, and the size to draw them at now.</summary>
        private static UnityEngine.Color Color(out float scale)
        {
            string text = TgmConfig.HighlightPlayersColor != null ? TgmConfig.HighlightPlayersColor.Value : null;
            if (text != _colorText)
            {
                _colorText = text;
                _parsed = Portals.ParseColor(text, new UnityEngine.Color(0.4f, 0.88f, 1f, 1f));
            }
            float size = TgmConfig.HighlightPlayersSize != null ? Mathf.Clamp(TgmConfig.HighlightPlayersSize.Value, 100, 400) : 170;
            // Unscaled, so the throb carries on with the map open and the game paused.
            float breath = (Mathf.Sin(Time.unscaledTime * 4f) + 1f) * 0.5f;
            scale = size / 100f * (0.92f + 0.08f * breath);
            return _parsed;
        }
    }
}
