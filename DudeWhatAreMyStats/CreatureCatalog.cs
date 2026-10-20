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

        /// <summary>What reading the prefab concluded, before the config has its say.</summary>
        internal CreatureKind InferredKind;

        /// <summary>
        /// What this counts as. The config wins: no reading of the game's data will ever be right
        /// about every creature in every mod, and a wrong row should be fixable without a new build.
        /// </summary>
        internal CreatureKind Kind
        {
            get
            {
                var forced = CreatureCatalog.Forced(Key);
                return forced != CreatureKind.Unknown ? forced : InferredKind;
            }
        }

        internal bool Overridden => CreatureCatalog.Forced(Key) != CreatureKind.Unknown;
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
        /// <summary>Which rule decided <see cref="Kind"/>. Printed by the dump so a wrong call names itself.</summary>
        internal string Reason = "";
        /// <summary>
        /// True if the world spawns this species on its own. False for a creature that only ever
        /// exists because a player made it: the hen and the chicken, which come from an egg and
        /// nothing else, so every one of them was somebody's and no kill of one is ambiguous.
        /// </summary>
        internal bool WildSpawns;

        /// <summary>
        /// Nothing in the world produces this except a player, it can be tamed, and it is not
        /// something that fights you. The last clause matters: a summoned creature can carry a
        /// Tameable and never spawn wild while being thoroughly hostile, and counting those as
        /// somebody's pet would be wrong in the direction that flatters people.
        /// </summary>
        internal bool OnlyEverTame => Tameable && !WildSpawns && Kind != CreatureKind.Hostile;

        /// <summary>1 Meadows to 8 Deep North, 0 when nothing says where it lives.</summary>
        internal int Tier;
        internal Heightmap.Biome Biome;

        internal string Display => DeathReport.CauseName(Key);

        /// <summary>
        /// True where this mod overruled the game's own grouping: something Valheim files under a
        /// monster faction that is counted as harmless because it carries no attack. The deer, the
        /// piggy and the lox calf belong here. Anything else that turns up in this list is the
        /// shape a mistake in the armament test would take, so it is worth listing on its own.
        /// </summary>
        internal bool OverruledFaction => Kind == CreatureKind.Critter && Reason == "no attack of any kind" && IsMonsterFaction;

        internal bool IsMonsterFaction
        {
            get
            {
                switch (Faction)
                {
                    case Character.Faction.ForestMonsters:
                    case Character.Faction.Undead:
                    case Character.Faction.Demon:
                    case Character.Faction.PlainsMonsters:
                    case Character.Faction.MountainMonsters:
                    case Character.Faction.SeaMonsters:
                    case Character.Faction.MistlandsMonsters:
                    case Character.Faction.DeepNorth:
                        return true;
                    default:
                        return false;
                }
            }
        }
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

        private static readonly Dictionary<string, CreatureKind> _forced = new Dictionary<string, CreatureKind>(StringComparer.OrdinalIgnoreCase);
        private static string _forcedFrom;

        /// <summary>
        /// What the config insists a species is, or Unknown where it says nothing.
        ///
        /// The escape hatch, and the honest admission that reading a prefab cannot settle every
        /// case. A creature that damages you by some means this does not recognise reads as
        /// harmless, and the dump names those rows so they can be corrected here rather than waiting
        /// for a new build.
        /// </summary>
        internal static CreatureKind Forced(string key)
        {
            string raw = DwamsConfig.TreatCreaturesAs != null ? DwamsConfig.TreatCreaturesAs.Value : "";
            if (raw != _forcedFrom)
            {
                _forcedFrom = raw;
                _forced.Clear();
                foreach (string part in (raw ?? "").Split(','))
                {
                    string entry = part.Trim();
                    int colon = entry.IndexOf(':');
                    if (colon <= 0) continue;
                    string name = entry.Substring(0, colon).Trim();
                    string want = entry.Substring(colon + 1).Trim();
                    if (name.Length == 0) continue;
                    CreatureKind kind;
                    if (want.Equals("hostile", StringComparison.OrdinalIgnoreCase)) kind = CreatureKind.Hostile;
                    else if (want.Equals("critter", StringComparison.OrdinalIgnoreCase)) kind = CreatureKind.Critter;
                    else if (want.Equals("neutral", StringComparison.OrdinalIgnoreCase)) kind = CreatureKind.Neutral;
                    else if (want.Equals("boss", StringComparison.OrdinalIgnoreCase)) kind = CreatureKind.Boss;
                    else
                    {
                        DudeWhatAreMyStatsMod.Log.LogWarning(
                            $"[DudeWhatAreMyStats] Could not read \"{entry}\" in Treat Creatures As; " +
                            "expected hostile, critter, neutral or boss.");
                        continue;
                    }
                    _forced[name] = kind;
                }
            }
            if (_forced.Count == 0 || string.IsNullOrEmpty(key)) return CreatureKind.Unknown;
            if (_forced.TryGetValue(key, out var exact)) return exact;
            int underscore = key.LastIndexOf('_');
            if (underscore >= 0 && underscore + 1 < key.Length && _forced.TryGetValue(key.Substring(underscore + 1), out var suffix))
                return suffix;
            return CreatureKind.Unknown;
        }

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

            var kind = Classify(go, character, out string reason);
            bool tameable = go.GetComponent<Tameable>() != null;

            if (_byKey.TryGetValue(key, out var have))
            {
                // Same species under two prefabs, as the skeletons are. Keep the toughest, and let
                // any variant that can be tamed or that fights make that true of the species.
                if (character.m_health > have.Health) have.Health = character.m_health;
                if (tameable) have.Tameable = true;
                if (kind > have.InferredKind && have.InferredKind != CreatureKind.Boss)
                {
                    // Take this prefab's evidence along with its verdict, or the dump would explain
                    // the row with the reasoning of a prefab that did not decide it.
                    have.InferredKind = kind;
                    have.Reason = reason;
                    have.Armed = HasAttack(go);
                    have.Flees = Flees(go);
                    have.Attacks = AttackNames(go);
                }
                return;
            }

            _byKey[key] = new CreatureInfo
            {
                Key = key,
                Prefab = go.name ?? "",
                InferredKind = kind,
                Faction = character.m_faction,
                Health = character.m_health,
                Tameable = tameable,
                Armed = HasAttack(go),
                Flees = Flees(go),
                Attacks = AttackNames(go),
                Reason = reason,
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
        private static CreatureKind Classify(GameObject go, Character character, out string reason)
        {
            if (character.m_boss || character.m_faction == Character.Faction.Boss)
            {
                reason = "boss";
                return CreatureKind.Boss;
            }
            if (go.GetComponent<Player>() != null)
            {
                reason = "a player";
                return CreatureKind.Player;
            }

            // Before the faction, because the faction is wrong about this. Valheim files the deer,
            // the piggy and the lox calf under monster factions along with the greydwarves, so
            // asking the faction first called a deer hostile. Carrying nothing that deals damage is
            // the one thing that settles it: every attack in the game is an item, so a creature
            // holding none cannot hurt anybody whatever it is grouped with.
            // "No attack" rather than "unarmed": a troll's fists and a wolf's bite are attacks held
            // in the same way a sword is, so this means it has nothing at all to hurt you with.
            if (!HasAttack(go))
            {
                reason = "no attack of any kind";
                return CreatureKind.Critter;
            }

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
                    reason = "monster faction";
                    return CreatureKind.Hostile;
            }

            var ai = go.GetComponent<BaseAI>();
            if (character.m_faction == Character.Faction.Dverger || (ai != null && ai.m_aggravatable))
            {
                reason = "aggravatable";
                return CreatureKind.Neutral;
            }

            // Two signals, because one is not enough. Carrying nothing that deals damage settles it
            // (the hare). Carrying something does not: the deer has an attack it uses on other
            // animals and still runs from you, which had it filed as hostile. m_fleeIfNotAlerted is
            // the designers saying this creature runs rather than fights, and it is the difference
            // between a deer and a boar, both of which are Faction.AnimalsVeg and both of which the
            // game's own IsEnemy calls an enemy of everything.
            // Armed, not a monster faction, not aggravatable: a boar or a lox. Note that fleeing is
            // NOT used here, though it is still reported. It looked like the signal that separates a
            // deer from a boar until the boar turned out to flee as well, and the deer turned out to
            // be unarmed all along.
            reason = "armed";
            return CreatureKind.Hostile;
        }

        /// <summary>
        /// Whether it runs away rather than coming for you when it has no target. Reported by the
        /// dump as a curiosity; it decides nothing, because boars flee too.
        /// </summary>
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
            // Damage dealt by simply being near the thing, rather than by it swinging anything: a
            // lava blob's heat, a blob that bursts. An Aoe on the creature itself is a weapon in
            // every sense that matters here, and it is held in none of the item slots.
            if (HasDamagingAoe(go)) return true;

            var humanoid = go.GetComponent<Humanoid>();
            if (humanoid == null) return false;
            // Fists, claws, a golem's stone arms. Humanoid.m_unarmedWeapon is a weapon the creature
            // always has rather than one it carries, and GetCurrentWeapon falls back to it, so a
            // creature that fights with nothing in its hands keeps its attack here and in no array.
            // This is what had the troll and the stone golem filed as harmless.
            if (Damaging(humanoid.m_unarmedWeapon)) return true;

            // The four places Humanoid.GiveDefaultItems draws from, for creatures that do carry
            // something: a troll with a log, a fuling with a club.
            if (Any(humanoid.m_defaultItems)) return true;
            if (Any(humanoid.m_randomWeapon)) return true;
            if (humanoid.m_randomSets != null)
                foreach (var set in humanoid.m_randomSets)
                    if (set != null && Any(set.m_items)) return true;
            if (humanoid.m_randomItems != null)
                foreach (var random in humanoid.m_randomItems)
                    if (random != null && Damaging(random.m_prefab)) return true;
            return false;
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
            if (Damaging(humanoid.m_unarmedWeapon))
                names.Add("fists: " + humanoid.m_unarmedWeapon.name + " " +
                          Mathf.RoundToInt(humanoid.m_unarmedWeapon.m_itemData.m_shared.m_damages.GetTotalDamage()));
            Collect(humanoid.m_defaultItems, names);
            Collect(humanoid.m_randomWeapon, names);
            if (humanoid.m_randomSets != null)
                foreach (var set in humanoid.m_randomSets)
                    if (set != null) Collect(set.m_items, names);
            if (humanoid.m_randomItems != null)
                foreach (var random in humanoid.m_randomItems)
                    if (random != null) Collect(new[] { random.m_prefab }, names);
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
                if (Damaging(item)) return true;
            return false;
        }

        /// <summary>An area effect on the creature or its children that does damage to somebody.</summary>
        private static bool HasDamagingAoe(GameObject go)
        {
            var effects = go.GetComponentsInChildren<Aoe>(true);
            if (effects == null) return false;
            foreach (var aoe in effects)
            {
                if (aoe == null) continue;
                // m_hitOwner would mean it hurts the creature carrying it, which is not an attack.
                if (aoe.m_damage.GetTotalDamage() > 0f) return true;
            }
            return false;
        }

        private static bool Damaging(GameObject item)
        {
            return item != null && Damaging(item.GetComponent<ItemDrop>());
        }

        private static bool Damaging(ItemDrop drop)
        {
            var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
            return shared != null && shared.m_damages.GetTotalDamage() > 0f;
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
