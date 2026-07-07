using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Quartermaster
{
    // A reusable tool for curating what the Quartermaster's Desk can see. Sneak + right-click
    // a container to toggle it between included and excluded. The engine always gives a plain
    // right-click to the block first (that's how chests open), so the tag can only ever act on
    // the sneak-click path — and there, HeldPriorityInteract makes it win over blocks with
    // their own sneak interactions (tool racks take/place a tool, ground-storage piles add an
    // item), which otherwise could never be tagged. Plain right-click keeps its normal
    // behavior, so containers still open while the tag is held. While the tag is held, nearby
    // excluded containers show a floating "Excluded" label (see QuartermasterModSystem).
    // The toggle runs server-side only, so it's authoritative.
    public class ItemQuartermasterTag : Item
    {
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            HeldPriorityInteract = true;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            // Only claim sneak-clicks on blocks with a block entity (containers, racks,
            // piles, shelves...). Anything else keeps its normal behavior.
            if (blockSel?.Position == null
                || !byEntity.Controls.ShiftKey
                || byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefault;
            if (!firstEvent || byEntity.World.Side != EnumAppSide.Server) return;

            if ((byEntity as EntityPlayer)?.Player is IServerPlayer player)
            {
                api.ModLoader.GetModSystem<QuartermasterModSystem>().ToggleExcluded(player, blockSel.Position);
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "quartermaster:heldhelp-tag-toggle",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "shift"
                }
            };
        }
    }
}
