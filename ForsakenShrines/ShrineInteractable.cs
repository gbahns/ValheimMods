using UnityEngine;

namespace ForsakenShrines
{
    /// <summary>
    /// Attached to each shrine prefab clone.  Implements vanilla Hoverable + Interactable so
    /// the player can mount the boss's trophy on the shrine and then channel the corresponding
    /// Forsaken Power.  Mounting requires the player's currently selected guardian power to
    /// match this shrine — which transitively requires the trophy to have been mounted at the
    /// world-center BossStone first (vanilla's selection UI only lists unlocked powers).
    /// </summary>
    internal class ShrineInteractable : MonoBehaviour, Hoverable, Interactable
    {
        public ShrineDefinition Definition;

        // ZDO key: per-instance flag for "trophy has been placed here".  Persists across saves.
        private const string ZdoKey_TrophyMounted = "ForsakenShrines_TrophyMounted";

        private ZNetView _znv;

        private void Awake()
        {
            _znv = GetComponent<ZNetView>();
        }

        private bool IsTrophyMounted()
        {
            if (_znv == null || !_znv.IsValid()) return false;
            return _znv.GetZDO().GetBool(ZdoKey_TrophyMounted, false);
        }

        // Valheim 1.0: new Hoverable member (vertical hover-text offset); 0 matches vanilla default.
        public float GetHoverOffset() => 0f;

        public string GetHoverName()
        {
            if (Definition == null) return "Shrine";
            return IsTrophyMounted()
                ? Definition.DisplayName
                : $"{Definition.DisplayName} (inactive)";
        }

        public string GetHoverText()
        {
            if (Definition == null) return string.Empty;
            if (IsTrophyMounted())
            {
                return $"[<color=yellow><b>$KEY_Use</b></color>] Channel its power\n<color=#aaaaaa>The 20-minute activation cooldown still applies.</color>";
            }
            string trophyDisplay = GetTrophyDisplayName();
            return $"[<color=yellow><b>$KEY_Use</b></color>] Place {trophyDisplay}\n<color=#aaaaaa>Requires the matching Forsaken Power to be your active selection.</color>";
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Definition == null) return false;
            if (user is not Player player) return false;

            if (IsTrophyMounted())
            {
                player.SetGuardianPower(Definition.PowerPrefab);
                return true;
            }

            // Trophy not yet mounted — try to mount.  Require the shrine's power to be
            // currently selected as the player's active Forsaken Power.  This transitively
            // requires the trophy to have been mounted at the world-center BossStone, since
            // vanilla's power-selection UI only lists unlocked powers.
            if (player.GetGuardianPowerName() != Definition.PowerPrefab)
            {
                player.Message(MessageHud.MessageType.Center,
                    $"Select {Definition.DisplayName}'s power at the world center first.");
                return false;
            }

            var inv = player.GetInventory();
            if (inv == null || !inv.HaveItem(Definition.IconItem))
            {
                player.Message(MessageHud.MessageType.Center,
                    $"Requires {GetTrophyDisplayName()}.");
                return false;
            }

            inv.RemoveItem(Definition.IconItem, 1);
            _znv.GetZDO().Set(ZdoKey_TrophyMounted, true);
            player.Message(MessageHud.MessageType.Center,
                $"Trophy placed on {Definition.DisplayName}.");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // Localized display name of the boss trophy (e.g. "Eikthyr Trophy") — falls back to the
        // prefab name if the item isn't found in ObjectDB.
        private string GetTrophyDisplayName()
        {
            if (Definition == null) return "trophy";
            var prefab = ObjectDB.instance?.GetItemPrefab(Definition.IconItem);
            var sharedName = prefab?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name;
            return string.IsNullOrEmpty(sharedName) ? Definition.IconItem : sharedName;
        }
    }
}
