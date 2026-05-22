using UnityEngine;

namespace ForesakenShrines
{
    /// <summary>
    /// Attached to each shrine prefab clone.  Implements vanilla Hoverable + Interactable so
    /// the player can channel a Forsaken Power by pressing [Use] on the shrine.
    /// </summary>
    internal class ShrineInteractable : MonoBehaviour, Hoverable, Interactable
    {
        public ShrineDefinition Definition;

        // Valheim's hover UI automatically translates $token strings — no manual Localize() call needed.
        public string GetHoverName() => Definition?.DisplayName ?? "Shrine";

        public string GetHoverText()
        {
            if (Definition == null) return string.Empty;
            return $"[<color=yellow><b>$KEY_Use</b></color>] Channel {Definition.DisplayName}\n<color=#aaaaaa>The 20-minute activation cooldown still applies.</color>";
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || Definition == null) return false;
            if (user is not Player player) return false;

            player.SetGuardianPower(Definition.PowerPrefab);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        private void Start()
        {
            if (!Physics.Raycast(transform.position + Vector3.up * 100f, Vector3.down, out var hit, 200f, LayerMask.GetMask("terrain")))
                return;

            if (transform.position.y - hit.point.y < -0.1f)
            {
                // ZNet-spawned clone landed underground (ZDO position offset bug).
                // Snap root to terrain and propagate via ZNetView so the ZDO is corrected.
                var correctedPos = new Vector3(transform.position.x, hit.point.y, transform.position.z);
                transform.position = correctedPos;
                var rb = GetComponent<Rigidbody>();
                if (rb != null) rb.position = correctedPos;
                GetComponent<ZNetView>()?.GetZDO()?.SetPosition(correctedPos);
            }
        }
    }
}
