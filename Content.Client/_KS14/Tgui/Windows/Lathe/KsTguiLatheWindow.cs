using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.Lathe;
using Content.Client._KS14.Tgui.Windows.Materials;
using Content.Client.Materials;
using Content.Client.UserInterface.Controls;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.Lathe;

/// <summary>
///     The lathe laid out as tgui's Fabricator: a category sidebar, the search and the designs, the build queue (which tg's
///         fabricator has no room for, so it takes a column of its own) and the material bar along the bottom. Every lathe
///         opens this when the tgui theme is on.
/// </summary>
public sealed partial class KsTguiLatheWindow : FancyWindow, IKsLatheWindow
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly SpriteSystem _spriteSystem;
    private readonly LatheSystem _lathe;
    private readonly MaterialStorageSystem _materialStorage;

    private readonly KsTguiTabs _categoryTabs;
    private readonly KsTguiTabItem _allCategoryTab;
    private readonly Dictionary<ProtoId<LatheCategoryPrototype>, KsTguiTabItem> _categoryTabItems = new();
    private readonly KsTguiButton _serverListButton;
    private readonly KsTguiSection _designsSection;
    private readonly LineEdit _searchBar;
    private readonly KsTguiNumberInput _amountInput;
    private readonly Label _recipeCount;
    private readonly BoxContainer _recipeList;
    private readonly BoxContainer _fabricatingContainer;
    private readonly BoxContainer _fabricatingDisplayContainer;
    private ProtoId<LatheRecipePrototype>? _fabricatingRecipe;
    private readonly Label _fabricatingName;
    private readonly KsTguiButton _deleteFabricating;
    private readonly BoxContainer _queueList;
    private readonly Label _queueEmptyLabel;
    private bool _isFabricating;
    private int _queuedBatches;
    private readonly KsTguiMaterialList _materialsList;

    public event Action<BaseButton.ButtonEventArgs>? OnServerListButtonPressed;
    public event Action<string, int>? RecipeQueueAction;
    public event Action<int>? QueueDeleteAction;
    public event Action<int>? QueueMoveUpAction;
    public event Action<int>? QueueMoveDownAction;
    public event Action? DeleteFabricatingAction;

    public List<ProtoId<LatheRecipePrototype>> Recipes = new();

    public List<ProtoId<LatheCategoryPrototype>>? Categories;

    public ProtoId<LatheCategoryPrototype>? CurrentCategory;

    public EntityUid Entity;

    public KsTguiLatheWindow()
    {
        IoCManager.InjectDependencies(this);

        _spriteSystem = _entityManager.System<SpriteSystem>();
        _lathe = _entityManager.System<LatheSystem>();
        _materialStorage = _entityManager.System<MaterialStorageSystem>();

        Title = Loc.GetString("lathe-menu-title");
        MinSize = new Vector2(760f, 450f);
        SetSize = new Vector2(940f, 600f);

        // Categories.
        _categoryTabs = new KsTguiTabs { Vertical = true };
        _allCategoryTab = _categoryTabs.AddTab(Loc.GetString("ks-tgui-lathe-all-designs"), selected: true);
        _categoryTabs.OnTabSelected += OnCategorySelected;

        _serverListButton = new KsTguiButton { Text = Loc.GetString("lathe-menu-server-list"), Icon = KsTguiIcons.Cog, Fluid = true };
        _serverListButton.OnPressed += args => OnServerListButtonPressed?.Invoke(args);

        var categoriesSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-lathe-categories"), Fill = true, Scrollable = true, MinWidth = 180 };
        categoriesSection.AddChild(_categoryTabs);
        var categoriesColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap, MinWidth = 180 };
        categoriesColumn.AddChild(categoriesSection);
        categoriesColumn.AddChild(_serverListButton);

        // Designs.
        _searchBar = new LineEdit { PlaceHolder = Loc.GetString("lathe-menu-search-designs"), HorizontalExpand = true };
        _searchBar.OnTextChanged += _ => PopulateRecipes();

        _amountInput = new KsTguiNumberInput
        {
            Format = "0", WidthPixels = 60f, MinValue = 1f, MaxValue = (float)LatheSystem.MaxItemsPerRequest, TickWhileDragging = true,
        };
        _amountInput.SetValue(1f);
        _amountInput.OnCommitted += _ => PopulateRecipes();

        _recipeCount = new Label { Align = Label.AlignMode.Right, HorizontalExpand = true };

        var searchRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        searchRow.AddChild(_searchBar);
        searchRow.AddChild(new Label { Text = Loc.GetString("lathe-menu-amount"), VerticalAlignment = VAlignment.Center });
        searchRow.AddChild(_amountInput);

        _recipeList = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };

        _designsSection = new KsTguiSection { Fill = true, HorizontalExpand = true };
        var designsBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, SeparationOverride = 6 };
        designsBody.AddChild(searchRow);
        designsBody.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _recipeList } });
        designsBody.AddChild(_recipeCount);
        _designsSection.AddChild(designsBody);

        // Queue.
        _deleteFabricating = new KsTguiButton
        {
            Icon = KsTguiIcons.Times,
            Color = KsTguiColor.Bad,
            Compact = true,
            ToolTip = Loc.GetString("lathe-menu-delete-fabricating-tooltip"),
        };
        _deleteFabricating.OnPressed += _ => DeleteFabricatingAction?.Invoke();

        _fabricatingDisplayContainer = new BoxContainer { MinSize = new Vector2(32f, 32f), Margin = new Thickness(0f, 0f, 4f, 0f), VerticalAlignment = VAlignment.Center };
        _fabricatingName = new Label { HorizontalExpand = true, VerticalAlignment = VAlignment.Center, ClipText = true };
        var fabricatingRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        fabricatingRow.AddChild(_fabricatingDisplayContainer);
        fabricatingRow.AddChild(_fabricatingName);
        fabricatingRow.AddChild(_deleteFabricating);

        _fabricatingContainer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, Margin = new Thickness(0f, 0f, 0f, 6f) };
        _fabricatingContainer.AddChild(new KsTguiNoticeBox { Text = Loc.GetString("lathe-menu-fabricating-message"), NoticeColor = KsTguiNoticeColor.Success });
        _fabricatingContainer.AddChild(fabricatingRow);

        _queueList = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        _queueEmptyLabel = new Label { Text = Loc.GetString("ks-tgui-lathe-queue-empty"), HorizontalAlignment = HAlignment.Center };

        var queueSection = new KsTguiSection { Title = Loc.GetString("lathe-menu-queue-title"), Fill = true, MinWidth = 250 };
        var queueBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };
        queueBody.AddChild(_fabricatingContainer);
        queueBody.AddChild(_queueEmptyLabel);
        queueBody.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _queueList } });
        queueSection.AddChild(queueBody);

        // Materials.
        _materialsList = new KsTguiMaterialList();
        var materialsSection = new KsTguiSection { Title = Loc.GetString("lathe-menu-materials-title") };
        materialsSection.AddChild(_materialsList);

        var topRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalExpand = true, SeparationOverride = KsTguiSection.Gap };
        topRow.AddChild(categoriesColumn);
        topRow.AddChild(_designsSection);
        topRow.AddChild(queueSection);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap, Margin = new Thickness(6f) };
        root.AddChild(topRow);
        root.AddChild(materialsSection);
        ContentsContainer.AddChild(root);

        RefreshDesignsTitle();
    }

    public void SetEntity(EntityUid uid)
    {
        Entity = uid;
        this.SetInfoFromEntity(_entityManager, Entity);

        if (_entityManager.TryGetComponent<LatheComponent>(Entity, out var latheComponent))
        {
            if (!latheComponent.DynamicPacks.Any())
                _serverListButton.Visible = false;

            _amountInput.SetValue(Math.Clamp(latheComponent.DefaultProductionAmount, 1, LatheSystem.MaxItemsPerRequest));
        }

        _materialsList.SetOwner(Entity);
    }

    public void SetRecipes(List<ProtoId<LatheRecipePrototype>> recipes)
    {
        Recipes = recipes;
    }

    private void OnCategorySelected(KsTguiTabItem item)
    {
        CurrentCategory = null;
        foreach (var (category, tabItem) in _categoryTabItems)
        {
            if (tabItem == item)
                CurrentCategory = category;
        }

        PopulateRecipes();
    }

    private void RefreshDesignsTitle()
    {
        var query = _searchBar.Text.Trim();
        if (query.Length > 0)
        {
            _designsSection.Title = Loc.GetString("ks-tgui-lathe-results", ("query", query));
            return;
        }

        _designsSection.Title = CurrentCategory is { } category && _prototypeManager.Resolve(category, out var categoryPrototype)
            ? Loc.GetString(categoryPrototype.Name)
            : Loc.GetString("ks-tgui-lathe-all-designs");
    }

    public void PopulateRecipes()
    {
        RefreshDesignsTitle();

        var recipesToShow = new List<LatheRecipePrototype>();
        var categoryCounts = new Dictionary<ProtoId<LatheCategoryPrototype>, int>();
        var search = _searchBar.Text.Trim().ToLowerInvariant();
        foreach (var recipe in Recipes)
        {
            if (!_prototypeManager.Resolve(recipe, out var proto))
                continue;

            foreach (var category in proto.Categories)
                categoryCounts[category] = categoryCounts.GetValueOrDefault(category) + 1;

            if (CurrentCategory != null)
            {
                if (proto.Categories.Count <= 0 || !proto.Categories.Any(category => category == CurrentCategory))
                    continue;
            }

            if (search.Length != 0 && !_lathe.GetRecipeName(recipe).ToLowerInvariant().Contains(search))
                continue;

            recipesToShow.Add(proto);
        }

        _allCategoryTab.Count = $"{Recipes.Count}";
        foreach (var (category, tabItem) in _categoryTabItems)
            tabItem.Count = $"{categoryCounts.GetValueOrDefault(category)}";

        var quantity = Math.Max(1, (int)_amountInput.Value);

        _recipeCount.Text = Loc.GetString("lathe-menu-recipe-count", ("count", recipesToShow.Count));

        var sortedRecipesToShow = recipesToShow.OrderBy(_lathe.GetRecipeName);

        var oldChildCount = _recipeList.ChildCount;
        _entityManager.TryGetComponent(Entity, out LatheComponent? lathe);

        var idx = 0;
        foreach (var prototype in sortedRecipesToShow)
        {
            var canProduce = _lathe.CanProduce(Entity, prototype, quantity, component: lathe);
            var canProduceFive = _lathe.CanProduce(Entity, prototype, 5, component: lathe);
            var canProduceTen = _lathe.CanProduce(Entity, prototype, 10, component: lathe);
            var tooltipFunction = () => GenerateTooltipText(prototype);

            KsTguiLatheRecipeRow row;
            if (idx >= oldChildCount)
            {
                row = new KsTguiLatheRecipeRow(_lathe);
                row.OnBuild += (recipeId, amount) =>
                {
                    if (amount <= 0)
                        amount = Math.Max(1, (int)_amountInput.Value);

                    RecipeQueueAction?.Invoke(recipeId, amount);
                };
                _recipeList.AddChild(row);
            }
            else
            {
                row = (KsTguiLatheRecipeRow)_recipeList.GetChild(idx);
            }

            row.SetRecipe(prototype, tooltipFunction, canProduce, canProduceFive, canProduceTen, () => GetRecipeDisplayControl(prototype));
            idx++;
        }

        for (var childIdx = oldChildCount - 1; idx <= childIdx; childIdx--)
        {
            _recipeList.RemoveChild(childIdx);
        }
    }

    private string GenerateTooltipText(LatheRecipePrototype prototype)
    {
        var sb = new StringBuilder();
        var multiplier = _entityManager.GetComponent<LatheComponent>(Entity).MaterialUseMultiplier;

        foreach (var (id, amount) in prototype.Materials)
        {
            if (!_prototypeManager.Resolve(id, out var proto))
                continue;

            var adjustedAmount = SharedLatheSystem.AdjustMaterial(amount, prototype.ApplyMaterialDiscount, multiplier);
            var sheetVolume = _materialStorage.GetSheetVolume(proto);

            var unit = Loc.GetString(proto.Unit);
            var sheets = adjustedAmount / (float)sheetVolume;

            var availableAmount = _materialStorage.GetMaterialAmount(Entity, id);
            var missingAmount = Math.Max(0, adjustedAmount - availableAmount);
            var missingSheets = missingAmount / (float)sheetVolume;

            var name = Loc.GetString(proto.Name);

            string tooltipText;
            if (missingSheets > 0)
            {
                tooltipText = Loc.GetString("lathe-menu-material-amount-missing", ("amount", sheets), ("missingAmount", missingSheets), ("unit", unit), ("material", name));
            }
            else
            {
                var amountText = Loc.GetString("lathe-menu-material-amount", ("amount", sheets), ("unit", unit));
                tooltipText = Loc.GetString("lathe-menu-tooltip-display", ("material", name), ("amount", amountText));
            }

            sb.AppendLine(tooltipText);
        }

        var desc = _lathe.GetRecipeDescription(prototype);
        if (!string.IsNullOrWhiteSpace(desc))
            sb.AppendLine(Loc.GetString("lathe-menu-description-display", ("description", desc)));

        if (sb.Length > 0)
            sb.Remove(sb.Length - 1, 1);

        return sb.ToString();
    }

    public void UpdateCategories()
    {
        var currentCategories = new List<ProtoId<LatheCategoryPrototype>>();
        foreach (var recipeId in Recipes)
        {
            var recipe = _prototypeManager.Index(recipeId);
            foreach (var category in recipe.Categories)
            {
                if (!currentCategories.Contains(category))
                    currentCategories.Add(category);
            }
        }

        if (Categories != null && Categories.Count == currentCategories.Count && Categories.All(currentCategories.Contains))
            return;

        Categories = currentCategories;

        // A category that is gone from the recipes can't stay the filter.
        if (CurrentCategory is { } selected && !currentCategories.Contains(selected))
            CurrentCategory = null;

        foreach (var tabItem in _categoryTabItems.Values)
            _categoryTabs.RemoveChild(tabItem);

        _categoryTabItems.Clear();

        var sortedCategories = currentCategories
            .Select(category => _prototypeManager.Index(category))
            .OrderBy(category => Loc.GetString(category.Name));

        foreach (var category in sortedCategories)
        {
            var tabItem = _categoryTabs.AddTab(Loc.GetString(category.Name));
            _categoryTabItems[category.ID] = tabItem;
        }

        _categoryTabs.Select(CurrentCategory is { } current ? _categoryTabItems[current] : _allCategoryTab);
        PopulateRecipes();
    }

    public void PopulateQueueList(IReadOnlyCollection<LatheRecipeBatch> queue)
    {
        _queuedBatches = queue.Count;
        RefreshQueueEmptyLabel();

        var oldChildCount = _queueList.ChildCount;

        var idx = 0;
        foreach (var batch in queue)
        {
            var recipe = _prototypeManager.Index(batch.Recipe);

            var itemName = _lathe.GetRecipeName(batch.Recipe);
            string displayText;
            if (batch.ItemsRequested > 1)
                displayText = Loc.GetString("lathe-menu-item-batch", ("index", idx + 1), ("name", itemName), ("printed", batch.ItemsPrinted), ("total", batch.ItemsRequested));
            else
                displayText = Loc.GetString("lathe-menu-item-single", ("index", idx + 1), ("name", itemName));

            if (idx >= oldChildCount)
            {
                var queuedRecipeBox = new KsTguiLatheQueueRow(displayText, idx, GetRecipeDisplayControl(recipe));
                queuedRecipeBox.TakeRecipe(batch.Recipe);
                queuedRecipeBox.OnDeletePressed += index => QueueDeleteAction?.Invoke(index);
                queuedRecipeBox.OnMoveUpPressed += index => QueueMoveUpAction?.Invoke(index);
                queuedRecipeBox.OnMoveDownPressed += index => QueueMoveDownAction?.Invoke(index);
                _queueList.AddChild(queuedRecipeBox);
            }
            else
            {
                var child = (KsTguiLatheQueueRow)_queueList.GetChild(idx);
                child.SetDisplayText(displayText);
                child.SetIndex(idx);
                if (child.TakeRecipe(batch.Recipe))
                    child.SetDisplayControl(GetRecipeDisplayControl(recipe));
            }

            idx++;
        }

        for (var childIdx = oldChildCount - 1; idx <= childIdx; childIdx--)
        {
            _queueList.RemoveChild(childIdx);
        }
    }

    /// <summary>
    ///     The notice is only true when the lathe is neither printing something nor holding a queue.
    /// </summary>
    private void RefreshQueueEmptyLabel()
    {
        _queueEmptyLabel.Visible = !_isFabricating && _queuedBatches == 0;
    }

    public void SetQueueInfo(ProtoId<LatheRecipePrototype>? recipeProto)
    {
        _fabricatingContainer.Visible = recipeProto != null;
        _isFabricating = recipeProto != null;
        RefreshQueueEmptyLabel();
        if (recipeProto == null)
            return;

        var recipe = _prototypeManager.Index(recipeProto.Value);

        // Only when it changes: this runs on every state, and a new icon is a new entity.
        if (_fabricatingRecipe != recipeProto)
        {
            _fabricatingRecipe = recipeProto;
            _fabricatingDisplayContainer.Children.Clear();
            _fabricatingDisplayContainer.AddChild(GetRecipeDisplayControl(recipe));
        }

        _fabricatingName.Text = _lathe.GetRecipeName(recipe);
    }

    private Control GetRecipeDisplayControl(LatheRecipePrototype recipe)
    {
        if (recipe.Icon != null)
            return new TextureRect { Texture = _spriteSystem.Frame0(recipe.Icon) };

        if (recipe.Result is { } result)
        {
            var entProtoView = new EntityPrototypeView();
            entProtoView.SetPrototype(result);
            return entProtoView;
        }

        return new Control();
    }
}
