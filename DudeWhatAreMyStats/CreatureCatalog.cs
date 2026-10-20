using System;
using System.Collections.Generic;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>What sort of thing a creature is, as far as a kill count should care.</summary>
    internal enum CreatureKind
    {
        /// <summary>Nothing in this world answers to that name, so nothing can be said about it.</summary>
        Unknown = 0,
        /// <summary>Cannot fight at all: deer, hare, fish, gulls. Killing them is harvesting.</summary>
        Critter = 1,
        /// <summary>Leaves you alone until you hit it first. The dverger.</summary>
        Neutral = 2,
        /// <summary>Will come for you: every monster faction, and the animals that fight back.</summary>
        Hostile = 3,
        /// <summary>A boss. Vanilla counts these apart from everything else and so does this.</summary>
        Boss = 4,
        /// <summary>Another player.</summary>
        Player = 5,
    }

    /// <summary>One species, as the game describes it.</summary>
    internal sealed class CreatureInfo
    {
        /// <summary>The game's own name key, which is what the kill tallies are filed under.</summary>
        internal string Key = "";
        internal string Prefab = "";
        internal CreatureKind Kind;
        internal Character.Faction Faction;
        /// <summary>Base health at one star, straight off the prefab. The toughness figure.</summary>
        internal float Health;
        /// <summary>True if it can be tamed, which is what makes an old kill ambiguous.</summary>
        internal bool Tameable;
        /// <summary>Carries something that deals damage. Not the same as being willing to use it on you.</summary>
        internal bool Armed;
        /// <summary>Runs rather than fights when it has no target, which is what makes a deer a deer.</summary>
        internal bool Flees;
        /// <summary>The damaging items it carries, named, so a surprising classification can be argued with.</summary>
        internal string Attacks = "";
        /// <summary>
        /// True if the world spawns this species on its own. False for a creature that only ever
        /// exists because a player made it: the hen and the chicken, which come from an egg and
        /// nothing else, so every one of them was somebody's and no kill of one is ambiguous.
        /// </summary>
        internal bool WildSpawns;

        /// <summary>Nothing in the world produces this except a player, and it can be tamed.</summary>
        internal bool OnlyEverTame => Tameable && !WildSpawns;

        /// <summary>1 Meadows to 8 Deep North, 0 when nothing says where it lives.</summary>
        internal int Tier;
        internal Heightmap.Biome Biome;

        internal string Display => DeathReport.CauseName(Key);
    }

    /// <summary>
    /// Every creature in this world, built from the game's own prefabs.
    ///
    /// This is what makes the old kill tallies readable. Valheim files kills under the species name
    /// and nothing else, so what a kill *was* can only come from the species -- and the species is
    /// still right there in ZNetScene, with its faction, its health and its spawn biome. Nothing
    /// here is a list this mod maintains: the game is asked, so a mod that adds creatures is
    /// classified by the same rules as the vanilla ones.
    ///
    /// Built once per world, after the scene is up. Keyed by Character.m_name, the same key the
    /// tallies use, so two prefabs that share a name (the several skeletons) are one entry holding
    /// the toughest of them.
    /// </summary>
    internal static class CreatureCatalog
    {
        private static readonly Dictionary<string, CreatureInfo> _byKey = new Dictionary<string, CreatureInfo>(StringComparer.Ordinal);
        private static bool _built;

        internal static bool Built => _built;
        internal static int Count => _byKey.Count;

        internal static void Reset()
        {
            _byKey.Clear();
            _built = false;
        }

        /// <summary>Everything known, worst first, for the dump command.</summary>
        internal static List<CreatureInfo> All()
        {
            var list = new List<CreatureInfo>(_byKey.Values);
            list.Sort((a, b) =>
            {
                int byTier = a.Tier.CompareTo(b.Tier);
                if (byTier != 0) return byTier;
                int byHealth = a.Health.CompareTo(b.Health);
                if (byHealth != 0) return byHealth;
                return string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });
            return list;
        }

        /// <summary>What this species is, or null for a name this world has no creature for.</summary>
        internal static CreatureInfo Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_built) Build();
            return _byKey.TryGetValue(key, out var info) ? info : null;
        }

        /// <summary>
        /// The kind a tally row should be counted as. A key with no creature behind it is almost
        /// always another player, since player kills are filed under the player's own name, so that
        /// is the guess rather than Unknown.
        /// </summary>
        internal static CreatureKind KindOf(string key)
        {
            var info = Find(key);
            if (info != null) return info.Kind;
            if (!string.IsNullOrEmpty(key) && key[0] != '$') return CreatureKind.Player;
            return CreatureKind.Unknown;
        }

        // ── building it ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Walks every prefab in the scene for a Character component. Cheap enough to do once: the
        /// list is a few thousand prefabs and only the ones with a Character are looked at twice.
        /// </summary>
        internal static void Build()
        {
            _byKey.Clear();
            var scene = ZNetScene.instance;
            // No scene yet means too early rather than nothing to find, so leave it unbuilt and let
            // the next lookup try again. Marking it built here would leave the catalog empty for the
            // whole session over one early call.
            if (scene == null) return;
            _built = true;      // set before the walk: a failure should not retry on every lookup
            try
            {
                var prefabs = scene.m_prefabs;
                for (int i = 0; i < prefabs.Count; i++)
                {
                    GameObject go = prefabs[i];
                    if (go == null) continue;
                    var character = go.GetComponent<Character>();
                    if (character == null) continue;
                    Add(go, character);
                }
                ReadBiomes();
                DudeWhatAreMyStatsMod.Log.LogInfo(
                    $"[DudeWhatAreMyStats] Catalogued {_byKey.Count} creature(s) from this world's prefabs.");
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    $"[DudeWhatAreMyStats] Could not read the creature list, so kills cannot be sorted by type: {e}");
            }
        }

        private static void Add(GameObject go, Character character)
        {
            string key = (character.m_name ?? "").Trim();
            if (key.Length == 0) return;

            var kind = Classify(go, character);
            bool tameable = go.GetComponent<Tameable>() != null;

            if (_byKey.TryGetValue(key, out var have))
            {
                // Same species under two prefabs, as the skeletons are. Keep the toughest, and let
                // any variant that can be tamed or that fights make that true of the species.
                if (character.m_health > have.Health) have.Health = character.m_health;
                if (tameable) have.Tameable = true;
                if (kind > have.Kind && have.Kind != CreatureKind.Boss) have.Kind = kind;
                return;
            }

            _byKey[key] = new CreatureInfo
            {
                Key = key,
                Prefab = go.name ?? "",
                Kind = kind,
                Faction = character.m_faction,
                Health = character.m_health,
                Tameable = tameable,
                Armed = HasAttack(go),
                Flees = Flees(go),
                Attacks = AttackNames(go),
                Tier = TierFromFaction(character.m_faction),
            };
        }

        /// <summary>
        /// Hostile, neutral or a critter, decided the way the game decides it.
        ///
        /// The monster factions are Valheim's own test for "is a monster", lifted from
        /// Character.IsMonsterFaction, which is also what makes a mod's creature classify itself.
        /// Two cases it does not cover. The dverger are aggravatable: they ignore you until you
        /// swing first, which is neither hostile nor harmless. And Faction.AnimalsVeg holds both
        /// the boar and lox that will happily gore you and the deer and hare that cannot do a thing
        /// about it, so those split on whether the prefab carries an attack at all.
        /// </summary>
        private static CreatureKind Classify(GameObject go, Character character)
        {
            if (character.m_boss || character.m_faction == Character.Faction.Boss) return CreatureKind.Boss;
            if (character.m_faction == Character.Faction.Players) return CreatureKind.Player;

            switch (character.m_faction)
            {
                case Character.Faction.ForestMonsters:
                case Character.Faction.Undead:
                case Character.Faction.Demon:
                case Character.Faction.PlainsMonsters:
                case Character.Faction.MountainMonsters:
                case Character.Faction.SeaMonsters:
                case Character.Faction.MistlandsMonsters:
                case Character.Faction.DeepNorth:
                    return CreatureKind.Hostile;
            }

            var ai = go.GetComponent<BaseAI>();
            if (character.m_faction == Character.Faction.Dverger || (ai != null && ai.m_aggravatable))
                return CreatureKind.Neutral;

            // Two signals, because one is not enough. Carrying nothing that deals damage settles it
            // (the hare). Carrying something does not: the deer has an attack it uses on other
            // animals and still runs from you, which had it filed as hostile. m_fleeIfNotAlerted is
            // the designers saying this creature runs rather than fights, and it is the difference
            // between a deer and a boar, both of which are Faction.AnimalsVeg and both of which the
            // game's own IsEnemy calls an enemy of everything.
            if (!HasAttack(go)) return CreatureKind.Critter;
            return Flees(go) ? CreatureKind.Critter : CreatureKind.Hostile;
        }

        /// <summary>Whether it runs away rather than coming for you when it has no target.</summary>
        internal static bool Flees(GameObject go)
        {
            var monster = go.GetComponent<MonsterAI>();
            return monster != null && monster.m_fleeIfNotAlerted;
        }

        /// <summary>
        /// Whether it carries anything that deals damage. Every attack in Valheim is an item, a bite
        /// and a claw included. Note that this is not the same as being willing to use it on you:
        /// see <see cref="Classify"/>.
        /// </summary>
        internal static bool HasAttack(GameObject go)
        {
            var humanoid = go.GetComponent<Humanoid>();
            if (humanoid == null) return false;
            if (Any(humanoid.m_defaultItems)) return true;
            return Any(humanoid.m_randomWeapon);
        }

        /// <summary>
        /// What it is carrying that deals damage, by prefab name. Only for the dump command: when a
        /// creature classifies as something surprising, the next question is always what the game
        /// thinks it is holding, and the answer is not in any file this mod can read.
        /// </summary>
        internal static string AttackNames(GameObject go)
        {
            var humanoid = go.GetComponent<Humanoid>();
            if (humanoid == null) return "";
            var names = new List<string>();
            Collect(humanoid.m_defaultItems, names);
            Collect(humanoid.m_randomWeapon, names);
            return names.Count == 0 ? "" : string.Join(", ", names.ToArray());
        }

        private static void Collect(GameObject[] items, List<string> into)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null || into.Count >= 6) continue;
                var drop = item.GetComponent<ItemDrop>();
                var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
                if (shared == null || shared.m_damages.GetTotalDamage() <= 0f) continue;
                into.Add(item.name + " " + Mathf.RoundToInt(shared.m_damages.GetTotalDamage()));
            }
        }

        private static bool Any(GameObject[] items)
        {
            if (items == null) return false;
            foreach (var item in items)
            {
                if (item == null) continue;
                var drop = item.GetComponent<ItemDrop>();
                var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
                if (shared == null) continue;
                if (shared.m_damages.GetTotalDamage() > 0f) return true;
            }
            return false;
        }

        // ── where it lives ──────────────────────────────────────────────────────────

        /// <summary>
        /// Biomes in the order you meet them, which is the only ordering that means anything for
        /// difficulty. The enum itself is a flags field with values that jump around, so this is a
        /// table rather than a cast.
        /// </summary>
        private static readonly KeyValuePair<Heightmap.Biome, int>[] Tiers =
        {
            new KeyValuePair<Heightmap.Biome, int>(Heightmap.Biome.Meadows, 1),
            new KeyValuePair<Heightmap.Biome, int>(Heightmap.Biome.BlackForest, 2),
            new KeyValuePair<Heightmap.Biome, int>(Heightmap.Biome.Swamp, 3),
            new KeyValuePair<Heightmap.Biome, int>(Heightmap.Biome.Ocean, 3),
            new KeyValuePair<Heightmap.Biome, int>(Heightmap.Biome.Mountain, 4),
            new KeyValuePair<Heightmap.Biome, int>(Heightmap.Biome.Plains, 5),
            new KeyValuePair<Heightmap.Biome, int>(Heightmap.Biome.Mistlands, 6),
            new KeyValuePair<Heightmap.Biome, int>(Heightmap.Biome.AshLands, 7),
            new KeyValuePair<Heightmap.Biome, int>(Heightmap.Biome.DeepNorth, 8),
        };

        internal static string TierName(int tier)
        {
            switch (tier)
            {
                case 1: return "Meadows";
                case 2: return "Black Forest";
                case 3: return "Swamp";
                case 4: return "Mountain";
                case 5: return "Plains";
                case 6: return "Mistlands";
                case 7: return "Ashlands";
                case 8: return "Deep North";
                default: return "anywhere";
            }
        }

        /// <summary>
        /// A rough tier from the faction, for anything the spawn lists never mention: the creatures
        /// that only come out of dungeons and events. Each monster faction belongs to one biome.
        /// The animals do not, which is why the lox is the reason this is only a fallback.
        /// </summary>
        private static int TierFromFaction(Character.Faction faction)
        {
            switch (faction)
            {
                case Character.Faction.AnimalsVeg:         return 1;
                case Character.Faction.ForestMonsters:     return 2;
                case Character.Faction.Undead:             return 3;
                case Character.Faction.SeaMonsters:        return 3;
                case Character.Faction.MountainMonsters:   return 4;
                case Character.Faction.PlainsMonsters:     return 5;
                case Character.Faction.MistlandsMonsters:  return 6;
                case Character.Faction.Dverger:            return 6;
                case Character.Faction.Demon:              return 7;
                case Character.Faction.DeepNorth:          return 8;
                default:                                   return 0;
            }
        }

        /// <summary>
        /// Replaces the faction guess with the real thing, where the world's spawn lists say where a
        /// creature spawns. The lowest biome it appears in is the one that counts: that is where you
        /// first meet it, and a greyling in the meadows is a meadows problem whatever else spawns it.
        ///
        /// Dungeon and event creatures are not in these lists at all, so they keep the faction guess.
        /// </summary>
        private static void ReadBiomes()
        {
            var system = UnityEngine.Object.FindAnyObjectByType<SpawnSystem>();
            if (system == null || system.m_spawnLists == null) return;
            foreach (var list in system.m_spawnLists)
            {
                if (list == null || list.m_spawners == null) continue;
                foreach (var spawner in list.m_spawners)
                {
                    if (spawner == null || spawner.m_prefab == null) continue;
                    var character = spawner.m_prefab.GetComponent<Character>();
                    if (character == null) continue;
                    string key = (character.m_name ?? "").Trim();
                    if (key.Length == 0 || !_byKey.TryGetValue(key, out var info)) continue;
                    // Seen in a spawn list at all is what matters here, whatever biome it names:
                    // it means the world produces this creature without anyone's help.
                    info.WildSpawns = true;
                    int tier = LowestTier(spawner.m_biome);
                    if (tier <= 0) continue;
                    // A spawn-list tier always beats the faction guess; between two spawn lists the
                    // earlier biome wins.
                    if (info.Biome == Heightmap.Biome.None || tier < info.Tier)
                    {
                        info.Tier = tier;
                        info.Biome = spawner.m_biome;
                    }
                }
            }
        }

        /// <summary>The earliest biome in a spawner's mask, since a mask can name several.</summary>
        private static int LowestTier(Heightmap.Biome mask)
        {
            int lowest = 0;
            foreach (var pair in Tiers)
            {
                if ((mask & pair.Key) == 0) continue;
                if (lowest == 0 || pair.Value < lowest) lowest = pair.Value;
            }
            return lowest;
        }
    }
}
