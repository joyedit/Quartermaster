using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Quartermaster
{
    // --- DATA DEFINITIONS ---
    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class PacketQuartermasterRequest { }

    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class PacketQuartermasterResponse
    {
        public List<QuartermasterItemDTO> Items = new List<QuartermasterItemDTO>();
        public bool LocateOnly;
    }

    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class QuartermasterItemDTO
    {
        public string Code;
        public int Count;
        public string Type;
        public string VariantType;
        public string Material;
        public List<SimplePos> Locations = new List<SimplePos>();
    }

    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class SimplePos { public int X, Y, Z; }

    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class PacketWithdraw
    {
        public string Code;
        public string Type;        // "Block" or "Item"
        public string VariantType; // itemstack "type" attribute (decorative chests, clutter)
        public string Material;    // itemstack "material" attribute
        public int Mode;           // 0 = one stack, 1 = single item, 2 = all
    }

    [ProtoBuf.ProtoContract(ImplicitFields = ProtoBuf.ImplicitFields.AllPublic)]
    public class PacketDeposit
    {
        public int Mode;      // 0 = whole cursor stack, 1 = one from cursor, 2 = deposit all from backpack
    }

    [Flags]
    public enum ItemCategory
    {
        None      = 0,
        Food      = 1,
        Tools     = 2,
        Fuel      = 4,
        Wood      = 8,
        Wearables = 16,
        Metals    = 32,
        Building  = 64
    }

    public class QuartermasterEntry
    {
        public ItemStack Stack;
        public List<BlockPos> Locations;
        public ItemCategory Category;
    }

    public class GuiDialogQuartermaster : GuiDialog
    {
        public override string ToggleKeyCombinationCode => null;

        private QuartermasterModSystem modSystem;
        private List<QuartermasterEntry> allEntries = new List<QuartermasterEntry>();
        private List<QuartermasterEntry> filteredEntries = new List<QuartermasterEntry>();
        private InventoryGeneric virtualInventory;
        // A always-empty 1-slot inventory rendered as the "drop to deposit" cell.
        private InventoryGeneric depositCellInventory;

        private string currentSearchText = "";
        private ItemCategory activeCategories = ItemCategory.None;
        private int currentPage = 0;

        // LAYOUT: 10 Columns, 9 Rows (90 Items)
        private const int COLS = 10;
        private int itemsPerPage = 90;

        private bool isWaitingForServer = false;
        private long openTime = 0;

        // Server-controlled read-only mode (LocateOnly config). When true the deposit/withdraw
        // controls are hidden and grid clicks locate instead of withdraw. Writes are also
        // blocked server-side regardless of this flag.
        private bool locateOnly = false;

        // The entries currently shown on the visible page, indexed to match the grid slots.
        private List<QuartermasterEntry> currentVisibleEntries = new List<QuartermasterEntry>();

        public GuiDialogQuartermaster(ICoreClientAPI capi, QuartermasterModSystem system) : base(capi)
        {
            this.modSystem = system;
        }

        public void UpdateDataFromServer(List<QuartermasterItemDTO> data, bool locateOnly)
        {
            this.locateOnly = locateOnly;
            allEntries.Clear();
            foreach (var dto in data)
            {
                try
                {
                    AssetLocation code = new AssetLocation(dto.Code);
                    CollectibleObject collectible = (dto.Type == "Block") ? (CollectibleObject)capi.World.GetBlock(code) : (CollectibleObject)capi.World.GetItem(code);

                    if (collectible != null)
                    {
                        ItemStack stack = new ItemStack(collectible, dto.Count);
                        // Attribute-variant blocks (decorative chests, clutter) share one code;
                        // reapply type/material so the correct name + icon show.
                        if (!string.IsNullOrEmpty(dto.VariantType)) stack.Attributes.SetString("type", dto.VariantType);
                        if (!string.IsNullOrEmpty(dto.Material)) stack.Attributes.SetString("material", dto.Material);
                        List<BlockPos> locs = dto.Locations.Select(p => new BlockPos(p.X, p.Y, p.Z)).ToList();
                        allEntries.Add(new QuartermasterEntry() { Stack = stack, Locations = locs, Category = ClassifyCollectible(collectible) });
                    }
                }
                catch { }
            }

            allEntries.Sort((a, b) => string.Compare(a.Stack.GetName(), b.Stack.GetName(), StringComparison.OrdinalIgnoreCase));
            isWaitingForServer = false;
            FilterItems(currentSearchText);

            if (IsOpened()) ComposeDialog();
        }

        // Food fallback for meal-only ingredients. These have no direct NutritionProps
        // (they only gain nutrition once cooked into a meal, via the "nutritionPropsWhenInMeal"
        // attribute), so when that attribute check doesn't catch them they'd vanish from the
        // Food tab — e.g. raw soybeans/peanuts ("beans"), raw eggs, dough, butter, raw cassava.
        // These are matched as code PREFIXES (e.g. "legume-soybean"), not substrings, so they
        // don't catch lookalikes like "seeds-soybean", "butterfly-*" or "leggings".
        private static readonly string[] FoodCodeParts =
        {
            "legume", "egg", "dough", "butter", "rawcassava"
        };

        // Fuel = what players actually stockpile as fuel. NOT "anything flammable":
        // in VS planks/sticks/candles burn at 700° (same as firewood) and ferns/grass
        // at 600°, so CombustibleProps/burn-temperature can't separate fuel from kindling.
        // lignite/anthracite codes don't contain "coal", so they're listed explicitly.
        private static readonly string[] FuelCodeParts = { "firewood", "charcoal", "coke", "coal", "lignite", "anthracite", "peat" };

        // Wood-material blocks already cover crafted wood (planks, logs, furniture, axles,
        // etc.); these catch the wood *items* that aren't blocks (no BlockMaterial to check).
        private static readonly string[] WoodItemCodeParts = { "firewood", "plank", "board", "log", "stick" };

        // Weapons / ammunition the Tool enum doesn't cover: arrows & sling bullets (class
        // ItemArrow), clubs (no tool), plus likely modded weapons. "bow" is omitted on
        // purpose (it collides with "bowl") — bows already classify via Tool != null.
        private static readonly string[] WeaponCodeParts =
        {
            "arrow", "bullets", "club", "spear", "javelin", "sword",
            "dagger", "mace", "halberd", "crossbow", "bolt"
        };

        // Classifies a collectible into one or more categories. Food/Tools/Wearables come
        // from real collectible properties; Fuel/Wood have no single defining property, so
        // they match against curated code lists (Wood also via the block's Wood material).
        private static ItemCategory ClassifyCollectible(CollectibleObject collectible)
        {
            ItemCategory cat = ItemCategory.None;
            if (collectible == null) return cat;

            // Food: directly edible (NutritionProps), plus ingredients that only gain
            // nutrition once cooked/in a meal (raw eggs, beans, etc. carry
            // "nutritionPropsWhenInMeal" instead of a direct NutritionProps).
            string path = collectible.Code?.Path ?? "";

            if (collectible.NutritionProps != null ||
                collectible.Attributes?["nutritionPropsWhenInMeal"].Exists == true) cat |= ItemCategory.Food;
            else foreach (var part in FoodCodeParts) if (path == part || path.StartsWith(part + "-")) { cat |= ItemCategory.Food; break; }

            // Tools: anything with a tool type (covers bow/spear/sling), plus weapons/ammo
            // the Tool enum misses (arrows, bullets, clubs, modded swords...).
            if (collectible.Tool != null) cat |= ItemCategory.Tools;
            else foreach (var part in WeaponCodeParts) if (path.Contains(part)) { cat |= ItemCategory.Tools; break; }
            foreach (var part in FuelCodeParts) if (path.Contains(part)) { cat |= ItemCategory.Fuel; break; }

            // Wood: any Wood-material block (raw + crafted: planks, furniture, axles...),
            // plus the wood items that aren't blocks.
            bool isWoodBlock = collectible is Block block && block.BlockMaterial == EnumBlockMaterial.Wood;
            if (isWoodBlock) cat |= ItemCategory.Wood;
            else foreach (var part in WoodItemCodeParts) if (path.Contains(part)) { cat |= ItemCategory.Wood; break; }

            // Wearables: slot-worn items. Clothing/hats/armor/jewelry carry a "clothescategory"
            // attribute; bags/backpacks carry a "backpack" attribute (mounts like boat seats
            // and saddles have neither, so they stay out). Plus temporal gears.
            if (collectible.Attributes?["clothescategory"].Exists == true) cat |= ItemCategory.Wearables;
            if (collectible.Attributes?["backpack"].Exists == true) cat |= ItemCategory.Wearables;
            if (path.Contains("gear") && path.Contains("temporal")) cat |= ItemCategory.Wearables;

            // Metals & Ore: ingots/nuggets/ore. "ore" is anchored (prefix/material) since the
            // bare substring hits forest/core/lore/etc.
            bool isOreBlock = collectible is Block oreBlock && oreBlock.BlockMaterial == EnumBlockMaterial.Ore;
            if (isOreBlock || path.StartsWith("ore-") || path == "ore" ||
                path.StartsWith("nugget") || path.StartsWith("ingot") ||
                path.Contains("metalbit") || path.Contains("metalplate") ||
                path.Contains("looseore") || path.Contains("crystalizedore"))
                cat |= ItemCategory.Metals;

            // Building: stone & ceramic blocks (rock, cobble, bricks, tiles, fired clay).
            if (collectible is Block buildBlock &&
                (buildBlock.BlockMaterial == EnumBlockMaterial.Stone || buildBlock.BlockMaterial == EnumBlockMaterial.Ceramic))
                cat |= ItemCategory.Building;

            return cat;
        }

        private void FilterItems(string searchText)
        {
            currentSearchText = searchText.ToLower();
            ApplyFilters();
        }

        // Search text (substring) AND category selection (OR across active categories).
        // No active categories means "All".
        private void ApplyFilters()
        {
            IEnumerable<QuartermasterEntry> query = allEntries;

            if (!string.IsNullOrEmpty(currentSearchText))
                query = query.Where(e => e.Stack.GetName()?.ToLower().Contains(currentSearchText) == true);

            if (activeCategories != ItemCategory.None)
                query = query.Where(e => (e.Category & activeCategories) != 0);

            filteredEntries = query.ToList();
            currentPage = 0;
            UpdateView();
        }

        private void OnCategoryToggle(ItemCategory category, bool on)
        {
            if (on) activeCategories |= category;
            else activeCategories &= ~category;
            ApplyFilters();
            ComposeDialog();
        }

        private void UpdateView()
        {
            int skip = currentPage * itemsPerPage;
            currentVisibleEntries = filteredEntries.Skip(skip).Take(itemsPerPage).ToList();
            virtualInventory = new InventoryGeneric(Math.Max(1, currentVisibleEntries.Count), "quartermaster-grid", capi, null);

            for (int i = 0; i < currentVisibleEntries.Count; i++)
            {
                virtualInventory[i].Itemstack = currentVisibleEntries[i].Stack;
            }
        }

        public void ComposeDialog()
        {
            if (virtualInventory == null) virtualInventory = new InventoryGeneric(1, "quartermaster-init", capi, null);

            double windowWidth = 850;
            double gridWidth = 480;
            double leftMargin = 170;
            double windowHeight = 620;

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            ElementBounds bgBounds = ElementBounds.Fixed(0, 0, windowWidth, windowHeight);

            ElementBounds searchBounds = ElementBounds.Fixed(leftMargin, 45, gridWidth, 30);

            // Category filter toggles, stacked vertically in the empty right-hand strip.
            const double catBtnW = 130, catBtnH = 30, catBtnGap = 8;
            double catColX = leftMargin + gridWidth + 45;
            double catColY = 85, catStep = catBtnH + catBtnGap;
            // Centered over the button column and vertically aligned with the search bar's middle.
            ElementBounds catLabelBounds     = ElementBounds.Fixed(catColX, 49, catBtnW, 22);
            ElementBounds foodBtnBounds      = ElementBounds.Fixed(catColX, catColY + 0 * catStep, catBtnW, catBtnH);
            ElementBounds toolsBtnBounds     = ElementBounds.Fixed(catColX, catColY + 1 * catStep, catBtnW, catBtnH);
            ElementBounds fuelBtnBounds      = ElementBounds.Fixed(catColX, catColY + 2 * catStep, catBtnW, catBtnH);
            ElementBounds woodBtnBounds      = ElementBounds.Fixed(catColX, catColY + 3 * catStep, catBtnW, catBtnH);
            ElementBounds wearablesBtnBounds = ElementBounds.Fixed(catColX, catColY + 4 * catStep, catBtnW, catBtnH);
            ElementBounds metalsBtnBounds    = ElementBounds.Fixed(catColX, catColY + 5 * catStep, catBtnW, catBtnH);
            ElementBounds buildingBtnBounds  = ElementBounds.Fixed(catColX, catColY + 6 * catStep, catBtnW, catBtnH);

            // HEIGHT: 435px (fits 9 rows).
            ElementBounds gridBounds = ElementBounds.Fixed(leftMargin, 85, gridWidth + 5, 435);

            ElementBounds prevButtonBounds = ElementBounds.Fixed(leftMargin, 575, 80, 30);
            ElementBounds nextButtonBounds = ElementBounds.Fixed(leftMargin + gridWidth - 80, 575, 80, 30);
            ElementBounds statusLabelBounds = ElementBounds.Fixed(windowWidth / 2 - 150, 580, 300, 30);

            // Controls / help live in the empty left strip (content starts at leftMargin = 170)
            ElementBounds depositTitleBounds = ElementBounds.Fixed(10, 42, 150, 22);
            ElementBounds depositCellBounds = ElementBounds.Fixed(57, 68, 48, 48);
            ElementBounds depositHintBounds = ElementBounds.Fixed(5, 120, 160, 34);
            ElementBounds depositAllBounds = ElementBounds.Fixed(15, 160, 140, 28);
            ElementBounds helpBounds = ElementBounds.Fixed(10, 205, 155, 350);

            if (depositCellInventory == null)
                depositCellInventory = new InventoryGeneric(1, "quartermaster-depositcell", capi, null);

            int totalPages = (int)Math.Ceiling((double)filteredEntries.Count / itemsPerPage);
            if (totalPages < 1) totalPages = 1;

            string statusText = isWaitingForServer ? "Scanning Storage..." : $"Page {currentPage + 1} / {totalPages} ({filteredEntries.Count} Items)";
            string helpText = locateOnly
                ? "Read-only station.\n\n" +
                  "Locate:\n" +
                  "• Middle-click an item\n" +
                  "• (or left-click an item)"
                : "Withdraw:\n" +
                  "• Left-click: one stack\n" +
                  "• Right-click: one item\n" +
                  "• Shift+click: all\n\n" +
                  "Locate:\n" +
                  "• Middle-click an item";

            var centered = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);

            var compo = capi.Gui.CreateCompo("quartermasterdialog", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(locateOnly ? "Quartermaster's Ledger (read-only)" : "Quartermaster's Ledger", OnTitleBarClose)
                .AddTextInput(searchBounds, OnSearchChanged, CairoFont.WhiteSmallText(), "searchBar")
                .AddStaticText("Categories", centered, catLabelBounds)
                .AddToggleButton("Food", CairoFont.WhiteSmallText(), on => OnCategoryToggle(ItemCategory.Food, on), foodBtnBounds, "catFood")
                .AddToggleButton("Tools", CairoFont.WhiteSmallText(), on => OnCategoryToggle(ItemCategory.Tools, on), toolsBtnBounds, "catTools")
                .AddToggleButton("Fuel", CairoFont.WhiteSmallText(), on => OnCategoryToggle(ItemCategory.Fuel, on), fuelBtnBounds, "catFuel")
                .AddToggleButton("Wood", CairoFont.WhiteSmallText(), on => OnCategoryToggle(ItemCategory.Wood, on), woodBtnBounds, "catWood")
                .AddToggleButton("Wearables", CairoFont.WhiteSmallText(), on => OnCategoryToggle(ItemCategory.Wearables, on), wearablesBtnBounds, "catWearables")
                .AddToggleButton("Ores & Metals", CairoFont.WhiteSmallText(), on => OnCategoryToggle(ItemCategory.Metals, on), metalsBtnBounds, "catMetals")
                .AddToggleButton("Building", CairoFont.WhiteSmallText(), on => OnCategoryToggle(ItemCategory.Building, on), buildingBtnBounds, "catBuilding")
                .AddItemSlotGrid(virtualInventory, OnSlotClick, COLS, gridBounds, "itemgrid")
                .AddSmallButton("Prev", OnPrevPage, prevButtonBounds)
                .AddSmallButton("Next", OnNextPage, nextButtonBounds)
                .AddDynamicText(statusText, CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center), statusLabelBounds, "statusLabel");

            // Deposit controls only when writes are allowed (LocateOnly hides them).
            if (!locateOnly)
            {
                compo
                    .AddStaticText("Deposit", centered, depositTitleBounds)
                    .AddItemSlotGrid(depositCellInventory, OnSlotClick, 1, depositCellBounds, "depositcell")
                    .AddStaticText("Drop a held item here\nto store it", CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center), depositHintBounds)
                    .AddSmallButton("Deposit All", OnDepositAll, depositAllBounds, EnumButtonStyle.Normal, "depositAllButton");
            }

            compo.AddStaticText(helpText, CairoFont.WhiteDetailText(), helpBounds, "helpText");

            SingleComposer = compo.Compose();

            // Disable the grids' built-in slot interaction; we handle clicks ourselves in
            // OnMouseDown so nothing is moved client-side on these virtual inventories.
            var slotGrid = SingleComposer.GetSlotGrid("itemgrid");
            if (slotGrid != null) slotGrid.CanClickSlot = (slotId) => false;
            var depositGrid = SingleComposer.GetSlotGrid("depositcell");
            if (depositGrid != null) depositGrid.CanClickSlot = (slotId) => false;

            if (!string.IsNullOrEmpty(currentSearchText))
                SingleComposer.GetTextInput("searchBar").SetValue(currentSearchText);

            // Recompose rebuilds the toggles in the off state; restore from the active set.
            SingleComposer.GetToggleButton("catFood").SetValue(activeCategories.HasFlag(ItemCategory.Food));
            SingleComposer.GetToggleButton("catTools").SetValue(activeCategories.HasFlag(ItemCategory.Tools));
            SingleComposer.GetToggleButton("catFuel").SetValue(activeCategories.HasFlag(ItemCategory.Fuel));
            SingleComposer.GetToggleButton("catWood").SetValue(activeCategories.HasFlag(ItemCategory.Wood));
            SingleComposer.GetToggleButton("catWearables").SetValue(activeCategories.HasFlag(ItemCategory.Wearables));
            SingleComposer.GetToggleButton("catMetals").SetValue(activeCategories.HasFlag(ItemCategory.Metals));
            SingleComposer.GetToggleButton("catBuilding").SetValue(activeCategories.HasFlag(ItemCategory.Building));
        }

        private void OnSearchChanged(string text)
        {
            if (capi.World.ElapsedMilliseconds - openTime < 500 && !string.IsNullOrEmpty(text)) return;
            if (text == currentSearchText) return;
            FilterItems(text);
            ComposeDialog();
            SingleComposer.FocusElement(SingleComposer.GetTextInput("searchBar").TabIndex);
        }

        private bool OnPrevPage()
        {
            if (currentPage > 0) { currentPage--; UpdateView(); ComposeDialog(); }
            return true;
        }

        private bool OnNextPage()
        {
            int totalPages = (int)Math.Ceiling((double)filteredEntries.Count / itemsPerPage);
            if (currentPage < totalPages - 1) { currentPage++; UpdateView(); ComposeDialog(); }
            return true;
        }

        // The grid's own click handling is disabled (CanClickSlot=false); this is a no-op
        // required by AddItemSlotGrid's signature. All clicks go through OnMouseDown.
        private void OnSlotClick(object packet) { }

        private bool OnDepositAll()
        {
            QuartermasterModSystem.clientChannel?.SendPacket(new PacketDeposit { Mode = 2 });
            return true;
        }

        public override void OnMouseDown(MouseEvent args)
        {
            var mouse = capi.World.Player.InventoryManager.MouseItemSlot;

            // Deposit cell: drop a held item here to store it.
            var depositCell = SingleComposer?.GetSlotGrid("depositcell");
            if (!args.Handled && depositCell != null && depositCell.Bounds.PointInside(args.X, args.Y))
            {
                if (mouse?.Itemstack != null)
                {
                    QuartermasterModSystem.clientChannel?.SendPacket(new PacketDeposit
                    {
                        Mode = args.Button == EnumMouseButton.Right ? 1 : 0
                    });
                }
                args.Handled = true;
                return;
            }

            var grid = SingleComposer?.GetSlotGrid("itemgrid");
            if (!args.Handled && grid != null && grid.Bounds.PointInside(args.X, args.Y))
            {
                // Deposits go through the deposit cell; ignore a held item over the grid
                // so nothing is placed/withdrawn by accident.
                if (mouse?.Itemstack != null) { args.Handled = true; return; }

                // grid.hoverSlotId is only refreshed on mouse-move events, so it goes stale
                // (resets to -1) whenever the dialog recomposes after a withdraw. Recompute it
                // from the actual click position so a click on a stationary cursor still works.
                grid.OnMouseMove(capi, args);
                int slotId = grid.hoverSlotId;
                if (slotId >= 0 && slotId < currentVisibleEntries.Count)
                {
                    var entry = currentVisibleEntries[slotId];
                    bool shift = capi.Input.KeyboardKeyState[(int)GlKeys.ShiftLeft] || capi.Input.KeyboardKeyState[(int)GlKeys.ShiftRight];

                    // Locate: middle-click (or any click in read-only mode).
                    if (locateOnly || args.Button == EnumMouseButton.Middle)
                    {
                        if (entry.Locations.Count > 0)
                        {
                            modSystem.SetHighlights(entry.Locations, entry.Stack.GetName());
                            TryClose();
                        }
                        else
                        {
                            capi.ShowChatMessage("Quartermaster: no known location for that item.");
                        }
                    }
                    else
                    {
                        int mode = shift ? 2 : (args.Button == EnumMouseButton.Right ? 1 : 0);
                        QuartermasterModSystem.clientChannel?.SendPacket(new PacketWithdraw
                        {
                            Code = entry.Stack.Collectible.Code.ToString(),
                            Type = entry.Stack.Class.ToString(),
                            VariantType = entry.Stack.Attributes?.GetString("type"),
                            Material = entry.Stack.Attributes?.GetString("material"),
                            Mode = mode
                        });
                    }
                }

                args.Handled = true;
                return;
            }

            base.OnMouseDown(args);
        }

        private void OnTitleBarClose() => TryClose();

        public override void OnGuiOpened()
        {
            openTime = capi.World.ElapsedMilliseconds;
            isWaitingForServer = true;
            // Start each session with a fresh, empty search and no category filters.
            currentSearchText = "";
            activeCategories = ItemCategory.None;
            currentPage = 0;
            FilterItems("");
            if (virtualInventory == null) virtualInventory = new InventoryGeneric(1, "quartermaster-init", capi, null);
            ComposeDialog();
            QuartermasterModSystem.clientChannel?.SendPacket(new PacketQuartermasterRequest());
            SingleComposer.FocusElement(-1);
        }

        public bool IsLookingAtStation()
        {
            var blockSel = capi.World.Player.CurrentBlockSelection;
            if (blockSel?.Block == null) return false;

            // The lectern can be consulted wherever it's placed (no foundation requirement);
            // this just confirms the player is actually looking at one so the hotkey opens it.
            return blockSel.Block.Code.Path.Contains("quartermasterdesk");
        }
    }
}
