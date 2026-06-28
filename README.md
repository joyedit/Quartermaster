# Quartermaster

A remote **inventory-management station** for [Vintage Story](https://www.vintagestory.at/) (1.22, .NET 10).

Place a **Quartermaster's Desk** anywhere and interact with it to open a ledger that scans
every container within range. From one spot you can browse, search, filter, **withdraw**,
**deposit**, and **locate** items across all your chests, trunks, barrels, tool racks,
display cases, and shelves.

> Quartermaster is the write-capable companion to **[Bookkeeper](https://github.com/joyedit/Bookkeeper)**.
> If you only want a safe, read-only catalog, use Bookkeeper. If you want full remote
> management, use Quartermaster — optionally in read-only mode via `LocateOnly` (below).

## ⚠️ Back up your world

Quartermaster moves items between your inventory and your containers. The moves are
written to be loss-free (items leave a place only once the destination accepts them, and
anything that doesn't fit stays where it was), but **any mod that writes to storage carries
risk**. Keep server world backups enabled before relying on it. If you want zero write
risk, run with `LocateOnly: true` or use Bookkeeper instead.

## Features

- **Aggregated ledger** of all storage within range, with item counts.
- **Search** by name and **filter** by category (Food, Tools, Fuel, Wood, Wearables,
  Ores & Metals, Building).
- **Withdraw** straight into your inventory — left-click a stack, right-click one item,
  shift+click all of that item from every container at once.
- **Deposit** — drop a held item on the Deposit cell (right-click stores one), or
  **Deposit All** to empty your backpack bags into storage (equipped bags and hotbar are
  left untouched). Deposits go to chests/trunks only; if storage is full, nothing is lost.
- **Locate** — middle-click an item to highlight its containers with blue markers,
  through-wall floating labels, and temporary map waypoints.
- **Read-only mode** via the `LocateOnly` config flag (server-enforced).
- Works **anywhere** — no foundation requirement.

## Usage

- **Open:** look at a Quartermaster's Desk and press `J` (rebindable), or right-click it.
- **Withdraw:** left-click = one stack · right-click = one item · shift+click = all.
- **Deposit:** hold an item and click the Deposit cell, or use **Deposit All**.
- **Locate:** middle-click an item.

## Crafting

Quartermaster's Desk (3×3 grid):

```
chisel  paper   charcoal
nails   planks  ink & quill
        planks
```

- `chisel` — any chisel (used as a tool; not consumed)
- `paper` — 1× parchment paper
- `charcoal` — 1× charcoal
- `nails` — 1× metal nails & strips (any metal)
- `ink & quill` — 1× ink and quill
- `planks` — 2× planks (any wood)

## Configuration

Server config at `ModConfig/QuartermasterConfig.json`:

| Setting | Default | Description |
|---|---|---|
| `ChunkRadius` | `2` | Horizontal scan radius in **chunks** (1 chunk = 32 blocks). |
| `VerticalRange` | `5` | Vertical range in blocks above/below the player. |
| `LocateOnly` | `false` | When `true`, the desk is **read-only** — browse/search/filter/locate work, but withdraw and deposit are disabled. Enforced server-side, so it applies to everyone on the world. |

The scan is centered on the player and chunk-granular; containers in chunks that aren't
currently loaded are not included.

## Building & deploying

Requires the Vintage Story DLLs. Set `VINTAGE_STORY_PATH` in `Quartermaster.csproj` to your
install, then:

```bash
./deploy.sh
```

This builds the mod (net10.0) and packages `QuartermasterMod.zip` into your
`VintagestoryData/Mods` folder.
