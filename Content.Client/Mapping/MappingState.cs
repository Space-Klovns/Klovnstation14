using System.Linq;
using System.Numerics;
using Content.Client.Administration.Managers;
using Content.Client.ContextMenu.UI;
using Content.Client.Decals;
using Content.Client.Gameplay;
using Content.Client.Maps; // KS14
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Client.Verbs;
using Content.Shared.Administration;
using Content.Shared.Decals;
using Content.Shared.Input;
using Content.Shared.Maps;
using Robust.Client.Console; // KS14
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components; // KS14
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Timing;
// using static System.StringComparison; // KS14: removed
// using static Robust.Client.UserInterface.Controls.LineEdit; // KS14: removed
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;
using static Robust.Client.UserInterface.Controls.OptionButton;
using static Robust.Shared.Input.Binding.PointerInputCmdHandler;
using Vector2 = System.Numerics.Vector2; // KS14

namespace Content.Client.Mapping;

public sealed partial class MappingState : GameplayStateBase
{
    [Dependency] private IClientAdminManager _admin = default!;
    [Dependency] private IClientConsoleHost _consoleHost = default!; // KS14: upstream PR #34302 port
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IEntityNetworkManager _entityNetwork = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IMapManager _mapMan = default!;
    [Dependency] private MappingManager _mapping = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILocalizationManager _localization = default!; // KS14: mapping editor overhaul port

    private EntityMenuUIController _entityMenuController = default!;

    private DecalPlacementSystem _decal = default!;
    private SpriteSystem _sprite = default!;
    private TransformSystem _transform = default!;
    private VerbSystem _verbs = default!;
// KS14 start: mapping editor overhaul port
    private MapSystem _map = default!;

    // 1 off in case something else uses these colors since we use them to compare
    private static readonly Color PickColor = new(1, 255, 0);
    private static readonly Color DeleteColor = new(255, 1, 0);
    private static readonly Color EraseDecalColor = Color.Red.WithAlpha(0.2f);
// KS14 end

    private readonly ISawmill _sawmill;
    private readonly GameplayStateLoadController _loadController;
    private bool _setup;
    private readonly Dictionary<Type, List<MappingPrototype>> _allPrototypes = new(); // KS14: mapping editor overhaul port
    private readonly Dictionary<IPrototype, MappingPrototype> _allPrototypesDict = new();
    private readonly Dictionary<Type, Dictionary<string, MappingPrototype>> _idDict = new();
    private (TimeSpan At, MappingSpawnButton Button)? _lastClicked;
// KS14 start: mapping editor overhaul port
    private (Control, MappingPrototypeList)? _scrollTo;
    private bool _tileErase;
// KS14 end

    private MappingScreen Screen => (MappingScreen) UserInterfaceManager.ActiveScreen!; // KS14: mapping editor overhaul port
    private MainViewport Viewport => UserInterfaceManager.ActiveScreen!.GetWidget<MainViewport>()!;

    public CursorMeta Meta { get; } // KS14: mapping editor overhaul port

    public MappingState()
    {
        IoCManager.InjectDependencies(this);

        _sawmill = _log.GetSawmill("mapping");
        _loadController = UserInterfaceManager.GetUIController<GameplayStateLoadController>();

        Meta = new CursorMeta(); // KS14
    }

    protected override void Startup()
    {
        EnsureSetup();
        base.Startup();

        UserInterfaceManager.LoadScreen<MappingScreen>();
        _loadController.LoadScreen();

        var context = _input.Contexts.GetContext("common");
        context.AddFunction(ContentKeyFunctions.MappingUnselect);
        context.AddFunction(ContentKeyFunctions.SaveMap);
        context.AddFunction(ContentKeyFunctions.MappingEnablePick);
        context.AddFunction(ContentKeyFunctions.MappingEnableDelete);
        context.AddFunction(ContentKeyFunctions.MappingPick);
        context.AddFunction(ContentKeyFunctions.MappingRemoveDecal);
        context.AddFunction(ContentKeyFunctions.MappingCancelEraseDecal);
        context.AddFunction(ContentKeyFunctions.MappingOpenContextMenu);
        context.AddFunction(ContentKeyFunctions.MouseMiddle); // KS14: mapping editor overhaul port

        Screen.DecalSystem = _decal;
// KS14 start: mapping editor overhaul port

        Screen.Entities.GetPrototypeData += OnGetData;
        Screen.Entities.SelectionChanged += OnSelected;
        Screen.Tiles.GetPrototypeData += OnGetData;
        Screen.Tiles.SelectionChanged += OnSelected;
        Screen.Decals.GetPrototypeData += OnGetData;
        Screen.Decals.SelectionChanged += OnSelected;

// KS14 end
        Screen.Pick.OnPressed += OnPickPressed;
        Screen.EntityReplaceButton.OnToggled += OnEntityReplacePressed;
        Screen.EntityPlacementMode.OnItemSelected += OnEntityPlacementSelected;
        Screen.EraseEntityButton.OnToggled += OnEraseEntityPressed;
        Screen.EraseTileButton.OnToggled += OnEraseTilePressed; // KS14: mapping editor overhaul port
        Screen.EraseDecalButton.OnToggled += OnEraseDecalPressed;
        // KS14 start: port supported mapping toolbar actions from upstream PR #34302
        Screen.FixGridAtmos.OnPressed += OnFixGridAtmosPressed;
        Screen.RemoveGrid.OnPressed += OnRemoveGridPressed;
        Screen.MoveGrid.OnPressed += OnMoveGridPressed;
        Screen.GridVV.OnPressed += OnGridVVPressed;
        // KS14 end
        _placement.PlacementChanged += OnPlacementChanged;
        _mapping.OnFavoritePrototypesLoaded += OnFavoritesLoaded; // KS14: mapping editor overhaul port

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.MappingUnselect, new PointerInputCmdHandler(HandleMappingUnselect, outsidePrediction: true))
            .Bind(ContentKeyFunctions.SaveMap, new PointerInputCmdHandler(HandleSaveMap, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEnablePick, new PointerStateInputCmdHandler(HandleEnablePick, HandleDisablePick, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingEnableDelete, new PointerStateInputCmdHandler(HandleEnableDelete, HandleDisableDelete, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingPick, new PointerInputCmdHandler(HandlePick, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingRemoveDecal, new PointerInputCmdHandler(HandleEditorCancelPlace, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingCancelEraseDecal, new PointerInputCmdHandler(HandleCancelEraseDecal, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MappingOpenContextMenu, new PointerInputCmdHandler(HandleOpenContextMenu, outsidePrediction: true))
            .Bind(ContentKeyFunctions.MouseMiddle, new PointerInputCmdHandler(HandleMouseMiddle, outsidePrediction: true)) // KS14: mapping editor overhaul port
            .Bind(Robust.Shared.Input.EngineKeyFunctions.Use, new PointerInputCmdHandler(HandleUse, outsidePrediction: true)) // KS14: upstream PR #34302 port
            .Register<MappingState>();

        _overlays.AddOverlay(new MappingOverlay(this));

        _prototypeManager.PrototypesReloaded += OnPrototypesReloaded;

        _mapping.LoadFavorites(); // KS14: mapping editor overhaul port
        ReloadPrototypes();
        UpdateLocale(); // KS14: mapping editor overhaul port
    }

    protected override void Shutdown()
    {
        SaveFavorites(); // KS14: mapping editor overhaul port
        CommandBinds.Unregister<MappingState>();

// KS14 start: mapping editor overhaul port
        Screen.Entities.GetPrototypeData -= OnGetData;
        Screen.Entities.SelectionChanged -= OnSelected;
        Screen.Tiles.GetPrototypeData -= OnGetData;
        Screen.Tiles.SelectionChanged -= OnSelected;
        Screen.Decals.GetPrototypeData -= OnGetData;
        Screen.Decals.SelectionChanged -= OnSelected;

// KS14 end
        Screen.Pick.OnPressed -= OnPickPressed;
        Screen.EntityReplaceButton.OnToggled -= OnEntityReplacePressed;
        Screen.EntityPlacementMode.OnItemSelected -= OnEntityPlacementSelected;
        Screen.EraseEntityButton.OnToggled -= OnEraseEntityPressed;
        Screen.EraseTileButton.OnToggled -= OnEraseTilePressed; // KS14: mapping editor overhaul port
        Screen.EraseDecalButton.OnToggled -= OnEraseDecalPressed;
        // KS14 start: port supported mapping toolbar actions from upstream PR #34302
        Screen.FixGridAtmos.OnPressed -= OnFixGridAtmosPressed;
        Screen.RemoveGrid.OnPressed -= OnRemoveGridPressed;
        Screen.MoveGrid.OnPressed -= OnMoveGridPressed;
        Screen.GridVV.OnPressed -= OnGridVVPressed;
        // KS14 end
        _placement.PlacementChanged -= OnPlacementChanged;
        _prototypeManager.PrototypesReloaded -= OnPrototypesReloaded;
        _mapping.OnFavoritePrototypesLoaded -= OnFavoritesLoaded; // KS14: mapping editor overhaul port

        UserInterfaceManager.ClearWindows();
        _loadController.UnloadScreen();
        UserInterfaceManager.UnloadScreen();

        var context = _input.Contexts.GetContext("common");
        context.RemoveFunction(ContentKeyFunctions.MappingUnselect);
        context.RemoveFunction(ContentKeyFunctions.SaveMap);
        context.RemoveFunction(ContentKeyFunctions.MappingEnablePick);
        context.RemoveFunction(ContentKeyFunctions.MappingEnableDelete);
        context.RemoveFunction(ContentKeyFunctions.MappingPick);
        context.RemoveFunction(ContentKeyFunctions.MappingRemoveDecal);
        context.RemoveFunction(ContentKeyFunctions.MappingCancelEraseDecal);
        context.RemoveFunction(ContentKeyFunctions.MappingOpenContextMenu);

        _overlays.RemoveOverlay<MappingOverlay>();

        base.Shutdown();
    }

    private void EnsureSetup()
    {
        if (_setup)
            return;

        _setup = true;

        _entityMenuController = UserInterfaceManager.GetUIController<EntityMenuUIController>();

        _decal = _entityManager.System<DecalPlacementSystem>();
        _sprite = _entityManager.System<SpriteSystem>();
        _transform = _entityManager.System<TransformSystem>();
        _verbs = _entityManager.System<VerbSystem>();
        _map = _entityManager.System<MapSystem>(); // KS14: mapping editor overhaul port
    }

    private void UpdateLocale() // KS14: mapping editor overhaul port
    {
// KS14 start: mapping editor overhaul port
        if (_input.TryGetKeyBinding(ContentKeyFunctions.MappingEnablePick, out var enablePickBinding))
            Screen.Pick.ToolTip = Loc.GetString("mapping-pick-tooltip", ("key", enablePickBinding.GetKeyString()));
// KS14 end

// KS14 start: mapping editor overhaul port
        if (_input.TryGetKeyBinding(ContentKeyFunctions.MappingEnableDelete, out var enableDeleteBinding))
            Screen.EraseEntityButton.ToolTip = Loc.GetString("mapping-erase-entity-tooltip", ("key", enableDeleteBinding.GetKeyString()));
    }

    private void SaveFavorites()
    {
        Screen.Entities.FavoritesPrototype.Children ??= new List<MappingPrototype>();
        Screen.Tiles.FavoritesPrototype.Children ??= new List<MappingPrototype>();
        Screen.Decals.FavoritesPrototype.Children ??= new List<MappingPrototype>();

        var children = Screen.Entities.FavoritesPrototype.Children
            .Union(Screen.Tiles.FavoritesPrototype.Children)
            .Union(Screen.Decals.FavoritesPrototype.Children)
            .ToList();

        _mapping.SaveFavorites(children);
    }

    private void ReloadPrototypes()
    {
// KS14 end
        var mappings = new Dictionary<string, MappingPrototype>();
        var entities = new MappingPrototype(null, Loc.GetString("mapping-entities")) { Children = new List<MappingPrototype>() }; // KS14: mapping editor overhaul port
        foreach (var entity in _prototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
// KS14 start: mapping editor overhaul port
            if (!entity.HideSpawnMenu)
                Register(entity, entity.ID, entities);
// KS14 end
        }

        Sort(mappings, entities);
        mappings.Clear();
        var tiles = new MappingPrototype(null, Loc.GetString("mapping-tiles")) { Children = new List<MappingPrototype>() };
        foreach (var tile in _prototypeManager.EnumeratePrototypes<ContentTileDefinition>())
        {
            Register(tile, tile.ID, tiles);
        }

        Sort(mappings, tiles);
        mappings.Clear();
        var decals = new MappingPrototype(null, Loc.GetString("mapping-decals")) { Children = new List<MappingPrototype>() };
        foreach (var decal in _prototypeManager.EnumeratePrototypes<DecalPrototype>())
        {
// KS14 start: mapping editor overhaul port
            if (decal.ShowMenu)
                Register(decal, decal.ID, decals);
// KS14 end
        }

        Sort(mappings, decals);
        mappings.Clear();

// KS14 start: mapping editor overhaul port
        Screen.Entities.UpdateVisible(
            new List<MappingPrototype> { entities },
            _allPrototypes.GetOrNew(typeof(EntityPrototype)));
// KS14 end

// KS14 start: mapping editor overhaul port
        Screen.Tiles.UpdateVisible(
            new List<MappingPrototype> { tiles },
            _allPrototypes.GetOrNew(typeof(ContentTileDefinition)));
// KS14 end

// KS14 start: mapping editor overhaul port
        Screen.Decals.UpdateVisible(
            new List<MappingPrototype> { decals },
            _allPrototypes.GetOrNew(typeof(DecalPrototype)));
// KS14 end
    }

    private MappingPrototype? Register<T>(T? prototype, string id, MappingPrototype topLevel) where T : class, IPrototype, IInheritingPrototype
    {
        {
            if (prototype == null &&
                _prototypeManager.TryIndex(id, out prototype) &&
                prototype is EntityPrototype entity)
            {
                if (entity.HideSpawnMenu || entity.Abstract)
                    prototype = null;
            }
        }

        if (prototype == null)
        {
            if (!_prototypeManager.TryGetMapping(typeof(T), id, out var node))
            {
                _sawmill.Error($"No {nameof(T)} found with id {id}");
                return null;
            }

            var ids = _idDict.GetOrNew(typeof(T));
            if (ids.TryGetValue(id, out var mapping))
            {
                return mapping;
            }
            else
            {
                var name = node.TryGet("name", out ValueDataNode? nameNode)
                    ? nameNode.Value
                    : id;

                if (node.TryGet("suffix", out ValueDataNode? suffix))
                    name = $"{name} [{suffix.Value}]";

                mapping = new MappingPrototype(prototype, name);
                _allPrototypes.GetOrNew(typeof(T)).Add(mapping); // KS14: mapping editor overhaul port
                ids.Add(id, mapping);

                if (node.TryGet("parent", out ValueDataNode? parentValue))
                {
                    var parent = Register<T>(null, parentValue.Value, topLevel);

                    if (parent != null)
                    {
                        mapping.Parents ??= new List<MappingPrototype>();
                        mapping.Parents.Add(parent);
                        parent.Children ??= new List<MappingPrototype>();
                        parent.Children.Add(mapping);
                    }
                }
                else if (node.TryGet("parent", out SequenceDataNode? parentSequence))
                {
                    foreach (var parentNode in parentSequence.Cast<ValueDataNode>())
                    {
                        var parent = Register<T>(null, parentNode.Value, topLevel);

                        if (parent != null)
                        {
                            mapping.Parents ??= new List<MappingPrototype>();
                            mapping.Parents.Add(parent);
                            parent.Children ??= new List<MappingPrototype>();
                            parent.Children.Add(mapping);
                        }
                    }
                }
                else
                {
                    topLevel.Children ??= new List<MappingPrototype>();
                    topLevel.Children.Add(mapping);
                    mapping.Parents ??= new List<MappingPrototype>();
                    mapping.Parents.Add(topLevel);
                }

                return mapping;
            }
        }
        else
        {
            var ids = _idDict.GetOrNew(typeof(T));
            if (ids.TryGetValue(id, out var mapping))
            {
                return mapping;
            }
            else
            {
                var entity = prototype as EntityPrototype;
// KS14 start: mapping editor overhaul port
                var tile = prototype as ContentTileDefinition;
                var name = entity?.Name ?? tile?.Name ?? prototype.ID;

                if (tile != null && _localization.TryGetString(tile.Name, out var locName))
                    name = locName;
// KS14 end

                if (!string.IsNullOrWhiteSpace(entity?.EditorSuffix))
                    name = $"{name} [{entity.EditorSuffix}]";

                mapping = new MappingPrototype(prototype, name);
                _allPrototypes.GetOrNew(typeof(T)).Add(mapping); // KS14: mapping editor overhaul port
                _allPrototypesDict.Add(prototype, mapping);
                ids.Add(prototype.ID, mapping);
            }

            if (prototype.Parents == null)
            {
                topLevel.Children ??= new List<MappingPrototype>();
                topLevel.Children.Add(mapping);
                mapping.Parents ??= new List<MappingPrototype>();
                mapping.Parents.Add(topLevel);
                return mapping;
            }

            foreach (var parentId in prototype.Parents)
            {
                var parent = Register<T>(null, parentId, topLevel);

                if (parent != null)
                {
                    mapping.Parents ??= new List<MappingPrototype>();
                    mapping.Parents.Add(parent);
                    parent.Children ??= new List<MappingPrototype>();
                    parent.Children.Add(mapping);
                }
            }

            return mapping;
        }
    }

    // KS14 start: maintain per-category selections, erase mode, and persisted mapping favorites
    private void Sort(Dictionary<string, MappingPrototype> prototypes, MappingPrototype topLevel)
    {
        static int Compare(MappingPrototype a, MappingPrototype b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }
        topLevel.Children ??= new List<MappingPrototype>();

        foreach (var prototype in prototypes.Values)
        {
            if (prototype.Parents == null && prototype != topLevel)
            {
                prototype.Parents = new List<MappingPrototype> { topLevel };
                topLevel.Children.Add(prototype);
            }

            prototype.Parents?.Sort(Compare);
            prototype.Children?.Sort(Compare);
        }

        topLevel.Children.Sort(Compare);
    }

    private void Deselect()
    {
        if (Screen.Entities.Selected is { } entitySelected)
        {
            entitySelected.Button.Pressed = false;
            Screen.Entities.Selected = null;

            if (entitySelected.Prototype?.Prototype is EntityPrototype)
                _placement.Clear();
        }

        if (Screen.Tiles.Selected is { } tileSelected)
        {
            tileSelected.Button.Pressed = false;
            Screen.Tiles.Selected = null;

            if (tileSelected.Prototype?.Prototype is ContentTileDefinition)
                _placement.Clear();
        }
        if (Screen.Decals.Selected is { } decalSelected)
        {
            decalSelected.Button.Pressed = false;
            Screen.Decals.Selected = null;
            if (decalSelected.Prototype?.Prototype is DecalPrototype)
                _decal.SetActive(false);
        }
    }

    private void EnableEntityEraser()
    {
        if (_placement.Eraser)
            return;

        Deselect();
        _placement.Clear();
        _placement.ToggleEraser();
        Screen.UnPressActionsExcept(Screen.EraseEntityButton);
        Screen.EntityPlacementMode.Disabled = true;

        Meta.State = CursorState.Entity;
        Meta.Color = DeleteColor;
    }

    private void DisableEntityEraser()
    {
        if (!_placement.Eraser)
            return;

        _placement.ToggleEraser();
        Meta.State = CursorState.None;
        Screen.EntityPlacementMode.Disabled = false;
    }

    #region On Event
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs obj)
    {
        if (!obj.WasModified<EntityPrototype>() &&
            !obj.WasModified<ContentTileDefinition>() &&
            !obj.WasModified<DecalPrototype>())
        {
            return;
        }
        SaveFavorites();
        ReloadPrototypes();
    }

    private void OnPlacementChanged(object? sender, EventArgs e)
    {
        if (!_placement.IsActive && _decal.GetActiveDecal().Decal == null)
            Deselect();

        Screen.EraseEntityButton.Pressed = _placement.Eraser;
        Screen.EntityPlacementMode.Disabled = _placement.Eraser;
    }

    private void OnFavoritesLoaded(List<IPrototype> prototypes)
    {
        Screen.Entities.FavoritesPrototype.Children = new List<MappingPrototype>();
        Screen.Decals.FavoritesPrototype.Children = new List<MappingPrototype>();
        Screen.Tiles.FavoritesPrototype.Children = new List<MappingPrototype>();

        foreach (var prototype in prototypes)
        {
            switch (prototype)
            {
                case EntityPrototype entityPrototype:
                {
                    if (_idDict.GetOrNew(typeof(EntityPrototype)).TryGetValue(entityPrototype.ID, out var entity))
                    {
                        Screen.Entities.FavoritesPrototype.Children.Add(entity);
                        entity.Parents ??= new List<MappingPrototype>();
                        entity.Parents.Add(Screen.Entities.FavoritesPrototype);
                        entity.Favorite = true;
                    }
                    break;
                }
                case DecalPrototype decalPrototype:
                {
                    if (_idDict.GetOrNew(typeof(DecalPrototype)).TryGetValue(decalPrototype.ID, out var decal))
                    {
                        Screen.Decals.FavoritesPrototype.Children.Add(decal);
                        decal.Parents ??= new List<MappingPrototype>();
                        decal.Parents.Add(Screen.Decals.FavoritesPrototype);
                        decal.Favorite = true;
                    }
                    break;
                }
                case ContentTileDefinition tileDefinition:
                {
                    if (_idDict.GetOrNew(typeof(ContentTileDefinition)).TryGetValue(tileDefinition.ID, out var tile))
                    {
                        Screen.Tiles.FavoritesPrototype.Children.Add(tile);
                        tile.Parents ??= new List<MappingPrototype>();
                        tile.Parents.Add(Screen.Tiles.FavoritesPrototype);
                        tile.Favorite = true;
                    }
                    break;
                }
            }
        }
    }

    protected override void OnKeyBindStateChanged(ViewportBoundKeyEventArgs args)
    {
        if (args.Viewport == null)
            base.OnKeyBindStateChanged(new ViewportBoundKeyEventArgs(args.KeyEventArgs, Viewport.Viewport));
        else
            base.OnKeyBindStateChanged(args);

        UpdateLocale();
    }
    // KS14 end

    private void OnGetData(IPrototype prototype, List<Texture> textures)
    {
        switch (prototype)
        {
            case EntityPrototype entity:
                textures.AddRange(SpriteComponent.GetPrototypeTextures(entity, _resources).Select(t => t.Default)); // KS14: mapping editor overhaul port
                break;
            case DecalPrototype decal:
                textures.Add(_sprite.Frame0(decal.Sprite));
                break;
            case ContentTileDefinition tile:
                if (tile.Sprite?.ToString() is { } sprite)
                    textures.Add(_resources.GetResource<TextureResource>(sprite).Texture);
                break;
        }
    }

    private void OnSelected(MappingPrototypeList list, MappingPrototype mapping) // KS14: mapping editor overhaul port
    {
        if (mapping.Prototype == null)
            return;

        var chain = new Stack<MappingPrototype>();
        chain.Push(mapping);

        var parent = mapping.Parents?.FirstOrDefault();
        while (parent != null)
        {
            chain.Push(parent);
            parent = parent.Parents?.FirstOrDefault();
        }

        _lastClicked = null;

        Control? last = null;
        var children = list.PrototypeList.Children.ToList(); // KS14: mapping editor overhaul port
        foreach (var prototype in chain)
        {
            foreach (var child in children)
            {
                if (child is MappingSpawnButton button &&
                    button.Prototype == prototype)
                {
// KS14 start: mapping editor overhaul port
                    button.CollapseButton.Pressed = true;
                    list.ToggleCollapse(button);
                    OnSelected(list, button, prototype.Prototype);
                    children = button.ChildrenPrototypes.Children.ToList();
                    children.AddRange(button.ChildrenPrototypesGallery.Children);
// KS14 end
                    last = child;
                    break;
                }
            }
        }

// KS14 start: mapping editor overhaul port
        if (last != null && list.PrototypeList.Visible)
            _scrollTo = (last, list);
// KS14 end
    }

    private void OnSelected(MappingPrototypeList list, MappingSpawnButton button, IPrototype? prototype) // KS14: mapping editor overhaul port
    {
        var time = _timing.CurTime;
        if (prototype is DecalPrototype)
            Screen.SelectDecal(prototype.ID);

        // Double-click functionality if it's collapsible.
        if (_lastClicked is { } lastClicked &&
            lastClicked.Button == button &&
// KS14 start: mapping editor overhaul port
            lastClicked.At > time - TimeSpan.FromSeconds(0.333))
        {
            if (button.CollapseButton.Visible && string.IsNullOrEmpty(list.SearchBar.Text))
            {
                button.CollapseButton.Pressed = !button.CollapseButton.Pressed;
                list.ToggleCollapse(button);
                button.Button.Pressed = true;
                list.Selected = button;
                _lastClicked = null;
                return;
            }

            if (button.Parent == list.SearchList && button.Prototype != null)
            {
                list.SearchBar.SetText(string.Empty, true);
                OnSelected(list, button.Prototype);
                _lastClicked = null;
                return;
            }
// KS14 end
        }

        // Toggle if it's the same button (at least if we just unclicked it).
        if (!button.Button.Pressed && button.Prototype?.Prototype != null && _lastClicked?.Button == button)
        {
            _lastClicked = null;
            Deselect();
            return;
        }

        _lastClicked = (time, button);

        if (button.Prototype == null)
            return;

        if (list.Selected is { } oldButton && // KS14: mapping editor overhaul port
            oldButton != button)
        {
            Deselect();
        }

// KS14 start: mapping editor overhaul port
        Meta.State = CursorState.None;
        Screen.UnPressActionsExcept(new Control());
// KS14 end

        switch (prototype)
        {
            case EntityPrototype entity:
// KS14 start: mapping editor overhaul port
            {
                var placementId = Screen.EntityPlacementMode.SelectedId;

                var placement = new PlacementInformation
// KS14 end
                {
// KS14 start: mapping editor overhaul port
                    PlacementOption = placementId > 0 ? EntitySpawnWindow.InitOpts[placementId] : entity.PlacementMode,
                    EntityType = entity.ID,
                    IsTile = false
                };
// KS14 end

// KS14 start: mapping editor overhaul port
                _decal.SetActive(false);
                _placement.BeginPlacing(placement);
                break;
            }
// KS14 end
            case DecalPrototype decal:
                _placement.Clear();

                _decal.SetActive(true);
                Screen.SelectDecal(decal.ID); // KS14: mapping editor overhaul port
                break;
            case ContentTileDefinition tile:
// KS14 start: mapping editor overhaul port
            {
                var placement = new PlacementInformation
// KS14 end
                {
// KS14 start: mapping editor overhaul port
                    PlacementOption = "AlignTileAny",
                    TileType = tile.TileId,
                    IsTile = true
                };
// KS14 end

// KS14 start: mapping editor overhaul port
                _decal.SetActive(false);
                _placement.BeginPlacing(placement);
                break;
            }
// KS14 end
            default:
                _placement.Clear();
                break;
        }

        list.Selected = button; // KS14: mapping editor overhaul port

        button.Button.Pressed = true;
    }

    private void OnEntityReplacePressed(ButtonToggledEventArgs args)
    {
        _placement.Replacement = args.Pressed;
    }

    private void OnEntityPlacementSelected(ItemSelectedEventArgs args)
    {
        Screen.EntityPlacementMode.SelectId(args.Id);

        if (_placement.CurrentMode != null)
        {
            var placement = new PlacementInformation
            {
                PlacementOption = EntitySpawnWindow.InitOpts[args.Id],
                EntityType = _placement.CurrentPermission!.EntityType,
                TileType = _placement.CurrentPermission.TileType,
                Range = 2,
                IsTile = _placement.CurrentPermission.IsTile,
            };

            _placement.BeginPlacing(placement);
        }
    }

    private void OnEraseEntityPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed == _placement.Eraser)
            return;

        if (args.Button.Pressed)
            EnableEntityEraser(); // KS14: mapping editor overhaul port
        else
            DisableEntityEraser(); // KS14: mapping editor overhaul port
    }

    private void OnEraseTilePressed(ButtonEventArgs args) // KS14: mapping editor overhaul port
    {
        Meta.State = CursorState.None; // KS14: mapping editor overhaul port
        _placement.Clear();
        Deselect();

// KS14 start: mapping editor overhaul port
        if (!args.Button.Pressed)
        {
            Screen.EntityPlacementMode.Disabled = false;
            _tileErase = false;
// KS14 end
            return;
        } // KS14: mapping editor overhaul port

// KS14 start: mapping editor overhaul port
        _placement.BeginPlacing(new PlacementInformation
        {
            PlacementOption = "AlignTileAny",
            TileType = 0,
            Range = 400,
            IsTile = true,
        });

        Screen.UnPressActionsExcept(Screen.EraseTileButton);
        _tileErase = true;
// KS14 end
        Screen.EntityPlacementMode.Disabled = true;
    }

    private void OnEraseDecalPressed(ButtonToggledEventArgs args) // KS14: mapping editor overhaul port
    {
// KS14 start: mapping editor overhaul port
        if (args.Button.Pressed)
        {
            Meta.State = CursorState.Tile;
            Meta.Color = EraseDecalColor;
// KS14 end

// KS14 start: mapping editor overhaul port
            Screen.UnPressActionsExcept(Screen.EraseDecalButton);
            _placement.Clear();
            Deselect();
        }
        else
        {
            Meta.State = CursorState.None;
        }
    }
    // KS14 start: port supported mapping toolbar actions from upstream PR #34302
    private void OnFixGridAtmosPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
            Screen.UnPressActionsExcept(Screen.FixGridAtmos);
    }

    private void OnRemoveGridPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
            Screen.UnPressActionsExcept(Screen.RemoveGrid);
    }

    private void OnMoveGridPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
            Screen.UnPressActionsExcept(Screen.MoveGrid);

        var gridDraggingSystem = _entityManager.System<GridDraggingSystem>();
        if (args.Button.Pressed != gridDraggingSystem.Enabled)
            _consoleHost.ExecuteCommand("griddrag");
    }

    private void OnGridVVPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
            Screen.UnPressActionsExcept(Screen.GridVV);
    }
    // KS14 end
    #endregion

    #region Mapping Actions
    private void OnPickPressed(ButtonEventArgs args)
    {
        if (args.Button.Pressed)
            EnablePick();
        else
            DisablePick();
// KS14 end
    }

    private void EnablePick()
    {
        Deselect(); // KS14: mapping editor overhaul port
        Screen.UnPressActionsExcept(Screen.Pick);
// KS14 start: mapping editor overhaul port
        Meta.State = CursorState.EntityOrTile;
        Meta.Color = PickColor;
        Meta.SecondColor = PickColor.WithAlpha(0.2f);
// KS14 end
    }

    private void DisablePick()
    {
        Screen.Pick.Pressed = false;
        Meta.State = CursorState.None; // KS14: mapping editor overhaul port
    }
    #endregion // KS14: mapping editor overhaul port

// KS14 start: mapping editor overhaul port
    #region Handle Bindings
    private bool HandleOpenContextMenu(in PointerInputCmdArgs args)
// KS14 end
    {
        Deselect(); // KS14: mapping editor overhaul port

// KS14 start: mapping editor overhaul port
        var coords = _transform.ToMapCoordinates(args.Coordinates);
        if (_verbs.TryGetEntityMenuEntities(coords, out var entities))
            _entityMenuController.OpenRootMenu(entities);

        return true;
// KS14 end
    }

    private bool HandleMappingUnselect(in PointerInputCmdArgs args)
    {
// KS14 start: mapping editor overhaul port
        if (_placement.Eraser)
            _placement.ToggleEraser();

        Screen.UnPressActionsExcept(new Control());
        Meta.State = CursorState.None;

        if (Screen.Decals.Selected is not { Prototype.Prototype: DecalPrototype })
// KS14 end
            return false;

        Deselect();
        return true;
    }

    private bool HandleSaveMap(in PointerInputCmdArgs args)
    {
#if FULL_RELEASE
        return false;
#endif // KS14: mapping editor overhaul port
        if (!_admin.IsAdmin(true) || !_admin.HasFlag(AdminFlags.Host))
            return false;

        SaveMap();
        return true;
    }

    private bool HandleEnablePick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        EnablePick();
        return true;
    }

    private bool HandleDisablePick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        DisablePick();
        return true;
    }

    private bool HandleEnableDelete(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
// KS14 start: mapping editor overhaul port
        Screen.EraseEntityButton.Pressed = true;
        EnableEntityEraser();
// KS14 end
        return true;
    }

    private bool HandleDisableDelete(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
// KS14 start: mapping editor overhaul port
        Screen.EraseEntityButton.Pressed = false;
        DisableEntityEraser();
// KS14 end
        return true;
    }

    private bool HandlePick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        MappingPrototype? button = null;

        if (Screen.Pick.Pressed) // KS14: mapping editor overhaul port
        {
            if (!uid.IsValid()) // KS14: mapping editor overhaul port
            {
// KS14 start: mapping editor overhaul port
                var mapPos = _transform.ToMapCoordinates(coords);

                if (_mapMan.TryFindGridAt(mapPos, out var gridUid, out var grid) &&
                    _entityManager.System<SharedMapSystem>().TryGetTileRef(gridUid, grid, coords, out var tileRef) &&
                    _allPrototypesDict.TryGetValue(_entityManager.System<TurfSystem>().GetContentTileDefinition(tileRef), out button))
                {
                    switch (button.Prototype)
                    {
                        case EntityPrototype:
                        {
                            OnSelected(Screen.Entities, button);
                            break;
                        }
                        case ContentTileDefinition:
                        {
                            OnSelected(Screen.Tiles, button);
                            break;
                        }
                    }

                    return true;
                }
// KS14 end
            }
        }
        else // KS14: mapping editor overhaul port
        {
// KS14 start: mapping editor overhaul port
            return false;
        }
// KS14 end

// KS14 start: mapping editor overhaul port
        if (button != null)
            return false;
// KS14 end

// KS14 start: mapping editor overhaul port
        if (uid == EntityUid.Invalid ||
            _entityManager.GetComponentOrNull<MetaDataComponent>(uid) is not
                { EntityPrototype: { } prototype } ||
            !_allPrototypesDict.TryGetValue(prototype, out button))
        {
            // we always block other input handlers if pick mode is enabled
            // this makes you not accidentally place something in space because you
            // miss-clicked while holding down the pick hotkey
            return true;
// KS14 end
        }

// KS14 start: mapping editor overhaul port
        // Selected an entity
        OnSelected(Screen.Entities, button);

        // Match rotation
        _placement.Direction = _entityManager.GetComponent<TransformComponent>(uid).LocalRotation.GetDir();

// KS14 end
        return true;
    }

    private bool HandleEditorCancelPlace(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!Screen.EraseDecalButton.Pressed)
            return false;

        _entityNetwork.SendSystemNetworkMessage(new RequestDecalRemovalEvent(_entityManager.GetNetCoordinates(coords)));
        return true;
    }

    private bool HandleCancelEraseDecal(in PointerInputCmdArgs args)
    {
        if (!Screen.EraseDecalButton.Pressed)
            return false;

        Screen.EraseDecalButton.Pressed = false;
        return true;
    }

    // KS14 start: port supported mapping toolbar actions from upstream PR #34302
    private bool HandleUse(in PointerInputCmdArgs args)
    {
        if (Screen.FixGridAtmos.Pressed)
        {
            Screen.FixGridAtmos.Pressed = false;
            if (GetHoveredGrid() is { } gridEntity)
                _consoleHost.ExecuteCommand($"fixgridatmos {_entityManager.GetNetEntity(gridEntity.Owner).Id}");

            return true;
        }

        if (Screen.RemoveGrid.Pressed)
        {
            Screen.RemoveGrid.Pressed = false;
            if (GetHoveredGrid() is { } gridEntity)
                _consoleHost.ExecuteCommand($"rmgrid {_entityManager.GetNetEntity(gridEntity.Owner).Id}");

            return true;
        }

        if (Screen.GridVV.Pressed)
        {
            Screen.GridVV.Pressed = false;
            if (GetHoveredGrid() is { } gridEntity)
                _consoleHost.ExecuteCommand($"vv {_entityManager.GetNetEntity(gridEntity.Owner).Id}");

            return true;
        }

        return false;
    }

    // KS14 end
    private bool HandleMouseMiddle(in PointerInputCmdArgs args) // KS14: mapping editor overhaul port
    {
        if (_decal.GetActiveDecal() is { Decal: not null }) // KS14: mapping editor overhaul port
        {
// KS14 start: mapping editor overhaul port
            Screen.ChangeDecalRotation(90f);
            return true;
// KS14 end
        }
// KS14 start: mapping editor overhaul port

        return false;
// KS14 end
    }
    #endregion // KS14: mapping editor overhaul port

    private async void SaveMap() // KS14: mapping editor overhaul port
    {
        await _mapping.SaveMap(); // KS14: mapping editor overhaul port
    }

    public EntityUid? GetHoveredEntity() // KS14: mapping editor overhaul port
    {
// KS14 start: mapping editor overhaul port
        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl viewport ||
            _input.MouseScreenPosition is not { IsValid: true } position)
        {
            return null;
        }
// KS14 end

// KS14 start: mapping editor overhaul port
        var mapPos = viewport.PixelToMap(position.Position);
        return GetClickedEntity(mapPos);
// KS14 end
    }

    public Entity<MapGridComponent>? GetHoveredGrid() // KS14: mapping editor overhaul port
    {
        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl viewport ||
            _input.MouseScreenPosition is not { IsValid: true } position)
        {
            return null;
        }

        var mapPos = viewport.PixelToMap(position.Position);
// KS14 start: mapping editor overhaul port
        if (_mapMan.TryFindGridAt(mapPos, out var gridUid, out var grid))
        {
            return new Entity<MapGridComponent>(gridUid, grid);
        }

        return null;
// KS14 end
    }

    public Box2Rotated? GetHoveredTileBox2() // KS14: mapping editor overhaul port
    {
// KS14 start: mapping editor overhaul port
        if (UserInterfaceManager.CurrentlyHovered is not IViewportControl viewport ||
            _input.MouseScreenPosition is not { IsValid: true } coords)
// KS14 end
        {
// KS14 start: mapping editor overhaul port
            return null;
        }
// KS14 end

// KS14 start: mapping editor overhaul port
        if (GetHoveredGrid() is not { } grid)
            return null;

        if (!_entityManager.TryGetComponent<TransformComponent>(grid, out var xform))
            return null;
// KS14 end

// KS14 start: mapping editor overhaul port
        var mapCoords = viewport.PixelToMap(coords.Position);
        var tileSize = grid.Comp.TileSize;
        var tileDimensions = new Vector2(tileSize, tileSize);
        var tileRef = _map.GetTileRef(grid, mapCoords);
        var worldCoord = _map.LocalToWorld(grid.Owner, grid.Comp, tileRef.GridIndices);
        var box = Box2.FromDimensions(worldCoord, tileDimensions);

        return new Box2Rotated(box, xform.LocalRotation, box.BottomLeft);
    }

    public override void FrameUpdate(FrameEventArgs e)
    {
        if (!Screen.EraseTileButton.Pressed && _tileErase)
        {
            _placement.Clear();
            _tileErase = false;
// KS14 end
        }

        if (_scrollTo is not { } scrollTo)
            return;

// KS14 start: mapping editor overhaul port
        var (control, list) = scrollTo;

// KS14 end
        // this is not ideal but we wait until the control's height is computed to use
        // its position to scroll to
        if (control.Height > 0 && list.PrototypeList.Visible) // KS14: mapping editor overhaul port
        {
// KS14 start: mapping editor overhaul port
            var y = control.GlobalPosition.Y - list.ScrollContainer.Height / 2 + control.Height - list.GlobalPosition.Y;
            var scroll = list.ScrollContainer;
// KS14 end
            scroll.SetScrollValue(scroll.GetScrollValue() + new Vector2(0, y));
            _scrollTo = null;
        }
    }

    public enum CursorState
    {
        None,
// KS14 start: mapping editor overhaul port
        Tile,
        Entity,
        EntityOrTile,
    }

    public sealed partial class CursorMeta
    {
        /// <summary>
        ///     Defines how the overlay will be rendered
        /// </summary>
        public CursorState State = CursorState.None;

        /// <summary>
        ///     Color with which the mapping overlay will be drawn
        /// </summary>
        public Color Color = Color.White;

        public Color? SecondColor;
// KS14 end
    }
}
