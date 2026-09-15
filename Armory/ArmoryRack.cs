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
            {
                m_nview.Register<string>("ArmoryRPC_SetData", RPC_SetData);
                m_nview.Register<long>("ArmoryRPC_RequestOpen", RPC_RequestOpen);
                m_nview.Register<bool>("ArmoryRPC_OpenResponse", RPC_OpenResponse);
            }

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
            if (_requestSentAt >= 0f && Time.unscaledTime - _requestSentAt > OpenResponseTimeout)
                TakeOwnershipAndOpen();

            if (_leftHinge == null || _rightHinge == null) return;

            float target = (ArmoryUI.IsOpen && ArmoryUI.CurrentRack == this) ? 1f : 0f;
            _doorAngle01 = Mathf.MoveTowards(_doorAngle01, target, DoorOpenRate * Time.deltaTime);

            float angle = _doorAngle01 * DoorOpenDegrees;
            _leftHinge.localRotation  = Quaternion.Euler(0, -angle, 0);
            _rightHinge.localRotation = Quaternion.Euler(0,  angle, 0);
        }

        public string GetHoverName() => "Armory";
        public float  GetHoverOffset() => 0f; // Valheim 1.0: new Hoverable member (vertical hover-text offset)

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

        // ── Opening the rack ───────────────────────────────────────────────────────────
        //
        // Vanilla never opens a container straight out of Interact.  Container.Interact asks the
        // ZDO's owner for it, the owner hands the ZDO over and answers, and only that answer calls
        // InventoryGui.Show.  The handshake is not ceremony: InventoryGui.UpdateContainer draws
        // the container panel only while m_currentContainer.IsOwner(), and Container.OnContainerChanged
        // saves the inventory only if (IsOwner()).  Opening the rack directly therefore gave a
        // player who did not happen to own the ZDO a loadout panel with no storage grid beside it,
        // and quietly dropped anything moved into the rack — including gear Load swapped back in.
        // So we mirror the vanilla exchange with our own RPC pair.

        // The catch: this mod is [BepInProcess("valheim.exe")] and so can never load on a dedicated
        // server, which owns every persisted ZDO after a restart.  A request addressed to the
        // server is relayed to a machine with no armory_rack prefab, no ZNetView and therefore no
        // handler, and is dropped in silence — the rack would simply never open.  So the request
        // is given a deadline, and when it runs out we claim the ZDO ourselves and open anyway.
        // ClaimOwnership is vanilla's own public call for this; what it gives up against the full
        // exchange is the owner's ForceSendZDO, so it is the fallback and not the first move.
        private const float OpenResponseTimeout = 0.6f;
        private float _requestSentAt = -1f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user is not Player) return false;
            if (m_nview == null || !m_nview.IsValid()) return false;

            var storage = GetStorage();
            if (storage != null && storage.m_checkGuardStone && !PrivateArea.CheckAccess(transform.position))
                return true;

            long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
            if (!CheckContainerAccess(playerID))
            {
                user.Message(MessageHud.MessageType.Center, "$msg_cantopen");
                return true;
            }

            // Already ours: nothing to ask for, and no round trip to wait through.
            if (m_nview.IsOwner())
            {
                ArmoryUI.Open(this);
                return true;
            }

            // Someone else has it open.  Read the ZDO rather than Container.IsInUse(), which
            // returns a local field the owner alone keeps current — a non-owner's copy is always
            // false.  The owner publishes the flag to the ZDO (it is what draws the open-chest
            // visual for everyone else), so this is the one form of the answer we can trust here,
            // and it still holds when the owner is in no position to answer for itself.
            if (m_nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1)
            {
                user.Message(MessageHud.MessageType.Center, "$msg_inuse");
                return true;
            }

            _requestSentAt = Time.unscaledTime;
            m_nview.InvokeRPC("ArmoryRPC_RequestOpen", playerID);
            return true;
        }

        /// <summary>Nobody answered — almost certainly a dedicated server holding the ZDO. Take it and open.</summary>
        private void TakeOwnershipAndOpen()
        {
            _requestSentAt = -1f;
            if (m_nview == null || !m_nview.IsValid()) return;
            Jotunn.Logger.LogInfo("[Armory] No answer to RequestOpen — claiming the ZDO and opening anyway.");
            m_nview.ClaimOwnership();
            ArmoryUI.Open(this);
        }

        /// <summary>Owner side: grant or refuse, and hand the ZDO over before answering yes.</summary>
        private void RPC_RequestOpen(long uid, long playerID)
        {
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner()) return;

            var storage = GetStorage();
            if (storage != null && storage.IsInUse() && uid != ZNet.GetUID())
            {
                Jotunn.Logger.LogInfo($"[Armory] RequestOpen from {uid}: refused, rack in use");
                m_nview.InvokeRPC(uid, "ArmoryRPC_OpenResponse", false);
                return;
            }
            if (!CheckContainerAccess(playerID))
            {
                Jotunn.Logger.LogInfo($"[Armory] RequestOpen from {uid}: refused, not theirs");
                m_nview.InvokeRPC(uid, "ArmoryRPC_OpenResponse", false);
                return;
            }

            ZDOMan.instance.ForceSendZDO(uid, m_nview.GetZDO().m_uid);
            m_nview.GetZDO().SetOwner(uid);
            m_nview.InvokeRPC(uid, "ArmoryRPC_OpenResponse", true);
            Jotunn.Logger.LogInfo($"[Armory] RequestOpen from {uid}: granted, ZDO handed over");
        }

        /// <summary>Requester side: we own the ZDO now, so the storage grid will draw and save.</summary>
        private void RPC_OpenResponse(long uid, bool granted)
        {
            bool waiting = _requestSentAt >= 0f;
            _requestSentAt = -1f;

            // An answer that arrives after the deadline has already been acted on.  Re-opening
            // would tear the panel down and rebuild it under the player's cursor.
            if (!waiting) return;
            if (Player.m_localPlayer == null) return;

            if (granted) ArmoryUI.Open(this);
            else Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_inuse");
        }

        /// <summary>
        /// Container.CheckAccess is private, so this repeats its rule: public is open to all,
        /// private is the builder's alone, and group is refused the same way vanilla refuses it.
        /// </summary>
        private bool CheckContainerAccess(long playerID)
        {
            var storage = GetStorage();
            if (storage == null) return true;
            switch (storage.m_privacy)
            {
                case Container.PrivacySetting.Public:
                    return true;
                case Container.PrivacySetting.Private:
                    var piece = GetComponent<Piece>();
                    return piece != null && piece.GetCreator() == playerID;
                default:
                    return false;
            }
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
