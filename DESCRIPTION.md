# Quartermaster

**Run your whole base from one desk.**

Quartermaster adds a single craftable block — the **Quartermaster's Desk** — that acts as a remote terminal for every container around it. Place it anywhere, interact, and a ledger opens showing everything stored nearby: chests, trunks, barrels, vessels, crates, tool racks, display cases, and shelves, all combined into one searchable list. No more running down rows of chests trying to remember where you put the copper nuggets.

---

## What it does

- **One ledger for everything.** All nearby storage is scanned and aggregated into a single grid with live item counts.
- **Search & filter.** Type to filter by name, or toggle category tabs — Food, Tools, Fuel, Wood, Wearables, Ores & Metals, Building.
- **Withdraw straight to your inventory.**
  - Left-click — one stack
  - Right-click — a single item
  - Shift+click — all of that item, gathered from every container at once
- **Deposit just as fast.** Drop a held item on the Deposit cell (right-click stores one), or hit **Deposit All** to empty your backpack bags into storage. Your worn bags and hotbar are never touched.
- **Locate anything.** Middle-click an item to highlight every container holding it — blue block markers, floating labels you can read through walls, and temporary map waypoints.
- **Place it anywhere.** No foundation requirement.

## Built to be safe

Withdrawals and deposits are written to be **loss-free**: items only leave a place once the destination actually accepts them, and anything that won't fit stays exactly where it was (with a chat note telling you why). Active work-stations — firepits, ovens, bloomeries, forges, querns, anvils — are deliberately **never** indexed, so you can't accidentally pull an item out of something mid-process.

Want a browse-only terminal with no moving at all? Set **`LocateOnly: true`** in the config for a server-enforced read-only station.

> **Back up your world** before relying on any storage-moving feature. Good practice for any mod that writes to containers.

## Crafting

Quartermaster's Desk (crafting grid):

```
chisel   parchment   charcoal
nails    planks      ink & quill
         planks
```

The chisel is used as a tool (not consumed); the nails and ink & quill are consumed.

## Configuration

`ModConfig/QuartermasterConfig.json`:

- **`ChunkRadius`** (default `2`) — horizontal scan radius in chunks (1 chunk = 32 blocks).
- **`VerticalRange`** (default `5`) — vertical range in blocks above/below the player.
- **`LocateOnly`** (default `false`) — read-only mode; disables withdraw and deposit, server-enforced.

## Looking for something simpler?

If all you want is a safe, **read-only catalog** — see what you have and where, with no ability to move anything — try the companion mod **Bookkeeper**. Quartermaster is the full-featured, write-capable version of the same idea. The two can be installed side by side.
