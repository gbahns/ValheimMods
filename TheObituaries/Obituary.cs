using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TheObituaries
{
    /// <summary>What the killer was swinging, as far as a hit can tell.</summary>
    internal enum WeaponKind
    {
        Unknown, Bow, Sword, Axe, Club, Spear, Knife, Fire, Frost, Lightning, Poison, Spirit,
        Bite, Punch, Log, Rock, Magic, Pickaxe, Blast
    }

    /// <summary>
    /// Turns a death into a Quake obituary. The lines are lifted from Quake (client.qc),
    /// Quake II (p_client.c) and Quake III (cg_event.c) and mapped onto what Valheim knows about
    /// a death: the HitData's type, the weapon behind it (its prefab name, its skill for a
    /// player, its dominant damage type otherwise), and the creature or player that dealt it.
    ///
    /// {v} is the victim, {k} the killer. {he} {him} {his} {himself} are the victim's pronouns,
    /// filled in by the victim's own game before the line is sent. Every pool is a list so the
    /// pick can vary.
    /// </summary>
    internal static class Obituary
    {
        private static readonly System.Random Rng = new System.Random();

        // ── environment: no killer, or a killer nothing can be said about ───────────────

        private static readonly Dictionary<HitData.HitType, string[]> Environment = new Dictionary<HitData.HitType, string[]>
        {
            { HitData.HitType.Undefined,     new[] { "{v} died" } },
            { HitData.HitType.EnemyHit,      new[] { "{v} died", "{v} got jacked up by something" } },
            { HitData.HitType.PlayerHit,     new[] { "{v} died" } },
            { HitData.HitType.Fall,          new[] { "{v} cratered", "{v} fell to {his} death", "{v} learned about gravity the hard way" } },
            { HitData.HitType.Drowning,      new[] { "{v} sank like a rock", "{v} sleeps with the fishes", "{v} went for a swim and never came back", "{v} drowned", "{v} thought {he} could breathe water" } },
            { HitData.HitType.Burning,       new[] { "{v} burnt to a crisp", "{v} was fried", "{v} burst into flames" } },
            { HitData.HitType.Freezing,      new[] { "{v} was frozen solid", "{v} should have brought a coat", "{v} turned to ice", "{v} turned into an ice cube" } },
            { HitData.HitType.Poisoned,      new[] { "{v} was poisoned to death", "{v} died from poisoning", "{v} can't exist on slime alone" } },
            { HitData.HitType.Water,         new[] { "{v} sank like a rock", "{v} sleeps with the fishes" } },
            { HitData.HitType.Smoke,         new[] { "{v} was smoked out", "{v} thought {he} could breathe smoke like air" } },
            { HitData.HitType.EdgeOfWorld,   new[] { "{v} tried to leave", "{v} found a way out", "{v} was in the wrong place" } },
            { HitData.HitType.Impact,        new[] { "{v} learned about gravity the hard way", "{v} was squished" } },
            { HitData.HitType.Cart,          new[] { "{v} was squished by a cart", "{v} was run over by a cart", "{v} was crushed by a cart", "{v} was flattened by a cart", "{v} lost a fight with a cart" } },
            { HitData.HitType.Tree,          new[] { "{v} was squished by a tree", "{v} took on a tree and lost", "{v} should have yelled timber" } },
            { HitData.HitType.Self,          new[] { "{v} suicides", "{v} becomes bored with life", "{v} killed {himself}", "{v} was fed up with this cruel world", "{v} was tired of life", "{v} wanted to see what it's like on the other side", "{v} had seen enough" } },
            { HitData.HitType.Structural,    new[] { "{v} was squished by a building", "{v} was buried by {his} own building" } },
            { HitData.HitType.Turret,        new[] { "{v} was gunned down by a ballista", "{v} was railed by a ballista" } },
            { HitData.HitType.Boat,          new[] { "{v} was keelhauled", "{v} was squished by a boat", "{v} ate a boat" } },
            { HitData.HitType.Stalagtite,    new[] { "{v} was spiked" } },
            { HitData.HitType.Catapult,      new[] { "{v} rode a catapult shot", "{v} caught a catapult shot", "{v} ate a catapult shot" } },
            { HitData.HitType.CinderFire,    new[] { "{v} burst into flames", "{v} visits the Volcano God" } },
            { HitData.HitType.AshlandsOcean, new[] { "{v} heats up the water", "{v} was boiled alive" } },
            { HitData.HitType.AshlandsLava,  new[] { "{v} does a back flip into the lava", "{v} visits the Volcano God" } },
            { HitData.HitType.Incinerator,   new[] { "{v} saw the light", "{v} was recycled" } },
            { HitData.HitType.DrawBridge,    new[] { "{v} was squished by a drawbridge", "{v} was crushed by a drawbridge", "{v} didn't realize how dangerous a drawbridge can be" } },
        };

        // A hit the victim dealt to themself (their own bomb, their own staff).
        private static readonly string[] SelfInflicted =
            { "{v} blew {himself} up", "{v} tripped on {his} own bomb", "{v} should have used a smaller gun" };

        // ── by weapon: what the killer hit with, creature or player alike ────────────────

        private static readonly Dictionary<WeaponKind, string[]> ByKind = new Dictionary<WeaponKind, string[]>
        {
            { WeaponKind.Bow,       new[] { "{v} accepted {k}'s shaft", "{v} ate {k}'s arrow", "{v} was railed by {k}", "{v} was turned into a pincushion by {k}", "{v} caught {k}'s arrow with {his} face", "{v} almost dodged {k}'s arrow", "{v} was perforated by {k}" } },
            { WeaponKind.Sword,     new[] { "{v} was run through by {k}", "{v} was impaled on {k}'s sword", "{v} was sliced in half by {k}", "{v} was carved up by {k}", "{v} was slashed to ribbons by {k}", "{v} was cut down by {k}" } },
            { WeaponKind.Axe,       new[] { "{v} was sliced in half by {k}", "{v} was ax-murdered by {k}", "{v} was chopped down by {k}", "{v} was split like firewood by {k}", "{v} was hacked to pieces by {k}" } },
            { WeaponKind.Club,      new[] { "{v} got {his} head bashed in by {k}", "{v} got {his} skull crushed by {k}", "{v} was pummeled by {k}", "{v} was clubbed like a seal by {k}", "{v} was beaten to a pulp by {k}", "{v} was bludgeoned by {k}" } },
            { WeaponKind.Spear,     new[] { "{v} was skewered by {k}", "{v} was run through by {k}", "{v} ate {k}'s spear", "{v} was spitted like a boar by {k}", "{v} was impaled by {k}" } },
            { WeaponKind.Knife,     new[] { "{v} was knifed by {k}", "{v} was shanked by {k}", "{v} was perforated by {k}", "{v} was gutted by {k}" } },
            { WeaponKind.Fire,      new[] { "{v} was fried by {k}", "{v} was toasted by {k}", "{v} was burnt to a crisp by {k}", "{v} was set on fire by {k}", "{v} was roasted by {k}", "{v} was melted by {k}" } },
            { WeaponKind.Frost,     new[] { "{v} was frozen solid by {k}", "{v} was turned into an ice cube by {k}", "{v} was put on ice by {k}" } },
            { WeaponKind.Lightning, new[] { "{v} was electrocuted by {k}", "{v} was zapped by {k}", "{v} accepted {k}'s discharge", "{v} was fried by {k}'s lightning" } },
            { WeaponKind.Poison,    new[] { "{v} was slimed by {k}", "{v} was poisoned by {k}", "{v} was vomited on by {k}", "{v} can't exist on {k}'s slime alone" } },
            { WeaponKind.Spirit,    new[] { "{v} was scragged by {k}", "{v} had {his} soul drained by {k}" } },
            { WeaponKind.Bite,      new[] { "{v} was mauled by {k}", "{v} was chewed up by {k}", "{v} was eviscerated by {k}", "{v} was torn apart by {k}", "{v} was gored by {k}", "{v} was ripped to shreds by {k}" } },
            { WeaponKind.Punch,     new[] { "{v} was smashed by {k}", "{v} was flattened by {k}", "{v} was pummeled by {k}", "{v} was stomped into the ground by {k}", "{v} was squashed like a bug by {k}", "{v} was pounded into paste by {k}" } },
            { WeaponKind.Log,       new[] { "{v} was swatted by {k}'s log", "{v} was clubbed with a tree by {k}", "{v} got {his} head bashed in with a log by {k}", "{v} was batted into next week by {k}" } },
            { WeaponKind.Rock,      new[] { "{v} was stoned by {k}", "{v} caught {k}'s rock with {his} face", "{v} was brained with a boulder by {k}" } },
            { WeaponKind.Magic,     new[] { "{v} was melted by {k}'s staff", "{v} saw the pretty lights from {k}'s staff", "{v} was hexed by {k}", "{v} was blasted by {k}" } },
            { WeaponKind.Pickaxe,   new[] { "{v} was mined by {k}", "{v} was pickaxed by {k}" } },
            { WeaponKind.Blast,     new[] { "{v} was blown up by {k}", "{v} was caught in {k}'s blast", "{v} stood too close to {k}", "{v} should have backed away from {k}", "{v} was blown to bits by {k}", "{v} didn't see {k} coming apart" } },
        };

        // When the weapon cannot be told: still not boring.
        private static readonly string[] ByAnything =
            { "{v} was killed by {k}", "{v} was slain by {k}", "{v} got jacked up by {k}", "{v} was wrecked by {k}", "{v} was destroyed by {k}", "{v} got owned by {k}" };

        // ── by creature: flavor on top of the weapon lines ───────────────────────────────
        //
        // By prefab name, most specific first, matched as a prefix (so "Wolf" also takes
        // "Wolf_cub" and "Skeleton" takes "Skeleton_NoArcher"). Quake's monster lines recast
        // for Valheim's bestiary. These are mixed in with the weapon pool, so a Draugr with a
        // bow reads "accepted the Draugr's shaft" most of the time and "joins the Draugr" once
        // in a while.

        private static readonly (string prefix, string[] lines)[] Creatures =
        {
            // bosses
            ("Eikthyr",          new[] { "{v} was electrocuted by {k}", "{v} became one with {k}" }),
            ("gd_king",          new[] { "{v} was smashed by {k}", "{v} became one with {k}" }),
            ("Bonemass",         new[] { "{v} was slimed by {k}", "{v} became one with {k}" }),
            ("Dragon",           new[] { "{v} was frozen solid by {k}", "{v} became one with {k}" }),
            ("GoblinKing",       new[] { "{v} was fried by {k}", "{v} became one with {k}" }),
            ("SeekerQueen",      new[] { "{v} was eviscerated by {k}", "{v} became one with {k}" }),
            ("Fader",            new[] { "{v} was incinerated by {k}", "{v} became one with {k}" }),
            // meadows, black forest
            ("Boar",             new[] { "{v} was gored by {k}", "{v} was mauled by {k}", "{v} was tusked by {k}" }),
            ("Neck",             new[] { "{v} was nibbled to death by {k}" }),
            ("Greyling",         new[] { "{v} was scragged by {k}" }),
            ("Greydwarf_Shaman", new[] { "{v} was vomited on by {k}" }),
            ("Greydwarf_Elite",  new[] { "{v} was mauled by {k}" }),
            ("Greydwarf",        new[] { "{v} was scragged by {k}" }),
            ("Troll",            new[] { "{v} was smashed by {k}" }),
            ("Skeleton_Poison",  new[] { "{v} was slimed by {k}" }),
            ("Skeleton",         new[] { "{v} joins the Skeletons" }),
            ("Ghost",            new[] { "{v} became one with {k}" }),
            // swamp
            ("Wraith",           new[] { "{v} was scragged by {k}" }),
            ("BlobTar",          new[] { "{v} was tarred by {k}" }),
            ("BlobLava",         new[] { "{v} was melted by {k}" }),
            ("Blob",             new[] { "{v} was slimed by {k}" }),
            ("Draugr",           new[] { "{v} joins the Draugr" }),
            ("Leech",            new[] { "{v} was fed to the leeches" }),
            ("Surtling",         new[] { "{v} was fried by {k}" }),
            ("Abomination",      new[] { "{v} was destroyed by {k}" }),
            // mountain
            ("Wolf",             new[] { "{v} was mauled by {k}" }),
            ("Fenring_Cultist",  new[] { "{v} was fried by {k}" }),
            ("Fenring",          new[] { "{v} was mauled by {k}" }),
            ("Ulv",              new[] { "{v} was mauled by {k}" }),
            ("Hatchling",        new[] { "{v} was frozen solid by {k}" }),
            ("StoneGolem",       new[] { "{v} was smashed by {k}" }),
            ("Bat",              new[] { "{v} was swarmed by {k}" }),
            // plains
            ("Deathsquito",      new[] { "{v} was perforated by {k}", "{v} was turned into a pincushion by {k}" }),
            ("Lox",              new[] { "{v} was trampled by {k}" }),
            ("GoblinShaman",     new[] { "{v} was fried by {k}" }),
            ("GoblinBrute",      new[] { "{v} was destroyed by {k}" }),
            ("GoblinArcher",     new[] { "{v} was gunned down by {k}" }),
            ("Goblin",           new[] { "{v} was slain by {k}" }),
            // ocean
            ("BonemawSerpent",   new[] { "{v} was fed to the Bonemaw" }),
            ("Serpent",          new[] { "{v} was fed to the Serpent", "{v} sleeps with the fishes" }),
            // mistlands
            ("SeekerBrute",      new[] { "{v} was smashed by {k}" }),
            ("SeekerBrood",      new[] { "{v} was nibbled to death by {k}" }),
            ("Seeker",           new[] { "{v} was eviscerated by {k}" }),
            ("Tick",             new[] { "{v} was drained by {k}" }),
            ("Gjall",            new[] { "{v} was exploded by {k}" }),
            ("DvergerArbalest",  new[] { "{v} was railed by {k}" }),
            ("DvergerMageFire",  new[] { "{v} was fried by {k}" }),
            ("DvergerMageIce",   new[] { "{v} was frozen solid by {k}" }),
            ("Dverger",          new[] { "{v} was blasted by {k}" }),
            // ashlands
            ("Charred_Archer",   new[] { "{v} was perforated by {k}" }),
            ("Charred_Mage",     new[] { "{v} was blasted by {k}" }),
            ("Charred_Twitcher", new[] { "{v} was scragged by {k}" }),
            ("Charred",          new[] { "{v} was slain by {k}" }),
            ("Morgen",           new[] { "{v} was destroyed by {k}" }),
            ("FallenValkyrie",   new[] { "{v} was slain by {k}" }),
            ("Volture",          new[] { "{v} was fried by {k}" }),
            ("Asksvin",          new[] { "{v} was trampled by {k}" }),
            // deep north
            ("Writh",            new[] { "{v} delivered the final blow to {k} from too close", "{v} didn't back away from {k}", "{v} was blown up by {k}" }),
        };

        // ── composing ────────────────────────────────────────────────────────────────────

        /// <summary>The obituary for this player's death, from the last hit it took.</summary>
        public static Notice Compose(Player victim, HitData hit, out string facts)
        {
            var n = new Notice { Victim = victim != null ? victim.GetPlayerName() : "" };
            if (string.IsNullOrEmpty(n.Victim)) n.Victim = "Someone";

            var type = hit != null ? hit.m_hitType : HitData.HitType.Undefined;
            ZDOID attackerId = hit != null ? hit.m_attacker : ZDOID.None;
            Character killer = hit?.GetAttacker();
            AttackerMemory.Entry remembered = AttackerMemory.Lookup(attackerId);
            string credit = null;   // for the log: how a killer with no attacker on the hit was found

            // The body is gone and the hit was never remembered (it was the first and last):
            // the ZDO may still say what it was.
            if (killer == null && remembered == null && attackerId != ZDOID.None)
            {
                remembered = DescribeById(attackerId);
                if (remembered != null) credit = "from its ZDO";
            }

            WeaponKind kind;
            if (killer == null && remembered == null && hit != null && (type == HitData.HitType.Poisoned || type == HitData.HitType.Burning))
            {
                // The poison or fire a hit left behind: its ticks name no attacker, so credit
                // whoever last dealt that element. Two of them inside a minute is rare.
                bool poison = type == HitData.HitType.Poisoned;
                remembered = AttackerMemory.LastWhere(e => poison ? e.Poison : e.Fire, 60f);
                if (remembered != null)
                {
                    type = HitData.HitType.EnemyHit;
                    credit = poison ? "last to poison us" : "last to burn us";
                }
                kind = poison ? WeaponKind.Poison : WeaponKind.Fire;
            }
            else if (killer == null && remembered == null && hit != null && attackerId == ZDOID.None
                     && type == HitData.HitType.EnemyHit && hit.m_radius > 0f)
            {
                // A blast nobody owns (a creature's death explosion): the last thing that hit
                // us in the seconds before is almost certainly what blew up.
                remembered = AttackerMemory.LastWhere(e => !e.IsPlayer, 10f);
                if (remembered != null) credit = "last to hit us, before an ownerless blast";
                kind = WeaponKind.Blast;
            }
            else
            {
                string weapon = remembered?.Weapon ?? WeaponName(killer);
                kind = Classify(hit, weapon, killer is Player || (remembered?.IsPlayer ?? false));
            }
            facts = Facts(type, killer, remembered, remembered?.Weapon ?? WeaponName(killer), kind, hit, credit);

            // A mod that dealt this death itself has already said what it should read as; the
            // hit it left behind says nothing, so there is nothing here worth working out.
            string claimed = Api.Take();
            if (claimed != null)
            {
                n.Template = Pronouns.Fill(claimed, Pronouns.For(victim));
                return n;
            }

            if (attackerId != ZDOID.None && victim != null && attackerId == victim.GetZDOID())
            {
                n.Template = Pick(type == HitData.HitType.Self ? Environment[HitData.HitType.Self] : SelfInflicted);
            }
            else if ((type == HitData.HitType.EnemyHit || type == HitData.HitType.PlayerHit) && (killer != null || remembered != null))
            {
                // The killer may no longer be in the scene (despawned, or killed since), but it
                // was there when it hit us, and AttackerMemory kept its description from then.
                bool isPlayer = killer != null ? killer is Player : remembered.IsPlayer;
                string prefab = killer != null ? PrefabName(killer) : remembered.Prefab;
                n.Killer         = killer != null ? Describe(killer) : remembered.Description;
                n.KillerId       = attackerId;
                n.KillerIsPlayer = isPlayer;
                n.Template       = Pick(isPlayer ? PlayerLines(kind) : CreatureLines(prefab, kind));
            }
            else
            {
                n.Template = Pick(EnvironmentLines(type));
            }

            n.Template = Pronouns.Fill(n.Template, Pronouns.For(victim));
            return n;
        }

        internal static string[] EnvironmentLines(HitData.HitType type)
            => Environment.TryGetValue(type, out var lines) ? lines : Environment[HitData.HitType.Undefined];

        internal static string[] PlayerLines(WeaponKind kind)
            => ByKind.TryGetValue(kind, out var lines) ? lines : ByAnything;

        /// <summary>The creature's own lines plus the weapon's, so both get a turn.</summary>
        // Beasts whose attack the damage types would file under a fist or a hoof (blunt, no
        // weapon) but which read as an animal savaging you: a boar "pummeling" someone is wrong.
        private static readonly (string prefix, WeaponKind kind)[] KindOverrides =
        {
            ("Boar", WeaponKind.Bite),
            ("Neck", WeaponKind.Bite),
            ("Wolf", WeaponKind.Bite),
            ("Ulv",  WeaponKind.Bite),
            ("Bat",  WeaponKind.Bite),
        };

        internal static string[] CreatureLines(string prefab, WeaponKind kind)
        {
            prefab = prefab ?? "";
            foreach (var (prefix, forced) in KindOverrides)
                if (prefab.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { kind = forced; break; }

            var pool = new List<string>();
            foreach (var (prefix, lines) in Creatures)
            {
                if (!prefab.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                pool.AddRange(lines);
                break;
            }
            pool.AddRange(ByKind.TryGetValue(kind, out var byKind) ? byKind : ByAnything);
            return pool.ToArray();
        }

        // ── what was it hit with ─────────────────────────────────────────────────────────

        /// <summary>The prefab name of the weapon the attacker is holding right now, or "".</summary>
        internal static string WeaponName(Character attacker)
        {
            try
            {
                var w = (attacker as Humanoid)?.GetCurrentWeapon();
                if (w == null) return "";
                if (w.m_dropPrefab != null) return w.m_dropPrefab.name ?? "";
                return w.m_shared?.m_name ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// A player's hit carries its weapon skill, which is reliable. A creature's does not
        /// (monster weapons leave the skill at its default, Swords), so for creatures the
        /// weapon's prefab name is read for keywords first and the dominant damage type after.
        /// </summary>
        internal static WeaponKind Classify(HitData hit, string weapon, bool byPlayer)
        {
            if (byPlayer && hit != null)
            {
                switch (hit.m_skill)
                {
                    case Skills.SkillType.Swords:         return WeaponKind.Sword;
                    case Skills.SkillType.Axes:           return WeaponKind.Axe;
                    case Skills.SkillType.Clubs:          return WeaponKind.Club;
                    case Skills.SkillType.Spears:
                    case Skills.SkillType.Polearms:       return WeaponKind.Spear;
                    case Skills.SkillType.Knives:         return WeaponKind.Knife;
                    case Skills.SkillType.Bows:
                    case Skills.SkillType.Crossbows:      return WeaponKind.Bow;
                    case Skills.SkillType.Unarmed:        return WeaponKind.Punch;
                    case Skills.SkillType.Pickaxes:       return WeaponKind.Pickaxe;
                    case Skills.SkillType.BloodMagic:     return WeaponKind.Magic;
                    case Skills.SkillType.ElementalMagic:
                    {
                        var k = ByDamage(hit, ranged: true);
                        return k == WeaponKind.Fire || k == WeaponKind.Frost || k == WeaponKind.Lightning ? k : WeaponKind.Magic;
                    }
                }
            }

            string w = (weapon ?? "").ToLowerInvariant();
            if (Has(w, "bow", "arbalest", "crossbow", "arrow", "bolt"))         return WeaponKind.Bow;
            if (Has(w, "sword", "dyrnwyn"))                                     return WeaponKind.Sword;
            if (Has(w, "axe"))                                                  return WeaponKind.Axe;
            if (Has(w, "mace", "club", "hammer", "sledge"))                     return WeaponKind.Club;
            if (Has(w, "spear", "polearm", "atgeir", "pike"))                   return WeaponKind.Spear;
            if (Has(w, "knife", "dagger"))                                      return WeaponKind.Knife;
            if (Has(w, "log"))                                                  return WeaponKind.Log;
            if (Has(w, "torch", "fire", "flame", "lava", "cinder", "meteor"))   return WeaponKind.Fire;
            if (Has(w, "ice", "frost", "cold"))                                 return WeaponKind.Frost;
            if (Has(w, "throw", "rock", "stone", "boulder"))                    return WeaponKind.Rock;
            if (Has(w, "poison", "spit", "vomit", "acid"))                      return WeaponKind.Poison;
            if (Has(w, "staff"))                                                return WeaponKind.Magic;

            return ByDamage(hit, hit?.m_ranged ?? false);
        }

        private static bool Has(string s, params string[] keys)
        {
            foreach (var k in keys) if (s.Contains(k)) return true;
            return false;
        }

        private static WeaponKind ByDamage(HitData hit, bool ranged)
        {
            if (hit == null) return WeaponKind.Unknown;
            var d = hit.m_damage;
            WeaponKind best = WeaponKind.Unknown; float most = 0f;
            void Consider(float amount, WeaponKind k) { if (amount > most) { most = amount; best = k; } }
            Consider(d.m_fire,      WeaponKind.Fire);
            Consider(d.m_frost,     WeaponKind.Frost);
            Consider(d.m_lightning, WeaponKind.Lightning);
            Consider(d.m_poison,    WeaponKind.Poison);
            Consider(d.m_spirit,    WeaponKind.Spirit);
            Consider(d.m_pierce,    ranged ? WeaponKind.Bow : WeaponKind.Bite);
            Consider(d.m_slash,     WeaponKind.Bite);
            // An area hit that is mostly blunt is an explosion; a poison cloud or fire splash
            // is still its element, taken above.
            Consider(d.m_blunt,     hit.m_radius > 0f ? WeaponKind.Blast : ranged ? WeaponKind.Rock : WeaponKind.Punch);
            return best;
        }

        /// <summary>One log line with everything the killing hit said, for tuning the tables.</summary>
        private static string Facts(HitData.HitType type, Character killer, AttackerMemory.Entry remembered, string weapon, WeaponKind kind, HitData hit, string credit)
        {
            var sb = new StringBuilder("Killing hit: ");
            sb.Append(hit != null ? hit.m_hitType : type);
            string prefab = killer != null ? PrefabName(killer) : remembered?.Prefab;
            if (!string.IsNullOrEmpty(prefab)) sb.Append(" by ").Append(prefab).Append(killer == null ? " (" + (credit ?? "from memory") + ")" : "");
            else if (hit != null && hit.m_attacker != ZDOID.None) sb.Append(" by an attacker nothing is known about");
            if (!string.IsNullOrEmpty(weapon)) sb.Append(" with ").Append(weapon);
            sb.Append(" -> ").Append(kind);
            if (hit != null && hit.m_radius > 0f) sb.Append("; area ").Append(hit.m_radius.ToString("0.#")).Append(" m");
            if (hit != null)
            {
                sb.Append("; skill ").Append(hit.m_skill).Append(hit.m_ranged ? ", ranged" : "");
                var d = hit.m_damage;
                sb.Append("; dmg");
                void Add(string name, float v) { if (v > 0f) sb.Append(' ').Append(name).Append(' ').Append(Mathf.RoundToInt(v)); }
                Add("blunt", d.m_blunt); Add("slash", d.m_slash); Add("pierce", d.m_pierce); Add("chop", d.m_chop); Add("pickaxe", d.m_pickaxe);
                Add("fire", d.m_fire); Add("frost", d.m_frost); Add("lightning", d.m_lightning); Add("poison", d.m_poison); Add("spirit", d.m_spirit); Add("plain", d.m_damage);
            }
            return sb.ToString();
        }

        // ── naming the killer ────────────────────────────────────────────────────────────

        /// <summary>
        /// The killer as it reads in a sentence: "Marco", "a Troll", "a 2-star Troll", "an
        /// Abomination", "Fluffy the Wolf", "a tame Wolf", "Eikthyr", "The Elder".
        /// </summary>
        internal static string Describe(Character killer)
        {
            if (killer is Player p)
            {
                string pn = p.GetPlayerName();
                return string.IsNullOrEmpty(pn) ? "someone" : pn;
            }

            string name = Localization.instance.Localize(killer.m_name ?? "").Trim();
            if (name.Length == 0) name = PrefabName(killer);

            if (killer.IsTamed())
            {
                string pet = "";
                if (TheObituariesMod.TameNames.Value)
                {
                    var nview = killer.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid())
                        pet = (nview.GetZDO().GetString(ZDOVars.s_tamedName) ?? "").Trim();
                }
                return pet.Length > 0 ? pet + " the " + name : "a tame " + name;
            }

            if (killer.IsBoss()) return name;   // "Eikthyr", "The Elder": no article

            int level = killer.GetLevel();
            if (level > 1 && TheObituariesMod.LevelStars.Value)
                name = (level - 1) + "-star " + name;
            return Article(name) + " " + name;
        }

        /// <summary>
        /// The attacker as far as its ZDO can say, for one whose body is already gone: a creature
        /// that blew up as it died has no Character to ask, but the ZDO lingers a moment with
        /// the prefab, the level and whether it was tamed. Null when even that is gone.
        /// </summary>
        internal static AttackerMemory.Entry DescribeById(ZDOID id)
        {
            try
            {
                if (id == ZDOID.None || ZDOMan.instance == null || ZNetScene.instance == null) return null;
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null) return null;
                GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                if (prefab == null) return null;
                var e = new AttackerMemory.Entry { Prefab = prefab.name ?? "", Weapon = "" };

                if (prefab.GetComponent<Player>() != null)
                {
                    string pn = zdo.GetString(ZDOVars.s_playerName, "");
                    e.Description = string.IsNullOrEmpty(pn) ? "someone" : pn;
                    e.IsPlayer = true;
                    return e;
                }

                var c = prefab.GetComponent<Character>();
                string name = c != null ? Localization.instance.Localize(c.m_name ?? "").Trim() : "";
                if (name.Length == 0) name = e.Prefab;

                if (zdo.GetBool(ZDOVars.s_tamed))
                {
                    string pet = TheObituariesMod.TameNames.Value ? (zdo.GetString(ZDOVars.s_tamedName) ?? "").Trim() : "";
                    e.Description = pet.Length > 0 ? pet + " the " + name : "a tame " + name;
                    return e;
                }
                if (c != null && c.m_boss) { e.Description = name; return e; }

                int level = zdo.GetInt(ZDOVars.s_level, 1);
                if (level > 1 && TheObituariesMod.LevelStars.Value) name = (level - 1) + "-star " + name;
                e.Description = Article(name) + " " + name;
                return e;
            }
            catch { return null; }
        }

        internal static string Article(string name)
        {
            if (string.IsNullOrEmpty(name)) return "a";
            return "aeiouAEIOU".IndexOf(name[0]) >= 0 ? "an" : "a";
        }

        internal static string PrefabName(Character c)
        {
            if (c == null) return "";
            string n = c.gameObject.name ?? "";
            int clone = n.IndexOf("(Clone)", StringComparison.Ordinal);
            return clone >= 0 ? n.Substring(0, clone) : n;
        }

        internal static string Pick(string[] lines)
        {
            if (lines == null || lines.Length == 0) return "{v} died";
            return lines[Rng.Next(lines.Length)];
        }

        // ── previews, for the console command ────────────────────────────────────────────

        /// <summary>Every creature prefix the table knows, for 'obituary' tab help and 'obituary all'.</summary>
        internal static IEnumerable<string> KnownCreatures()
        {
            foreach (var (prefix, _) in Creatures) yield return prefix;
        }

        /// <summary>
        /// A made-up obituary for a named cause, so the display can be checked without dying.
        /// The cause is a hit type, a creature, or "pvp"; a weapon kind may follow it
        /// ("troll log", "draugr bow", "pvp club").
        /// </summary>
        internal static Notice Sample(string cause, string weaponKind, Player victim)
        {
            var n = new Notice { Victim = victim != null ? victim.GetPlayerName() : "You" };
            if (string.IsNullOrEmpty(n.Victim)) n.Victim = "You";
            WeaponKind kind;
            if (string.IsNullOrEmpty(weaponKind))
            {
                var kinds = (WeaponKind[])Enum.GetValues(typeof(WeaponKind));
                kind = kinds[Rng.Next(kinds.Length)];
            }
            else if (!Enum.TryParse(weaponKind, ignoreCase: true, out kind)) return null;

            if (cause.Equals("pvp", StringComparison.OrdinalIgnoreCase))
            {
                n.Killer = "Ragnar"; n.KillerIsPlayer = true;
                n.Template = Pick(PlayerLines(kind));
            }
            else if (cause.Equals("self", StringComparison.OrdinalIgnoreCase) && Rng.Next(2) == 0)
            {
                n.Template = Pick(SelfInflicted);
            }
            else if (Enum.TryParse(cause, ignoreCase: true, out HitData.HitType type))
            {
                n.Template = Pick(EnvironmentLines(type));
            }
            else
            {
                string prefix = null;
                foreach (var (p, _) in Creatures)
                    if (p.Equals(cause, StringComparison.OrdinalIgnoreCase)) { prefix = p; break; }
                if (prefix == null) return null;
                string name = prefix.Replace("_", " ");
                n.Killer   = Article(name) + " " + name;
                n.Template = Pick(CreatureLines(prefix, kind));
            }

            n.Template = Pronouns.Fill(n.Template, Pronouns.For(victim));
            return n;
        }
    }
}
