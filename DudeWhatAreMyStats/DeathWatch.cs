using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Keeps a ledger of the fight you are in, and turns it into a <see cref="DeathReport"/> when
    /// you lose it.
    ///
    /// Valheim records that you died and, by category, what sort of thing did it, but never which
    /// creature. That is the gap this fills. Everything here runs on the local player's own client:
    /// the hit that killed lives on their character, and the creatures around them are loaded only
    /// in their scene, so nobody else could compose this even in principle.
    ///
    /// Three hooks, doing different jobs, and the difference matters:
    ///
    ///  * <b>Character.Damage</b> sees a blow before block, resistance and armor, and before the
    ///    early exit that drops a hit you dodged. It is therefore the right place to record that
    ///    something swung at you and connected, and the only place that still sees the poison, fire
    ///    and spirit components of a hit -- Damage strips those out into damage-over-time effects
    ///    before passing the rest on. It is the wrong place to read damage numbers from.
    ///  * <b>Character.ApplyDamage</b> sees what the blow actually cost you, after everything. A hit
    ///    you blocked to nothing arrives here worth nothing, which is exactly right: the Draugr
    ///    gets credit for the swing and none for the damage.
    ///  * <b>Humanoid.BlockAttack</b> spends the stamina itself and is handed the attacker, so the
    ///    stamina a creature cost you by making you block can be charged to that creature by name.
    ///
    /// On top of those, a twice-a-second sweep of the loaded characters picks up everything that has
    /// you as its target, whether or not it ever lands a blow. That is what puts the ten greydwarves
    /// who surrounded you in the record alongside the troll that finished you.
    /// </summary>
    internal static class DeathWatch
    {
        /// <summary>How often the target sweep runs. Twice a second is plenty for a roster.</summary>
        private const float SampleInterval = 0.5f;

        /// <summary>Ignore something targeting you from further off than this, in meters.</summary>
        private const float PresenceRange = 40f;

        /// <summary>
        /// The most creatures one fight can hold. A raid can put dozens of things on you; past this
        /// the ones already in the ledger keep their history rather than being displaced by
        /// newcomers, and the report says it was capped.
        /// </summary>
        private const int MaxParticipants = 24;

        private static readonly Dictionary<ZDOID, Participant> _live = new Dictionary<ZDOID, Participant>();

        /// <summary>
        /// Who last dealt each kind of over-time damage, for attributing its ticks.
        ///
        /// A poison or burning tick is dealt by the status effect, not the creature: it carries no
        /// attacker at all, because StatusEffect.SetAttacker is an empty virtual that neither
        /// SE_Poison nor SE_Burning overrides. The creature that applied it is known only from the
        /// hit that landed, so it is remembered here and the ticks are charged to it, marked as
        /// inferred. That is how bleeding out from a Blob's poison is credited to the Blob rather
        /// than to nothing, even after the Blob itself is dead.
        /// </summary>
        private static readonly Dictionary<string, ZDOID> _lastDot = new Dictionary<string, ZDOID>();

        private static float _fightStart;
        private static float _lastContact;
        private static float _nextSample;
        private static float _lastSample;
        private static int _hitsTaken;
        private static float _biggestHit;
        private static float _staminaBlocking;
        private static bool _capped;
        private static float _lastSeal;


        /// <summary>
        /// Set if something in here throws, which switches the ledger off for the rest of the
        /// session rather than logging a stack trace every time you are hit. The config setting is
        /// left alone: this is a fault, not a preference, and it should be gone next launch.
        /// </summary>
        private static bool _failed;

        internal static bool Enabled => !_failed && DwamsConfig.RecordDeaths != null && DwamsConfig.RecordDeaths.Value;

        internal static bool InFight => _fightStart > 0f;
        internal static int LiveCount => _live.Count;

        /// <summary>Drops the ledger. Called when a world starts or ends, and after every death.</summary>
        internal static void Reset()
        {
            _live.Clear();
            _lastDot.Clear();
            _fightStart = 0f;
            _lastContact = 0f;
            _hitsTaken = 0;
            _biggestHit = 0f;
            _staminaBlocking = 0f;
            _capped = false;
        }

        // ── the sweep ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Ages out a finished fight and picks up everything currently targeting you. Called once a
        /// frame from the mod's Update; does its real work twice a second.
        /// </summary>
        internal static void Update()
        {
            if (!Enabled) return;
            var player = Player.m_localPlayer;
            if (player == null) return;

            float now = Time.time;
            if (_fightStart > 0f && now - _lastContact > DwamsConfig.FightGapSeconds.Value)
            {
                // Quiet long enough to call it over. Nothing is kept: a report is only ever made
                // from a fight that was still running when it killed you.
                Reset();
            }
            if (now < _nextSample) return;

            float dt = _lastSample > 0f ? Mathf.Min(now - _lastSample, SampleInterval * 4f) : SampleInterval;
            _lastSample = now;
            _nextSample = now + SampleInterval;
            if (!DwamsConfig.RecordBystanders.Value) return;

            try
            {
                Vector3 me = player.transform.position;
                float range = PresenceRange * PresenceRange;
                // The live list, not a copy; read only.
                var all = Character.GetAllCharacters();
                for (int i = 0; i < all.Count; i++)
                {
                    Character c = all[i];
                    if (c == null || c == player || c.IsDead() || c.IsPlayer()) continue;
                    if ((c.transform.position - me).sqrMagnitude > range) continue;
                    BaseAI ai = c.GetBaseAI();
                    if (ai == null || ai.GetTargetCreature() != player) continue;
                    Participant p = Touch(c);
                    if (p == null) continue;
                    p.SecondsTargeting += dt;
                    // Something is still on you, so the fight has not gone quiet.
                    _lastContact = now;
                    if (_fightStart <= 0f) _fightStart = now;
                }
            }
            catch (Exception e)
            {
                Warn("Could not sweep for nearby attackers", e);
            }
        }

        // ── the ledger ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The row for this creature, adding it if the fight has room. Null once the fight is full,
        /// which leaves the creatures already in it with their history intact.
        /// </summary>
        private static Participant Touch(Character c, bool swinging = false)
        {
            ZDOID id = c.GetZDOID();
            if (id == ZDOID.None) return null;
            if (_live.TryGetValue(id, out var existing))
            {
                // Only when it actually swung: a creature can switch weapons mid-fight, and the one
                // worth recording is the one it last hit you with. Asking on every sweep instead
                // would walk the equipment of two dozen creatures twice a second for no gain.
                if (swinging)
                {
                    string weapon = WeaponName(c);
                    if (weapon.Length > 0) existing.Weapon = weapon;
                }
                return existing;
            }
            if (_live.Count >= MaxParticipants)
            {
                _capped = true;
                return null;
            }
            var p = new Participant
            {
                Key = NameKey(c),
                Display = Describe(c),
                Level = c.GetLevel(),
                IsPlayer = c is Player,
                Weapon = WeaponName(c),
                Involvement = Involvement.Present,
            };
            _live[id] = p;
            return p;
        }

        /// <summary>Marks contact, starting the fight clock if this is the first of it.</summary>
        private static void Contact()
        {
            float now = Time.time;
            if (_fightStart <= 0f) _fightStart = now;
            _lastContact = now;
        }

        /// <summary>
        /// A blow reached you, before anything reduced it. Records the attempt, and remembers who
        /// dealt over-time damage while the hit still carries it.
        /// </summary>
        internal static void RecordAttempt(Character attacker, HitData hit, bool dodged)
        {
            if (!Enabled) return;
            try
            {
                Contact();
                if (attacker == null) return;
                ZDOID id = attacker.GetZDOID();
                if (id != ZDOID.None)
                {
                    // Damage() has not stripped these out yet; this is the only place they are visible.
                    if (hit.m_damage.m_poison > 0f) _lastDot["poison"] = id;
                    if (hit.m_damage.m_fire > 0f) _lastDot["fire"] = id;
                    if (hit.m_damage.m_spirit > 0f) _lastDot["spirit"] = id;
                }
                Participant p = Touch(attacker, swinging: true);
                if (p == null) return;
                p.Attempts++;
                if (dodged) p.Dodged++;
                p.Raise(Involvement.Pressed);
            }
            catch (Exception e)
            {
                Warn("Could not record an attack", e);
            }
        }

        /// <summary>What the blow actually cost, after block, resistance and armor.</summary>
        internal static void RecordDamage(HitData hit)
        {
            if (!Enabled) return;
            try
            {
                Contact();
                // The same modifier ApplyDamage is about to apply, so this is the health it takes.
                float amount = hit.GetTotalDamage() * Game.m_localDamgeTakenRate;
                bool real = amount > 0.1f;      // vanilla's own threshold for "took damage"

                // A poison or burning tick comes straight here from the status effect, not through
                // Damage, and there are dozens of them. They are damage, and they are not blows:
                // counting them would report thirty hits taken from one bite.
                bool tick = DotKind(hit.m_hitType) != null && !hit.HaveAttacker();
                if (real && !tick)
                {
                    _hitsTaken++;
                    if (amount > _biggestHit) _biggestHit = amount;
                }

                Participant p = Resolve(hit, out bool inferred);
                if (p == null) return;
                if (!real)
                {
                    p.Raise(Involvement.Pressed);
                    return;
                }
                p.Damage += amount;
                if (!tick)
                {
                    p.Landed++;
                    if (amount > p.BiggestHit) p.BiggestHit = amount;
                }
                if (inferred) p.Inferred = true;
                p.Raise(Involvement.Hurt);
            }
            catch (Exception e)
            {
                Warn("Could not record damage taken", e);
            }
        }

        /// <summary>Stamina a creature cost you by making you block its blow.</summary>
        internal static void RecordBlock(Character attacker, float stamina)
        {
            if (!Enabled) return;
            try
            {
                Contact();
                if (stamina > 0f) _staminaBlocking += stamina;
                if (attacker == null) return;
                Participant p = Touch(attacker);
                if (p == null) return;
                p.Blocked++;
                if (stamina > 0f) p.StaminaDrained += stamina;
                p.Raise(Involvement.Pressed);
            }
            catch (Exception e)
            {
                Warn("Could not record a block", e);
            }
        }

        /// <summary>
        /// Who to charge this hit to: its attacker, the one we remember under its id once the
        /// creature has despawned, or, for an over-time tick that carries no attacker at all,
        /// whoever last dealt that kind of damage.
        /// </summary>
        private static Participant Resolve(HitData hit, out bool inferred)
        {
            inferred = false;
            Character attacker = hit.HaveAttacker() ? hit.GetAttacker() : null;
            if (attacker != null) return Touch(attacker);

            // The attacker is named but no longer in the scene: killed, or unloaded. Its row is
            // already here from the hit that landed while it was.
            if (hit.m_attacker != ZDOID.None && _live.TryGetValue(hit.m_attacker, out var known)) return known;

            string kind = DotKind(hit.m_hitType);
            if (kind == null) return null;
            if (_lastDot.TryGetValue(kind, out var source) && _live.TryGetValue(source, out var p))
            {
                inferred = true;
                return p;
            }
            // Burning covers both fire and spirit; try the other one before giving up.
            if (kind == "fire" && _lastDot.TryGetValue("spirit", out var spirit) && _live.TryGetValue(spirit, out var sp))
            {
                inferred = true;
                return sp;
            }
            return null;
        }

        private static string DotKind(HitData.HitType type)
        {
            if (type == HitData.HitType.Poisoned) return "poison";
            if (type == HitData.HitType.Burning) return "fire";
            return null;
        }

        // ── sealing a death ─────────────────────────────────────────────────────────

        /// <summary>
        /// Turns the fight into a report and hands it to the log. Called from Player.OnDeath, which
        /// runs on the dying player's own client and is the last place the killing hit exists.
        /// </summary>
        internal static void Seal(Player player, HitData hit)
        {
            if (!Enabled) return;
            // OnDeath is vanilla's own one-per-death call, but a mod, or a second blow landing in
            // the same frame, could reach it twice; a death should not be filed twice for that.
            float sealNow = Time.time;
            if (_lastSeal > 0f && sealNow - _lastSeal < 2f) return;
            _lastSeal = sealNow;
            try
            {
                var r = new DeathReport { WhenUtc = Snapshot.NowUtc() };

                if (EnvMan.instance != null)
                {
                    r.Day = EnvMan.instance.GetDay();
                    r.AtNight = EnvMan.IsNight();
                }
                r.Biome = BiomeName(player);
                r.MaxHealth = player.GetMaxHealth();
                r.Armor = player.GetBodyArmor();
                r.MyWeapon = WeaponName(player);
                ReadEffects(player, r.Effects);

                Participant killer = null;
                if (hit != null)
                {
                    r.HitType = hit.m_hitType.ToString();
                    r.BlowRanged = hit.m_ranged;
                    r.BlowBackstab = hit.m_backstabBonus > 1f;
                    r.BlowBlockable = hit.m_blockable;
                    r.BlowTotal = hit.GetTotalDamage();
                    Breakdown(hit, r.Blow);

                    Character c = hit.HaveAttacker() ? hit.GetAttacker() : null;
                    if (c != null) killer = Touch(c);
                    // Despawned or already dead: its row is here from the hit that landed while it
                    // was still around, which is the whole reason the ledger is kept as it goes.
                    else if (hit.m_attacker != ZDOID.None) _live.TryGetValue(hit.m_attacker, out killer);
                    if (killer == null)
                    {
                        string dot = DotKind(hit.m_hitType);
                        if (dot != null && _lastDot.TryGetValue(dot, out var src)) _live.TryGetValue(src, out killer);
                    }
                }

                if (killer != null)
                {
                    killer.Raise(Involvement.Killer);
                    r.Cause = killer.Key;
                    r.CauseDisplay = killer.Display;
                    r.KillerWeapon = killer.Weapon;
                    r.KillerLevel = killer.Level;
                    r.KillerIsPlayer = killer.IsPlayer;
                }
                else
                {
                    var type = hit != null ? hit.m_hitType : HitData.HitType.Undefined;
                    r.Cause = DeathReport.EnvironmentPrefix + type;
                    r.CauseDisplay = EnvironmentName(type);
                }

                float now = Time.time;
                r.FightSeconds = _fightStart > 0f ? Mathf.Max(0f, now - _fightStart) : 0f;
                r.HitsTaken = _hitsTaken;
                r.BiggestHit = _biggestHit;
                r.StaminaSpentBlocking = _staminaBlocking;

                // Something that merely had you in its sights for an instant was not in the fight.
                // Anything that swung, hurt you or killed you is in regardless of how long it took.
                float floor = DwamsConfig.BystanderSeconds.Value;
                foreach (var p in _live.Values)
                {
                    if (p.Involvement == Involvement.Present && p.SecondsTargeting < floor) continue;
                    r.Participants.Add(p);
                }
                if (_capped)
                    DudeWhatAreMyStatsMod.Log.LogInfo(
                        $"[DudeWhatAreMyStats] That fight had more than {MaxParticipants} creatures in it; the rest are not in the report.");

                DeathLog.Record(r);
            }
            catch (Exception e)
            {
                Warn("Could not record that death", e);
            }
            finally
            {
                // One fight, one report. Whatever happened above, the next fight starts clean.
                Reset();
            }
        }

        // ── naming things ───────────────────────────────────────────────────────────

        /// <summary>
        /// The key the aggregate tables count this creature under.
        ///
        /// Character.m_name is a localization key -- "$enemy_troll" -- and it is the very same key
        /// Valheim uses for its own per-creature kill tally, so a "killed by" table built on it can
        /// be read against "creatures killed" without translating between two naming schemes. A
        /// player goes in as "#" plus their name, which keeps every player from collapsing into one
        /// row the way a shared prefab name would. Anything nameless falls back to its prefab.
        /// </summary>
        internal static string NameKey(Character c)
        {
            if (c == null) return "";
            if (c is Player p)
            {
                string pn = p.GetPlayerName();
                return "#" + (string.IsNullOrEmpty(pn) ? "someone" : pn);
            }
            string key = (c.m_name ?? "").Trim();
            if (key.StartsWith("$", StringComparison.Ordinal)) return key;
            return PrefabName(c);
        }

        /// <summary>The prefab name, with Unity's "(Clone)" taken off.</summary>
        internal static string PrefabName(Character c)
        {
            if (c == null) return "";
            string n = c.gameObject.name ?? "";
            int clone = n.IndexOf("(Clone)", StringComparison.Ordinal);
            return clone >= 0 ? n.Substring(0, clone) : n;
        }

        /// <summary>
        /// What to call it in a report: a player's name, a pet's given name, "2-star Troll".
        /// Unlike the aggregate tables, which count every Troll as a Troll, this keeps the detail.
        /// </summary>
        internal static string Describe(Character c)
        {
            if (c == null) return "something";
            if (c is Player p)
            {
                string pn = p.GetPlayerName();
                return string.IsNullOrEmpty(pn) ? "someone" : pn;
            }

            string name = Localization.instance != null ? Localization.instance.Localize(c.m_name ?? "").Trim() : "";
            if (name.Length == 0) name = PrefabName(c);

            if (c.IsTamed())
            {
                string pet = "";
                var nview = c.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid()) pet = (nview.GetZDO().GetString(ZDOVars.s_tamedName) ?? "").Trim();
                return pet.Length > 0 ? pet + " the " + name : "tame " + name;
            }
            if (c.IsBoss()) return name;

            int level = c.GetLevel();
            return level > 1 ? (level - 1) + "-star " + name : name;
        }

        /// <summary>What it was holding, where the game names an item at all.</summary>
        internal static string WeaponName(Character c)
        {
            try
            {
                var w = (c as Humanoid)?.GetCurrentWeapon();
                if (w == null) return "";
                if (w.m_shared != null && !string.IsNullOrEmpty(w.m_shared.m_name) && Localization.instance != null)
                {
                    string localized = Localization.instance.Localize(w.m_shared.m_name).Trim();
                    if (localized.Length > 0 && !localized.StartsWith("[", StringComparison.Ordinal)) return localized;
                }
                return w.m_dropPrefab != null ? w.m_dropPrefab.name ?? "" : "";
            }
            catch { return ""; }
        }

        /// <summary>A readable name for a death nothing dealt: "a fall", "lava", "drowning".</summary>
        internal static string EnvironmentName(HitData.HitType type)
        {
            switch (type)
            {
                case HitData.HitType.Fall:          return "a fall";
                case HitData.HitType.Drowning:      return "drowning";
                case HitData.HitType.Water:         return "drowning";
                case HitData.HitType.Burning:       return "burning";
                case HitData.HitType.Freezing:      return "the cold";
                case HitData.HitType.Poisoned:      return "poison";
                case HitData.HitType.Smoke:         return "smoke";
                case HitData.HitType.EdgeOfWorld:   return "the edge of the world";
                case HitData.HitType.Tree:          return "a tree";
                case HitData.HitType.Cart:          return "a cart";
                case HitData.HitType.Boat:          return "a boat";
                case HitData.HitType.Structural:    return "a collapsing building";
                case HitData.HitType.DrawBridge:    return "a drawbridge";
                case HitData.HitType.Turret:        return "a ballista";
                case HitData.HitType.Catapult:      return "a catapult";
                case HitData.HitType.Stalagtite:    return "a stalactite";
                case HitData.HitType.Incinerator:   return "an incinerator";
                case HitData.HitType.CinderFire:    return "cinders";
                case HitData.HitType.AshlandsLava:  return "lava";
                case HitData.HitType.AshlandsOcean: return "boiling water";
                case HitData.HitType.Impact:        return "an impact";
                case HitData.HitType.Self:          return "your own hand";
                case HitData.HitType.EnemyHit:      return "something unknown";
                case HitData.HitType.PlayerHit:     return "another player";
                default:                            return "something unknown";
            }
        }

        private static string BiomeName(Player player)
        {
            try
            {
                var biome = player.GetCurrentBiome();
                if (biome == Heightmap.Biome.None) return "";
                string localized = Localization.instance != null
                    ? Localization.instance.Localize("$biome_" + biome.ToString().ToLowerInvariant()).Trim()
                    : "";
                if (localized.Length > 0 && !localized.StartsWith("[", StringComparison.Ordinal)) return localized;
                return biome.ToString();
            }
            catch { return ""; }
        }

        private static void ReadEffects(Player player, List<string> into)
        {
            try
            {
                var seman = player.GetSEMan();
                if (seman == null) return;
                foreach (var se in seman.GetStatusEffects())
                {
                    if (se == null || into.Count >= 8) break;
                    string name = Localization.instance != null ? Localization.instance.Localize(se.m_name ?? "").Trim() : "";
                    if (name.Length == 0 || name.StartsWith("[", StringComparison.Ordinal)) name = se.name;
                    if (!string.IsNullOrEmpty(name)) into.Add(name);
                }
            }
            catch { /* the report is worth having without them */ }
        }

        /// <summary>The killing blow split by damage type, leaving out the types that were zero.</summary>
        private static void Breakdown(HitData hit, List<KeyValuePair<string, float>> into)
        {
            var d = hit.m_damage;
            Add(into, "blunt", d.m_blunt);
            Add(into, "slash", d.m_slash);
            Add(into, "pierce", d.m_pierce);
            Add(into, "chop", d.m_chop);
            Add(into, "pickaxe", d.m_pickaxe);
            Add(into, "fire", d.m_fire);
            Add(into, "frost", d.m_frost);
            Add(into, "lightning", d.m_lightning);
            Add(into, "poison", d.m_poison);
            Add(into, "spirit", d.m_spirit);
            into.Sort((a, b) => b.Value.CompareTo(a.Value));
        }

        private static void Add(List<KeyValuePair<string, float>> into, string name, float value)
        {
            if (value > 0f) into.Add(new KeyValuePair<string, float>(name, value));
        }

        private static void Warn(string what, Exception e)
        {
            if (_failed) return;
            _failed = true;
            DudeWhatAreMyStatsMod.Log.LogWarning(
                $"[DudeWhatAreMyStats] {what}; death details are off for the rest of this session. {e}");
        }
    }
}
