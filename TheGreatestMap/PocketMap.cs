using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheGreatestMap
{
    /// <summary>
    /// The pocket map: no inventory item, just player state. Taking it out sheathes both hand
    /// items (vanilla HideHandItems, which remembers them), shows a parchment in the left hand
    /// and a pencil in the right, and flags the player's ZDO so other clients show the same.
    /// Attacking, equipping a hand item, drawing weapons, taking damage, swimming, teleporting
    /// or dying puts it away.
    /// </summary>
    internal static class PocketMap
    {
        internal static bool IsOut { get; private set; }
        internal static bool Busy => _busy;

        private static bool _busy;
        private static GameObject[] _localVisuals;
        private static Material _mapMaterial;
        private static readonly int MapOutHash = "TGM_MapOut".GetStableHashCode();
        private static readonly Dictionary<Player, GameObject[]> _remoteVisuals = new Dictionary<Player, GameObject[]>();
        private static float _nextRemote, _nextUv;

        internal static void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                if (IsOut) ResetState();
                return;
            }

            if (Keys.IsDown(TgmConfig.TakeOutMapKey.Value) && Keys.CanTakeInput())
                Toggle(player);

            if (IsOut)
            {
                if (player.IsDead() || player.IsTeleporting() || (player.IsSwimming() && !player.IsOnGround()))
                    PutAway(player);
                else if (Time.time >= _nextUv)
                {
                    _nextUv = Time.time + 0.5f;
                    UpdateMapView(player);
                }
            }

            if (Time.time >= _nextRemote)
            {
                _nextRemote = Time.time + 0.5f;
                UpdateRemotePlayers(player);
                if (IsOut) OfferMapExchanges(player);
            }
        }

        /// <summary>Two players standing together with their maps out compare and merge them.</summary>
        private static void OfferMapExchanges(Player local)
        {
            float radius = TgmConfig.ExchangeRadius.Value;
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null || p == local) continue;
                if (Vector3.Distance(p.transform.position, local.transform.position) > radius) continue;
                var nview = Access.NView(p);
                if (nview == null || !nview.IsValid() || nview.GetZDO().GetInt(MapOutHash) != 1) continue;
                SyncEngine.TryExchange(p);
            }
        }

        internal static void Toggle(Player player)
        {
            if (IsOut) PutAway(player);
            else TakeOut(player);
        }

        internal static void TakeOut(Player player)
        {
            if (IsOut || player == null) return;
            if (player.IsDead() || player.IsTeleporting() || player.InAttack() || player.InDodge() || player.IsAttached())
                return;
            _busy = true;
            try
            {
                player.HideHandItems();
                IsOut = true;
                SetFlag(player, 1);
                if (TgmConfig.ShowMapInHands.Value)
                    _localVisuals = CreateVisuals(player, out _mapMaterial);
                if (TgmConfig.MapPose.Value > 0 && Access.ZAnim(player) != null)
                    Access.ZAnim(player).SetInt("crafting", TgmConfig.MapPose.Value);
                TheGreatestMapMod.Message("You unfold your map.");
            }
            finally { _busy = false; }
        }

        internal static void PutAway(Player player, bool showHands = true)
        {
            if (!IsOut) return;
            _busy = true;
            try
            {
                IsOut = false;
                if (player != null) SetFlag(player, 0);
                DestroyLocalVisuals();
                if (player != null && TgmConfig.MapPose.Value > 0 && Access.ZAnim(player) != null)
                    Access.ZAnim(player).SetInt("crafting", 0);
                if (showHands && player != null && !player.IsDead())
                    Access.ShowHandItems(player);
                TheGreatestMapMod.Message("You fold up your map.");
            }
            finally { _busy = false; }
        }

        /// <summary>Vanilla is about to draw the hidden hand items itself (R key, and so on).</summary>
        internal static void OnVanillaShowHands(Player player)
        {
            if (_busy || !IsOut) return;
            PutAway(player, showHands: false);
        }

        internal static void ResetState()
        {
            IsOut = false;
            _busy = false;
            DestroyLocalVisuals();
            foreach (var kv in _remoteVisuals) DestroyObjects(kv.Value);
            _remoteVisuals.Clear();
        }

        private static void SetFlag(Player player, int value)
        {
            var nview = Access.NView(player);
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            nview.GetZDO().Set(MapOutHash, value);
        }

        // ── visuals ─────────────────────────────────────────────────────────────────

        private static GameObject[] CreateVisuals(Humanoid humanoid, out Material mapMaterial)
        {
            mapMaterial = null;
            var vis = Access.VisEquip(humanoid);
            if (vis == null) return null;
            var list = new List<GameObject>();
            if (vis.m_leftHand != null)
            {
                var map = BuildMap(vis.m_leftHand, out mapMaterial);
                if (map != null) list.Add(map);
            }
            if (vis.m_rightHand != null)
            {
                var pencil = BuildPencil(vis.m_rightHand);
                if (pencil != null) list.Add(pencil);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        private static GameObject BuildMap(Transform parent, out Material material)
        {
            material = MakeMaterial();
            if (material == null) return null;
            var go = BuildPrimitive(parent, "TGM_PocketMap",
                TgmConfig.ParseVector(TgmConfig.MapOffset.Value, new Vector3(0f, 0.08f, 0.02f)),
                TgmConfig.ParseVector(TgmConfig.MapRotation.Value, new Vector3(0f, 90f, 0f)),
                TgmConfig.ParseVector(TgmConfig.MapScale.Value, new Vector3(0.28f, 0.2f, 0.004f)),
                material);
            if (Minimap.instance != null && Minimap.instance.m_mapTexture != null)
                material.mainTexture = Minimap.instance.m_mapTexture;
            return go;
        }

        /// <summary>
        /// The pencil is a vanilla held item's model (default: the Club) scaled down and placed the
        /// way VisEquipment places that item, so it sits in the grip and points the right way. A
        /// hand-built primitive centered on the wrist bone ends up inside the hand mesh.
        /// </summary>
        private static GameObject BuildPencil(Transform parent)
        {
            var go = AttachVanillaVisual(parent, TgmConfig.PencilItem.Value);
            if (go == null)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not attach pencil model '{TgmConfig.PencilItem.Value}'; using a plain stick instead.");
                var material = MakeMaterial();
                if (material == null) return null;
                if (material.HasProperty("_Color")) material.color = new Color(0.55f, 0.38f, 0.2f);
                return BuildPrimitive(parent, "TGM_Pencil", new Vector3(0f, 0f, 0.08f), Vector3.zero, new Vector3(0.02f, 0.02f, 0.16f), material);
            }
            go.name = "TGM_Pencil";
            go.transform.localPosition += TgmConfig.ParseVector(TgmConfig.PencilPositionTweak.Value, Vector3.zero);
            go.transform.localRotation *= Quaternion.Euler(TgmConfig.ParseVector(TgmConfig.PencilRotationTweak.Value, Vector3.zero));
            go.transform.localScale = TgmConfig.ParseVector(TgmConfig.PencilSize.Value, new Vector3(0.25f, 0.25f, 0.25f));
            return go;
        }

        /// <summary>Instantiates a vanilla item's held model (its "attach" child) under a hand bone, mirroring VisEquipment.AttachItem.</summary>
        private static GameObject AttachVanillaVisual(Transform joint, string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || ObjectDB.instance == null) return null;
            var prefab = ObjectDB.instance.GetItemPrefab(itemName.Trim());
            if (prefab == null) return null;
            Transform attach = null;
            for (int i = 0; i < prefab.transform.childCount; i++)
            {
                var child = prefab.transform.GetChild(i);
                if (child.name == "attach") { attach = child; break; }
            }
            if (attach == null) return null;
            var go = Object.Instantiate(attach.gameObject);
            go.SetActive(true);
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var light in go.GetComponentsInChildren<Light>(true)) light.enabled = false;
            go.transform.SetParent(joint);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var equipOffset = prefab.transform.Find("equipoffset");
            if (equipOffset != null)
            {
                go.transform.localPosition += equipOffset.position;
                go.transform.localRotation *= equipOffset.rotation;
            }
            return go;
        }

        private static GameObject BuildPrimitive(Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
            go.layer = parent.gameObject.layer;
            return go;
        }

        /// <summary>A material that renders in Valheim: cloned from a vanilla item, or a built-in shader as a fallback.</summary>
        private static Material MakeMaterial()
        {
            Material baseMaterial = null;
            try
            {
                var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Wood") : null;
                var renderer = prefab != null ? prefab.GetComponentInChildren<Renderer>() : null;
                if (renderer != null) baseMaterial = renderer.sharedMaterial;
            }
            catch { /* fall through to built-in shader */ }

            if (baseMaterial != null) return new Material(baseMaterial) { name = "TGM_Material" };
            var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Sprites/Default");
            return shader != null ? new Material(shader) { name = "TGM_Material" } : null;
        }

        private static void UpdateMapView(Player player)
        {
            if (_mapMaterial == null || Minimap.instance == null) return;
            Access.WorldToMapPoint(Minimap.instance, player.transform.position, out float mx, out float my);
            float s = Mathf.Clamp(TgmConfig.MapViewFraction.Value, 0.02f, 1f);
            _mapMaterial.mainTextureScale = new Vector2(s, s);
            _mapMaterial.mainTextureOffset = new Vector2(mx - s * 0.5f, my - s * 0.5f);
        }

        private static void DestroyLocalVisuals()
        {
            DestroyObjects(_localVisuals);
            _localVisuals = null;
            _mapMaterial = null;
        }

        private static void DestroyObjects(GameObject[] objects)
        {
            if (objects == null) return;
            foreach (var go in objects) if (go != null) Object.Destroy(go);
        }

        private static void UpdateRemotePlayers(Player local)
        {
            var players = Player.GetAllPlayers();
            if (_remoteVisuals.Count > 0)
            {
                var gone = new List<Player>();
                foreach (var kv in _remoteVisuals)
                    if (kv.Key == null || !players.Contains(kv.Key)) gone.Add(kv.Key);
                foreach (var p in gone)
                {
                    DestroyObjects(_remoteVisuals[p]);
                    _remoteVisuals.Remove(p);
                }
            }
            if (!TgmConfig.ShowMapInHands.Value) return;
            foreach (var p in players)
            {
                if (p == null || p == local) continue;
                var nview = Access.NView(p);
                if (nview == null || !nview.IsValid()) continue;
                bool isOut = nview.GetZDO().GetInt(MapOutHash) == 1;
                bool shown = _remoteVisuals.ContainsKey(p);
                if (isOut && !shown)
                {
                    var visuals = CreateVisuals(p, out _);
                    if (visuals != null) _remoteVisuals[p] = visuals;
                }
                else if (!isOut && shown)
                {
                    DestroyObjects(_remoteVisuals[p]);
                    _remoteVisuals.Remove(p);
                }
            }
        }

        internal static bool IsHandItem(ItemDrop.ItemData item)
        {
            if (item == null) return false;
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                    return true;
                default:
                    return false;
            }
        }
    }

    // ── patches ────────────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Humanoid), "ShowHandItems")]
    internal static class Humanoid_ShowHandItems_Patch
    {
        private static void Prefix(Humanoid __instance)
        {
            if (PocketMap.IsOut && __instance == Player.m_localPlayer)
                PocketMap.OnVanillaShowHands((Player)__instance);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class Humanoid_StartAttack_Patch
    {
        private static bool Prefix(Humanoid __instance, ref bool __result)
        {
            if (!PocketMap.IsOut || PocketMap.Busy || __instance != Player.m_localPlayer) return true;
            PocketMap.PutAway((Player)__instance);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class Humanoid_EquipItem_Patch
    {
        private static void Prefix(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (!PocketMap.IsOut || PocketMap.Busy || __instance != Player.m_localPlayer) return;
            if (PocketMap.IsHandItem(item)) PocketMap.PutAway((Player)__instance, showHands: false);
        }
    }

    // Character.Damage only forwards an RPC to the owner; OnDamaged runs on the victim's own
    // client, which is where the local player's map state lives.
    [HarmonyPatch(typeof(Player), "OnDamaged")]
    internal static class Player_OnDamaged_Patch
    {
        private static void Postfix(Player __instance, HitData hit)
        {
            if (__instance != Player.m_localPlayer || hit == null || hit.GetTotalDamage() <= 0f) return;
            Recorder.NoteHit(); // writing pauses for a moment whether or not the map was out
            if (!PocketMap.IsOut || PocketMap.Busy) return;
            PocketMap.PutAway(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class Player_OnDeath_Patch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) PocketMap.ResetState();
        }
    }
}
