using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WhatsMyWorkbenchMissing
{
    /// <summary>
    /// Works out, for one crafting station, which of its upgrades the local player has built,
    /// could build, and has yet to discover, and writes that up as tooltip text.
    ///
    /// An "upgrade" is any build piece carrying a StationExtension whose target station has the
    /// station's name: the chopping block and tanning rack for the workbench, the bellows and
    /// anvils for the forge, and whatever other mods add. The pieces are found by walking every
    /// build tool's piece table in the ObjectDB, so an extension only the hoe or a modded hammer
    /// offers is still counted. Whether one is attached is decided exactly as the game decides
    /// the station's level: StationExtension.FindExtensions, the same call GetLevel makes.
    ///
    /// The text is left with the game's $tokens in it (piece and item names); UITooltip runs it
    /// through Localization when it shows it.
    /// </summary>
    internal static class UpgradeReport
    {
        private const string ColorReady   = "#9be27a";  // buildable with what you carry
        private const string ColorKnown   = "#ffb866";  // known, but short of materials
        private const string ColorShort   = "#ff5e5e";  // an amount you are short of
        private const string ColorDim     = "#9a9a9a";  // built already, or the undiscovered count

        // Extension pieces grouped by the station name they extend, built once per ObjectDB.
        private static Dictionary<string, List<Piece>> _byStation;
        private static ObjectDB _indexedDb;
        private static int _indexedItemCount = -1;

        private static readonly List<StationExtension> _attached = new List<StationExtension>();
        private static readonly HashSet<string> _builtNames = new HashSet<string>();
        private static readonly List<Piece> _ready = new List<Piece>();
        private static readonly List<Piece> _known = new List<Piece>();
        private static readonly List<Piece> _built = new List<Piece>();

        /// <summary>The tooltip topic and body for this station as things stand right now.</summary>
        internal static void Build(CraftingStation station, Player player, out string topic, out string text)
        {
            int level = station.GetLevel();
            topic = $"{station.m_name} level {level}";

            var index = Index();
            List<Piece> all = null;
            if (index != null) index.TryGetValue(station.m_name, out all);
            if (all == null || all.Count == 0)
            {
                text = "This station has no upgrades.";
                return;
            }

            _attached.Clear();
            StationExtension.FindExtensions(station, station.transform.position, _attached);
            _builtNames.Clear();
            foreach (var ext in _attached)
            {
                var piece = ext ? ext.GetComponent<Piece>() : null;
                if (piece != null) _builtNames.Add(piece.m_name);
            }

            _ready.Clear();
            _known.Clear();
            _built.Clear();
            int undiscovered = 0;
            foreach (var piece in all)
            {
                if (_builtNames.Contains(piece.m_name)) { _built.Add(piece); continue; }
                if (!piece.m_enabled) continue;          // not in this game at all
                if (!IsKnown(piece, player)) { undiscovered++; continue; }
                if (player.HaveRequirements(piece, Player.RequirementMode.CanBuild)) _ready.Add(piece);
                else _known.Add(piece);
            }

            var inventory = player.GetInventory();
            var sb = new StringBuilder();
            if (_ready.Count > 0)
            {
                sb.Append("<color=").Append(ColorReady).Append(">Ready to build:</color>\n");
                foreach (var piece in _ready) AppendPiece(sb, piece, inventory, ColorReady);
            }
            if (_known.Count > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<color=").Append(ColorKnown).Append(">Missing materials:</color>\n");
                foreach (var piece in _known) AppendPiece(sb, piece, inventory, ColorKnown);
            }
            if (_ready.Count == 0 && _known.Count == 0)
            {
                if (undiscovered == 0) sb.Append("Fully upgraded.\n");
                else if (_built.Count == 0) sb.Append("No upgrades discovered yet.\n");
                else sb.Append("Every upgrade you know is built.\n");
            }
            if (WhatsMyWorkbenchMissingMod.ShowUndiscovered.Value && undiscovered > 0)
            {
                sb.Append("<color=").Append(ColorDim).Append('>')
                  .Append(undiscovered).Append(undiscovered == 1 ? " more upgrade" : " more upgrades")
                  .Append(" not yet discovered.</color>\n");
            }
            if (WhatsMyWorkbenchMissingMod.ShowBuilt.Value && _built.Count > 0)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<color=").Append(ColorDim).Append(">Built:\n");
                foreach (var piece in _built) sb.Append("  ").Append(piece.m_name).Append('\n');
                sb.Append("</color>");
            }
            text = sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Known the way the hammer menu means it: the piece is in the player's recipe list (which
        /// the game fills in as the materials are discovered), and the station it is built at is
        /// known and any DLC it needs is present. NoCostCheat shows everything, as in the menu.
        /// </summary>
        private static bool IsKnown(Piece piece, Player player)
        {
            if (player.NoCostCheat()) return true;
            return player.IsRecipeKnown(piece.m_name)
                && player.HaveRequirements(piece, Player.RequirementMode.IsKnown);
        }

        private static void AppendPiece(StringBuilder sb, Piece piece, Inventory inventory, string nameColor)
        {
            sb.Append("  <color=").Append(nameColor).Append('>').Append(piece.m_name).Append("</color>");
            if (WhatsMyWorkbenchMissingMod.ShowMaterials.Value)
            {
                bool first = true;
                foreach (var req in piece.m_resources)
                {
                    if (req.m_resItem == null || req.m_amount <= 0) continue;
                    string itemName = req.m_resItem.m_itemData.m_shared.m_name;
                    int have = inventory != null ? inventory.CountItems(itemName) : 0;
                    sb.Append(first ? "   " : ", ");
                    first = false;
                    if (have >= req.m_amount)
                    {
                        sb.Append(req.m_amount).Append(' ').Append(itemName);
                    }
                    else
                    {
                        sb.Append("<color=").Append(ColorShort).Append('>')
                          .Append(have).Append('/').Append(req.m_amount).Append(' ').Append(itemName)
                          .Append("</color>");
                    }
                }
            }
            sb.Append('\n');
        }

        /// <summary>
        /// Every StationExtension piece any build tool offers, keyed by the name of the station it
        /// extends. Rebuilt when the ObjectDB is replaced (a new world) or grows (a mod adding
        /// items after we first looked).
        /// </summary>
        private static Dictionary<string, List<Piece>> Index()
        {
            var db = ObjectDB.instance;
            if (db == null) return null;
            if (_byStation != null && _indexedDb == db && _indexedItemCount == db.m_items.Count) return _byStation;

            var index = new Dictionary<string, List<Piece>>();
            var seenPrefabs = new HashSet<GameObject>();
            var seenNames = new HashSet<string>();
            foreach (var item in db.m_items)
            {
                var drop = item ? item.GetComponent<ItemDrop>() : null;
                var table = drop?.m_itemData?.m_shared?.m_buildPieces;
                if (table == null) continue;
                foreach (var prefab in table.m_pieces)
                {
                    if (prefab == null || !seenPrefabs.Add(prefab)) continue;
                    var ext = prefab.GetComponent<StationExtension>();
                    if (ext == null || ext.m_craftingStation == null) continue;
                    var piece = prefab.GetComponent<Piece>();
                    if (piece == null) continue;
                    // The game tells extensions apart by piece name, so two prefabs sharing one
                    // name would count as one upgrade; keep the first.
                    if (!seenNames.Add(ext.m_craftingStation.m_name + "\n" + piece.m_name)) continue;
                    if (!index.TryGetValue(ext.m_craftingStation.m_name, out var list))
                    {
                        list = new List<Piece>();
                        index[ext.m_craftingStation.m_name] = list;
                    }
                    list.Add(piece);
                }
            }

            _byStation = index;
            _indexedDb = db;
            _indexedItemCount = db.m_items.Count;
            int total = 0;
            foreach (var list in index.Values) total += list.Count;
            WhatsMyWorkbenchMissingMod.Log.LogInfo($"Indexed {total} station upgrades for {index.Count} stations.");
            return index;
        }
    }
}
