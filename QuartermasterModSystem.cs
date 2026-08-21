using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
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
        private ICoreServerAPI sapi;
        private static QuartermasterConfig config;

        // Containers players have marked with a Quartermaster's Tag, keyed "x,y,z".
        // What a tag MEANS depends on config.TagOptInMode: in the default opt-out mode
        // these are the containers hidden from the desk; in opt-in mode they are the only
        // containers the desk sees. Each mode persists under its own save key (TagSaveKey)
        // so flipping the setting never silently inverts a world's existing tags.
        // Server-side, persisted in the world save.
        private HashSet<string> taggedPositions = new HashSet<string>();

        // Client-side held-tag overlay state.
        private bool holdingTag;
        private bool showingExcluded;
        private float tagRefreshAccum;

        // Accumulator for the periodic slot-stats refresh while the ledger is open.
        private float slotStatsAccum;

        // Locate state, kept in sync as containers are opened.
        private string currentItemName;
        private List<ContainerLabel> currentLabels = new List<ContainerLabel>();
        // The temporary waypoint map-components we created, so we can remove them again.
        private List<MapComponent> bkWaypointComponents = new List<MapComponent>();
        private System.Reflection.FieldInfo wpTmpField, wpAllField;

        public override void Start(ICoreAPI api)
        {
            api.RegisterBlockClass("BlockQuartermasterDesk", typeof(BlockQuartermasterDesk));
            api.RegisterItemClass("ItemQuartermasterTag", typeof(ItemQuartermasterTag));

            api.Network.RegisterChannel("quartermaster")
               .RegisterMessageType(typeof(PacketQuartermasterRequest))
               .RegisterMessageType(typeof(PacketQuartermasterResponse))
               .RegisterMessageType(typeof(SimplePos))
               .RegisterMessageType(typeof(PacketWithdraw))
               .RegisterMessageType(typeof(PacketDeposit))
               .RegisterMessageType(typeof(PacketExcludedRequest))
               .RegisterMessageType(typeof(PacketExcludedList))
               .RegisterMessageType(typeof(PacketSlotStatsRequest))
               .RegisterMessageType(typeof(PacketSlotStats));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;
            clientChannel = capi.Network.GetChannel("quartermaster")
                .SetMessageHandler<PacketQuartermasterResponse>(OnQuartermasterDataReceived)
                .SetMessageHandler<PacketExcludedList>(OnExcludedListReceived)
                .SetMessageHandler<PacketSlotStats>(OnSlotStatsReceived);

            dialog = new GuiDialogQuartermaster(capi, this);

            capi.Input.RegisterHotKey("quartermaster", "Open Quartermaster's Desk", GlKeys.J, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler("quartermaster", OnHotKey);

            // Uses GameTickListener for instant click detection/removal
            capi.Event.RegisterGameTickListener(OnClientTick, 50);

            // Shift-click deposit while the ledger is open. This raw event fires before
            // any GUI dialog sees the click (see OnClientMouseDown).
            capi.Event.MouseDown += OnClientMouseDown;

            // Register the floating label renderer for through-wall container labels
            labelRenderer = new ContainerLabelRenderer(capi);
            capi.Event.RegisterRenderer(labelRenderer, EnumRenderStage.Ortho);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            this.sapi = sapi;

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
                .SetMessageHandler<PacketDeposit>(OnDeposit)
                .SetMessageHandler<PacketExcludedRequest>(OnExcludedRequest)
                .SetMessageHandler<PacketSlotStatsRequest>(OnSlotStatsRequest);

            sapi.Event.SaveGameLoaded += LoadTaggedPositions;
            sapi.Event.GameWorldSave += SaveTaggedPositions;

            // Exclusions are keyed to a position, not a block entity — drop the entry the
            // moment its block is broken (or something new is placed over an excluded spot,
            // e.g. after an explosion) so a replacement container never inherits it. The lazy
            // prune in SendTaggedList remains as a backstop for other removal paths.
            sapi.Event.DidBreakBlock += OnServerBlockBroken;
            sapi.Event.DidPlaceBlock += OnServerBlockPlaced;
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
            UpdateTagOverlay(dt);

            // While the ledger is open, refresh the "Empty slots" label every couple of
            // seconds so chests placed/broken/filled in range show up without reopening.
            if (dialog != null && dialog.IsOpened())
            {
                slotStatsAccum += dt;
                if (slotStatsAccum > 2f)
                {
                    slotStatsAccum = 0f;
                    clientChannel?.SendPacket(new PacketSlotStatsRequest());
                }
            }
            else slotStatsAccum = 0f;

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

        // Shift+Left-clicking an item in the hotbar or bags while the ledger is open
        // deposits it straight into storage. Hooked on the raw mouse-down event because it
        // fires before any GUI dialog gets the click — the vanilla inventory dialog is
        // ahead of ours in input order and would otherwise shift-move the stack between
        // bags and hotbar before we ever saw it.
        private void OnClientMouseDown(MouseEvent args)
        {
            if (args.Handled || args.Button != EnumMouseButton.Left) return;
            if (dialog == null || !dialog.IsOpened() || dialog.IsLocateOnly) return;
            // Read the RAW key state: a focused text input (e.g. the ledger's search bar)
            // marks the Shift keydown event handled, and ClientMain then skips updating the
            // filtered KeyboardKeyState array entirely — so with the search bar focused,
            // Shift reads as "not held" there even while physically down. KeyboardKeyStateRaw
            // is set before GUI dispatch and is what the engine's own text-selection code
            // reads for the same reason.
            if (!capi.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftLeft] &&
                !capi.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftRight]) return;

            // CurrentHoveredSlot is a cache refreshed only by slot enter/leave events, and
            // slot grids only fire "enter" when the hovered slot *changes*. The hotbar HUD
            // is never recomposed between ledger sessions, so its grid still remembers the
            // slot hovered last session while the engine nulled CurrentHoveredSlot on dialog
            // close — reopen the ledger with the cursor on that same slot and no enter event
            // ever refires. A move replayed at the cursor position alone (the v1.0.16 fix)
            // can't heal that, so first replay a move at an off-screen point to make every
            // grid fire "leave" and forget its remembered slot, then one at the real cursor
            // position so the grid under it fires a fresh "enter".
            var moveOut = new MouseEvent(-100, -100);
            foreach (GuiDialog dlg in capi.Gui.LoadedGuis)
            {
                if (!dlg.ShouldReceiveMouseEvents()) continue;
                dlg.OnMouseMove(moveOut);
            }
            var move = new MouseEvent(args.X, args.Y);
            foreach (GuiDialog dlg in capi.Gui.LoadedGuis)
            {
                if (!dlg.ShouldReceiveMouseEvents()) continue;
                dlg.OnMouseMove(move);
                if (move.Handled) break;
            }

            var invMgr = capi.World.Player?.InventoryManager;
            ItemSlot hovered = invMgr?.CurrentHoveredSlot;
            // Only filled slots in the player's own hotbar/backpack, never an equipped bag
            // itself, and only with an empty cursor (a held stack keeps its vanilla
            // place/swap behavior). The class-name check also leaves the ledger's own
            // virtual grid alone, where shift-click still means "withdraw all".
            if (hovered?.Itemstack == null || hovered is ItemSlotBackpack) return;
            if (invMgr.MouseItemSlot?.Itemstack != null) return;

            string invClass = hovered.Inventory?.ClassName;
            if (invClass != GlobalConstants.hotBarInvClassName &&
                invClass != GlobalConstants.backpackInvClassName) return;

            int slotId = hovered.Inventory.GetSlotId(hovered);
            if (slotId < 0) return;

            clientChannel.SendPacket(new PacketDeposit { Mode = 3, InventoryClass = invClass, SlotId = slotId });
            args.Handled = true;
        }

        // While the player holds a Quartermaster's Tag, nearby tagged containers show an
        // "Excluded" (opt-out) or "Included" (opt-in) label, reusing the through-wall label
        // renderer. The list refreshes on equip and every couple of seconds while held (so it
        // tracks toggles and newly loaded areas); putting the tag away restores the normal
        // locate labels.
        private void UpdateTagOverlay(float dt)
        {
            bool holding = capi.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is ItemQuartermasterTag;

            if (holding)
            {
                tagRefreshAccum += dt;
                if (!holdingTag || tagRefreshAccum > 2f)
                {
                    tagRefreshAccum = 0f;
                    clientChannel.SendPacket(new PacketExcludedRequest());
                }
            }
            else if (holdingTag)
            {
                showingExcluded = false;
                RefreshHighlights();
            }

            holdingTag = holding;
        }

        private void OnExcludedListReceived(PacketExcludedList packet)
        {
            if (!holdingTag) return;
            showingExcluded = true;

            string text = Lang.Get(packet.TagOptIn ? "quartermaster:tag-label-included"
                                                   : "quartermaster:tag-label-excluded");
            var labels = packet.Positions
                .Select(p => new ContainerLabel { X = p.X, Y = p.Y, Z = p.Z, Text = text })
                .ToList();
            labelRenderer?.SetLabels(labels);
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

                // Show labels only for containers still open. While the held-tag "Excluded"
                // overlay owns the renderer, leave its labels alone.
                if (!showingExcluded)
                {
                    var remaining = currentLabels
                        .Where(l => activeHighlights.Any(p => p.X == l.X && p.Y == l.Y && p.Z == l.Z))
                        .ToList();
                    labelRenderer?.SetLabels(remaining);
                }
            }
            else
            {
                capi.World.HighlightBlocks(capi.World.Player, 56, new List<BlockPos>(), new List<int>());
                if (!showingExcluded) labelRenderer?.ClearLabels();
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
                            // Block kinds the desk must never touch at all (work stations,
                            // barrels, buckets, planters, bookshelves) — see IsScannableKind.
                            if (!IsScannableKind(be)) continue;
                            // Server-configured exclusions by block code, for modded storage
                            // the desk handles badly (see config.ExcludedBlockCodes).
                            if (IsExcludedByCode(be)) continue;
                            // The Quartermaster's Tag curates what the desk sees, and the
                            // config decides which way round that works. Opt-out (default):
                            // a tagged container is invisible to the desk. Opt-in: the tag
                            // list is a whitelist and everything untagged is invisible.
                            // Either way "invisible" is total — no browse, locate, withdraw,
                            // or deposit — because every one of those paths comes through here.
                            bool tagged = taggedPositions.Contains(PosKey(entry.Key));
                            if (config.TagOptInMode ? !tagged : tagged) continue;
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
        // anvil, grain in a quern, or iron plates packed in a stone coffin mid-cementation.
        // These must never appear in the ledger or be withdrawable —
        // pulling from them removes the item from an active device (destructive). `is` checks
        // also catch modded subclasses. Barrels and placed buckets are excluded separately in
        // EnumerateContainers, since they hold liquids (and, for barrels, sealed
        // curing/fermenting recipes).
        // Land-claim check: true if the player may USE (open) a block at pos — owners and
        // granted players/groups pass, everyone else is denied. Lets the remote terminal honor
        // claims by skipping containers the player couldn't access by hand. Unclaimed land and
        // single-player return Granted, so there's no behavior change where claims aren't used.
        private static bool HasClaimAccess(IServerPlayer player, BlockPos pos)
        {
            return player.Entity.World.Claims.TestAccess(player, pos, EnumBlockAccessFlags.Use)
                == EnumWorldAccessResponse.Granted;
        }

        // Block kinds the desk never scans, whatever the tag settings say. Shared by the
        // scan itself and by ToggleTag, so a player can't tag a block the desk would then
        // ignore anyway (which would report success and do nothing visible).
        //   - Work stations hold items mid-process, not in storage.
        //   - Barrels hold liquids and sealed curing/fermenting recipes.
        //   - Placed buckets hold liquids, empty or not.
        //   - Planters and flowerpots/vases keep their planted flower in a 1-slot
        //     inventory; a withdraw would strip decor out of builds.
        //   - Bookshelves are a BlockEntityDisplay and so read as ordinary storage, but
        //     their books are a library arranged by hand and "withdraw all" would empty
        //     every shelf in range. (Purely decorative clutter bookshelves use
        //     BEBehaviorClutterBookshelf, have no inventory, and were never scanned.)
        // Says nothing about whether the block actually has an inventory — callers that
        // need one still go through GetInventory.
        // Compiled form of config.ExcludedBlockCodes, built once on first use.
        private static Regex[] excludedCodePatterns;

        // Matches a block entity against the configured code patterns ("*barrelrack*").
        // Kept separate from IsScannableKind because that one is about kinds the mod itself
        // knows are unsafe, while this is the server admin's own list.
        private static bool IsExcludedByCode(BlockEntity be)
        {
            string[] patterns = config.ExcludedBlockCodes;
            if (patterns == null || patterns.Length == 0) return false;

            if (excludedCodePatterns == null || excludedCodePatterns.Length != patterns.Length)
            {
                excludedCodePatterns = patterns
                    .Where(pat => !string.IsNullOrWhiteSpace(pat))
                    .Select(pat => new Regex(
                        "^" + string.Join(".*", pat.Trim().Split('*').Select(Regex.Escape)) + "$",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled))
                    .ToArray();
            }

            string code = be.Block?.Code?.ToString();
            if (string.IsNullOrEmpty(code)) return false;

            foreach (var rx in excludedCodePatterns)
                if (rx.IsMatch(code)) return true;

            return false;
        }

        private static bool IsScannableKind(BlockEntity be)
        {
            return be != null
                && !IsProcessingDevice(be)
                && !(be is BlockEntityBarrel)
                && !(be is BlockEntityBucket)
                && !(be is BlockEntityPlantContainer)
                && !(be is BlockEntityBookshelf);
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
                || be is BlockEntityStoneCoffin
                || be is BlockEntityAnvil;
        }

        // --- CONTAINER EXCLUSION (Quartermaster's Tag) ---

        private static string PosKey(BlockPos pos) => $"{pos.X},{pos.Y},{pos.Z}";

        // Each tag mode owns a separate save key. Sharing one list would mean flipping
        // TagOptInMode inverts every tag in the world — the chests a player deliberately
        // hid would become the only ones visible. With separate keys the switch is
        // non-destructive and reversible: the other mode's list simply lies dormant.
        private static string TagSaveKey =>
            config.TagOptInMode ? "quartermasterIncluded" : "quartermasterExcluded";

        private void LoadTaggedPositions()
        {
            try
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData(TagSaveKey);
                if (data != null)
                    taggedPositions = new HashSet<string>(SerializerUtil.Deserialize<List<string>>(data));
            }
            catch (Exception ex)
            {
                Mod.Logger.Error("[Quartermaster] Failed to load tagged-container list: {0}", ex);
                taggedPositions = new HashSet<string>();
            }
        }

        private void SaveTaggedPositions()
        {
            sapi.WorldManager.SaveGame.StoreData(TagSaveKey, SerializerUtil.Serialize(taggedPositions.ToList()));
        }

        // Toggles a container in or out of the ledger. Called server-side by
        // ItemQuartermasterTag when a player sneak-clicks a block while holding a tag.
        // Only blocks the desk would actually scan can be tagged, and claim access is
        // checked so players can't tag containers they couldn't open by hand.
        public void ToggleTag(IServerPlayer player, BlockPos pos)
        {
            BlockEntity be = player.Entity.World.BlockAccessor.GetBlockEntity(pos);
            bool scannable = IsScannableKind(be) && !IsExcludedByCode(be) && GetInventory(be) != null;
            if (!scannable)
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup,
                    "Quartermaster: that block isn't a container the desk scans.", EnumChatType.Notification);
                return;
            }
            if (config.HonorClaims && !HasClaimAccess(player, pos))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup,
                    "Quartermaster: you don't have permission to use that container.", EnumChatType.Notification);
                return;
            }

            string key = PosKey(pos);
            bool nowTagged = taggedPositions.Add(key);
            if (!nowTagged) taggedPositions.Remove(key);
            SaveTaggedPositions();

            // A tag means the opposite thing in each mode, so say what actually happened
            // rather than a generic "toggled".
            string message = config.TagOptInMode
                ? (nowTagged ? "Quartermaster: container added to the ledger."
                             : "Quartermaster: container removed from the ledger.")
                : (nowTagged ? "Quartermaster: container excluded from the ledger."
                             : "Quartermaster: container returned to the ledger.");
            player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);

            // Keep the held-tag overlay in sync immediately.
            SendTaggedList(player);
        }

        private void OnServerBlockBroken(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel)
            => RemoveTagAt(blockSel?.Position);

        private void OnServerBlockPlaced(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel, ItemStack withItemStack)
            => RemoveTagAt(blockSel?.Position);

        private void RemoveTagAt(BlockPos pos)
        {
            if (pos == null || !taggedPositions.Remove(PosKey(pos))) return;
            SaveTaggedPositions();
        }

        private void OnExcludedRequest(IServerPlayer player, PacketExcludedRequest packet) => SendTaggedList(player);

        // Sends the tagged positions near the player, for the held-tag label overlay
        // (labelled "Excluded" or "Included" client-side depending on the tag mode).
        // Also lazily prunes entries whose container no longer exists — but only when the
        // chunk is loaded, so a chest in an unloaded area is never mistaken for a removed one.
        private void SendTaggedList(IServerPlayer player)
        {
            var accessor = player.Entity.World.BlockAccessor;
            BlockPos pPos = player.Entity.Pos.AsBlockPos;
            int range = (config.ChunkRadius + 1) * 32;

            var nearby = new List<SimplePos>();
            var stale = new List<string>();

            foreach (string key in taggedPositions)
            {
                string[] parts = key.Split(',');
                if (parts.Length != 3
                    || !int.TryParse(parts[0], out int x)
                    || !int.TryParse(parts[1], out int y)
                    || !int.TryParse(parts[2], out int z))
                {
                    stale.Add(key);
                    continue;
                }

                if (Math.Abs(x - pPos.X) > range || Math.Abs(z - pPos.Z) > range) continue;

                BlockPos pos = new BlockPos(x, y, z, 0);
                if (accessor.GetChunkAtBlockPos(pos) != null)
                {
                    BlockEntity be = accessor.GetBlockEntity(pos);
                    if (be == null || GetInventory(be) == null) { stale.Add(key); continue; }
                }

                // Honor land claims: never reveal excluded containers inside claims this
                // player can't use — the through-wall "Excluded" label would otherwise leak
                // the position of storage someone deliberately hid.
                if (config.HonorClaims && !HasClaimAccess(player, pos)) continue;

                nearby.Add(new SimplePos { X = x, Y = y, Z = z });
            }

            if (stale.Count > 0)
            {
                foreach (string key in stale) taggedPositions.Remove(key);
                SaveTaggedPositions();
            }

            serverChannel.SendPacket(new PacketExcludedList { Positions = nearby, TagOptIn = config.TagOptInMode }, player);
        }

        private void OnClientRequest(IServerPlayer player, PacketQuartermasterRequest packet) => SendLedger(player);

        // Scans all nearby containers and sends the consolidated ledger to the client.
        private void SendLedger(IServerPlayer player)
        {
            Dictionary<string, QuartermasterItemDTO> consolidated = new Dictionary<string, QuartermasterItemDTO>();
            int freeSlots = 0, totalSlots = 0;
            // How many containers the desk could actually see this scan. In opt-in mode a
            // zero here means "nothing tagged yet", which the ledger explains rather than
            // showing a bare empty grid that looks like a bug.
            int scannedContainers = 0;

            foreach (var (pos, inv, be) in EnumerateContainers(player))
            {
                scannedContainers++;
                ScanInventory(inv, consolidated, pos);
                // Slot stats count only deposit-eligible storage (chests/trunks), so the
                // "Empty slots" label matches what a deposit could actually fill — racks,
                // shelves and display cases are browsable but not deposit targets.
                if (IsStorageContainer(be)) CountSlots(inv, ref freeSlots, ref totalSlots);
            }

            serverChannel.SendPacket(new PacketQuartermasterResponse
            {
                Items = consolidated.Values.ToList(),
                LocateOnly = config.LocateOnly,
                FreeSlots = freeSlots,
                TotalSlots = totalSlots,
                TagOptIn = config.TagOptInMode,
                ScannedContainers = scannedContainers
            }, player);
        }

        private static void CountSlots(IInventory inv, ref int freeSlots, ref int totalSlots)
        {
            foreach (var slot in inv)
            {
                totalSlots++;
                if (slot?.Itemstack == null) freeSlots++;
            }
        }

        // Cheap periodic refresh for the "Empty slots" label: counts slots without
        // building the item DTOs of a full ledger scan.
        private void OnSlotStatsRequest(IServerPlayer player, PacketSlotStatsRequest packet)
        {
            int freeSlots = 0, totalSlots = 0;
            foreach (var (pos, inv, be) in EnumerateContainers(player, IsStorageContainer))
                CountSlots(inv, ref freeSlots, ref totalSlots);

            serverChannel.SendPacket(new PacketSlotStats { FreeSlots = freeSlots, TotalSlots = totalSlots }, player);
        }

        private void ScanInventory(IInventory inventory, Dictionary<string, QuartermasterItemDTO> list, BlockPos pos)
        {
            foreach (var slot in inventory) {
                if (slot?.Itemstack == null) continue;
                string code = slot.Itemstack.Collectible.Code.ToString();
                // Decorative chests, clutter, and other attribute-variant blocks share one block
                // code; the specific kind lives in the "type"/"variant"/"material" attributes. Key
                // on all of them so distinct variants don't collapse into a single generic entry.
                string variantType = slot.Itemstack.Attributes?.GetString("type") ?? "";
                string variant = slot.Itemstack.Attributes?.GetString("variant") ?? "";
                string material = slot.Itemstack.Attributes?.GetString("material") ?? "";
                string key = code + "|" + variantType + "|" + variant + "|" + material;
                if (!list.ContainsKey(key))
                    list[key] = new QuartermasterItemDTO {
                        Code = code, Count = 0, Type = slot.Itemstack.Class.ToString(),
                        VariantType = variantType, Variant = variant, Material = material,
                        // Keep a representative attribute tree so the client can rebuild the exact
                        // look (mesh + name) — cheap: one snapshot per distinct variant, not per stack.
                        AttributesData = (slot.Itemstack.Attributes as TreeAttribute)?.ToBytes()
                    };

                list[key].Count += slot.Itemstack.StackSize;
                if (list[key].Locations.Count < 20 && !list[key].Locations.Any(l => l.X == pos.X && l.Y == pos.Y && l.Z == pos.Z))
                    list[key].Locations.Add(new SimplePos { X = pos.X, Y = pos.Y, Z = pos.Z });
            }
        }

        private void OnQuartermasterDataReceived(PacketQuartermasterResponse packet)
            => dialog?.UpdateDataFromServer(packet.Items, packet.LocateOnly, packet.FreeSlots, packet.TotalSlots,
                                            packet.TagOptIn, packet.ScannedContainers);

        private void OnSlotStatsReceived(PacketSlotStats packet) => dialog?.UpdateSlotStats(packet.FreeSlots, packet.TotalSlots);

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
                    if ((st.Attributes?.GetString("variant") ?? "") != (packet.Variant ?? "")) continue;
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

            // Mode 3: shift-click deposit of one specific hotbar/backpack slot. Only the
            // player's own inventories are ever resolved — the class name is validated
            // rather than trusted, so a forged packet can't reference anything else.
            if (packet.Mode == 3)
            {
                if (packet.InventoryClass != GlobalConstants.hotBarInvClassName &&
                    packet.InventoryClass != GlobalConstants.backpackInvClassName) return;

                IInventory inv = player.InventoryManager.GetOwnInventory(packet.InventoryClass);
                if (inv == null || packet.SlotId < 0 || packet.SlotId >= inv.Count) return;

                ItemSlot slot = inv[packet.SlotId];
                if (slot?.Itemstack == null || slot is ItemSlotBackpack) return;

                string name = slot.Itemstack.GetName();
                StoreIntoContainers(player, slot.Itemstack);
                int remaining = slot.Itemstack.StackSize; // whatever didn't fit stays put
                if (remaining <= 0) slot.Itemstack = null;
                slot.MarkDirty();
                WarnIfStorageFull(player, remaining, name);
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
        //
        // retrieveOnly is vanilla's own "you may take from this, but not put into it" flag —
        // it's what marks the ruin containers found in the world: collapsed chests
        // (chest-collapsed1..4) and aged baskets (stationarybasket-aged*). Opening one by hand
        // won't let you store anything in it, so the desk must not either. Without this check
        // a "Deposit All" could fling your pack into a derelict chest in a ruin two hundred
        // blocks away and underground — the desk doing something the game itself forbids.
        // Withdrawing from them is untouched: taking is exactly what retrieveOnly permits,
        // so ruins stay lootable through the ledger.
        private bool IsStorageContainer(BlockEntity be)
        {
            return be is BlockEntityGenericTypedContainer gtc && !gtc.isPerPlayer && !gtc.retrieveOnly;
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
