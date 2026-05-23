using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Attached to the Armory Rack prefab clone. Implements Hoverable + Interactable so the
    /// player can open the loadout management UI by pressing [Use] on the rack.
    /// Loadout data is persisted in the ZDO so it survives world saves and multiplayer.
    /// </summary>
    public class ArmoryRack : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView m_nview;

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            if (m_nview != null)
                m_nview.Register<string>("ArmoryRPC_SetData", RPC_SetData);
        }

        public string GetHoverName() => "Armory Rack";

        public string GetHoverText()
        {
            if (!m_nview.IsValid()) return string.Empty;
            return "[<color=yellow><b>$KEY_Use</b></color>] Manage Loadouts\n" +
                   "<color=#aaaaaa>Save and recall named equipment sets.</color>";
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user is not Player) return false;
            ArmoryUI.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public ArmoryData GetData()
        {
            if (m_nview == null || !m_nview.IsValid())
                return new ArmoryData();
            var json = m_nview.GetZDO().GetString("armory_data", "");
            if (string.IsNullOrEmpty(json))
                return new ArmoryData();
            try
            {
                return JsonUtility.FromJson<ArmoryData>(json) ?? new ArmoryData();
            }
            catch
            {
                return new ArmoryData();
            }
        }

        public void SaveData(ArmoryData data)
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            var json = JsonUtility.ToJson(data);
            // Send to ZDO owner; in singleplayer that is always the local client.
            m_nview.InvokeRPC("ArmoryRPC_SetData", json);
        }

        private void RPC_SetData(long sender, string json)
        {
            if (!m_nview.IsOwner()) return;
            m_nview.GetZDO().Set("armory_data", json);
        }
    }
}
