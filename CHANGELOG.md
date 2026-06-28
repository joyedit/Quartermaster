# Changelog

All notable changes to Quartermaster are documented here. Newest first.

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
