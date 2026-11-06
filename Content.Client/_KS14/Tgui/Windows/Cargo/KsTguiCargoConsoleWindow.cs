using System.Linq;
using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.Cargo.Systems;
using Content.Client.UserInterface.Controls;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Cargo;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Windows.Cargo;

/// <summary>
///     The cargo request console laid out after tgui's Cargo.tsx: a status section, tabs for the catalog (category sidebar,
///         search), the requests with approve and deny, and the checkout. tgui's Help tab has no counterpart; its place
///         holds the account transfers the stock console has under "Transfers".
/// </summary>
/// <remarks>
///     tgui puts an item in a cart and confirms it. Here the catalog asks for an order (requester, reason, amount) that
///         becomes a request, a request is approved or denied, and approved orders wait for the shuttle in the checkout.
///         tg's loan and shuttle send buttons have no equivalent: the telepad needs no button.
/// </remarks>
public sealed partial class KsTguiCargoConsoleWindow : FancyWindow
{
    private enum Page : byte
    {
        Catalog,
        Requests,
        Checkout,
        Funds,
    }

    private static readonly XColor Gold = XColor.FromHex("#FFD700");

    [Dependency] private IGameTiming _timing = default!;

    private readonly IEntityManager _entityManager;
    private readonly IPrototypeManager _protoManager;
    private readonly CargoSystem _cargoSystem;
    private readonly SpriteSystem _spriteSystem;
    private readonly EntityUid _owner;
    private readonly string _defaultRequester;

    private readonly EntityQuery<CargoOrderConsoleComponent> _orderConsoleQuery;
    private readonly EntityQuery<StationBankAccountComponent> _bankQuery;

    private EntityUid? _station;
    private Page _page;
    private int _orderCount;
    private string? _category;
    private List<CargoOrderData> _orders = new();

    private readonly List<string> _categoryStrings = new();

    private readonly KsTguiSection _statusSection;
    private readonly KsTguiLabeledListItem _accountItem;
    private readonly KsTguiLabeledListItem _capacityItem;
    private readonly Label _balanceLabel;

    private readonly KsTguiSection _tabsSection;
    private readonly KsTguiTabs _pageTabs;
    private readonly KsTguiTabItem _requestsTab;
    private readonly KsTguiTabItem _checkoutTab;
    private readonly KsTguiButton _clearButton;

    private readonly BoxContainer _catalogPage;
    private readonly LineEdit _searchBar;
    private readonly KsTguiTabs _categoryTabs;
    private readonly BoxContainer _productsBox;

    private readonly BoxContainer _requestsPage;
    private readonly BoxContainer _requestsBox;

    private readonly BoxContainer _checkoutPage;
    private readonly BoxContainer _checkoutBox;

    private readonly BoxContainer _fundsPage;
    private readonly Label _transferLimitLabel;
    private readonly Label _unlimitedLabel;
    private readonly OptionButton _actionOptions;
    private readonly KsTguiNumberInput _transferSpinBox;
    private readonly KsTguiButton _accountActionButton;
    private readonly KsTguiButton _limitToggleButton;

    private readonly KsTguiCargoOrderWindow _orderWindow;

    public List<ProtoId<CargoProductPrototype>> ProductCatalogue = new();

    /// <summary>
    ///     Requester, reason, product and amount of an order the player filled in.
    /// </summary>
    public event Action<string, string, ProtoId<CargoProductPrototype>, int>? OnOrderSubmitted;

    public event Action<CargoOrderData>? OnOrderApproved;
    public event Action<CargoOrderData>? OnOrderCanceled;
    public event Action<ProtoId<CargoAccountPrototype>?, int>? OnAccountAction;
    public event Action? OnToggleUnboundedLimit;

    public KsTguiCargoConsoleWindow(
        EntityUid owner,
        IEntityManager entityManager,
        IPrototypeManager protoManager,
        SpriteSystem spriteSystem,
        string defaultRequester)
    {
        IoCManager.InjectDependencies(this);
        _entityManager = entityManager;
        _protoManager = protoManager;
        _cargoSystem = entityManager.System<CargoSystem>();
        _spriteSystem = spriteSystem;
        _owner = owner;
        _defaultRequester = defaultRequester;

        _orderConsoleQuery = entityManager.GetEntityQuery<CargoOrderConsoleComponent>();
        _bankQuery = entityManager.GetEntityQuery<StationBankAccountComponent>();

        Title = entityManager.GetComponent<MetaDataComponent>(owner).EntityName;
        SetSize = new Vector2(800f, 700f);
        MinSize = new Vector2(600f, 500f);

        _orderWindow = new KsTguiCargoOrderWindow();
        _orderWindow.OnSubmitted += (requester, reason, product, amount) => OnOrderSubmitted?.Invoke(requester, reason, product, amount);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            Margin = new Thickness(6f),
        };

        // Status
        _statusSection = new KsTguiSection();
        _balanceLabel = new Label
        {
            FontColorOverride = Gold,
            Text = Loc.GetString("cargo-console-menu-points-amount", ("amount", 0)),
        };
        _statusSection.Buttons = _balanceLabel;
        var statusList = new KsTguiLabeledList();
        _accountItem = statusList.AddItem(Loc.GetString("ks-tgui-cargo-account"), Loc.GetString("cargo-console-menu-account-name-none-text"));
        _capacityItem = statusList.AddItem(Loc.GetString("ks-tgui-cargo-capacity"), "0/0");
        _statusSection.AddChild(statusList);
        root.AddChild(_statusSection);

        if (entityManager.TryGetComponent<CargoOrderConsoleComponent>(owner, out var orderConsole))
        {
            var accountProto = _protoManager.Index(orderConsole.Account);
            _statusSection.Title = Loc.GetString(accountProto.Name);
            _accountItem.Text = Loc.GetString(accountProto.Code);
            _accountItem.TextColor = accountProto.Color;
        }

        // Tabs
        _clearButton = new KsTguiButton
        {
            Text = Loc.GetString("ks-tgui-cargo-clear"),
            Icon = KsTguiIcons.Times,
            Color = KsTguiColor.Transparent,
            Compact = true,
            Visible = false,
        };
        _clearButton.OnPressed += _ => ClearRequests();

        _tabsSection = new KsTguiSection { Buttons = _clearButton };
        _pageTabs = new KsTguiTabs { Fluid = true, Margin = new Thickness(-1f) };
        _pageTabs.AddTab(Loc.GetString("ks-tgui-cargo-tab-catalog"), KsTguiIcons.List, selected: true);
        _requestsTab = _pageTabs.AddTab(Loc.GetString("ks-tgui-cargo-tab-requests"), KsTguiIcons.Envelope);
        _checkoutTab = _pageTabs.AddTab(Loc.GetString("ks-tgui-cargo-tab-checkout"), KsTguiIcons.Cart);
        _pageTabs.AddTab(Loc.GetString("cargo-console-menu-tab-title-funds"), KsTguiIcons.Coins);
        _pageTabs.OnTabSelected += _ => SelectPage((Page)_pageTabs.SelectedIndex);
        _tabsSection.AddChild(_pageTabs);
        root.AddChild(_tabsSection);

        // Catalog
        _catalogPage = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalExpand = true };

        var sidebar = new KsTguiSection { Fill = true, HorizontalExpand = true, SizeFlagsStretchRatio = 1f };
        var sidebarBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            SeparationOverride = 6,
        };
        _searchBar = new LineEdit { PlaceHolder = Loc.GetString("cargo-console-menu-search-bar-placeholder"), HorizontalExpand = true };
        _searchBar.OnTextChanged += _ => PopulateProducts();
        sidebarBox.AddChild(_searchBar);
        _categoryTabs = new KsTguiTabs { Vertical = true, Candystripe = true };
        _categoryTabs.OnTabSelected += OnCategorySelected;
        var categoryScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        categoryScroll.AddChild(_categoryTabs);
        sidebarBox.AddChild(categoryScroll);
        sidebar.AddChild(sidebarBox);
        _catalogPage.AddChild(sidebar);

        var productSection = new KsTguiSection { Fill = true, Scrollable = true, HorizontalExpand = true, SizeFlagsStretchRatio = 3f };
        _productsBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        productSection.AddChild(_productsBox);
        _catalogPage.AddChild(productSection);
        root.AddChild(_catalogPage);

        // Requests
        _requestsPage = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, Visible = false };
        var requestsSection = new KsTguiSection { Fill = true, Scrollable = true };
        _requestsBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        requestsSection.AddChild(_requestsBox);
        _requestsPage.AddChild(requestsSection);
        root.AddChild(_requestsPage);

        // Checkout
        _checkoutPage = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, Visible = false };
        var checkoutSection = new KsTguiSection { Fill = true, Scrollable = true };
        _checkoutBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        checkoutSection.AddChild(_checkoutBox);
        _checkoutPage.AddChild(checkoutSection);
        root.AddChild(_checkoutPage);

        // Funds
        _fundsPage = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, Visible = false };
        var fundsSection = new KsTguiSection { Fill = true, Scrollable = true };
        var fundsList = new KsTguiLabeledList();

        _transferLimitLabel = new Label();
        _unlimitedLabel = new Label
        {
            Text = Loc.GetString("ks-tgui-cargo-unlimited"),
            FontColorOverride = Gold,
            Visible = false,
            Margin = new Thickness(8f, 0f, 0f, 0f),
        };
        var limitItem = fundsList.AddItem(Loc.GetString("ks-tgui-cargo-transfer-limit"));
        limitItem.AddChild(_transferLimitLabel);
        limitItem.AddChild(_unlimitedLabel);

        _actionOptions = new OptionButton { HorizontalExpand = true };
        _actionOptions.OnItemSelected += args => _actionOptions.SelectId(args.Id);
        fundsList.AddItem(Loc.GetString("ks-tgui-cargo-account-action")).AddChild(_actionOptions);

        _transferSpinBox = new KsTguiNumberInput { Format = "0", WidthPixels = 75f, MinValue = 0f };
        _transferSpinBox.SetValue(10f);
        fundsList.AddItem(Loc.GetString("ks-tgui-cargo-amount")).AddChild(_transferSpinBox);
        fundsSection.AddChild(fundsList);

        _accountActionButton = new KsTguiButton
        {
            Text = Loc.GetString("cargo-console-menu-account-action-button"),
            Icon = KsTguiIcons.Check,
            Color = KsTguiColor.Green,
            Fluid = true,
            Margin = new Thickness(0f, 6f, 0f, 0f),
        };
        _accountActionButton.OnPressed += _ =>
        {
            var account = SelectedAccount();
            OnAccountAction?.Invoke(account, (int)_transferSpinBox.Value);
        };
        fundsSection.AddChild(_accountActionButton);

        _limitToggleButton = new KsTguiButton
        {
            Text = Loc.GetString("cargo-console-menu-toggle-account-lock-button"),
            Icon = KsTguiIcons.Lock,
            Fluid = true,
            Margin = new Thickness(0f, 6f, 0f, 0f),
        };
        _limitToggleButton.OnPressed += _ => OnToggleUnboundedLimit?.Invoke();
        fundsSection.AddChild(_limitToggleButton);

        _fundsPage.AddChild(fundsSection);
        root.AddChild(_fundsPage);

        ContentsContainer.AddChild(root);

        SelectPage(Page.Catalog);
    }

    /// <summary>
    ///     The account picked in the action list; null for a withdrawal, which has none.
    /// </summary>
    private ProtoId<CargoAccountPrototype>? SelectedAccount()
    {
        if (_actionOptions.ItemCount == 0)
            return null;

        return (ProtoId<CargoAccountPrototype>?)_actionOptions.SelectedMetadata;
    }

    private bool IsTransferValid(int value)
    {
        if (!_orderConsoleQuery.TryComp(_owner, out var console) ||
            !_bankQuery.TryComp(_station, out var bank))
            return true;

        return value >= 0 && value <= (int)(console.TransferLimit * bank.Accounts[console.Account]);
    }

    private void SelectPage(Page page)
    {
        _page = page;
        _catalogPage.Visible = page == Page.Catalog;
        _requestsPage.Visible = page == Page.Requests;
        _checkoutPage.Visible = page == Page.Checkout;
        _fundsPage.Visible = page == Page.Funds;
        _clearButton.Visible = page == Page.Requests && HasPendingRequests();

        _tabsSection.Title = page switch
        {
            Page.Catalog => Loc.GetString("ks-tgui-cargo-tab-catalog"),
            Page.Requests => Loc.GetString("ks-tgui-cargo-tab-requests"),
            Page.Checkout => Loc.GetString("ks-tgui-cargo-tab-checkout"),
            _ => Loc.GetString("cargo-console-menu-tab-title-funds"),
        };
    }

    private bool HasPendingRequests()
    {
        return _orders.Any(order => !order.Approved);
    }

    /// <summary>
    ///     tgui's "Clear": denies every request.
    /// </summary>
    private void ClearRequests()
    {
        foreach (var order in _orders.Where(order => !order.Approved).ToList())
            OnOrderCanceled?.Invoke(order);
    }

    private IEnumerable<CargoProductPrototype> ProductPrototypes
    {
        get
        {
            var allowedGroups = _entityManager.GetComponentOrNull<CargoOrderConsoleComponent>(_owner)?.AllowedGroups;

            foreach (var cargoPrototypeId in ProductCatalogue)
            {
                var cargoPrototype = _protoManager.Index(cargoPrototypeId);

                if (!allowedGroups?.Contains(cargoPrototype.Group) ?? false)
                    continue;

                yield return cargoPrototype;
            }
        }
    }

    private void OnCategorySelected(KsTguiTabItem item)
    {
        var index = _categoryTabs.SelectedIndex;
        if (index < 0 || index >= _categoryStrings.Count)
            return;

        _category = index == 0 ? null : _categoryStrings[index];
        _searchBar.Text = "";
        PopulateProducts();
    }

    /// <summary>
    ///     Takes a new state from the console: the catalogue, the orders and the shuttle's capacity.
    /// </summary>
    public void UpdateState(CargoConsoleInterfaceState state, EntityUid station)
    {
        _station = station;
        _orders = state.Orders;
        _orderCount = state.Count;
        _orderWindow.Capacity = state.Capacity;
        ProductCatalogue = state.Products;

        _capacityItem.Text = Loc.GetString("cargo-console-menu-order-capacity-number", ("count", state.Count), ("capacity", state.Capacity));

        PopulateCategories();
        PopulateProducts();
        PopulateOrders();
        PopulateAccountActions();
    }

    private void PopulateCategories()
    {
        _categoryStrings.Clear();
        _categoryTabs.RemoveAllChildren();

        var counts = new Dictionary<string, int>();
        var total = 0;
        foreach (var prototype in ProductPrototypes)
        {
            var category = Loc.GetString(prototype.Category);
            counts[category] = counts.GetValueOrDefault(category) + 1;
            total++;
        }

        _categoryStrings.AddRange(counts.Keys.OrderBy(name => name));
        _categoryStrings.Insert(0, Loc.GetString("cargo-console-menu-populate-categories-all-text"));

        // The tabs are rebuilt under a selection that stays: the category is kept, and a vanished one falls back to all.
        var selected = _category == null ? 0 : _categoryStrings.IndexOf(_category);
        if (selected < 0)
        {
            _category = null;
            selected = 0;
        }

        for (var i = 0; i < _categoryStrings.Count; i++)
        {
            var count = i == 0 ? total : counts[_categoryStrings[i]];
            _categoryTabs.AddTab(_categoryStrings[i], count: count.ToString(), selected: i == selected);
        }
    }

    private void PopulateProducts()
    {
        _productsBox.RemoveAllChildren();
        var products = ProductPrototypes.ToList();
        products.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));

        // A search looks through every category, as the stock console's does.
        var search = _searchBar.Text.Trim().ToLowerInvariant();
        foreach (var prototype in products)
        {
            if (!(search.Length == 0 && _category == null ||
                  search.Length != 0 && prototype.Name.ToLowerInvariant().Contains(search) ||
                  search.Length != 0 && prototype.Description.ToLowerInvariant().Contains(search) ||
                  search.Length == 0 && _category != null && Loc.GetString(prototype.Category).Equals(_category)))
            {
                continue;
            }

            var cost = Loc.GetString("cargo-console-menu-points-amount", ("amount", prototype.Cost.ToString()));
            var row = new KsTguiCargoProductRow(prototype, _spriteSystem.Frame0(prototype.Icon), prototype.Name, cost)
            {
                ToolTip = prototype.Description,
            };
            var captured = prototype;
            row.OnPressed += _ => _orderWindow.Show(captured, captured.Name, captured.Description, cost, _defaultRequester);
            _productsBox.AddChild(row);
        }
    }

    private void PopulateOrders()
    {
        var pending = _orders.Where(order => !order.Approved && _protoManager.HasIndex(order.Product)).ToList();
        var approved = _orders.Where(order => order.Approved && _protoManager.HasIndex(order.Product)).ToList();

        _requestsTab.Count = pending.Count > 0 ? pending.Count.ToString() : null;
        _requestsTab.SelectedColor = pending.Count > 0 ? KsTguiColor.Yellow : KsTguiColor.Default;
        var itemCount = approved.Sum(order => order.OrderQuantity - order.NumDispatched);
        _checkoutTab.Count = itemCount > 0 ? itemCount.ToString() : null;
        _clearButton.Visible = _page == Page.Requests && pending.Count > 0;

        BuildOrderTable(_requestsBox, pending, requests: true);
        BuildOrderTable(_checkoutBox, approved, requests: false);
    }

    private void BuildOrderTable(BoxContainer into, List<CargoOrderData> orders, bool requests)
    {
        into.RemoveAllChildren();

        if (orders.Count == 0)
        {
            into.AddChild(new KsTguiNoticeBox
            {
                Text = Loc.GetString(requests ? "ks-tgui-cargo-no-requests" : "ks-tgui-cargo-empty-checkout"),
                NoticeColor = requests ? KsTguiNoticeColor.Success : KsTguiNoticeColor.Default,
            });
            return;
        }

        var orderConsole = _orderConsoleQuery.CompOrNull(_owner);
        var canApprove = requests && orderConsole != null && orderConsole.Mode != CargoOrderConsoleMode.SendToPrimary;
        var full = _orderCount >= (_orderWindow.Capacity);

        var table = new KsTguiTable { HorizontalExpand = true };
        var headerRow = new KsTguiTableRow { Header = true };
        table.AddChild(headerRow);
        AddHeader(headerRow, "ks-tgui-cargo-column-id", expand: false);
        AddHeader(headerRow, "ks-tgui-cargo-column-object", expand: true);
        AddHeader(headerRow, "ks-tgui-cargo-column-orderer", expand: false);
        AddHeader(headerRow, requests ? "ks-tgui-cargo-column-reason" : "ks-tgui-cargo-column-approver", expand: true);
        AddHeader(headerRow, "ks-tgui-cargo-column-account", expand: false);
        AddHeader(headerRow, "ks-tgui-cargo-column-cost", expand: false);
        AddHeader(headerRow, requests ? "ks-tgui-cargo-column-actions" : "ks-tgui-cargo-column-dispatched", expand: false);

        foreach (var order in orders)
        {
            // tgui stripes the rows of the cart and the request list, the header row counting as the first.
            var orderRow = new KsTguiTableRow { Candystripe = true };
            table.AddChild(orderRow);

            var productProto = _protoManager.Index(order.Product);
            var product = _protoManager.Index<EntityPrototype>(productProto.Product);
            var account = _protoManager.Index(order.Account);
            var requester = !string.IsNullOrEmpty(order.Requester)
                ? order.Requester
                : Loc.GetString("cargo-console-menu-order-row-alerts-requester-unknown");

            orderRow.AddChild(new Label { Text = $"#{order.OrderId}", FontColorOverride = KsTguiPalettes.Label });

            var objectCell = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4,
            };
            objectCell.AddChild(new TextureRect
            {
                Texture = _spriteSystem.Frame0(product),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                MinSize = new Vector2(24f, 24f),
                MaxSize = new Vector2(24f, 24f),
            });
            objectCell.AddChild(new Label
            {
                Text = $"{productProto.Name} x{order.OrderQuantity}",
                VerticalAlignment = Control.VAlignment.Center,
            });
            orderRow.AddChild(objectCell);

            orderRow.AddChild(new Label { Text = requester, FontColorOverride = XColor.White });

            var detail = requests ? order.Reason : order.Approver;
            var detailMessage = new FormattedMessage();
            detailMessage.AddText(!string.IsNullOrEmpty(detail)
                ? detail
                : requests ? Loc.GetString("cargo-console-menu-order-row-alerts-reason-absent") : "-");
            var detailLabel = new RichTextLabel { HorizontalExpand = true, Modulate = XColor.LightGray };
            detailLabel.SetMessage(detailMessage);
            orderRow.AddChild(detailLabel);

            orderRow.AddChild(new Label { Text = Loc.GetString(account.Code), FontColorOverride = account.Color });
            orderRow.AddChild(new Label
            {
                Text = Loc.GetString("cargo-console-menu-points-amount", ("amount", productProto.Cost * order.OrderQuantity)),
                FontColorOverride = Gold,
            });

            if (requests)
            {
                var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
                if (canApprove)
                {
                    var approve = new KsTguiButton
                    {
                        Icon = KsTguiIcons.Check,
                        Color = KsTguiColor.Good,
                        Disabled = full,
                        ToolTip = Loc.GetString("cargo-console-menu-order-row-button-approve"),
                    };
                    var approvedOrder = order;
                    approve.OnPressed += _ => OnOrderApproved?.Invoke(approvedOrder);
                    actions.AddChild(approve);
                }

                var deny = new KsTguiButton
                {
                    Icon = KsTguiIcons.Times,
                    Color = KsTguiColor.Bad,
                    ToolTip = Loc.GetString("cargo-console-menu-order-row-button-cancel"),
                };
                var deniedOrder = order;
                deny.OnPressed += _ => OnOrderCanceled?.Invoke(deniedOrder);
                actions.AddChild(deny);
                orderRow.AddChild(actions);
            }
            else
            {
                orderRow.AddChild(new Label { Text = $"{order.NumDispatched}/{order.OrderQuantity}" });
            }
        }

        into.AddChild(table);
    }

    private static void AddHeader(KsTguiTableRow headerRow, string key, bool expand)
    {
        headerRow.AddChild(new Label
        {
            Text = Loc.GetString(key),
            FontColorOverride = XColor.Gray,
            HorizontalExpand = expand,
        });
    }

    private void PopulateAccountActions()
    {
        if (!_bankQuery.TryComp(_station, out var bank) ||
            !_orderConsoleQuery.TryComp(_owner, out var console))
            return;

        var selectedAccount = SelectedAccount();
        var selectedId = _actionOptions.SelectedId;

        var i = 0;
        _actionOptions.Clear();
        _actionOptions.AddItem(Loc.GetString("cargo-console-menu-account-action-option-withdraw"), i);
        _actionOptions.SetItemMetadata(i, null!);
        i++;
        foreach (var account in bank.Accounts.Keys)
        {
            if (account == console.Account)
                continue;

            var accountProto = _protoManager.Index(account);
            _actionOptions.AddItem(Loc.GetString("cargo-console-menu-account-action-option-transfer", ("code", Loc.GetString(accountProto.Code))), i);
            _actionOptions.SetItemMetadata(i, account);
            i++;
        }

        // Keep the player's choice across the state updates that rebuild the list.
        if (selectedId > 0 && selectedId < i && Equals(_actionOptions.GetItemMetadata(selectedId), (object?)selectedAccount))
            _actionOptions.SelectId(selectedId);
        else
            _actionOptions.SelectId(0);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_bankQuery.TryComp(_station, out var bankAccount) ||
            !_orderConsoleQuery.TryComp(_owner, out var orderConsole))
        {
            return;
        }

        var balance = _cargoSystem.GetBalanceFromAccount((_station.Value, bankAccount), orderConsole.Account);
        _balanceLabel.Text = Loc.GetString("cargo-console-menu-points-amount", ("amount", balance));
        _transferLimitLabel.Text = Loc.GetString("ks-tgui-cargo-transfer-limit-value", ("limit", (int)(balance * orderConsole.TransferLimit)));

        _unlimitedLabel.Visible = orderConsole.TransferUnbounded;
        _transferSpinBox.MaxValue = (int)(orderConsole.TransferLimit * balance);
        _accountActionButton.Disabled = _transferSpinBox.Value <= 0 ||
                                        _transferSpinBox.Value > bankAccount.Accounts[orderConsole.Account] * orderConsole.TransferLimit ||
                                        _timing.CurTime < orderConsole.NextAccountActionTime;

        // A slip printer has no requests to approve and no orders to wait on.
        var printSlip = orderConsole.Mode == CargoOrderConsoleMode.PrintSlip;
        _requestsTab.Visible = !printSlip;
        _checkoutTab.Visible = !printSlip;
        if (printSlip && (_page == Page.Requests || _page == Page.Checkout))
        {
            _pageTabs.Select((KsTguiTabItem)_pageTabs.GetChild(0));
            SelectPage(Page.Catalog); // Select doesn't raise OnTabSelected.
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _orderWindow.Dispose();
    }
}
