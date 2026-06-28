using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Quartermaster
{
    public class QuartermasterModSystem : ModSystem
    {
        public static IClientNetworkChannel clientChannel;
        public static IServerNetworkChannel serverChannel;
        public static GuiDialogQuartermaster dialog;

        private List<BlockPos> activeHighlights = new List<BlockPos>();
        private WaypointMapLayer wpLayer;
        private ContainerLabelRenderer labelRenderer;
        private ICoreClientAPI capi;
        private static QuartermasterConfig config;

        // Locate state, kept in sync as containers are opened.
        private string currentItemName;
        private List<ContainerLabel> currentLabels = new List<ContainerLabel>();
        // The temporary waypoint map-components we created, so we can remove them again.
        private List<MapComponent> bkWaypointComponents = new List<MapComponent>();
        private System.Reflection.FieldInfo wpTmpField, wpAllField;

        public override void Start(ICoreAPI api)
        {
            api.RegisterBlockClass("BlockQuartermasterDesk", typeof(BlockQuartermasterDesk));

            api.Network.RegisterChannel("quartermaster")
               .RegisterMessageType(typeof(PacketQuartermasterRequest))
               .RegisterMessageType(typeof(PacketQuartermasterResponse))
               .RegisterMessageType(typeof(SimplePos))
               .RegisterMessageType(typeof(PacketWithdraw))
               .RegisterMessageType(typeof(PacketDeposit));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;
            clientChannel = capi.Network.GetChannel("quartermaster")
                .SetMessageHandler<PacketQuartermasterResponse>(OnQuartermasterDataReceived);

            dialog = new GuiDialogQuartermaster(capi, this);

            capi.Input.RegisterHotKey("quartermaster", "Open Quartermaster's Desk", GlKeys.J, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler("quartermaster", OnHotKey);

            // Uses GameTickListener for instant click detection/removal
            capi.Event.RegisterGameTickListener(OnClientTick, 50);

            // Register the floating label renderer for through-wall container labels
            labelRenderer = new ContainerLabelRenderer(capi);
            capi.Event.RegisterRenderer(labelRenderer, EnumRenderStage.Ortho);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            try
            {
                config = sapi.LoadModConfig<QuartermasterConfig>("QuartermasterConfig.json");
            }
            catch { }

            if (config == null)
            {
                config = new QuartermasterConfig();
            }

            sapi.StoreModConfig(config, "QuartermasterConfig.json");

            serverChannel = sapi.Network.GetChannel("quartermaster")
                .SetMessageHandler<PacketQuartermasterRequest>(OnClientRequest)
                .SetMessageHandler<PacketWithdraw>(OnWithdraw)
                .SetMessageHandler<PacketDeposit>(OnDeposit);
        }

        private bool OnHotKey(KeyCombination comb)
        {
            if (dialog == null) return true;
            if (dialog.IsOpened()) dialog.TryClose();
            else if (dialog.IsLookingAtStation()) dialog.TryOpen();
            // IsLookingAtStation will show appropriate error message if needed
            return true;
        }

        // --- INSTANT REMOVAL LOGIC ---
        private void OnClientTick(float dt)
        {
            if (!capi.Input.MouseButton.Right) return;

            var blockSel = capi.World.Player.CurrentBlockSelection;
            if (blockSel?.Position == null) return;

            int removed = activeHighlights.RemoveAll(p =>
                p.X == blockSel.Position.X &&
                p.Y == blockSel.Position.Y &&
                p.Z == blockSel.Position.Z);

            if (removed > 0)
            {
                // An opened container's highlight, label AND map waypoint all clear together.
                RefreshHighlights();
            }
        }

        public void SetHighlights(List<BlockPos> positions, string itemName = null, Dictionary<string, int> perLocationCounts = null)
        {
            activeHighlights.Clear();
            activeHighlights.AddRange(positions);
            currentItemName = itemName ?? "Quartermaster";

            // Build the full label set; RefreshHighlights filters it to remaining containers.
            currentLabels = new List<ContainerLabel>();
            foreach (var pos in positions)
            {
                string key = $"{pos.X},{pos.Y},{pos.Z}";
                int count = 0;
                perLocationCounts?.TryGetValue(key, out count);
                string labelText = (itemName != null && count > 0) ? $"{itemName} x{count}" : (itemName ?? "");
                currentLabels.Add(new ContainerLabel { X = pos.X, Y = pos.Y, Z = pos.Z, Text = labelText });
            }

            RefreshHighlights();

            capi.ShowChatMessage($"Locating {positions.Count} containers. Markers clear as you open each one.");
        }

        // Lazily resolves the waypoint map layer and its private component lists (used to
        // remove our temporary map markers, which have no public removal API).
        private void EnsureWaypointLayer()
        {
            if (wpLayer != null) return;
            try
            {
                var mapManager = capi.ModLoader.GetModSystem<WorldMapManager>();
                wpLayer = mapManager?.MapLayers?.OfType<WaypointMapLayer>().FirstOrDefault();
                if (wpLayer != null)
                {
                    var t = wpLayer.GetType();
                    wpTmpField = t.GetField("tmpWayPointComponents", BindingFlags.Instance | BindingFlags.NonPublic);
                    wpAllField = t.GetField("wayPointComponents", BindingFlags.Instance | BindingFlags.NonPublic);
                }
            }
            catch (Exception ex)
            {
                capi.ShowChatMessage($"Quartermaster: Could not access waypoint system: {ex.Message}");
            }
        }

        private List<MapComponent> WpList(System.Reflection.FieldInfo f) => f?.GetValue(wpLayer) as List<MapComponent>;

        // Removes the markers we previously added, then adds fresh ones for the positions
        // still active. Keeps the map dots and in-world beacons in sync with activeHighlights.
        private void SyncWaypoints()
        {
            EnsureWaypointLayer();
            if (wpLayer == null) return;

            var tmpList = WpList(wpTmpField);
            var allList = WpList(wpAllField);

            // Remove our existing markers.
            foreach (var comp in bkWaypointComponents)
            {
                tmpList?.Remove(comp);
                allList?.Remove(comp);
                try { comp.Dispose(); } catch { }
            }
            bkWaypointComponents.Clear();

            // Re-add for remaining containers.
            foreach (var pos in activeHighlights)
            {
                var wp = new Waypoint
                {
                    Position = new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5),
                    Title = currentItemName ?? "Quartermaster",
                    Icon = "circle",
                    Color = ColorUtil.ToRgba(255, 51, 153, 255),
                    ShowInWorld = true,
                    Pinned = false,
                    Temporary = true,
                    OwningPlayerUid = capi.World.Player.PlayerUID
                };
                wpLayer.AddTemporaryWaypoint(wp);
                var added = tmpList ?? WpList(wpTmpField);
                if (added != null && added.Count > 0) bkWaypointComponents.Add(added[added.Count - 1]);
            }
        }

        private void RefreshHighlights()
        {
            // BLUE: (Alpha=150, Blue=255, Green=0, Red=0)
            int blue = ColorUtil.ToRgba(150, 255, 0, 0);

            if (activeHighlights.Count > 0)
            {
                List<int> colors = activeHighlights.Select(_ => blue).ToList();
                capi.World.HighlightBlocks(capi.World.Player, 56, activeHighlights, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Cube);

                // Show labels only for containers still open.
                var remaining = currentLabels
                    .Where(l => activeHighlights.Any(p => p.X == l.X && p.Y == l.Y && p.Z == l.Z))
                    .ToList();
                labelRenderer?.SetLabels(remaining);
            }
            else
            {
                capi.World.HighlightBlocks(capi.World.Player, 56, new List<BlockPos>(), new List<int>());
                labelRenderer?.ClearLabels();
            }

            SyncWaypoints();
        }

        // Walks every chunk within the configured range and yields each block entity
        // that exposes an inventory, optionally filtered by a predicate. Shared by the
        // read-only scan and the sort feature.
        private IEnumerable<(BlockPos pos, IInventory inv, BlockEntity be)> EnumerateContainers(
            IServerPlayer player, System.Func<BlockEntity, bool> predicate = null)
        {
            BlockPos pPos = player.Entity.Pos.AsBlockPos;
            int radius = config.ChunkRadius;
            int vertRange = config.VerticalRange;

            for (int x = -radius; x <= radius; x++) {
                for (int z = -radius; z <= radius; z++) {
                    int chunkX = pPos.X / 32 + x;
                    int chunkZ = pPos.Z / 32 + z;
                    int minChunkY = Math.Max(0, pPos.Y - vertRange) / 32;
                    int maxChunkY = Math.Min(player.Entity.World.BlockAccessor.MapSizeY, pPos.Y + vertRange) / 32;

                    for (int y = minChunkY; y <= maxChunkY; y++) {
                        IWorldChunk chunk = player.Entity.World.BlockAccessor.GetChunk(chunkX, y, chunkZ);
                        if (chunk == null) continue;
                        foreach (var entry in chunk.BlockEntities) {
                            BlockEntity be = entry.Value;
                            if (be == null) continue;
                            // Never index or touch work-station inventories (cooking pots on a
                            // firepit, ore in a bloomery, a workitem on an anvil, etc.).
                            if (IsProcessingDevice(be)) continue;
                            if (predicate != null && !predicate(be)) continue;
                            // Honor land claims: skip containers the player isn't allowed to use.
                            if (config.HonorClaims && !HasClaimAccess(player, entry.Key)) continue;

                            IInventory inv = GetInventory(be);
                            if (inv != null)
                                yield return (entry.Key, inv, be);
                        }
                    }
                }
            }
        }

        // Resolves a block entity's inventory: IBlockEntityContainer first, then the
        // "Inventory" property, then a non-public "inventory" field (display entities).
        private IInventory GetInventory(BlockEntity be)
        {
            if (be is IBlockEntityContainer container && container.Inventory != null)
                return container.Inventory;

            var invProp = be.GetType().GetProperty("Inventory");
            if (invProp != null && invProp.GetValue(be) is IInventory invFromProp)
                return invFromProp;

            var invField = be.GetType().GetField("inventory",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (invField != null && invField.GetValue(be) is IInventory invFromField)
                return invFromField;

            return null;
        }

        // Work-station / processing block entities hold items mid-process, not in storage:
        // a cooking pot on a firepit, ore in a bloomery, metal in a forge, a workitem on an
        // anvil, grain in a quern. These must never appear in the ledger or be withdrawable —
        // pulling from them removes the item from an active device (destructive). `is` checks
        // also catch modded subclasses. Barrels are intentionally NOT excluded (real storage).
        // Land-claim check: true if the player may USE (open) a block at pos — owners and
        // granted players/groups pass, everyone else is denied. Lets the remote terminal honor
        // claims by skipping containers the player couldn't access by hand. Unclaimed land and
        // single-player return Granted, so there's no behavior change where claims aren't used.
        private static bool HasClaimAccess(IServerPlayer player, BlockPos pos)
        {
            return player.Entity.World.Claims.TestAccess(player, pos, EnumBlockAccessFlags.Use)
                == EnumWorldAccessResponse.Granted;
        }

        private static bool IsProcessingDevice(BlockEntity be)
        {
            return be is BlockEntityFirepit
                || be is BlockEntityOven
                || be is BlockEntityBloomery
                || be is BlockEntityForge
                || be is BlockEntityQuern
                || be is BlockEntityStove
                || be is BlockEntityBoiler
                || be is BlockEntityAnvil;
        }

        private void OnClientRequest(IServerPlayer player, PacketQuartermasterRequest packet) => SendLedger(player);

        // Scans all nearby containers and sends the consolidated ledger to the client.
        private void SendLedger(IServerPlayer player)
        {
            Dictionary<string, QuartermasterItemDTO> consolidated = new Dictionary<string, QuartermasterItemDTO>();

            foreach (var (pos, inv, be) in EnumerateContainers(player))
                ScanInventory(inv, consolidated, pos);

            serverChannel.SendPacket(new PacketQuartermasterResponse { Items = consolidated.Values.ToList(), LocateOnly = config.LocateOnly }, player);
        }

        private void ScanInventory(IInventory inventory, Dictionary<string, QuartermasterItemDTO> list, BlockPos pos)
        {
            foreach (var slot in inventory) {
                if (slot?.Itemstack == null) continue;
                string code = slot.Itemstack.Collectible.Code.ToString();
                // Decorative chests, clutter, and other attribute-variant blocks share one block
                // code; the specific kind lives in the "type"/"material" attributes. Key on all
                // three so variants don't collapse into a single generic entry.
                string variantType = slot.Itemstack.Attributes?.GetString("type") ?? "";
                string material = slot.Itemstack.Attributes?.GetString("material") ?? "";
                string key = code + "|" + variantType + "|" + material;
                if (!list.ContainsKey(key))
                    list[key] = new QuartermasterItemDTO { Code = code, Count = 0, Type = slot.Itemstack.Class.ToString(), VariantType = variantType, Material = material };

                list[key].Count += slot.Itemstack.StackSize;
                if (list[key].Locations.Count < 20 && !list[key].Locations.Any(l => l.X == pos.X && l.Y == pos.Y && l.Z == pos.Z))
                    list[key].Locations.Add(new SimplePos { X = pos.X, Y = pos.Y, Z = pos.Z });
            }
        }

        private void OnQuartermasterDataReceived(PacketQuartermasterResponse packet) => dialog?.UpdateDataFromServer(packet.Items, packet.LocateOnly);

        private void OnWithdraw(IServerPlayer player, PacketWithdraw packet)
        {
            // Server-authoritative read-only guard: even a modified client can't move items.
            if (config.LocateOnly) { SendLedger(player); return; }
            try { DoWithdraw(player, packet); }
            catch (Exception ex) { Mod.Logger.Error("[Quartermaster] Withdraw failed: {0}", ex); }
            SendLedger(player);
        }

        private void DoWithdraw(IServerPlayer player, PacketWithdraw packet)
        {
            var world = player.Entity.World;
            AssetLocation code = new AssetLocation(packet.Code);
            EnumItemClass cls = packet.Type == "Block" ? EnumItemClass.Block : EnumItemClass.Item;
            CollectibleObject coll = cls == EnumItemClass.Block
                ? (CollectibleObject)world.GetBlock(code)
                : (CollectibleObject)world.GetItem(code);
            if (coll == null) return;

            int want = packet.Mode == 1 ? 1 : (packet.Mode == 0 ? coll.MaxStackSize : int.MaxValue);
            int delivered = 0;

            foreach (var (pos, inv, be) in EnumerateContainers(player))
            {
                bool dirty = false;
                foreach (var slot in inv)
                {
                    if (delivered >= want) break;
                    var st = slot.Itemstack;
                    if (st == null || st.Class != cls || !st.Collectible.Code.Equals(code)) continue;
                    // Match the exact attribute-variant the player clicked (normalize null↔"").
                    if ((st.Attributes?.GetString("type") ?? "") != (packet.VariantType ?? "")) continue;
                    if ((st.Attributes?.GetString("material") ?? "") != (packet.Material ?? "")) continue;

                    int take = Math.Min(st.StackSize, want - delivered);
                    // Give first, then remove only the amount actually accepted (loss-free).
                    ItemStack give = st.Clone();
                    give.StackSize = take;
                    player.InventoryManager.TryGiveItemstack(give, true);
                    int accepted = take - give.StackSize;
                    if (accepted <= 0) continue;

                    st.StackSize -= accepted;
                    if (st.StackSize <= 0) slot.Itemstack = null;
                    slot.MarkDirty();
                    dirty = true;
                    delivered += accepted;
                }
                if (dirty) be.MarkDirty(true);
                if (delivered >= want) break;
            }
        }

        private void OnDeposit(IServerPlayer player, PacketDeposit packet)
        {
            // Server-authoritative read-only guard: even a modified client can't move items.
            if (config.LocateOnly) { SendLedger(player); return; }
            try { DoDeposit(player, packet); }
            catch (Exception ex) { Mod.Logger.Error("[Quartermaster] Deposit failed: {0}", ex); }
            SendLedger(player);
        }

        private void DoDeposit(IServerPlayer player, PacketDeposit packet)
        {
            // Mode 2: deposit the contents of the player's backpack bags only. Never the
            // worn bag containers themselves (ItemSlotBackpack slots), and never the hotbar.
            if (packet.Mode == 2)
            {
                IInventory backpack = player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
                if (backpack == null) return;
                int notDeposited = 0;
                foreach (var slot in backpack)
                {
                    if (slot?.Itemstack == null) continue;
                    if (slot is ItemSlotBackpack) continue; // skip equipped bags
                    StoreIntoContainers(player, slot.Itemstack);
                    notDeposited += slot.Itemstack.StackSize; // whatever didn't fit stays in the bag
                    if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;
                    slot.MarkDirty();
                }
                WarnIfStorageFull(player, notDeposited, null);
                return;
            }

            // Modes 0/1: deposit from the cursor.
            ItemSlot mouse = player.InventoryManager.MouseItemSlot;
            if (mouse?.Itemstack == null) return;
            string itemName = mouse.Itemstack.GetName();

            if (packet.Mode == 1)
            {
                ItemStack one = mouse.Itemstack.Clone();
                one.StackSize = 1;
                int stored = StoreIntoContainers(player, one);
                if (stored > 0)
                {
                    mouse.Itemstack.StackSize -= stored;
                    if (mouse.Itemstack.StackSize <= 0) mouse.Itemstack = null;
                    mouse.MarkDirty();
                }
                WarnIfStorageFull(player, stored > 0 ? 0 : 1, itemName);
            }
            else
            {
                StoreIntoContainers(player, mouse.Itemstack);
                int notDeposited = mouse.Itemstack.StackSize; // remainder stays on the cursor
                if (mouse.Itemstack.StackSize <= 0) mouse.Itemstack = null;
                mouse.MarkDirty();
                WarnIfStorageFull(player, notDeposited, itemName);
            }
        }

        // Tells the player when a deposit couldn't be (fully) stored because nearby storage
        // is full. Nothing is lost — the leftover stays on the cursor / in the backpack — so
        // this is purely to explain why the click appeared to do nothing.
        private void WarnIfStorageFull(IServerPlayer player, int notDeposited, string itemName)
        {
            if (notDeposited <= 0) return;
            string what = itemName != null ? $"{notDeposited}x {itemName}" : $"{notDeposited} item{(notDeposited == 1 ? "" : "s")}";
            player.SendMessage(GlobalConstants.GeneralChatGroup,
                $"Quartermaster: storage full — {what} couldn't be deposited.", EnumChatType.Notification);
        }

        // Only real storage (chests + trunks); never per-player or display racks/cases.
        private bool IsStorageContainer(BlockEntity be)
        {
            return be is BlockEntityGenericTypedContainer gtc && !gtc.isPerPlayer;
        }

        // Stores as much of 'stack' as possible into nearby storage containers: tops up
        // existing matching stacks first, then fills empty slots. Decrements stack.StackSize
        // by the amount stored and returns that amount.
        private int StoreIntoContainers(IServerPlayer player, ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return 0;
            int stored = 0;

            // Pass 1: top up existing matching stacks.
            foreach (var (pos, inv, be) in EnumerateContainers(player, IsStorageContainer))
            {
                bool dirty = false;
                foreach (var slot in inv)
                {
                    if (stack.StackSize <= 0) break;
                    var st = slot.Itemstack;
                    if (st == null) continue;
                    int mergable = st.Collectible.GetMergableQuantity(st, stack, EnumMergePriority.AutoMerge);
                    if (mergable <= 0) continue;
                    int move = Math.Min(Math.Min(mergable, st.Collectible.MaxStackSize - st.StackSize), stack.StackSize);
                    if (move <= 0) continue;
                    st.StackSize += move;
                    stack.StackSize -= move;
                    stored += move;
                    slot.MarkDirty();
                    dirty = true;
                }
                if (dirty) be.MarkDirty(true);
                if (stack.StackSize <= 0) return stored;
            }

            // Pass 2: place into empty slots.
            foreach (var (pos, inv, be) in EnumerateContainers(player, IsStorageContainer))
            {
                bool dirty = false;
                foreach (var slot in inv)
                {
                    if (stack.StackSize <= 0) break;
                    if (slot.Itemstack != null) continue;
                    int move = Math.Min(stack.Collectible.MaxStackSize, stack.StackSize);
                    ItemStack place = stack.Clone();
                    place.StackSize = move;
                    slot.Itemstack = place;
                    stack.StackSize -= move;
                    stored += move;
                    slot.MarkDirty();
                    dirty = true;
                }
                if (dirty) be.MarkDirty(true);
                if (stack.StackSize <= 0) return stored;
            }

            return stored;
        }
    }
}
