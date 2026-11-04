using System;
using System.Collections.Generic;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Adds up who hurt a creature, over the whole fight, and reports it when the creature dies.
    ///
    /// All damage to one creature is processed by one game. <c>Character.Damage</c> only invokes
    /// <c>RPC_Damage</c>, and that returns immediately unless <c>m_nview.IsOwner()</c>, so the
    /// owner sees every blow from every player and nobody else sees more than their own. That makes
    /// the arithmetic easy and leaves exactly one hard problem: the owner can change mid-fight, and
    /// a ten-minute boss fight is where that is most likely.
    ///
    /// So the running total lives on the creature's **ZDO**, which migrates with ownership. That is
    /// the same trick vanilla uses for its own attacker flags, for the same reason. It is kept as
    /// one packed blob under one key rather than a float per player, which costs a read and a write
    /// per flush and buys three things: the participants are named in the record, so a player who
    /// logs out before the kill is still credited; the whole fight can be reset in one write when
    /// it turns out to be two fights; and the ZDO gains one key instead of one per player forever.
    ///
    /// Writes are flushed about once a second rather than per hit, because a boss fight is hundreds
    /// of hits and every ZDO change is replicated to everyone who has the creature loaded.
    ///
    /// On a dedicated server the server owns most of the world, so it does most of this. Without
    /// the mod on the server, a fight is only recorded while a player's own game happens to own the
    /// creature -- which is why <see cref="FightReport.Unaccounted"/> exists and is reported rather
    /// than hidden.
    /// </summary>
    internal static class FightWatch
    {
        /// <summary>One ZDO key, holding the whole record. See the class comment for why.</summary>
        private const string ZdoKey = "dwams_fight";
        private const int BlobVersion = 1;

        /// <summary>How long a creature has to go unharmed before the next blow starts a new fight.</summary>
        private const float LullSeconds = 60f;
        /// <summary>How long after the last blow a surviving creature is forgotten locally.</summary>
        private const float ForgetSeconds = 90f;
        private const float FlushSeconds = 1f;

        private sealed class Fight
        {
            /// <summary>Damage not yet written to the ZDO, by share key.</summary>
            internal readonly Dictionary<string, float> Pending = new Dictionary<string, float>(StringComparer.Ordinal);
            internal float LastTouch;
            internal float NextFlush;

            // Who applied each damage-over-time effect. The ticks themselves carry no attacker at
            // all -- SE_Poison and SE_Burning build a fresh HitData with nothing but a hit type and
            // call ApplyDamage directly, and StatusEffect.SetAttacker is an empty virtual that
            // neither of them overrides -- so the only place to learn this is the blow that applied
            // the effect. Deliberately not stored on the ZDO: it is worth a few ticks landing in
            // the world's bucket after a handover, not a write on every hit.
            internal string PoisonBy;
            internal string FireBy;
            internal string SpiritBy;
        }

        private static readonly Dictionary<ZDOID, Fight> _live = new Dictionary<ZDOID, Fight>();
        private static readonly List<ZDOID> _expired = new List<ZDOID>();
        private static bool _failed;

        // The blow currently being processed, so the three private AddXDamage methods -- which run
        // only when an element really is applied, and do not get told by whom -- can learn who did
        // it. Alive for the length of one RPC_Damage call.
        private static ZDOID _pendingOn = ZDOID.None;
        private static string _pendingBy;

        internal static bool Enabled =>
            !_failed && DwamsConfig.RecordFights != null && DwamsConfig.RecordFights.Value;

        internal static int LiveCount => _live.Count;

        // ── the hooks ───────────────────────────────────────────────────────────────

        /// <summary>A blow is about to be processed on this creature, by this attacker.</summary>
        internal static void BeginHit(Character creature, Character attacker)
        {
            _pendingOn = ZDOID.None;
            _pendingBy = null;
            if (!Enabled || creature == null) return;
            try
            {
                _pendingOn = creature.GetZDOID();
                _pendingBy = ShareKey(attacker);
            }
            catch { Fail(); }
        }

        internal static void EndHit()
        {
            _pendingOn = ZDOID.None;
            _pendingBy = null;
        }

        /// <summary>
        /// An element was just handed to a status effect, which is the only moment it is certain the
        /// effect is really being applied rather than the blow having been dodged or discarded.
        /// </summary>
        internal static void NoteElement(Character creature, char element)
        {
            if (!Enabled || creature == null || string.IsNullOrEmpty(_pendingBy)) return;
            try
            {
                var id = creature.GetZDOID();
                if (id != _pendingOn) return;
                var fight = Open(creature, id);
                if (fight == null) return;
                switch (element)
                {
                    case 'p': fight.PoisonBy = _pendingBy; break;
                    case 'f': fight.FireBy = _pendingBy; break;
                    case 's': fight.SpiritBy = _pendingBy; break;
                }
            }
            catch { Fail(); }
        }

        /// <summary>
        /// Health a creature actually lost, and who to credit. <paramref name="lost"/> is a delta
        /// measured across ApplyDamage, not anything read off the hit.
        /// </summary>
        internal static void Record(Character creature, HitData hit, float lost)
        {
            if (!Enabled || creature == null || lost <= 0f) return;
            try
            {
                var id = creature.GetZDOID();
                var fight = Open(creature, id);
                if (fight == null) return;
                fight.LastTouch = Time.unscaledTime;

                Character attacker = null;
                if (hit != null && hit.HaveAttacker())
                {
                    try { attacker = hit.GetAttacker(); }
                    catch { /* fall through to the DOT and world cases */ }
                }
                if (attacker != null)
                {
                    Add(fight, ShareKey(attacker), lost);
                    return;
                }

                // No attacker on the hit: either a damage-over-time tick, which carries none by
                // design, or something the world did to it.
                var type = hit != null ? hit.m_hitType : HitData.HitType.Undefined;
                if (type == HitData.HitType.Poisoned)
                {
                    Add(fight, fight.PoisonBy ?? FightReport.World, lost);
                    return;
                }
                if (type == HitData.HitType.Burning)
                {
                    // Burning carries fire and spirit together and the two can come from different
                    // people, so a tick with both is split the way the tick itself is split.
                    float fire = hit != null ? Mathf.Max(0f, hit.m_damage.m_fire) : 0f;
                    float spirit = hit != null ? Mathf.Max(0f, hit.m_damage.m_spirit) : 0f;
                    float both = fire + spirit;
                    if (both <= 0f) { Add(fight, fight.FireBy ?? FightReport.World, lost); return; }
                    if (fire > 0f) Add(fight, fight.FireBy ?? FightReport.World, lost * (fire / both));
                    if (spirit > 0f) Add(fight, fight.SpiritBy ?? FightReport.World, lost * (spirit / both));
                    return;
                }
                Add(fight, FightReport.World, lost);
            }
            catch { Fail(); }
        }

        /// <summary>The creature is dying. Runs on the owner, which is where OnDeath credits kills too.</summary>
        internal static void Seal(Character creature)
        {
            if (!Enabled || creature == null) return;
            try
            {
                var id = creature.GetZDOID();
                var zdo = Zdo(creature);
                if (zdo == null) return;

                // Flush whatever has not been written yet, then read the record back: the blob is
                // the authority, because part of it may have been written by another game.
                if (_live.TryGetValue(id, out var fight))
                {
                    Flush(zdo, fight);
                    _live.Remove(id);
                }
                var blob = Read(zdo);
                if (blob == null || blob.Total <= 0f) return;

                var report = new FightReport
                {
                    CreatureKey = (creature.m_name ?? "").Trim(),
                    Boss = creature.IsBoss(),
                    WhenUtc = DateTime.UtcNow.Ticks,
                    TotalDamage = blob.Total,
                };
                try { report.Level = creature.GetLevel(); } catch { report.Level = 1; }
                try { report.MaxHealth = creature.GetMaxHealth(); } catch { /* 0 is honest */ }
                report.Seconds = Mathf.Max(0f, (float)new TimeSpan(Math.Max(0L, blob.Last - blob.Start)).TotalSeconds);
                foreach (var kv in blob.Shares)
                    if (kv.Value > 0f) report.Shares.Add(new FightShare { Key = kv.Key, Damage = kv.Value });
                report.Sort();

                if (!Qualifies(report)) return;
                Deliver(report);
            }
            catch { Fail(); }
        }

        /// <summary>
        /// Whether a fight is worth anyone's attention. Bosses always; otherwise it has to have both
        /// lasted a while and been something substantial, or every greydwarf would file a report.
        /// </summary>
        private static bool Qualifies(FightReport report)
        {
            if (report.Boss) return true;
            float seconds = DwamsConfig.FightSeconds != null ? DwamsConfig.FightSeconds.Value : 15f;
            float health = DwamsConfig.FightHealth != null ? DwamsConfig.FightHealth.Value : 200f;
            return report.Seconds >= seconds && report.MaxHealth >= health;
        }

        /// <summary>
        /// Hands the report to everyone named in it. Only players who actually did damage are told,
        /// so a creature two trolls killed between them does not interrupt anybody.
        /// </summary>
        private static void Deliver(FightReport report)
        {
            var znet = ZNet.instance;
            if (znet == null) return;
            long self = ZNet.GetUID();
            foreach (var player in znet.GetPlayerList())
            {
                if (report.DamageFor(player.m_name) <= 0f) continue;
                long peer = player.m_characterID.UserID;
                if (peer == self) FightLog.Record(report, announce: true);
                else StatsNetwork.SendFight(peer, report);
            }
        }

        // ── bookkeeping ─────────────────────────────────────────────────────────────

        internal static void Update()
        {
            if (!Enabled || _live.Count == 0) return;
            try
            {
                float now = Time.unscaledTime;
                _expired.Clear();
                foreach (var kv in _live)
                {
                    var fight = kv.Value;
                    if (now >= fight.NextFlush && fight.Pending.Count > 0)
                    {
                        var zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(kv.Key) : null;
                        if (zdo != null) Flush(zdo, fight);
                        fight.NextFlush = now + FlushSeconds;
                    }
                    if (now - fight.LastTouch > ForgetSeconds) _expired.Add(kv.Key);
                }
                // A creature that got away keeps its record on its own ZDO; only the local
                // scratchpad is dropped. The next blow lands in the same record, or starts a new
                // one if enough time has passed, which is what the lull check is for.
                foreach (var id in _expired) _live.Remove(id);
                _expired.Clear();
            }
            catch { Fail(); }
        }

        /// <summary>Forgets everything local. The ZDO records are the world's and are left alone.</summary>
        internal static void Clear()
        {
            _live.Clear();
            _expired.Clear();
            _pendingOn = ZDOID.None;
            _pendingBy = null;
        }

        /// <summary>
        /// Starts following a creature, or returns the fight already being followed.
        ///
        /// Small creatures are turned away here, and that is about the save file rather than about
        /// tidiness. The record lives on the creature's ZDO, ZDOs are persisted with the world, and
        /// a creature that survives keeps whatever was written on it. Following every greydwarf
        /// anyone ever shot would add a key to thousands of ZDOs forever. Health is the half of the
        /// qualifying rule that is knowable in advance -- it is a property of the species -- so
        /// anything too small to ever be worth a report is never written to at all.
        /// </summary>
        private static Fight Open(Character creature, ZDOID id)
        {
            if (id == ZDOID.None) return null;
            if (_live.TryGetValue(id, out var fight)) return fight;
            if (Zdo(creature) == null) return null;
            if (!WorthFollowing(creature)) return null;
            fight = new Fight { LastTouch = Time.unscaledTime, NextFlush = Time.unscaledTime + FlushSeconds };
            _live[id] = fight;
            return fight;
        }

        private static bool WorthFollowing(Character creature)
        {
            try
            {
                if (creature.IsBoss()) return true;
                float floor = DwamsConfig.FightHealth != null ? DwamsConfig.FightHealth.Value : 200f;
                return creature.GetMaxHealth() >= floor;
            }
            catch { return false; }
        }

        private static void Add(Fight fight, string key, float amount)
        {
            if (string.IsNullOrEmpty(key) || amount <= 0f) return;
            fight.Pending.TryGetValue(key, out float had);
            fight.Pending[key] = had + amount;
        }

        /// <summary>A player by name, a pet or a creature by bucket. Mirrors vanilla: it keys its
        /// own attacker flags by character name too.</summary>
        private static string ShareKey(Character attacker)
        {
            if (attacker == null) return null;
            if (attacker is Player player)
            {
                string name = player.GetPlayerName();
                // A name starting with the bucket marker would land in a bucket, so it loses it.
                if (!string.IsNullOrEmpty(name)) return name[0] == '#' ? name.Substring(1) : name;
                return null;
            }
            bool tame = false;
            try { tame = attacker.IsTamed(); } catch { /* treat as wild */ }
            return tame ? FightReport.Tames : FightReport.Creatures;
        }

        private static ZDO Zdo(Character creature)
        {
            var nview = creature != null ? creature.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return null;
            return nview.GetZDO();
        }

        private static void Fail()
        {
            if (_failed) return;
            _failed = true;
            _live.Clear();
            DudeWhatAreMyStatsMod.Log.LogWarning(
                "[DudeWhatAreMyStats] Could not follow a fight; fight reports are off for the rest of this session.");
        }

        // ── the record on the ZDO ───────────────────────────────────────────────────

        private sealed class Blob
        {
            internal long Start;
            internal long Last;
            internal float Total;
            internal readonly Dictionary<string, float> Shares = new Dictionary<string, float>(StringComparer.Ordinal);
        }

        private static Blob Read(ZDO zdo)
        {
            try
            {
                var bytes = zdo.GetByteArray(ZdoKey);
                if (bytes == null || bytes.Length == 0) return null;
                var pkg = new ZPackage(bytes);
                int version = pkg.ReadInt();
                if (version < 1) return null;
                var blob = new Blob
                {
                    Start = pkg.ReadLong(),
                    Last = pkg.ReadLong(),
                    Total = pkg.ReadSingle(),
                };
                int count = pkg.ReadInt();
                for (int i = 0; i < count; i++)
                {
                    string key = pkg.ReadString();
                    float value = pkg.ReadSingle();
                    if (!string.IsNullOrEmpty(key)) blob.Shares[key] = value;
                }
                return blob;
            }
            catch { return null; }
        }

        /// <summary>
        /// Merges the pending damage into the creature's record and writes it back. Read, merge,
        /// write is safe without any locking because only the owner ever writes, and ownership is
        /// exclusive.
        /// </summary>
        private static void Flush(ZDO zdo, Fight fight)
        {
            if (zdo == null || fight.Pending.Count == 0) return;
            long now = DateTime.UtcNow.Ticks;
            var blob = Read(zdo);
            // Two separate fights with the same troll are two reports, not one that claims to have
            // lasted all afternoon.
            if (blob == null || new TimeSpan(Math.Max(0L, now - blob.Last)).TotalSeconds > LullSeconds)
                blob = new Blob { Start = now };

            foreach (var kv in fight.Pending)
            {
                blob.Shares.TryGetValue(kv.Key, out float had);
                blob.Shares[kv.Key] = had + kv.Value;
                blob.Total += kv.Value;
            }
            fight.Pending.Clear();
            blob.Last = now;

            var pkg = new ZPackage();
            pkg.Write(BlobVersion);
            pkg.Write(blob.Start);
            pkg.Write(blob.Last);
            pkg.Write(blob.Total);
            pkg.Write(blob.Shares.Count);
            foreach (var kv in blob.Shares)
            {
                pkg.Write(kv.Key);
                pkg.Write(kv.Value);
            }
            zdo.Set(ZdoKey, pkg.GetArray());
        }
    }
}
