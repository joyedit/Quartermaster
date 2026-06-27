using Vintagestory.API.Common;

namespace Quartermaster
{
    public class BlockQuartermasterDesk : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.Side != EnumAppSide.Client) return true;

            // Open the quartermaster
            var dialog = QuartermasterModSystem.dialog;
            if (dialog != null)
            {
                if (dialog.IsOpened())
                    dialog.TryClose();
                else
                    dialog.TryOpen();
            }

            return true;
        }
    }
}
