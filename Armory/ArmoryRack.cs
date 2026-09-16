using HarmonyLib;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Attached to the Armory Rack prefab clone.  Holds the loadout data in the ZDO, so it
    /// survives world saves and is the same for everyone, and draws the rack's hover text and
    /// door animation.
    ///
    /// Deliberately Hoverable but NOT Interactable.  The rack is a real Container, and pressing
    /// [Use] is the vanilla Container's job: it owns the request-and-grant exchange with the ZDO's
    /// owner, the ownership transfer, the in-use refusal and the guard-stone and privacy checks.
    /// Taking [Use] for ourselves meant re-implementing all of that, and getting it wrong.  The
    /// loadout panel opens off the back of InventoryGui.Show instead — see ArmoryOpenPatch.
    /// Player resolves Hoverable and Interactable in two separate GetComponentInParent calls, so
    /// this keeps the rack's own hover text while the Container answers the keypress.
    /// </summary>
    public class ArmoryRack : MonoBehaviour, Hoverable
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
                m_nview.Register<bool>("ArmoryRPC_SetPrivate", RPC_SetPrivate);
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
            ApplyPrivacy();

            if (_leftHinge == null || _rightHinge == null) return;

            float target = (ArmoryUI.IsOpen && ArmoryUI.CurrentRack == this) ? 1f : 0f;
            _doorAngle01 = Mathf.MoveTowards(_doorAngle01, target, DoorOpenRate * Time.deltaTime);

            float angle = _doorAngle01 * DoorOpenDegrees;
            _leftHinge.localRotation  = Quaternion.Euler(0, -angle, 0);
            _rightHinge.localRotation = Quaternion.Euler(0,  angle, 0);
        }

        // ── Personal or shared ─────────────────────────────────────────────────────────
        //
        // Vanilla already enforces this: Container.CheckAccess gates both Container.Interact and
        // RPC_RequestOpen, and CanBeRemoved stops anyone hammering down a private container that
        // still holds items.  What vanilla has no answer for is letting a player *choose* —
        // m_privacy is a prefab field that nothing in the game ever writes, which is why the
        // Personal Chest is a separate piece rather than a setting.
        //
        // The choice lives on the rack rather than in config on purpose.  CheckAccess runs on the
        // requester AND again on the ZDO's owner, so a value held per-client would let the two
        // ends disagree about who may open what.  In the ZDO, everyone reads the same answer.
        private const string PrivateKey = "armory_private";

        /// <summary>True when the rack answers only to whoever built it.</summary>
        public bool IsPrivate =>
            m_nview != null && m_nview.IsValid() && m_nview.GetZDO().GetBool(PrivateKey, false);

        /// <summary>
        /// Who built this rack, as Valheim records it — 0 when the game never stamped anyone,
        /// which happens for pieces spawned by console or admin tools rather than placed.
        /// </summary>
        public long CreatorId
        {
            get
            {
                var piece = GetComponent<Piece>();
                return piece == null ? 0L : piece.GetCreator();
            }
        }

        /// <summary>The id vanilla compares the creator against in Container.CheckAccess.</summary>
        public static long LocalPlayerId =>
            Game.instance == null ? 0L : Game.instance.GetPlayerProfile().GetPlayerID();

        /// <summary>True when the local player is the one who built this rack.</summary>
        public bool IsBuilder
        {
            get
            {
                long creator = CreatorId;
                return creator != 0L && creator == LocalPlayerId;
            }
        }

        /// <summary>
        /// A rack nobody is recorded as having built must never be made personal.  Vanilla's rule
        /// is creator == playerID, and with a creator of 0 that is false for everyone — the rack
        /// would refuse the whole server for good, and vanilla will not let a private container
        /// holding items be removed either.  Better to refuse the switch than to brick the rack.
        /// </summary>
        public bool CanChangePrivacy => CreatorId != 0L && IsBuilder;

        public void SetPrivate(bool value)
        {
            if (m_nview == null || !m_nview.IsValid())
            {
                Jotunn.Logger.LogWarning("[Armory] SetPrivate: ZNetView not valid, ignoring");
                return;
            }
            if (value && !CanChangePrivacy)
            {
                Jotunn.Logger.LogWarning($"[Armory] SetPrivate refused: creator={CreatorId}, me={LocalPlayerId}");
                return;
            }
            if (m_nview.IsOwner()) m_nview.GetZDO().Set(PrivateKey, value);
            else                   m_nview.InvokeRPC("ArmoryRPC_SetPrivate", value);
            ApplyPrivacy();
            Jotunn.Logger.LogInfo($"[Armory] SetPrivate({value}) — isOwner={m_nview.IsOwner()}");
        }

        private void RPC_SetPrivate(long sender, bool value)
        {
            if (!m_nview.IsOwner()) return;
            m_nview.GetZDO().Set(PrivateKey, value);
        }

        /// <summary>
        /// Mirror the ZDO onto the Container field vanilla actually reads.  Done every frame
        /// rather than once, because the value can change under us — the builder toggling it on
        /// another client, or simply the ZDO arriving after Awake has already run.
        /// </summary>
        private void ApplyPrivacy()
        {
            var storage = GetStorage();
            if (storage == null) return;
            var want = IsPrivate ? Container.PrivacySetting.Private : Container.PrivacySetting.Public;
            if (storage.m_privacy != want) storage.m_privacy = want;
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
                (IsPrivate
                    ? "<color=#ffb14e>Personal</color> <color=#aaaaaa>— only you can open it</color>"
                    : "<color=#aaaaaa>Shared — anyone can open it</color>"));
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
