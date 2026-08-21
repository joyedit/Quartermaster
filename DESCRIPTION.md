# Quartermaster

**Run your whole base from one desk.**

Quartermaster adds a single craftable block — the **Quartermaster's Desk** — that acts as a remote terminal for every container around it. Place it anywhere, interact, and a ledger opens showing everything stored nearby: chests, trunks, vessels, crates, tool racks, display cases, shelves, even loose stacks of goods on the floor — all combined into one searchable list. No more running down rows of chests trying to remember where you put the copper nuggets.

---

## What it does

- **One ledger for everything.** All nearby storage is scanned and aggregated into a single grid with live item counts.
- **Search & filter.** Type to filter by name, or toggle category tabs — Food, Tools, Fuel, Wood, Wearables, Ores & Metals, Building, Plants, Decor, Powders.
- **Withdraw straight to your inventory.**
  - Left-click — one stack
  - Right-click — a single item
  - Shift+click — all of that item, gathered from every container at once
- **Deposit just as fast.** Shift+click any item in your hotbar or bags to send it straight into storage, drop a held item on the Deposit cell (right-click stores one), or hit **Deposit All** to empty your backpack bags into storage. Your worn bags are never touched.
- **Locate anything.** Middle-click an item to highlight every container holding it — blue block markers, floating labels you can read through walls, and temporary map waypoints.
- **Respects land claims.** On a claimed server it only reaches containers you're actually allowed to use — others' claimed chests stay private.
- **Keep private storage private.** Craft a **Quartermaster's Tag** and sneak + right-click any storage — chests, vessels, crates, shelves, display cases, tool racks, even floor stacks — to exclude it from the desk entirely: no listing, locating, withdrawing, or depositing. Sneak + right-click again to bring it back. While holding the tag, excluded containers show a floating "Excluded" label through walls so you can always see what's hidden (claims are honored — you never see exclusions inside land you can't use). Exclusions are shared by all players, saved with the world, and clear automatically when the container is broken — a new container placed in the same spot starts visible.
- **Place it anywhere.** No foundation requirement.

## Built to be safe

Withdrawals and deposits are written to be **loss-free**: items only leave a place once the destination actually accepts them, and anything that won't fit stays exactly where it was (with a chat note telling you why). Active work-stations — firepits, ovens, bloomeries, forges, querns, anvils, stone coffins — are deliberately **never** indexed, so you can't accidentally pull an item out of something mid-process. Barrels are left alone too: they hold liquids and sealed curing/fermenting recipes, so remote pulls would be destructive.

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

Quartermaster's Tag (vertical, any grid column):

```
ink & quill
parchment
flax twine
```

The ingredients are consumed, but the tag itself is reusable and never wears out.

## Configuration

`ModConfig/QuartermasterConfig.json`:

- **`ChunkRadius`** (default `3`) — horizontal scan radius in chunks (1 chunk = 32 blocks), a 7×7 box ~224 blocks across.
- **`VerticalRange`** (default `32`) — vertical range in blocks above/below the player. Chunk-quantized, so values below 32 make coverage depend on where in the 32-block layer you stand.
- **`ExcludedBlockCodes`** (defaults to Food Shelves' barrel and tun racks) — wildcard block codes the desk never scans, so you can exclude modded storage it handles badly without waiting on a fix.
- **`TagOptInMode`** (default `false`) — flips the Quartermaster's Tag from *hide this container* (opt-out) to *show only this container* (opt-in). In opt-in mode the desk lists nothing until you tag chests in. Each mode remembers its own tags, so you can switch back without losing either list.
- **`LocateOnly`** (default `false`) — read-only mode; disables withdraw and deposit, server-enforced.
- **`HonorClaims`** (default `true`) — respects land claims; containers you don't have permission to use are hidden and inaccessible.

## Looking for something simpler?

If all you want is a safe, **read-only catalog** — see what you have and where, with no ability to move anything — try the companion mod **Bookkeeper**. Quartermaster is the full-featured, write-capable version of the same idea. The two can be installed side by side.
