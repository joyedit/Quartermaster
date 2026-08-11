# Changelog

All notable changes to Quartermaster are documented here. Newest first.

## v1.0.21
### Fixed
- **Tall builds no longer lose their storage from the upper floors.** `VerticalRange` selects whole 32-block chunk layers rather than individual blocks, so the old default of `5` only reached past a chunk seam when you happened to be standing within 5 blocks of it. In a tall house that meant the ledger could go completely empty — not filtered, but finding zero containers — simply from walking upstairs: at Y=201 the desk scanned only blocks 192–223, while the trunks sat in 160–191. The default is now `32`, the smallest value that always reaches one full layer either way, giving a consistent ~96-block vertical reach regardless of where in a layer you stand.
- Widened the horizontal default from `ChunkRadius` `2` to `3` (a 7×7 box, ~224 blocks across). The scan box is centered on the player's chunk and is likewise chunk-quantized, so containers near the fringe of a large base would pop in and out of the ledger as you crossed a chunk seam.

Existing worlds keep the values already written to `ModConfig/QuartermasterConfig.json` — edit `VerticalRange` to `32` and `ChunkRadius` to `3` there, or delete the file to regenerate it with the new defaults.

## v1.0.20
### Fixed
- The desk no longer takes plants out of planters and flowerpots/vases. Both use the game's `PlantContainer` block entity, which stores the planted flower in a real 1-slot inventory — so planted flowers showed up in the ledger as stock, and a withdraw (especially "withdraw all") swept them out of every planter and vase in range. Planters and flowerpots are now excluded from scanning entirely, like barrels and buckets: their contents no longer appear in the ledger and can't be located or withdrawn. Deposits were never affected (they only ever target chests and trunks).

## v1.0.19
### Added
- **"Empty slots" counter.** The ledger now shows how much free storage is left in range, as `Empty slots: <free> / <total>` in the bottom-left of the window. It counts only deposit-eligible storage (chests and trunks — the containers a deposit could actually fill), honoring the same exclusions as the rest of the desk (tagged containers, claims, barrels, buckets, work stations). The count refreshes automatically every couple of seconds while the ledger is open, so placing a new chest in range — or watching storage fill up — updates the number live without reopening, and without disturbing the search bar or your place in the list.

## 1.0.18 — 2026-07-23

- Compatibility release for Vintage Story 1.22.5. Rebuilt against the 1.22.5
  assemblies; no code changes.

## v1.0.17
### Fixed
- Shift+Left-click deposit from the hotbar no longer picks the item up on the first click after opening the ledger. Root cause: the search bar silently regained focus when the item list arrived from the server, and a focused text box swallows the Shift key-press before the game records it — so the whole engine (our deposit hook *and* the vanilla slot grid) saw a plain unshifted click, which picks the stack up. The deposit hook now reads the raw keyboard state (which a focused text box can't hide), and the dialog no longer hands the search bar focus when it recomposes on server replies, page turns or category toggles — so it also stops stealing your movement keys mid-session. Clicking into the search bar and typing works exactly as before.
- Hardened the hover-slot refresh from v1.0.16: the hotbar keeps its "last hovered slot" memory across ledger sessions (dialog grids are rebuilt fresh each open), so reopening the ledger with the cursor parked on the same slot could leave the hover cache stale. Each shift-click now clears every grid's hover memory before replaying the cursor position.

## v1.0.16
### Fixed
- The first Shift+Left-click after opening the ledger no longer picks the item up instead of depositing it. The game's "which slot is hovered" cache is only refreshed by mouse-move events and could still be empty on the first click after the dialog opened; the click now refreshes the hover state itself before deciding, so shift-click deposits work from the very first click.

## v1.0.15
### Added
- **Shift-click deposit.** While the ledger is open, Shift+Left-click any item in your hotbar or bags to send it straight into storage — no more dragging stacks onto the Deposit cell one by one. Works on any slot in your hotbar or backpack bags (equipped bags themselves are never deposited), uses the same loss-free storage routine as the Deposit cell, and tells you if storage is full. While the ledger is open this replaces the vanilla shift-move between bags and hotbar; on read-only (`LocateOnly`) stations the vanilla behavior is untouched. Shift+click on the ledger grid still means "withdraw all".

## v1.0.14
### Changed
- Placed buckets are no longer scanned, empty or full. Like barrels, they hold liquids, so their contents no longer appear in the ledger and can't be located or withdrawn.

## v1.0.13
### Fixed
- The held-tag "Excluded" overlay no longer reveals excluded containers inside land claims you can't use. Previously, holding a Quartermaster's Tag near someone else's claim showed through-wall labels for their tagged containers — leaking the exact position of storage they had deliberately hidden. The overlay now applies the same claim check as the ledger (`HonorClaims`, default `true`).
- Exclusions no longer carry over to a replacement container. An exclusion is keyed to a position, and cleanup used to be lazy — so breaking a tagged chest and placing a new container in the same spot could silently inherit the exclusion. The exclusion is now removed the moment the block is broken (or when something new is placed over an excluded spot, e.g. after an explosion), so a new container always starts visible to the desk.

## v1.0.12
### Added
- **Quartermaster's Tag** — a craftable, reusable tool for hiding individual containers from the desk. Hold the tag and sneak + right-click any storage — chests, vessels, crates, shelves, display cases, tool racks, even stacks of goods on the floor — to exclude it: it vanishes from the ledger completely (no browse, count, locate, withdraw, or deposit). Sneak + right-click again to bring it back. The tag takes priority over the block's own sneak interaction, so racks and floor stacks can be tagged without taking a tool or adding to the pile; a plain right-click keeps its normal behavior, so containers still open while the tag is held. Nearby excluded containers show a floating "Excluded" label visible through walls while the tag is in hand. Exclusions apply to the container itself (shared by all players), are stored in the world save, honor land claims, and clean themselves up when a tagged container is removed. Crafted from an ink & quill over parchment over flax twine (ingredients consumed; the tag itself is reusable forever).

## v1.0.11
### Fixed
- Clutter bookshelves (and other attribute-driven decor) no longer show as blank white "?" cubes in the ledger. These blocks store their appearance in itemstack attributes beyond `type`/`material` (e.g. a bookshelf's `variant`); the ledger now preserves a representative stack's full attribute tree, so the correct mesh, name, and material render. The `variant` attribute is also part of the item key now, so distinct variants list separately and withdraw the exact kind clicked.

## v1.0.10
### Fixed
- Flower pots no longer appear under the **Plants** tab. The empty decorative pot was matching the `flower` keyword; it's now excluded.

## v1.0.9
### Added
- Three new category filters: **Plants** (flowers, grass, ferns, mushrooms, saplings, seeds, cuttings, reeds), **Decor** (paintings/pictures, tapestries, decorative clutter), and **Powders** (flour and crushed/pulverized substances).

## v1.0.8
### Changed
- Barrels are no longer scanned. Because barrels hold liquids and sealed curing/fermenting recipes, remotely pulling from them is destructive — so their contents no longer appear in the ledger and can't be located or withdrawn. Deposits were already never routed to barrels.

## v1.0.7
### Fixed
- Stone coffins are no longer scanned. Iron plates and charcoal packed into a stone coffin during cementation (steel-making) were treated as ordinary storage — they could appear in the ledger and even be withdrawn mid-process, ruining the burn. The coffin is now excluded like other processing devices (firepit, bloomery, forge, oven, etc.).

## v1.0.6
### Added
- **Land-claim support.** The desk now honors land claims: containers inside a claim you don't have permission to use are hidden and can't be accessed (browse/withdraw/deposit/locate). Owners, granted players/groups, and unclaimed land are unaffected. Toggle with the `HonorClaims` config option (default `true`).

## v1.0.5
### Changed
- Updated the crafting recipe. The Quartermaster's Desk now also requires **metal nails & strips** and an **ink and quill**, in addition to the chisel, parchment, charcoal, and planks.

## v1.0.4
### Fixed
- Attribute-variant blocks no longer collapse in the ledger. Decorative chests (owl/golden/aged), clutter (e.g. "Aged book lectern"), and other blocks that store their specific kind in itemstack attributes were merging into a single unnamed entry. Each variant now shows with the correct name and icon, is searchable, and withdraws the exact kind you click.

## v1.0.3
### Changed
- Trimmed the `LocateOnly` note from the in-launcher mod description. The option and its behavior are unchanged.

## v1.0.2
### Added
- Custom mod icon.

## v1.0.1
### Changed
- Reskinned the Quartermaster's Desk to the vanilla "drafting table" model (a clerk's desk with schematics, ruler, inkwell, and quill).

## v1.0.0
### Added
- Initial release. A remote inventory-management station: place a Quartermaster's Desk, then browse, search, filter, withdraw, deposit, and locate items across nearby containers from one spot.
- Withdraw (one stack / one item / all) and deposit (deposit cell + Deposit All), built to be loss-free.
- Locate via middle-click: block highlights, through-wall floating labels, and temporary map waypoints.
- `LocateOnly` config option for a server-enforced read-only station.
- Designed to coexist with the read-only [Bookkeeper](https://github.com/joyedit/Bookkeeper) mod — separate block, hotkey, network channel, and config.
