namespace Quartermaster
{
    public class QuartermasterConfig
    {
        // Vertical range in blocks above and below the player position. Note this
        // selects whole 32-block chunk layers, not individual blocks: a value below 32
        // leaves coverage dependent on where in the chunk you happen to stand, so a
        // container one storey down can drop out of the ledger as you climb stairs.
        // 32 is the smallest value that always reaches one full layer either way.
        public int VerticalRange { get; set; } = 32;

        // Horizontal scan radius in chunks (1 chunk = 32 blocks). Like VerticalRange
        // this is chunk-quantized and centered on the player's chunk, so containers
        // near the edge pop in and out as you cross a seam; 3 keeps a normal base
        // comfortably inside the box.
        public int ChunkRadius { get; set; } = 3;

        // When true, the desk is read-only: browse, search, filter, and locate still work
        // but withdraw and deposit are disabled. Enforced server-side, so a modified client
        // still cannot move items. The client UI hides the deposit controls.
        public bool LocateOnly { get; set; } = false;

        // Flips what the Quartermaster's Tag means, i.e. how the ledger is curated.
        //
        // false (default, opt-out): the desk scans every container in range, and a tag
        // HIDES the ones you tag. Good when most of your storage should be on the ledger.
        //
        // true (opt-in): the desk scans nothing by default, and a tag ADDS a container to
        // the ledger. Good when you want the desk pointed at a few specific chests.
        //
        // Each mode keeps its own tag list in the world save, so switching back and forth
        // is non-destructive: flipping to opt-in leaves your exclusions dormant (the ledger
        // starts empty until you tag something in), and flipping back restores them intact.
        public bool TagOptInMode { get; set; } = false;

        // Block codes the desk must never scan, as wildcard patterns matched against the
        // full block code ("domain:path"), case-insensitive. `*` matches any run of
        // characters — e.g. "*barrelrack*" or "foodshelves:*".
        //
        // This is the escape hatch for modded storage the desk handles badly. The defaults
        // cover Food Shelves' barrel and tun racks: those hold barrels, so they carry all
        // the problems vanilla barrels do (liquids, sealed curing/fermenting recipes) and
        // additionally let the desk pull the barrel itself out of the rack, which leaves
        // the rack's liquid in a broken state.
        //
        // Add to this list rather than waiting on a mod update when some other container
        // misbehaves; set it to [] to scan everything the desk otherwise allows.
        public string[] ExcludedBlockCodes { get; set; } = new string[]
        {
            "*barrelrack*",
            "*tunrack*"
        };

        // When true, the desk honors land claims: containers the player isn't allowed to use
        // (e.g. inside someone else's claim) are hidden and can't be accessed. Owners/granted
        // players and unclaimed land are unaffected. Defers to the game's own claim permissions.
        public bool HonorClaims { get; set; } = true;
    }
}
