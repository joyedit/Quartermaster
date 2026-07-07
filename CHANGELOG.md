# Changelog

All notable changes to Quartermaster are documented here. Newest first.

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
