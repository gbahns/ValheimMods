# Scaling the shared map

What the shared map costs today, what each proposed change is actually worth, and what order to
do them in. Measured on the live server map, not estimated.

**Source of every number below:** `C:\ValheimServer\server\BepInEx\config\TheGreatestMap\DeepNorth_-313114985.pins.bin`,
read on 2026-10-21. 593,624 bytes, 4,644 markers, 659 tombstones, 17 suppressions. Deflated, as it
goes on the wire, that is 227,299 bytes.

Re-measure before acting on any of this. The map grows; it was 543 KB five days ago.

---

## The hard limit this is all about

Steam carries at most **524,288 bytes in one reliable message**. Over that,
`ZSteamSocket.SendMessageToConnection` fails, the send loop *breaks* rather than continuing, and
the queue behind it stalls -- which is what the server stopped hearing from the client, and why it
closed the connection after thirty seconds.

Compression (3.2.0) bought room, not a cure: 227 KB against a 512 KB ceiling is about **six
thousand more markers** at the present density. Everything below either buys more room or removes
the ceiling.

---

## What a marker record is made of

Format 6, measured on the live server map on 2026-10-22: 4,641 markers, 587,444 bytes, 169,594
deflated. Marker records are 412,554 of those bytes -- **88.9 bytes each**.

The deflated column is the one to read. A field compression already squeezes costs almost nothing
to leave alone, however fat it looks on disk, and measuring that is what stopped two rounds of
this work optimizing the wrong things.

| Field | Bytes/rec | Deflated | Shrinks | What it is for |
|---|---:|---:|---:|---|
| `Pos` | 12.0 | **51,213** | 1.1x | Three floats, the world position. Also what makes a marker the same marker on two maps, since the world is identical for everyone, and **part of the key**. Raw float bits are near enough random, which is why this one field is 30% of the compressed map. |
| `Created` | 8.0 | **19,409** | 1.9x | `DateTime.UtcNow.Ticks` when first written. Orders markers for the label-thinning pass so the same set thins the same way everywhere. 100-nanosecond ticks barely compress; seconds would do the job in half the bytes. |
| `Modified` | 8.0 | 4,297 | 8.6x | Ticks of the last change. **Decides merges** -- later change wins -- and is what a delta sync would compare against. |
| `Icon` | 14.7 | 3,286 | 20.7x | How the marker is *drawn*: `item:Dandelion`, `pin:Icon1`. Changes whenever we decide a different picture suits a thing better. 39 distinct values. |
| `Source` | 13.6 | 3,009 | 20.9x | What the marker **is**: the prefab the catalog matched (`SunkenCrypt4`), or `Manual` for one a player placed. The durable half of the 3.0.0 split, and the third **part of the key**. 38 distinct values; still blank on 528 recorded markers. |
| `Kind` | 7.1 | 2,229 | 14.8x | The `Category` it was recorded under (`Herbs`, `Dungeon`). Drives the show/hide switches, focus, ping and the counts. Blank on the 62 placed by hand. 16 distinct values. |
| `Type` | 4.0 | 2,180 | 8.5x | The creating client's local `Minimap.PinType`. **Dead weight:** the receiver throws it away and recomputes it from `Icon` (`ClientPins.cs:305`). It survives only because format 1 had no `Icon`. |
| *(length prefix)* | 4.0 | -- | -- | Added in format 5. Lets a reader step over a record written by a newer version it does not understand, which is what makes a mixed-version group work at all. A varint would make it 1--2 bytes. |
| `Name` | 1.2 | 923 | 6.2x | The label shown on the map. Blank on 4,537 of 4,641 -- almost every recorded marker is unlabeled. |
| `OwnerId` | 8.0 | 456 | 81x | The player id of whoever recorded it. Decides who may edit or erase, who `tgm_dupes` blames, and is the first **part of the key**. Five distinct values, so deflate reduces it to almost nothing. |
| `Author` | 6.3 | 457 | 64x | The player *name*, for display. Five distinct values -- it is `OwnerId` spelled out, carried on every record. |
| `Checked` | 1.0 | 459 | 10.1x | Crossed off: you have taken what was there. 811 are. |
| `Auto` | 1.0 | 33 | 141x | Recorded by the mod rather than placed by hand. Erasing an `Auto` marker also writes a suppression so the recorder does not put it back. 4,579 are. |

`SharedPin` also carries **`LegacyId`**, which costs nothing here because it is never written: it
holds the GUID of a marker read from format 4 or older, only so a merge can ask whether that old
id was erased before the rekey. It dies with the last pre-6 map.

Everything that is not a marker is the other 11%: erasures, and the handful of suppressions. On
2026-10-22 that is 4,823 erasures at 29 bytes and 660 older ones at 41, which is 175 KB -- and
most of it should not be there; see the note on folding below.

---

## What each change is worth

Prototyped against the real file and deflated, so these compare like with like. Compression
already removes repetition, so the raw saving and the real saving are very different numbers --
which is the whole point of measuring instead of reasoning.

| Change | Raw | Deflated | vs. today |
|---|---:|---:|---:|
| today (format 5) | 593,624 | 227,299 | -- |
| drop `Id`, key on a natural key | 440,372 | **112,253** | **49%** |
| + intern `Author`/`Icon`/`Kind`/`Source` | 267,175 | 104,875 | 46% |
| + drop `Type` | 248,599 | 104,294 | 46% |
| + 4-byte timestamps, packed flags, varint framing | 192,871 | 89,910 | 40% |
| *(interning alone, keeping `Id`)* | 420,427 | 218,204 | *96%* |

Two findings worth keeping:

- **Dropping the `Id` is the whole prize.** A GUID is random, so it is the one field deflate
  cannot do anything with: 26% of the raw file and roughly half the compressed one. Removing it
  halves what crosses the wire by itself.
- **Interning the repeated strings is nearly worthless.** It looks like the obvious win -- 38
  distinct icons across 4,644 markers -- and it cuts the raw file by 29%. Deflate was already
  doing it: 4% off the compressed size. Not worth the format churn on its own.

### The natural key is not Owner + Pos

That was the proposal, and on this map it **collides eight times**. Two different things really do
get recorded at one position:

```
Death @ (-885.46, 60.29, -776.93)   item:CryptKey  / SunkenCrypt4
                                    item:TrophySkeleton / Crypt2
Shakes @ (1664.95, 299.63, 994.69)  item:CopperOre / rock4_copper
                                    item:SilverOre / silvervein
```

Eight markers quietly swallowed by their twin is not an acceptable cost. Adding a third component
fixes it -- `Owner + Pos + Source` and `Owner + Pos + Icon` are both unique across all 4,644.
`Source` is the right one: it is what the marker *is*, and unlike `Icon` it does not change when we
repaint a category. `Source` is already interned by then, so the key costs one extra byte.

(`Pos + Icon`, with no owner at all, is also unique here, and would collapse the same discovery
found by two players into one record -- dedupe for free. Tempting, and wrong: it loses the record
of who found it, and two people may legitimately place different markers on one spot.)

---

## To do

Ordered by what each one is actually for. The first removes a failure; the rest buy room.

- [ ] **1. Chunking.** Split a map that exceeds a safe size across several messages and reassemble
      on arrival. **This is the only item that removes the 512 KB ceiling** rather than moving it
      further away; until it exists, every other saving here is a delay on the same failure.
      Needs a sequence number, a reassembly buffer keyed by sender, and a timeout that discards a
      half-arrived map rather than holding it forever.

- [ ] **2. Deltas -- send only what changed.** `Modified` already exists on every record, so a peer
      that says "I have everything up to tick N" can be sent only what is newer. The common case at
      a cartography table is *nothing changed* or *three markers changed*; today both cost the
      whole map. 3.1.3 skips the send when the map is byte-identical, which is the crude version of
      this. Tombstones and suppressions need the same watermark treatment.
      Catch: a peer whose watermark we do not trust (new player, rejoined after a rollback) still
      needs the whole map, so the full path cannot go away.

- [x] **3. Drop `Id`; key on `Owner + Pos + Source`.** Done in 4.0.0, and since confirmed on the
      live server rather than in simulation. The server converted its map, a client synced at a
      cartography table, and the file it wrote back was compared field by field with the copy
      taken beforehand:

      | | before | after |
      |---|---:|---:|
      | format | 5 | 6 |
      | bytes | 593,624 | 454,251 |
      | deflated | 227,299 | **113,120** |
      | markers | 4,644 | 4,644 |
      | erasures | 659 | 659 + 337 |

      No marker lost, none gained, no field changed on any marker except the 337 sources the
      migration filled in, and every one of the 659 old erasures still recorded. The 337 new
      tombstones are the rekeys, one each. 43% of Steam's message budget down to 22%. The table
      sync itself: 246 ms of table work, 111.5 KB sent, the server answering in 1.6 s.

      Two things turned out differently from the plan. Tombstones could not be converted at all:
      a tombstone names a marker that is gone, so there is nothing left to compute its new key
      from. They are carried as they are in a section of their own (`LegacyTombstones`) and
      consulted against the old id that an incoming pre-6 marker still carries -- dropping them
      would have resurrected all 659 erasures, the crop markers among them. And `Source` turned
      out to be part of the key *and* occasionally written, by tgm_identify, so changing it is a
      rekey: `ClientPins.Reidentify` erases the old record and writes a new one, which is
      honestly what it is.

- [ ] **4. Drop `Type` from the wire.** It is recomputed from `Icon` on arrival and read nowhere
      else. Four bytes a record, and one less field to keep honest. Deliberately *not* taken
      along with item 3, to keep that change reviewable; it now needs a format 7 of its own,
      which is a poor trade for 18 KB raw and about 600 bytes compressed. Worth doing only when
      the format is being opened for something else -- item 5, most likely.

- [ ] **5. The small fields, if the format is being changed anyway.** 4-byte timestamps against a
      mod epoch instead of 8-byte .NET ticks, `Checked` and `Auto` packed into one byte, varint
      record framing, and interning the four repeated strings. Together about 6% more off the
      compressed size -- the reason to do them is that they ride along with 3 and 4 for almost
      nothing, not that any of them is worth a release.

- [ ] **6. Tombstones never expire.** 659 today, 27 KB, and the number only goes up for the life of
      the world. A tombstone exists to stop an old copy of the map resurrecting an erased marker,
      so it only needs to outlive the oldest copy anyone might still be carrying. A horizon --
      drop tombstones older than some months -- bounds a cost that is currently unbounded.
      Think hard before picking the number: too short and a player returning from a long absence
      brings their erased markers back for everyone.

- [ ] **7. Check the personal map in the character file.** About 310 KB today and written on the
      same record format, so items 3--5 shrink it too. Worth a measurement at 20,000 markers
      before anyone finds the limit the hard way.

### Decided against

- **Interning alone.** Measured at 4% off the compressed size. Only worth doing as part of item 5.
- **Owner + Pos as the natural key.** Collides eight times on the live map; see above.
- **`Created` as the third part of the key instead of `Source`.** It is provably immutable, where
  `Source` is written once and can be taken back, so it needs no rekey machinery. `Source` was
  chosen anyway and the machinery written, because it buys something `Created` cannot: recording
  the same thing at the same place twice produces the *same key*, so a duplicate cannot be
  created in the first place. Idempotence was worth more than the simpler migration.

## The field set we are heading for

One rule: **store only what cannot be derived.** `Source` says what a thing is, and the icon, the
kind, the pin type and whether it was recorded all follow from that -- so all four are working
copies of an answer the record already gives, and a working copy can disagree with the thing it
copies. Twice now that is exactly what went wrong.

`Source` takes one of four shapes, and between them they leave no gaps:

| shape | means | example |
|---|---|---|
| `<prefab>` | the thing the catalog matched | `SunkenCrypt4` |
| `kind:<Category>` | the kind is the whole answer, nothing further to work out | `kind:Runestone` |
| `Manual` | a player put this here; the stored `Icon` is their choice | `Manual` |
| *blank* | **a defect.** Recorded, not yet identified -- counted, and repaired | |

Blank meaning exactly one thing is the point. While it meant both "nobody has worked this out"
and "there is nothing to work out", every rule that read it was guessing, and the fold rule threw
away 325 markers' identities in an evening before anyone noticed.

### What a record would hold

| field | keep? | why |
|---|---|---|
| `Pos` | **keep** | the key, and the thing a marker is about |
| `OwnerId` | **keep** | the key, and who found it |
| `Source` | **keep** | the key, and the one authoritative statement of what this is |
| `Created` | **keep** | credit for finding it first, and the fold tie-break |
| `Modified` | **keep** | decides merges, carries `Checked`, and is the watermark a delta sync needs |
| `Checked` | **keep** | the one piece of state that genuinely changes in play |
| `Name` | **keep** | a label a player wrote; no longer mutates now portals are live |
| `Icon` | **only when it cannot be derived** | hand-placed markers, and anything whose source the catalog cannot read |
| `Kind` | **only when it cannot be derived** | same rule: usually empty, kept where `Source` is blank or unknown |
| `Type` | **drop** | already recomputed from `Icon` on arrival and read nowhere else |
| `Auto` | **drop** | `Source != "Manual"` says it |
| *(length prefix)* | **keep, shrink** | what lets an older reader step over a field it does not know; a varint makes it 1 byte instead of 4 |

### The rule for a derived field, and why it is not simply "drop it"

> **Write a derived field only when deriving it would not reproduce it.**

At write time, work out what `Kind` would come to from `Source`. If that matches what the record
holds, write an empty string -- one byte. If it does not match, or there is nothing to derive
from, write the real value. The check is made rather than assumed, which matters in two cases
that a flat "drop it" would quietly destroy:

- **A marker with a blank source.** There is nothing to derive from, so dropping its kind loses
  it outright. A player who upgrades straight from 4.1.0, having never run the repair in 4.2.0,
  has hundreds of these -- and a format change that only works if some earlier release happened
  to run is a trap set for whoever upgrades twice in one sitting.
- **A source the catalog cannot read.** A prefab removed from the lists, or a mod uninstalled.
  The stored kind is the last thing anybody knew; derivation would answer nothing.

So the conversion is lossless from any version, and 4.2.0's repair is an accelerant rather than
a prerequisite. On the live map that is 528 markers carrying a kind and 4,113 carrying one empty
byte -- about 500 bytes where the field costs 33,000 now, which keeps nearly all of a saving that
was never the point. The point is that a field which can disagree with `Source` eventually will.

**None of it needs doing yet.** It is a format break, and the rule from the last two rounds
stands: break the format once, for all of this at once, together with the timestamps and the
positions below. Not three migrations for a few percent each.

## What is left to win

**`Pos` and `Created` are 41% of the compressed map between them**, and they are the two fields
compression cannot touch. Everything else together is about 9%. This is the second time measuring
has pointed somewhere other than the list did -- items 4 and 5 below both target the cheap half.

Prototyped on the real file, each change on top of the last:

| | raw | deflated | vs. now |
|---|---:|---:|---:|
| format 6 as shipped | 454,251 | 113,120 | -- |
| columnar (every field its own run) | 431,068 | 109,865 | 97% |
| + string and owner tables | 221,299 | 105,104 | 93% |
| + drop `Type`, packed flags | 202,720 | 101,868 | 90% |
| + times as seconds, delta-coded | 139,596 | 86,258 | 76% |
| + positions to the centimeter | 120,960 | 70,532 | 62% |
| + sorted by place, positions delta-coded | 114,077 | **65,301** | **58%** |

Both caveats check out on the live map. Snapping positions to the centimeter causes **no**
collisions -- the closest two markers anywhere are 23.7 cm apart, a 23x margin -- though it does
change the key, so it needs a format bump and a version gate like 4.0.0 (but no tombstones: every
client converts identically, so it is a reinterpretation, not an edit). Coarser timestamps only
risk a merge tie if two clients change *the same marker* within one tick of each other;
milliseconds cost almost nothing once delta-coded, so use those and the question goes away.

### But this is no longer the bottleneck

42% more off a map that already uses **22% of the message budget** is not worth a third format
migration on its own. Items 1 and 2 are worth more, and item 2 is worth the most of all: with
deltas, the everyday table sync sends nothing at all, which makes the size of a full map
something that matters only when somebody joins. Shrinking a message you have stopped sending is
the wrong order of work.

So: **deltas, then chunking, and the format last or never.** If the format is ever broken again,
break it once and take all of the above together -- not 3% here and 5% there across three
migrations.

## When a key carries meaning, every rule that sorts it changes meaning too

4.0.1. `Duplicates.Fold` kept the **lowest key** of a group, so that every machine folded a pair
the same way round. That was right for three years: a key was a GUID invented on the spot, so
"lowest" was an arbitrary choice made identically everywhere, which is all it had to be.

Format 6 ended a key with what the marker stands for -- and the blank that means *nobody has
worked this out yet* sorts before every real prefab name. The rule did not change. What it sorted
did, and overnight it stopped choosing at random and started always choosing the copy that knew
less. One player who had never run `tgm_identify` was enough: their blank copy of a house stood at
the same point with the same icon as an identified one, the two folded, the answer was thrown
away, and a tombstone was left behind. **325 structures, 31 spawners and a dungeon lost their
identity in a single evening**, and the shared map went from 113 KB to 170 KB on the wire, almost
all of it tombstones from folds that should never have happened.

The fix is that the copy which knows what it is survives, with the key as the tie-break. The
lesson is larger than the fix: a natural key is data, and every comparison, sort and tie-break
anywhere in the codebase silently acquires that data's meaning. Grep for the others before
changing a key again.

## Data fixes run themselves now (4.0.0)

`Migrations.cs`. Anything that brings an old map up to date happens a few seconds after login,
once, with no command and nothing for a player to read -- the same principle as the crop markers
clearing themselves. Two rules for anything added there: it must be safe to run twice, because
there is no stamp saying it has been done, and it must be cheap when there is nothing to do.

Filling in what a marker stands for is the first one, which is what `tgm_identify apply` did by
hand. It cannot settle every blank: `Portal` and `Runestone` have no prefab list, because for
those kinds the kind *is* the whole answer, so on the live map 109 of the 430 blanks stay blank.
Giving those a source of their own -- as hand-placed markers now get `Manual` -- is unfinished
business.

---

## Things to be careful of

- **Never refuse a shape you have written before.** A client that rejects an old message cannot see
  the players still sending it, and the group splits into halves that cannot see each other. Read
  every version; enforce none. The length-prefixed framing from format 5 exists for this.
- **The server must be updated with the clients** for anything that changes what crosses the wire.
  3.2.0 compressed the client's send and the server's reply separately, and a server left behind
  meant the large half was still large.
- **Measure against the compressed size, not the raw one.** Half the intuitions here were wrong in
  that gap.
