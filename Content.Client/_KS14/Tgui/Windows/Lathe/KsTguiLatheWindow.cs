using System.Linq;
using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Shared._KS14.Tgui;
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
///     The lathe in tgui's design browser: a category sidebar, the search and the designs, and the build queue, which tg has no
///         equivalent of. Every lathe opens this when the tgui theme is on, in one of two layouts:
///         <list type="bullet">
///             <item>tg's Fabricator, for every lathe but the autolathe: the browser with the queue in a column of its own,
///                 and the material bar along the bottom.</item>
///             <item>tg's Autolathe, for a lathe with <see cref="KsTguiAutolatheLayoutComponent"/>: a Total Materials section on
///                 top, then the browser, 670 wide as tg's window is. The queue goes under the designs, in a section that is
///                 there only while something is printing or waiting, so the designs keep the width.</item>
///         </list>
/// </summary>
public sealed partial class KsTguiLatheWindow : FancyWindow, IKsLatheWindow
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly SpriteSystem _spriteSystem;
    private readonly LatheSystem _lathe;
    private readonly MaterialStorageSystem _materialStorage;

    private readonly BoxContainer _categoryTabs;
    private readonly BoxContainer _categoriesColumn;
    private readonly KsTguiSection _queueSection;
    private readonly KsTguiSection _materialsSection;
    private readonly KsTguiLatheMaterialSummary _materialSummary;
    private bool _autolatheLayout;
    private readonly KsTguiFabricatorTab _allCategoryTab;
    private readonly Dictionary<ProtoId<LatheCategoryPrototype>, KsTguiFabricatorTab> _categoryTabItems = new();
    private readonly KsTguiButton _serverListButton;
    private readonly KsTguiSection _designsSection;
    private readonly KsTguiSearchBar _searchBar;
    private readonly KsTguiSection _searchSection;
    private readonly KsTguiSection _recipeListSection;
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

        // Categories.
        _categoryTabs = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _allCategoryTab = AddCategoryTab(Loc.GetString("ks-tgui-lathe-all-designs"));
        _allCategoryTab.Selected = true;

        _serverListButton = new KsTguiButton { Text = Loc.GetString("lathe-menu-server-list"), Icon = KsTguiIcons.Cog, Fluid = true };
        _serverListButton.OnPressed += args => OnServerListButtonPressed?.Invoke(args);

        var categoriesSection = new KsTguiSection { Title = Loc.GetString("ks-tgui-lathe-categories"), Fill = true, MinWidth = 180 };
        // Not the section's own Scrollable: that reports the whole content as its height, so a long list could not shrink and
        //      would push the button under it out of the window. A scroll container reports nothing and takes what is left.
        categoriesSection.AddChild(new KsTguiScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _categoryTabs } });
        _categoriesColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap, MinWidth = 180 };
        _categoriesColumn.AddChild(categoriesSection);
        _categoriesColumn.AddChild(_serverListButton);

        // Designs, as DesignBrowser.tsx: the titled section holds the search in a section of its own and the designs in
        //      another, which scrolls.
        _searchBar = new KsTguiSearchBar { PlaceHolder = Loc.GetString("ks-tgui-lathe-search") };
        _searchBar.Input.OnTextChanged += _ => PopulateRecipes();
        _searchSection = new KsTguiSection();
        _searchSection.AddChild(_searchBar);

        _recipeList = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        // The list is the flexible part: a scroll container asks for no height of its own and takes what the rest leaves.
        _recipeListSection = new KsTguiSection { Fill = true };
        _recipeListSection.AddChild(new KsTguiScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _recipeList } });

        _designsSection = new KsTguiSection { Fill = true, HorizontalExpand = true };
        var designsBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true, SeparationOverride = KsTguiSection.Gap };
        // Each is in a Stack.Item of its own in tg, so each is the first child of its parent: both are pulled up 6px
        //      (.Section .Section:first-child), which is what brings the gap to ~16px (7.92 + 6 + 7.92 - 6) rather than 22.
        designsBody.AddChild(StackItem(_searchSection, expand: false));
        designsBody.AddChild(StackItem(_recipeListSection, expand: true));
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

        _queueSection = new KsTguiSection { Title = Loc.GetString("lathe-menu-queue-title") };
        var queueBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };
        queueBody.AddChild(_fabricatingContainer);
        queueBody.AddChild(_queueEmptyLabel);
        queueBody.AddChild(new KsTguiScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _queueList } });
        _queueSection.AddChild(queueBody);

        // Materials: the Fabricator's bar along the bottom, or the Autolathe's Total Materials section on top.
        _materialsList = new KsTguiMaterialList();
        _materialsSection = new KsTguiSection { Title = Loc.GetString("lathe-menu-materials-title") };
        _materialsSection.AddChild(_materialsList);
        _materialSummary = new KsTguiLatheMaterialSummary();

        ApplyLayout(autolathe: false);
        RefreshDesignsTitle();
    }

    /// <summary>
    ///     tg's <c>Stack.Item</c>: a box of its own around one child, so the child is its first child.
    /// </summary>
    private static BoxContainer StackItem(Control child, bool expand)
    {
        var item = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = expand };
        item.AddChild(child);
        return item;
    }

    /// <summary>
    ///     What the Autolathe layout's queue section keeps: the print notice, a row or two and the section around them.
    /// </summary>
    private const float QueueMinHeight = 150f;

    /// <summary>
    ///     Which of the two layouts the window is in.
    /// </summary>
    public bool IsAutolatheLayout => _autolatheLayout;

    /// <summary>
    ///     The designs section's title: the search text as tg's <c>Results for "..."</c>, else the category.
    /// </summary>
    public string? DesignsTitle => _designsSection.Title;

    /// <summary>
    ///     Whether the build queue section is on show. The Autolathe layout hides it while the queue is empty.
    /// </summary>
    public bool QueueShown => _queueSection.Visible;

    /// <summary>
    ///     Arranges the controls the window is made of as one of its two layouts. They are kept, so this can be run again when
    ///         the machine turns out to be an autolathe.
    /// </summary>
    private void ApplyLayout(bool autolathe)
    {
        _autolatheLayout = autolathe;

        ContentsContainer.RemoveAllChildren();
        foreach (var control in new Control[] { _categoriesColumn, _designsSection, _queueSection, _materialsSection, _materialSummary })
        {
            if (control.Parent != null)
                control.Orphan();
        }

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap, Margin = new Thickness(6f) };

        if (autolathe)
        {
            // Autolathe.tsx is 670x600. The queue sits under the designs, so they keep the width: beside them they would be left
            //      with less than half of it. It is only there while it has something to show.
            MinSize = new Vector2(670f, 450f);
            SetSize = new Vector2(670f, 600f);

            _queueSection.Fill = false;
            _queueSection.MinWidth = 0f;
            _queueSection.MinHeight = QueueMinHeight;
            _queueSection.Visible = _isFabricating || _queuedBatches > 0;

            var designsColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, SeparationOverride = KsTguiSection.Gap };
            designsColumn.AddChild(_designsSection);
            designsColumn.AddChild(_queueSection);

            var browser = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalExpand = true, SeparationOverride = KsTguiSection.Gap };
            browser.AddChild(_categoriesColumn);
            browser.AddChild(designsColumn);

            root.AddChild(_materialSummary);
            root.AddChild(browser);
        }
        else
        {
            MinSize = new Vector2(760f, 450f);
            SetSize = new Vector2(940f, 600f);

            _queueSection.Fill = true;
            _queueSection.MinWidth = 250f;
            _queueSection.MinHeight = 0f;
            _queueSection.Visible = true;

            var topRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, VerticalExpand = true, SeparationOverride = KsTguiSection.Gap };
            topRow.AddChild(_categoriesColumn);
            topRow.AddChild(_designsSection);
            topRow.AddChild(_queueSection);

            root.AddChild(topRow);
            root.AddChild(_materialsSection);
        }

        ContentsContainer.AddChild(root);
    }

    public void SetEntity(EntityUid uid)
    {
        Entity = uid;
        this.SetInfoFromEntity(_entityManager, Entity);

        if (_entityManager.TryGetComponent<LatheComponent>(Entity, out var latheComponent))
        {
            if (!latheComponent.DynamicPacks.Any())
                _serverListButton.Visible = false;
        }

        var autolathe = _entityManager.HasComponent<KsTguiAutolatheLayoutComponent>(Entity);
        if (autolathe != _autolatheLayout)
            ApplyLayout(autolathe);

        _materialsList.SetOwner(Entity);
        _materialSummary.SetOwner(Entity);
    }

    public void SetRecipes(List<ProtoId<LatheRecipePrototype>> recipes)
    {
        Recipes = recipes;
    }

    private KsTguiFabricatorTab AddCategoryTab(string name)
    {
        var tab = new KsTguiFabricatorTab { Text = name };
        tab.OnPressed += _ => OnCategorySelected(tab);
        _categoryTabs.AddChild(tab);
        return tab;
    }

    private void SelectCategoryTab(KsTguiFabricatorTab selected)
    {
        foreach (var child in _categoryTabs.Children)
        {
            if (child is KsTguiFabricatorTab tab)
                tab.Selected = tab == selected;
        }
    }

    private void OnCategorySelected(KsTguiFabricatorTab item)
    {
        // tg's onCategorySelected: the category it already shows is left alone, and a new one clears the search.
        if (item.Selected)
            return;

        SelectCategoryTab(item);
        CurrentCategory = null;
        foreach (var (category, tabItem) in _categoryTabItems)
        {
            if (tabItem == item)
                CurrentCategory = category;
        }

        _searchBar.Input.SetText(string.Empty, invokeEvent: false);
        PopulateRecipes();
    }

    private void RefreshDesignsTitle()
    {
        var query = _searchBar.Input.Text;
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
        // tg searches every design whatever the category, by the text as typed.
        var search = _searchBar.Input.Text.ToLowerInvariant();
        foreach (var recipe in Recipes)
        {
            if (!_prototypeManager.Resolve(recipe, out var proto))
                continue;

            foreach (var category in proto.Categories)
                categoryCounts[category] = categoryCounts.GetValueOrDefault(category) + 1;

            if (CurrentCategory != null && search.Length == 0)
            {
                if (proto.Categories.Count <= 0 || !proto.Categories.Any(category => category == CurrentCategory))
                    continue;
            }

            if (search.Length != 0 && !_lathe.GetRecipeName(recipe).ToLowerInvariant().Contains(search))
                continue;

            recipesToShow.Add(proto);
        }

        _allCategoryTab.Count = Recipes.Count;
        foreach (var (category, tabItem) in _categoryTabItems)
            tabItem.Count = categoryCounts.GetValueOrDefault(category);

        const int quantity = 1;

        var sortedRecipesToShow = recipesToShow.OrderBy(_lathe.GetRecipeName);

        var oldChildCount = _recipeList.ChildCount;
        _entityManager.TryGetComponent(Entity, out LatheComponent? lathe);

        var idx = 0;
        foreach (var prototype in sortedRecipesToShow)
        {
            var canProduce = _lathe.CanProduce(Entity, prototype, quantity, component: lathe);
            var canProduceFive = _lathe.CanProduce(Entity, prototype, 5, component: lathe);
            var canProduceTen = _lathe.CanProduce(Entity, prototype, 10, component: lathe);
            var costs = (Func<int, IReadOnlyList<KsTguiMaterialCost>>)(amount => MaterialCosts(prototype, amount));

            KsTguiLatheRecipeRow row;
            if (idx >= oldChildCount)
            {
                row = new KsTguiLatheRecipeRow(_lathe);
                row.OnBuild += (recipeId, amount) => RecipeQueueAction?.Invoke(recipeId, amount);
                _recipeList.AddChild(row);
            }
            else
            {
                row = (KsTguiLatheRecipeRow)_recipeList.GetChild(idx);
            }

            row.SetRecipe(prototype, costs, canProduce, canProduceFive, canProduceTen, MaxPrints(prototype), () => GetRecipeDisplayControl(prototype));
            idx++;
        }

        for (var childIdx = oldChildCount - 1; idx <= childIdx; childIdx--)
        {
            _recipeList.RemoveChild(childIdx);
        }
    }

    /// <summary>
    ///     tg's <c>maxmult</c>: how many prints of a design the machine's materials allow, capped. tg's customMaterials branch has
    ///         no equivalent, as a design here always names its materials.
    /// </summary>
    private int MaxPrints(LatheRecipePrototype prototype)
    {
        if (!_entityManager.TryGetComponent<LatheComponent>(Entity, out var latheComponent))
            return 0;

        var most = KsTguiLatheRecipeRow.MaxPrintsCap;
        foreach (var (id, perPrint) in prototype.Materials)
        {
            var needed = SharedLatheSystem.AdjustMaterial(perPrint, prototype.ApplyMaterialDiscount, latheComponent.MaterialUseMultiplier);
            if (needed <= 0)
                continue;

            most = Math.Min(most, _materialStorage.GetMaterialAmount(Entity, id) / needed);
        }

        return most;
    }

    /// <summary>
    ///     What <paramref name="amount"/> prints of a design take, for its tooltip, as tg's <c>MaterialCostSequence</c>.
    /// </summary>
    private IReadOnlyList<KsTguiMaterialCost> MaterialCosts(LatheRecipePrototype prototype, int amount)
    {
        var costs = new List<KsTguiMaterialCost>();
        if (!_entityManager.TryGetComponent<LatheComponent>(Entity, out var latheComponent))
            return costs;

        foreach (var (id, perPrint) in prototype.Materials)
        {
            if (!_prototypeManager.Resolve(id, out var material))
                continue;

            var needed = SharedLatheSystem.AdjustMaterial(perPrint, prototype.ApplyMaterialDiscount, latheComponent.MaterialUseMultiplier) * amount;
            costs.Add(new KsTguiMaterialCost(
                _spriteSystem.Frame0(material.Icon),
                needed,
                _materialStorage.GetMaterialAmount(Entity, id),
                _materialStorage.GetSheetVolume(material)));
        }

        return costs;
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
            var tabItem = AddCategoryTab(Loc.GetString(category.Name));
            _categoryTabItems[category.ID] = tabItem;
        }

        SelectCategoryTab(CurrentCategory is { } current ? _categoryTabItems[current] : _allCategoryTab);
        PopulateRecipes();
    }

    public void PopulateQueueList(IReadOnlyCollection<LatheRecipeBatch> queue)
    {
        _queuedBatches = queue.Count;
        RefreshQueueEmptyLabel();
        RefreshQueueSection();

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
    private void RefreshQueueSection()
    {
        // The Autolathe layout shows the queue only while it has something in it; the other has a column for it.
        if (_autolatheLayout)
            _queueSection.Visible = _isFabricating || _queuedBatches > 0;
    }

    private void RefreshQueueEmptyLabel()
    {
        _queueEmptyLabel.Visible = !_isFabricating && _queuedBatches == 0;
    }

    public void SetQueueInfo(ProtoId<LatheRecipePrototype>? recipeProto)
    {
        _fabricatingContainer.Visible = recipeProto != null;
        _isFabricating = recipeProto != null;
        RefreshQueueEmptyLabel();
        RefreshQueueSection();
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
