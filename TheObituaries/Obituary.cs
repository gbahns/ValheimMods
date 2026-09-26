using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheObituaries
{
    /// <summary>
    /// Turns a death into a Quake obituary. The lines are lifted from Quake (client.qc),
    /// Quake II (p_client.c) and Quake III (cg_event.c) and mapped onto what Valheim knows about
    /// a death: the HitData's type, its dominant damage type, the weapon skill behind it, and
    /// the creature or player that dealt it.
    ///
    /// {v} is the victim, {k} the killer. Every pool is a list so the pick can vary.
    /// </summary>
    internal static class Obituary
    {
        private static readonly System.Random Rng = new System.Random();

        // ── environment: no killer, or a killer nothing can be said about ───────────────

        private static readonly Dictionary<HitData.HitType, string[]> Environment = new Dictionary<HitData.HitType, string[]>
        {
            { HitData.HitType.Undefined,     new[] { "{v} died" } },
            { HitData.HitType.EnemyHit,      new[] { "{v} died" } },
            { HitData.HitType.PlayerHit,     new[] { "{v} died" } },
            { HitData.HitType.Fall,          new[] { "{v} cratered", "{v} fell to their death" } },
            { HitData.HitType.Drowning,      new[] { "{v} sank like a rock", "{v} sleeps with the fishes", "{v} went for a swim and never came back", "{v} drowned", "{v} thought they could breathe water" } },
            { HitData.HitType.Burning,       new[] { "{v} burnt to a crisp", "{v} was fried" } },
            { HitData.HitType.Freezing,      new[] { "{v} was frozen solid", "{v} should have brought a coat", "{v} turned to ice", "{v} turned into an ice cube" } },
            { HitData.HitType.Poisoned,      new[] { "{v} was poisoned to death", "{v} died from poisoning" } },
            { HitData.HitType.Water,         new[] { "{v} sank like a rock", "{v} sleeps with the fishes" } },
            { HitData.HitType.Smoke,         new[] { "{v} was smoked out", "{v} thought they could breathe smoke like air" } },
            { HitData.HitType.EdgeOfWorld,   new[] { "{v} tried to leave", "{v} found a way out", "{v} was in the wrong place" } },
            { HitData.HitType.Impact,        new[] { "{v} learned about gravity the hard way" } },
            { HitData.HitType.Cart,          new[] { "{v} was squished by a cart", "{v} was run over by a cart", "{v} was crushed by a cart", "{v} was flattened by a cart", "{v} lost a fight with a cart" } },
            { HitData.HitType.Tree,          new[] { "{v} was squished by a tree", "{v} took on a tree and lost" } },
            { HitData.HitType.Self,          new[] { "{v} suicides", "{v} becomes bored with life", "{v} killed themself", "{v} was fed up with this cruel world", "{v} was tired of life", "{v} wanted to see what it's like on the other side", "{v} had seen enough" } },
            { HitData.HitType.Structural,    new[] { "{v} was squished by a building", "{v} was buried by their own building" } },
            { HitData.HitType.Turret,        new[] { "{v} was gunned down by a ballista" } },
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
            { "{v} blew themself up", "{v} tripped on their own bomb", "{v} should have used a smaller gun" };

        // ── killed by a creature ─────────────────────────────────────────────────────────
        //
        // By prefab name, most specific first, matched as a prefix (so "Wolf" also takes
        // "Wolf_cub" and "Skeleton" takes "Skeleton_NoArcher"). Quake's monster lines are
        // recast for Valheim's bestiary; anything not here falls back on the damage type.

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
            ("Boar",             new[] { "{v} was gored by {k}" }),
            ("Neck",             new[] { "{v} was nibbled to death by {k}" }),
            ("Greyling",         new[] { "{v} was scragged by {k}" }),
            ("Greydwarf_Shaman", new[] { "{v} was vomited on by {k}" }),
            ("Greydwarf_Elite",  new[] { "{v} was mauled by {k}" }),
            ("Greydwarf",        new[] { "{v} was scragged by {k}" }),
            ("Troll",            new[] { "{v} was smashed by {k}" }),
            ("Skeleton_Poison",  new[] { "{v} was slimed by {k}" }),
            ("Skeleton",         new[] { "{v} was slashed by {k}" }),
            ("Ghost",            new[] { "{v} became one with {k}" }),
            // swamp
            ("Wraith",           new[] { "{v} was scragged by {k}" }),
            ("BlobTar",          new[] { "{v} was tarred by {k}" }),
            ("BlobLava",         new[] { "{v} was melted by {k}" }),
            ("Blob",             new[] { "{v} was slimed by {k}" }),
            ("Draugr_Ranged",    new[] { "{v} was perforated by {k}" }),
            ("Draugr",           new[] { "{v} joins the Draugr", "{v} was slain by {k}" }),
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
            ("Deathsquito",      new[] { "{v} was perforated by {k}" }),
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
        };

        // Fallback by the hit's dominant damage type, for creatures the table does not know.
        private static readonly string[] ByBlunt = { "{v} was smashed by {k}", "{v} was pummeled by {k}", "{v} was destroyed by {k}" };
        private static readonly string[] BySlash = { "{v} was slashed by {k}", "{v} was eviscerated by {k}" };
        private static readonly string[] ByPierce = { "{v} was perforated by {k}", "{v} was spiked by {k}" };
        private static readonly string[] ByArrow = { "{v} was railed by {k}", "{v} ate {k}'s arrow", "{v} was gunned down by {k}" };
        private static readonly string[] ByFire = { "{v} was fried by {k}", "{v} was melted by {k}" };
        private static readonly string[] ByFrost = { "{v} was frozen solid by {k}" };
        private static readonly string[] ByLightning = { "{v} was electrocuted by {k}", "{v} was zapped by {k}" };
        private static readonly string[] ByPoison = { "{v} was slimed by {k}" };
        private static readonly string[] BySpirit = { "{v} was scragged by {k}" };
        private static readonly string[] ByAnything = { "{v} was killed by {k}", "{v} was slain by {k}" };

        // ── killed by a player, by the weapon skill ──────────────────────────────────────

        private static readonly Dictionary<Skills.SkillType, string[]> ByWeapon = new Dictionary<Skills.SkillType, string[]>
        {
            { Skills.SkillType.Unarmed,        new[] { "{v} was pummeled by {k}" } },
            { Skills.SkillType.Swords,         new[] { "{v} was slashed by {k}", "{v} was cut in half by {k}" } },
            { Skills.SkillType.Axes,           new[] { "{v} was ax-murdered by {k}" } },
            { Skills.SkillType.Knives,         new[] { "{v} was knifed by {k}", "{v} was perforated by {k}" } },
            { Skills.SkillType.Clubs,          new[] { "{v} was pummeled by {k}", "{v} was smashed by {k}" } },
            { Skills.SkillType.Polearms,       new[] { "{v} was skewered by {k}" } },
            { Skills.SkillType.Spears,         new[] { "{v} was spiked by {k}", "{v} eats {k}'s spear" } },
            { Skills.SkillType.Bows,           new[] { "{v} was railed by {k}", "{v} ate {k}'s arrow", "{v} almost dodged {k}'s arrow" } },
            { Skills.SkillType.Crossbows,      new[] { "{v} was railed by {k}", "{v} was nailed by {k}" } },
            { Skills.SkillType.ElementalMagic, new[] { "{v} was melted by {k}'s staff", "{v} was electrocuted by {k}", "{v} was fried by {k}" } },
            { Skills.SkillType.BloodMagic,     new[] { "{v} saw the pretty lights from {k}'s staff", "{v} was hexed by {k}" } },
            { Skills.SkillType.Pickaxes,       new[] { "{v} was mined by {k}" } },
        };
        private static readonly string[] ByPlayerAnything = { "{v} was killed by {k}", "{v} was gunned down by {k}" };

        // ── composing ────────────────────────────────────────────────────────────────────

        /// <summary>The obituary for this player's death, from the last hit it took.</summary>
        public static Notice Compose(Player victim, HitData hit)
        {
            var n = new Notice { Victim = victim != null ? victim.GetPlayerName() : "" };
            if (string.IsNullOrEmpty(n.Victim)) n.Victim = "Someone";

            var type = hit != null ? hit.m_hitType : HitData.HitType.Undefined;
            ZDOID attackerId = hit != null ? hit.m_attacker : ZDOID.None;
            Character killer = hit?.GetAttacker();

            if (attackerId != ZDOID.None && victim != null && attackerId == victim.GetZDOID())
            {
                n.Template = Pick(type == HitData.HitType.Self ? Environment[HitData.HitType.Self] : SelfInflicted);
                return n;
            }

            if (type == HitData.HitType.EnemyHit || type == HitData.HitType.PlayerHit)
            {
                if (killer != null)
                {
                    n.Killer = Describe(killer);
                    n.KillerId = attackerId;
                    n.KillerIsPlayer = killer is Player;
                    n.Template = killer is Player ? Pick(PlayerLines(hit)) : Pick(CreatureLines(killer, hit));
                    return n;
                }

                // The killer is no longer in the scene (despawned, or killed since), but it was
                // there when it hit us, and AttackerMemory kept its description from then.
                var remembered = AttackerMemory.Lookup(attackerId);
                if (remembered != null)
                {
                    n.Killer = remembered.Description;
                    n.KillerId = attackerId;
                    n.KillerIsPlayer = remembered.IsPlayer;
                    n.Template = remembered.IsPlayer ? Pick(PlayerLines(hit)) : Pick(CreatureLines(remembered.Prefab, hit));
                    return n;
                }
            }

            n.Template = Pick(EnvironmentLines(type));
            return n;
        }

        internal static string[] EnvironmentLines(HitData.HitType type)
            => Environment.TryGetValue(type, out var lines) ? lines : Environment[HitData.HitType.Undefined];

        internal static string[] PlayerLines(HitData hit)
            => hit != null && ByWeapon.TryGetValue(hit.m_skill, out var lines) ? lines : ByPlayerAnything;

        internal static string[] CreatureLines(Character killer, HitData hit)
            => CreatureLines(PrefabName(killer), hit);

        internal static string[] CreatureLines(string prefab, HitData hit)
        {
            foreach (var (prefix, lines) in Creatures)
                if (prefab.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return lines;
            return DamageLines(hit);
        }

        private static string[] DamageLines(HitData hit)
        {
            if (hit == null) return ByAnything;
            var d = hit.m_damage;
            string[] best = ByAnything; float most = 0f;
            void Consider(float amount, string[] lines) { if (amount > most) { most = amount; best = lines; } }
            Consider(d.m_blunt, ByBlunt);
            Consider(d.m_slash, BySlash);
            Consider(d.m_pierce, hit.m_ranged ? ByArrow : ByPierce);
            Consider(d.m_fire, ByFire);
            Consider(d.m_frost, ByFrost);
            Consider(d.m_lightning, ByLightning);
            Consider(d.m_poison, ByPoison);
            Consider(d.m_spirit, BySpirit);
            return best;
        }

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

        /// <summary>A made-up obituary for a named cause, so the display can be checked without dying.</summary>
        internal static Notice Sample(string cause, string victim)
        {
            var n = new Notice { Victim = victim };
            if (cause.Equals("pvp", StringComparison.OrdinalIgnoreCase))
            {
                n.Killer = "Ragnar"; n.KillerIsPlayer = true;
                var skills = new List<Skills.SkillType>(ByWeapon.Keys);
                n.Template = Pick(ByWeapon[skills[Rng.Next(skills.Count)]]);
                return n;
            }
            if (cause.Equals("self", StringComparison.OrdinalIgnoreCase) && Rng.Next(2) == 0)
            {
                n.Template = Pick(SelfInflicted);
                return n;
            }
            if (Enum.TryParse(cause, ignoreCase: true, out HitData.HitType type))
            {
                n.Template = Pick(EnvironmentLines(type));
                return n;
            }
            foreach (var (prefix, lines) in Creatures)
            {
                if (!prefix.Equals(cause, StringComparison.OrdinalIgnoreCase)) continue;
                string name = prefix.Replace("_", " ");
                n.Killer = Article(name) + " " + name;
                n.Template = Pick(lines);
                return n;
            }
            return null;
        }
    }
}
