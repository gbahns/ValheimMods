using System.Collections.Generic;
using System.Reflection;
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
    public class ArmoryRack : MonoBehaviour, Hoverable, TextReceiver
    {
        private ZNetView m_nview;

        // Door animation — purely local visual (no ZDO sync).  Hinges are GameObjects under
        // the ArmoryVisual child RackVisual builds; found by name in Awake and again when the
        // look is swapped.
        private Transform _leftHinge;
        private Transform _rightHinge;
        private float     _doorAngle01;        // 0 = closed, 1 = fully open
        private const float DoorOpenRate   = 4f;   // 1/seconds — full swing in ~0.25s
        private const float DoorOpenDegrees = 95f; // slightly past 90 reads more naturally

        /// <summary>Every rack currently in the world, for settings that reach all of them.</summary>
        internal static readonly HashSet<ArmoryRack> Instances = new HashSet<ArmoryRack>();

        // ── Which piece (#84) ──────────────────────────────────────────────────────────
        //
        // The Small Armory Rack is the same component on a second prefab: two thirds the size
        // in the world and a grid that follows the Rack Width / Rack Height settings at the
        // same proportion (8×5 at the default 12×6).  Public and unadorned on purpose: a
        // field set on the prefab reaches every spawned copy only when Unity serializes it.
        public bool Small;

        /// <summary>The small rack's size against the standard one, in the world and for its icon.</summary>
        public const float SmallScale = 2f / 3f;
        public float VisualScale => Small ? SmallScale : 1f;

        private void Awake()
        {
            Instances.Add(this);
            m_nview = GetComponent<ZNetView>();
            if (m_nview != null)
            {
                m_nview.Register<string>("ArmoryRPC_SetData", RPC_SetData);
                m_nview.Register<bool>("ArmoryRPC_SetPrivate", RPC_SetPrivate);
                m_nview.Register<string>("ArmoryRPC_SetName", RPC_SetName);
                m_nview.Register<string, string, string, string, string>("ArmoryRPC_SetStyle", RPC_SetStyle);
            }

            // The prefab was drawn with the settings' style; a rack with its own catches up in
            // its first Update, once the ZDO is there to read.
            _applied = RackVisual.Defaults;
            RefreshHinges();
        }

        // ── Style (#77) ────────────────────────────────────────────────────────────────
        //
        // A rack can be dressed in game, from the Manage Loadouts panel, and keeps that look
        // for everyone: the model, decoration, wood and crown sit in the ZDO as enum names
        // (empty = follow the Default settings), so a server full of racks can each look
        // different and a reordered enum later cannot shuffle them.
        private const string ModelKey = "armory_model";
        private const string DecorKey = "armory_decor";
        private const string WoodKey  = "armory_wood";
        private const string CurveKey = "armory_crown_curve";
        private const string CrownKey = "armory_crown_height";
        private RackStyle _applied;

        /// <summary>The model this rack was given in game, or null when it follows the setting.</summary>
        public RackModel?   OwnModel => Parse<RackModel>(ModelKey);
        public RackDecor?   OwnDecor => Parse<RackDecor>(DecorKey);
        public RackWood?    OwnWood  => Parse<RackWood>(WoodKey);
        public CrownCurve?  OwnCurve => Parse<CrownCurve>(CurveKey);
        public CrownHeight? OwnCrown => Parse<CrownHeight>(CrownKey);
        public RackModel    EffectiveModel => OwnModel ?? RackVisual.Model;
        public RackDecor    EffectiveDecor => OwnDecor ?? RackVisual.Decor;
        public RackWood     EffectiveWood  => OwnWood  ?? RackVisual.Wood;
        /// <summary>What this rack wears: its own choices where it has them, the settings elsewhere.</summary>
        public RackStyle EffectiveStyle => new RackStyle(EffectiveModel, EffectiveDecor, EffectiveWood,
                                                         OwnCurve ?? RackVisual.Curve, OwnCrown ?? RackVisual.Crown);

        private T? Parse<T>(string key) where T : struct
        {
            if (m_nview == null || !m_nview.IsValid()) return null;
            var text = m_nview.GetZDO().GetString(key, "");
            return text.Length > 0 && System.Enum.TryParse<T>(text, out var value) ? value : (T?)null;
        }

        /// <summary>Dresses this rack; null for any part means "follow the setting".</summary>
        public void SetStyle(RackModel? model, RackDecor? decor, RackWood? wood, CrownCurve? curve, CrownHeight? crown)
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            string m = model?.ToString() ?? "", d = decor?.ToString() ?? "", w = wood?.ToString() ?? "";
            string c = curve?.ToString() ?? "", h = crown?.ToString() ?? "";
            if (m_nview.IsOwner()) Store(m, d, w, c, h);
            else                   m_nview.InvokeRPC("ArmoryRPC_SetStyle", m, d, w, c, h);
        }

        private void RPC_SetStyle(long sender, string model, string decor, string wood, string curve, string crown)
        {
            if (m_nview.IsOwner()) Store(model, decor, wood, curve, crown);
        }

        private void Store(string model, string decor, string wood, string curve, string crown)
        {
            var zdo = m_nview.GetZDO();
            zdo.Set(ModelKey, model ?? "");
            zdo.Set(DecorKey, decor ?? "");
            zdo.Set(WoodKey,  wood  ?? "");
            zdo.Set(CurveKey, curve ?? "");
            zdo.Set(CrownKey, crown ?? "");
        }

        /// <summary>RackVisual says what it just drew, so Update knows when the ZDO moves away from it.</summary>
        internal void NoteApplied(RackStyle style) { _applied = style; }

        // Redraws the rack when its style changes under us: someone restyling it on another
        // client, the ZDO arriving after Awake, or a setting moving while the rack follows it.
        private void ApplyStyle()
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            if (EffectiveStyle.Same(_applied)) return;
            RackVisual.Rebuild(this);
        }

        /// <summary>Finds the door hinges again, after RackVisual has redrawn the rack.</summary>
        internal void RefreshHinges()
        {
            _leftHinge  = FindDeep(transform, RackVisual.LeftHinge);
            _rightHinge = FindDeep(transform, RackVisual.RightHinge);
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindDeep(t.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        // The Container component attached to the same GameObject (added in Phase 2).  Used by
        // the UI to show the rack's storage grid alongside the player inventory, and by the
        // LoadoutManager to source loadout items from the rack as well as the player's inv.
        // Looked up lazily rather than in Awake: ArmoryPieces removes and re-adds the
        // Container after ArmoryRack, so it is not necessarily present when Awake runs.
        // Cached because Update calls ApplyPrivacy and ApplyName -- each of which asked for
        // it -- on every rack in range on every frame.
        private Container _storage;
        public Container GetStorage() => _storage != null ? _storage : (_storage = GetComponent<Container>());

        private void OnDestroy()
        {
            Instances.Remove(this);
        }

        /// <summary>Whether an inventory is some rack's storage.  A Container makes its Inventory
        /// once, in Awake, and keeps it for life, so the object identifies the rack wherever it
        /// turns up — the open-container panel or a peek preview.</summary>
        internal static bool IsRackInventory(Inventory inventory)
        {
            if (inventory == null) return false;
            foreach (var rack in Instances)
                if (rack != null && rack.GetStorageInventory() == inventory) return true;
            return false;
        }
        public Inventory  GetStorageInventory() => GetStorage()?.GetInventory();

        private void Update()
        {
            ApplyPrivacy();
            ApplyName();
            ApplyStyle();

            if (_leftHinge == null || _rightHinge == null) return;

            float target = (ArmoryUI.IsOpen && ArmoryUI.CurrentRack == this) ? 1f : 0f;
            _doorAngle01 = Mathf.MoveTowards(_doorAngle01, target, DoorOpenRate * Time.deltaTime);

            float angle = _doorAngle01 * DoorOpenDegrees;
            _leftHinge.localRotation  = Quaternion.Euler(0, -angle, 0);
            _rightHinge.localRotation = Quaternion.Euler(0,  angle, 0);
        }

        // ── Naming ─────────────────────────────────────────────────────────────────────
        //
        // Shift+[Use] renames the rack, the way Shift is the modifier for portals and signs.
        // ArmoryRack is not the Interactable any more (the Container is, deliberately), so the
        // keypress is caught in a prefix on Container.Interact — see ArmoryRenamePatch.
        //
        // TextReceiver is vanilla's own interface for this: TextInput.RequestText hands the
        // player the same dialog a portal tag uses, and calls SetText when they confirm.
        private const string NameKey     = "armory_name";
        public  const string DefaultName = "Armory";

        // Inventory.m_name is private and set only in the constructor, so the name the vanilla
        // container panel shows has to be written through reflection.  Compiling against the
        // publicized assembly would build fine and then throw at runtime.
        private static readonly FieldInfo _inventoryName = AccessTools.Field(typeof(Inventory), "m_name");

        /// <summary>What this rack is called until someone names it: "Armory", or "Small Armory".</summary>
        public string DefaultRackName => Small ? "Small Armory" : DefaultName;

        /// <summary>The rack's name, or its default when it has not been given one.</summary>
        public string RackName
        {
            get
            {
                if (m_nview == null || !m_nview.IsValid()) return DefaultRackName;
                var stored = m_nview.GetZDO().GetString(NameKey, "");
                return string.IsNullOrEmpty(stored) ? DefaultRackName : stored;
            }
        }

        /// <summary>TextReceiver: what the rename box starts with. Empty for an unnamed rack.</summary>
        public string GetText() => m_nview != null && m_nview.IsValid()
            ? m_nview.GetZDO().GetString(NameKey, "")
            : "";

        /// <summary>TextReceiver: the player confirmed a new name.</summary>
        public void SetText(string text)
        {
            if (m_nview == null || !m_nview.IsValid()) return;
            var clean = (text ?? "").Trim();
            if (m_nview.IsOwner()) m_nview.GetZDO().Set(NameKey, clean);
            else                   m_nview.InvokeRPC("ArmoryRPC_SetName", clean);
            ApplyName();
            Jotunn.Logger.LogInfo($"[Armory] Renamed to '{clean}' — isOwner={m_nview.IsOwner()}");
        }

        private void RPC_SetName(long sender, string text)
        {
            if (!m_nview.IsOwner()) return;
            m_nview.GetZDO().Set(NameKey, text ?? "");
        }

        /// <summary>
        /// Push the name onto the Container and its Inventory, so the vanilla storage panel is
        /// headed with it too.  Kept in sync here rather than at Awake because Unity does not
        /// promise component Awake order, and because the name can change while we are stood
        /// in front of it — someone else renaming it, or the ZDO arriving late.
        /// </summary>
        private void ApplyName()
        {
            var storage = GetStorage();
            if (storage == null) return;
            string want = RackName;
            if (storage.m_name != want) storage.m_name = want;

            var inv = storage.GetInventory();
            if (inv != null && _inventoryName != null && (inv.GetName() ?? "") != want)
                _inventoryName.SetValue(inv, want);
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

        public string GetHoverName() => RackName;
        public float  GetHoverOffset() => 0f; // Valheim 1.0: new Hoverable member (vertical hover-text offset)

        // Hud.Update calls UpdateCrosshair every frame, and UpdateCrosshair calls
        // GetHoverText on whatever the crosshair is on -- so this runs once a frame for as
        // long as the player looks at the rack. The text only changes when the name or the
        // privacy flag changes, so it is built once and kept until one of those two moves.
        // Rebuilt when the name or privacy changes, and in any case every few seconds:
        // Localize also expands $KEY_* into the glyph for the key the player has bound, so a
        // rebind would otherwise leave a stale glyph on the cached string until relog. A
        // timeout covers that and a language change too, and still spends one build per
        // RefreshSeconds instead of one per frame.
        private const float RefreshSeconds = 5f;
        private string _hoverCache;
        private string _hoverCacheName;
        private bool   _hoverCachePrivate;
        private float  _hoverCacheAt;

        public string GetHoverText()
        {
            if (m_nview == null || !m_nview.IsValid()) return string.Empty;

            string rackName = RackName;
            bool isPriv = IsPrivate;
            if (_hoverCache != null && _hoverCacheName == rackName && _hoverCachePrivate == isPriv
                && Time.unscaledTime - _hoverCacheAt < RefreshSeconds)
                return _hoverCache;
            // Valheim's hover UI shows the result of this method verbatim — it does not run
            // localization on it for us.  Vanilla Localization.instance.Localize() expands BOTH
            // $-translation tokens AND $KEY_* key-binding tokens into the player's actual bound
            // glyph (e.g. "[E]").  Jotunn's LocalizationManager only handles the former, which
            // is why $KEY_Use stayed raw through it.
            // Another player's personal rack opens to read (ArmoryRenamePatch), and is not
            // theirs to rename.
            bool theirs = isPriv && !IsBuilder;
            _hoverCache = LocalizeText(
                rackName + "\n" +
                (theirs ? "[<color=yellow><b>$KEY_Use</b></color>] Read Loadouts\n"
                        : "[<color=yellow><b>$KEY_Use</b></color>] Manage Loadouts\n" +
                          "[<color=yellow><b>$KEY_AltPlace</b> + <b>$KEY_Use</b></color>] Rename\n") +
                (theirs ? "<color=#ffb14e>Personal</color> <color=#aaaaaa>— only its builder can open it; you can read and copy its loadouts</color>"
                 : isPriv ? "<color=#ffb14e>Personal</color> <color=#aaaaaa>— only you can open it; others can read and copy its loadouts</color>"
                          : "<color=#aaaaaa>Shared — anyone can open it</color>"));
            _hoverCacheName = rackName;
            _hoverCachePrivate = isPriv;
            _hoverCacheAt = Time.unscaledTime;
            return _hoverCache;
        }

        // Localization is a plain global type in assembly_valheim, which this project already
        // references -- CaptainsLog, Comfortometer, DiagnoseServerLag and DudeWhatAreMyStats
        // all call it directly. The comment that used to be here claimed otherwise and resolved
        // it through AccessTools.TypeByName on every call. TypeByName is not cached: it falls
        // through to scanning every type in every loaded assembly, allocating an array per
        // assembly. With fifty-odd plugins loaded that cost milliseconds -- once a frame, for
        // as long as the player looked at the rack. That is what tanked the frame rate,
        // reported 2026-11-10.
        private static string LocalizeText(string text)
        {
            try
            {
                var loc = Localization.instance;
                return loc != null ? loc.Localize(text) : text;
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
