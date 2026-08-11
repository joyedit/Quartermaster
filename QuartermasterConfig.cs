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

        // When true, the desk honors land claims: containers the player isn't allowed to use
        // (e.g. inside someone else's claim) are hidden and can't be accessed. Owners/granted
        // players and unclaimed land are unaffected. Defers to the game's own claim permissions.
        public bool HonorClaims { get; set; } = true;
    }
}
