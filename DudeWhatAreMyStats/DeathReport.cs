using System;
using System.Collections.Generic;
using System.Text;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// How much a creature had to do with a death, lowest to highest. A participant is filed under
    /// the highest it earned over the whole fight, so the Draugr that pinned you down for twenty
    /// seconds and then landed the kill is a Killer, not three separate rows.
    /// </summary>
    internal enum Involvement
    {
        /// <summary>Had you as its target and never connected. The greydwarves circling you.</summary>
        Present = 0,
        /// <summary>Connected for no health: blocked, dodged, or a blow armor swallowed whole.</summary>
        Pressed = 1,
        /// <summary>Took health off you.</summary>
        Hurt = 2,
        /// <summary>Landed the blow that finished you.</summary>
        Killer = 3,
    }

    /// <summary>One creature's part in one fight.</summary>
    internal sealed class Participant
    {
        /// <summary>
        /// What the aggregate tables count this creature under, and the one thing here that has to
        /// stay stable between builds. It is the game's own name key -- "$enemy_troll" -- which is
        /// exactly what vanilla keys its own kill tally by, so "killed by Troll" and "Trolls killed"
        /// line up. A player is "#" plus their name, and anything with no key falls back to its
        /// prefab name.
        /// </summary>
        internal string Key = "";
        /// <summary>What to call it: "2-star Troll", "Fluffy the Wolf", a player's name.</summary>
        internal string Display = "";
        internal int Level = 1;
        internal bool IsPlayer;
        internal Involvement Involvement;

        /// <summary>Blows that reached you at all, blocked and dodged ones included.</summary>
        internal int Attempts;
        internal int Landed;
        internal int Blocked;
        internal int Dodged;

        /// <summary>Health actually lost to it, after block, resistance and armor.</summary>
        internal float Damage;
        /// <summary>Stamina this creature cost you by making you block.</summary>
        internal float StaminaDrained;
        internal float SecondsTargeting;
        internal float BiggestHit;

        /// <summary>Its weapon, where a hit named one.</summary>
        internal string Weapon = "";

        /// <summary>
        /// True when some of its damage was credited by inference rather than read off a hit.
        /// Poison and burning ticks carry no attacker, so they are charged to whoever last dealt
        /// that kind of damage, which is nearly always right and occasionally not.
        /// </summary>
        internal bool Inferred;

        internal void Raise(Involvement to)
        {
            if (to > Involvement) Involvement = to;
        }

        internal void Pack(ZPackage pkg)
        {
            pkg.Write(Key ?? "");
            pkg.Write(Display ?? "");
            pkg.Write(Level);
            pkg.Write(IsPlayer);
            pkg.Write((int)Involvement);
            pkg.Write(Attempts);
            pkg.Write(Landed);
            pkg.Write(Blocked);
            pkg.Write(Dodged);
            pkg.Write(Damage);
            pkg.Write(StaminaDrained);
            pkg.Write(SecondsTargeting);
            pkg.Write(BiggestHit);
            pkg.Write(Weapon ?? "");
            pkg.Write(Inferred);
        }

        internal static Participant Unpack(ZPackage pkg)
        {
            return new Participant
            {
                Key = pkg.ReadString(),
                Display = pkg.ReadString(),
                Level = pkg.ReadInt(),
                IsPlayer = pkg.ReadBool(),
                Involvement = (Involvement)pkg.ReadInt(),
                Attempts = pkg.ReadInt(),
                Landed = pkg.ReadInt(),
                Blocked = pkg.ReadInt(),
                Dodged = pkg.ReadInt(),
                Damage = pkg.ReadSingle(),
                StaminaDrained = pkg.ReadSingle(),
                SecondsTargeting = pkg.ReadSingle(),
                BiggestHit = pkg.ReadSingle(),
                Weapon = pkg.ReadString(),
                Inferred = pkg.ReadBool(),
            };
        }
    }

    /// <summary>
    /// One death, in as much detail as the game will give up.
    ///
    /// Only the dying player's own client knows any of this: the hit that killed is on their
    /// character and nowhere else, and the creatures around them are loaded only in their scene.
    /// So a report is composed locally, kept locally, and never rides the scoreboard message --
    /// the aggregate counts do that instead. See <see cref="DeathLog"/>.
    ///
    /// Everything here is what the victim's own game observed. It is a record among friends, not
    /// an audited one, exactly as the live stats already are.
    /// </summary>
    internal sealed class DeathReport
    {
        /// <summary>A cause that is not a creature is written with this in front, which no prefab name contains.</summary>
        internal const string EnvironmentPrefix = "@";

        internal long WhenUtc;
        internal int Day;
        internal string Biome = "";
        internal bool AtNight;

        /// <summary>Either a creature's prefab name, or "@" plus the hit type for a death nothing dealt.</summary>
        internal string Cause = "";
        internal string CauseDisplay = "";
        internal string KillerWeapon = "";
        internal int KillerLevel = 1;
        internal bool KillerIsPlayer;

        /// <summary>The game's own name for what did it: EnemyHit, Fall, AshlandsLava, and so on.</summary>
        internal string HitType = "";

        /// <summary>The killing blow's damage, by type, only the types that were not zero.</summary>
        internal readonly List<KeyValuePair<string, float>> Blow = new List<KeyValuePair<string, float>>();
        internal float BlowTotal;
        internal bool BlowRanged;
        internal bool BlowBackstab;
        internal bool BlowBlockable;

        // your own state as you went down
        internal float MaxHealth;
        internal float Armor;
        internal string MyWeapon = "";
        internal readonly List<string> Effects = new List<string>();

        // the shape of the fight
        internal float FightSeconds;
        internal int HitsTaken;
        internal float BiggestHit;
        internal float StaminaSpentBlocking;

        internal readonly List<Participant> Participants = new List<Participant>();

        internal bool IsEnvironment => Cause != null && Cause.StartsWith(EnvironmentPrefix, StringComparison.Ordinal);

        internal string WhenText
        {
            get
            {
                long seconds = Snapshot.NowUtc() - WhenUtc;
                if (seconds < 90L) return "just now";
                if (seconds < 3600L) return (seconds / 60L) + "m ago";
                if (seconds < 86400L) return (seconds / 3600L) + "h ago";
                return (seconds / 86400L) + "d ago";
            }
        }

        // ── reading one out ─────────────────────────────────────────────────────────

        /// <summary>The report as lines of text, for the console and the log.</summary>
        internal List<string> Lines(bool full)
        {
            var lines = new List<string>();
            var head = new StringBuilder();
            head.Append(WhenText).Append(" - ").Append(Headline());
            if (Day > 0)
            {
                head.Append(" (day ").Append(Day);
                if (AtNight) head.Append(", night");
                if (Biome.Length > 0) head.Append(", ").Append(Biome);
                head.Append(")");
            }
            lines.Add(head.ToString());
            if (!full) return lines;

            var blow = new StringBuilder("  blow: ");
            blow.Append(StatGroups.Count(BlowTotal)).Append(" damage");
            if (Blow.Count > 0)
            {
                blow.Append(" (");
                for (int i = 0; i < Blow.Count; i++)
                {
                    if (i > 0) blow.Append(", ");
                    blow.Append(Blow[i].Key).Append(' ').Append(StatGroups.Count(Blow[i].Value));
                }
                blow.Append(")");
            }
            if (HitType.Length > 0) blow.Append(", ").Append(HitType);
            if (BlowRanged) blow.Append(", ranged");
            if (BlowBackstab) blow.Append(", backstab");
            if (!BlowBlockable) blow.Append(", unblockable");
            lines.Add(blow.ToString());

            var you = new StringBuilder("  you: ");
            you.Append(StatGroups.Count(MaxHealth)).Append(" max health, ").Append(StatGroups.Count(Armor)).Append(" armor");
            if (MyWeapon.Length > 0) you.Append(", holding ").Append(MyWeapon);
            if (Effects.Count > 0) you.Append(", ").Append(string.Join(" + ", Effects.ToArray()));
            lines.Add(you.ToString());

            var fight = new StringBuilder("  fight: ");
            fight.Append(StatGroups.Duration(FightSeconds)).Append(", ").Append(HitsTaken).Append(" hit(s) taken");
            if (BiggestHit > 0f) fight.Append(", worst ").Append(StatGroups.Count(BiggestHit));
            if (StaminaSpentBlocking > 0f) fight.Append(", ").Append(StatGroups.Count(StaminaSpentBlocking)).Append(" stamina blocking");
            lines.Add(fight.ToString());

            foreach (var p in Sorted()) lines.Add("  " + Row(p));
            return lines;
        }

        /// <summary>The one-line summary: what killed you, and how much company it had.</summary>
        internal string Headline()
        {
            var sb = new StringBuilder();
            sb.Append(CauseDisplay.Length > 0 ? CauseDisplay : "something unknown");
            if (KillerWeapon.Length > 0) sb.Append(" (").Append(KillerWeapon).Append(")");
            int others = 0;
            foreach (var p in Participants)
                if (p.Involvement != Involvement.Killer) others++;
            if (others > 0) sb.Append(", with ").Append(others).Append(others == 1 ? " other in the fight" : " others in the fight");
            return sb.ToString();
        }

        /// <summary>Participants, most involved first, then by the damage they did.</summary>
        internal List<Participant> Sorted()
        {
            var list = new List<Participant>(Participants);
            list.Sort((a, b) =>
            {
                int byTier = b.Involvement.CompareTo(a.Involvement);
                if (byTier != 0) return byTier;
                int byDamage = b.Damage.CompareTo(a.Damage);
                if (byDamage != 0) return byDamage;
                return b.SecondsTargeting.CompareTo(a.SecondsTargeting);
            });
            return list;
        }

        /// <summary>One participant as a line: what it is, then what it did.</summary>
        internal static string Row(Participant p)
        {
            var sb = new StringBuilder();
            if (p.Involvement == Involvement.Killer) sb.Append("killed you: ");
            sb.Append(p.Display);
            var did = new List<string>();
            if (p.Damage > 0f) did.Add(StatGroups.Count(p.Damage) + " damage" + (p.Inferred ? " (inferred)" : ""));
            if (p.Landed > 0) did.Add(p.Landed + " hit(s)");
            if (p.Blocked > 0) did.Add(p.Blocked + " blocked");
            if (p.Dodged > 0) did.Add(p.Dodged + " dodged");
            if (p.StaminaDrained > 0f) did.Add(StatGroups.Count(p.StaminaDrained) + " stamina");
            if (did.Count == 0 && p.SecondsTargeting > 0f) did.Add("on you " + StatGroups.Duration(p.SecondsTargeting));
            if (did.Count == 0) did.Add("in the fight");
            sb.Append(": ").Append(string.Join(", ", did.ToArray()));
            return sb.ToString();
        }

        /// <summary>
        /// A cause key as something to read: "$enemy_troll" becomes Troll, "@Fall" becomes a fall,
        /// "#Marco" becomes Marco. Used by every table that counts causes, so they all agree.
        /// </summary>
        internal static string CauseName(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(unknown)";
            if (key[0] == EnvironmentPrefix[0])
            {
                string name = key.Substring(1);
                try { return DeathWatch.EnvironmentName((HitData.HitType)Enum.Parse(typeof(HitData.HitType), name)); }
                catch { return name; }
            }
            if (key[0] == '#') return key.Substring(1);
            if (key[0] == '$' && Localization.instance != null)
            {
                string localized = Localization.instance.Localize(key);
                if (!string.IsNullOrEmpty(localized) && localized != key && !localized.StartsWith("[", StringComparison.Ordinal))
                    return localized;
            }
            return key.TrimStart('$');
        }

        // ── the file format ─────────────────────────────────────────────────────────
        //
        // Reports live only in this character's own file, so there is no wire version to agree
        // with anyone about; DeathLog's file schema covers the whole shape.

        internal void Pack(ZPackage pkg)
        {
            pkg.Write(WhenUtc);
            pkg.Write(Day);
            pkg.Write(Biome ?? "");
            pkg.Write(AtNight);
            pkg.Write(Cause ?? "");
            pkg.Write(CauseDisplay ?? "");
            pkg.Write(KillerWeapon ?? "");
            pkg.Write(KillerLevel);
            pkg.Write(KillerIsPlayer);
            pkg.Write(HitType ?? "");

            pkg.Write(Blow.Count);
            foreach (var kv in Blow)
            {
                pkg.Write(kv.Key ?? "");
                pkg.Write(kv.Value);
            }
            pkg.Write(BlowTotal);
            pkg.Write(BlowRanged);
            pkg.Write(BlowBackstab);
            pkg.Write(BlowBlockable);

            pkg.Write(MaxHealth);
            pkg.Write(Armor);
            pkg.Write(MyWeapon ?? "");
            pkg.Write(Effects.Count);
            foreach (string e in Effects) pkg.Write(e ?? "");

            pkg.Write(FightSeconds);
            pkg.Write(HitsTaken);
            pkg.Write(BiggestHit);
            pkg.Write(StaminaSpentBlocking);

            pkg.Write(Participants.Count);
            foreach (var p in Participants) p.Pack(pkg);
        }

        internal static DeathReport Unpack(ZPackage pkg)
        {
            var r = new DeathReport
            {
                WhenUtc = pkg.ReadLong(),
                Day = pkg.ReadInt(),
                Biome = pkg.ReadString(),
                AtNight = pkg.ReadBool(),
                Cause = pkg.ReadString(),
                CauseDisplay = pkg.ReadString(),
                KillerWeapon = pkg.ReadString(),
                KillerLevel = pkg.ReadInt(),
                KillerIsPlayer = pkg.ReadBool(),
                HitType = pkg.ReadString(),
            };
            int blow = pkg.ReadInt();
            for (int i = 0; i < blow; i++)
            {
                string key = pkg.ReadString();
                r.Blow.Add(new KeyValuePair<string, float>(key, pkg.ReadSingle()));
            }
            r.BlowTotal = pkg.ReadSingle();
            r.BlowRanged = pkg.ReadBool();
            r.BlowBackstab = pkg.ReadBool();
            r.BlowBlockable = pkg.ReadBool();

            r.MaxHealth = pkg.ReadSingle();
            r.Armor = pkg.ReadSingle();
            r.MyWeapon = pkg.ReadString();
            int effects = pkg.ReadInt();
            for (int i = 0; i < effects; i++) r.Effects.Add(pkg.ReadString());

            r.FightSeconds = pkg.ReadSingle();
            r.HitsTaken = pkg.ReadInt();
            r.BiggestHit = pkg.ReadSingle();
            r.StaminaSpentBlocking = pkg.ReadSingle();

            int participants = pkg.ReadInt();
            for (int i = 0; i < participants; i++) r.Participants.Add(Participant.Unpack(pkg));
            return r;
        }
    }
}
