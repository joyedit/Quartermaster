namespace Quartermaster
{
    public class QuartermasterConfig
    {
        // Vertical range in blocks above and below the player position
        public int VerticalRange { get; set; } = 5;

        // Horizontal scan radius in chunks (1 chunk = 32 blocks)
        public int ChunkRadius { get; set; } = 2;

        // When true, the desk is read-only: browse, search, filter, and locate still work
        // but withdraw and deposit are disabled. Enforced server-side, so a modified client
        // still cannot move items. The client UI hides the deposit controls.
        public bool LocateOnly { get; set; } = false;
    }
}
