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

4,644 markers, 565,975 bytes of records -- **121.9 bytes each**, 95.3% of the file.

| Field | Bytes/rec | % of file | What it is for |
|---|---:|---:|---|
| `Id` | 33.0 | 25.8% | A GUID, made when the marker is created. The only thing identifying a marker across machines: merges, tombstones and edits all key on it. Nothing a player ever sees. |
| `Icon` | 14.7 | 11.5% | How the marker is *drawn* -- `item:Dandelion`, `pin:Boss`. Changed whenever we decide a different picture suits a thing better. 38 distinct values on this map. |
| `Source` | 13.6 | 10.6% | What the marker **is**: the prefab the catalog matched when it was recorded (`SunkenCrypt4`). The durable half of the 3.0.0 split -- `Icon` is the picture, this is the thing. Empty for the 501 markers placed by hand. 76 distinct values. |
| `Pos` | 12.0 | 9.4% | Three floats. World position. Also the thing that makes a marker the same marker on two maps, since the world is identical for everyone. |
| `OwnerId` | 8.0 | 6.3% | The player id of whoever recorded it. Decides who may edit or erase it, and who a `tgm_dupes` listing blames. |
| `Created` | 8.0 | 6.3% | `DateTime.UtcNow.Ticks` when it was first written. Orders markers for the label-thinning pass, so the same set of markers thins the same way on every machine. |
| `Modified` | 8.0 | 6.3% | Ticks of the last change. **Decides merges** -- later change wins -- and is what a delta sync would compare against. |
| `Kind` | 7.1 | 5.6% | The `Category` it was recorded under (`Herbs`, `Dungeon`). Drives the show/hide switches, focus, ping and the counts. Empty for the 71 placed by hand. 15 distinct values. |
| `Author` | 6.3 | 4.9% | The player *name*, for display. Five distinct values on a map of 4,644 markers -- it is `OwnerId` spelled out, carried per record. |
| *(record length prefix)* | 4.0 | 3.1% | Added in format 5. Lets a reader step over a record written by a newer version it does not understand, which is what makes a mixed-version group work at all. Worth keeping; a varint would make it 1--2 bytes. |
| `Type` | 4.0 | 3.1% | The creating client's local `Minimap.PinType`. **Dead weight on the wire:** the receiver throws it away and recomputes it from `Icon` (`ClientPins.cs:305`). It survives only because format 1 had no `Icon`. |
| `Name` | 1.2 | 0.9% | The label shown on the map. Empty on 4,548 of 4,644 -- almost every recorded marker is unlabeled, and the ones that have a name are dungeons. |
| `Checked` | 1.0 | 0.8% | Crossed off: you have taken what was there. 830 are. |
| `Auto` | 1.0 | 0.8% | Recorded by the mod rather than placed by hand. Erasing an `Auto` marker also writes a suppression so the recorder does not put it back. 4,573 are. |

Tombstones are 27,019 bytes (659 of them, ~41 bytes each: a GUID and a timestamp). Suppressions
are 614 bytes and are not worth a thought.

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

- [x] **3. Drop `Id`; key on `Owner + Pos + Source`.** Done in 4.0.0. Measured by converting the
      live map and reading it back: 4,644 markers, **no two on one key**, every field identical
      after a round trip, 227,299 -> 112,076 bytes deflated (**49%**), 43% of Steam's message
      budget down to 21%.

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
