using HarmonyLib;
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

        // Door animation — purely local visual (no ZDO sync).  Hinges are child GameObjects
        // created in ArmoryPieces.BuildArmoireMesh; we find them by name in Awake.
        private Transform _leftHinge;
        private Transform _rightHinge;
        private float     _doorAngle01;        // 0 = closed, 1 = fully open
        private const float DoorOpenRate   = 4f;   // 1/seconds — full swing in ~0.25s
        private const float DoorOpenDegrees = 95f; // slightly past 90 reads more naturally

        private void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            if (m_nview != null)
                m_nview.Register<string>("ArmoryRPC_SetData", RPC_SetData);

            _leftHinge  = transform.Find("ArmoryDoorLeft");
            _rightHinge = transform.Find("ArmoryDoorRight");
        }

        // The Container component attached to the same GameObject (added in Phase 2).  Used by
        // the UI to show the rack's storage grid alongside the player inventory, and by the
        // LoadoutManager to source loadout items from the rack as well as the player's inv.
        public Container GetStorage() => GetComponent<Container>();
        public Inventory  GetStorageInventory() => GetStorage()?.GetInventory();

        private void Update()
        {
            if (_leftHinge == null || _rightHinge == null) return;

            float target = (ArmoryUI.IsOpen && ArmoryUI.CurrentRack == this) ? 1f : 0f;
            _doorAngle01 = Mathf.MoveTowards(_doorAngle01, target, DoorOpenRate * Time.deltaTime);

            float angle = _doorAngle01 * DoorOpenDegrees;
            _leftHinge.localRotation  = Quaternion.Euler(0, -angle, 0);
            _rightHinge.localRotation = Quaternion.Euler(0,  angle, 0);
        }

        public string GetHoverName() => "Armory";

        public string GetHoverText()
        {
            if (m_nview == null || !m_nview.IsValid()) return string.Empty;
            // Valheim's hover UI shows the result of this method verbatim — it does not run
            // localization on it for us.  Vanilla Localization.instance.Localize() expands BOTH
            // $-translation tokens AND $KEY_* key-binding tokens into the player's actual bound
            // glyph (e.g. "[E]").  Jotunn's LocalizationManager only handles the former, which
            // is why $KEY_Use stayed raw through it.
            return LocalizeText(
                "Armory\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] Manage Loadouts\n" +
                "<color=#aaaaaa>Save and recall named equipment sets.</color>");
        }

        // The vanilla Localization class isn't in any of the assemblies we reference at compile
        // time (it's loaded into the game process at runtime, but not in assembly_valheim_*.dll).
        // Use HarmonyLib's AccessTools to find it across loaded assemblies at runtime.
        private static string LocalizeText(string text)
        {
            try
            {
                var locType = AccessTools.TypeByName("Localization");
                if (locType == null) return text;
                var instance = AccessTools.Property(locType, "instance")?.GetValue(null)
                            ?? AccessTools.Field(locType, "instance")?.GetValue(null);
                if (instance == null) return text;
                var localize = AccessTools.Method(locType, "Localize", new[] { typeof(string) });
                return localize?.Invoke(instance, new object[] { text }) as string ?? text;
            }
            catch { return text; }
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
            {
                Jotunn.Logger.LogWarning("[Armory] GetData: ZNetView not valid, returning empty data");
                return new ArmoryData();
            }
            var text = m_nview.GetZDO().GetString("armory_data", "");
            Jotunn.Logger.LogInfo($"[Armory] GetData: read {text.Length} bytes from ZDO");
            if (string.IsNullOrEmpty(text))
                return new ArmoryData();
            try
            {
                var data = ArmorySerializer.Deserialize(text);
                Jotunn.Logger.LogInfo($"[Armory] GetData: parsed {data.Slots.Count} slot(s)");
                return data;
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogError($"[Armory] GetData parse error: {e.Message}");
                return new ArmoryData();
            }
        }

        public void SaveData(ArmoryData data)
        {
            if (m_nview == null || !m_nview.IsValid())
            {
                Jotunn.Logger.LogWarning("[Armory] SaveData: ZNetView not valid, dropping save");
                return;
            }
            // Use our own line-based serializer — UnityEngine.JsonUtility silently produces "{}"
            // for our types in this Valheim runtime (stripped JsonSerializeModule).
            var text = ArmorySerializer.Serialize(data);
            // In singleplayer the local client is always the ZDO owner — write directly so we
            // don't depend on RPC self-routing.  Only fall through to RPC for non-owner clients
            // (multiplayer guests pushing changes to the host).
            if (m_nview.IsOwner())
            {
                m_nview.GetZDO().Set("armory_data", text);
                Jotunn.Logger.LogInfo($"[Armory] SaveData: direct write ({text.Length} bytes) — isOwner=true");
            }
            else
            {
                m_nview.InvokeRPC("ArmoryRPC_SetData", text);
                Jotunn.Logger.LogInfo($"[Armory] SaveData: RPC to owner ({text.Length} bytes) — isOwner=false");
            }
        }

        private void RPC_SetData(long sender, string json)
        {
            if (!m_nview.IsOwner()) return;
            m_nview.GetZDO().Set("armory_data", json);
            Jotunn.Logger.LogInfo($"[Armory] RPC_SetData: applied {json.Length} bytes");
        }
    }
}
