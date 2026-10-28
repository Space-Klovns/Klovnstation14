using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared._KS14.Procedural;
using Content.Shared._KS14.Procedural.Editor;
using Content.Shared.Eui;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Procedural.Editor;

public sealed partial class KsProcgenThemeEditorEui : BaseEui
{
    private static readonly ProtoId<KsProcgenRoomThemePrototype> DefaultTheme = "KsProcgenSimpleOffice";
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;
    private readonly Guid _documentId = Guid.NewGuid();
    private int _revision;
    private KsProcgenThemeEditorValues _values;
    private string _status = "ks-procgen-theme-ready";
    private string _detail = string.Empty;
    private readonly string? _loadKey;

    public KsProcgenThemeEditorEui(string? loadKey = null)
    {
        IoCManager.InjectDependencies(this);
        _loadKey = loadKey;
        _values = new();
        if (_prototypes.TryIndex(DefaultTheme, out var theme))
        {
            _values.TilePack = theme.TilePacks?.FirstOrDefault()?.Pack ?? string.Empty;
            _values.WallPack = theme.WallPacks?.FirstOrDefault()?.Pack ?? string.Empty;
            _values.LightPack = theme.LightingPacks?.FirstOrDefault()?.Pack ?? string.Empty;
            _values.EntityPack = theme.EntityPacks?.FirstOrDefault()?.Pack ?? string.Empty;
            _values.AtmosphereProfile = theme.AtmosphereProfile ?? string.Empty;
            _values.ElectricalProfile = theme.ElectricalProfile ?? string.Empty;
        }
    }

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermissionsChanged;
        if (!_admins.HasAdminFlag(Player, AdminFlags.Mapping)) { Close(); return; }
        if (_loadKey != null)
        {
            if (_entities.System<KsProcgenThemeLibrarySystem>().TryLoad(_loadKey, out var values, out _, out var issue))
            { _values = values!; _status = "ks-procgen-theme-loaded-copy"; }
            else { _status = "ks-procgen-theme-error"; _detail = issue!.Code; }
        }
        StateDirty();
    }

    public override void Closed()
    {
        _admins.OnPermsChanged -= OnPermissionsChanged;
        base.Closed();
    }

    private void OnPermissionsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !_admins.HasAdminFlag(Player, AdminFlags.Mapping)) Close();
    }

    public override void HandleMessage(EuiMessageBase message)
    {
        if (message is CloseEuiMessage) { base.HandleMessage(message); return; }
        if (!_admins.HasAdminFlag(Player, AdminFlags.Mapping)) { Close(); return; }
        if (message is not KsProcgenThemeEditorSave save) return;
        if (save.ExpectedRevision != _revision || _revision == int.MaxValue)
        { _status = "ks-procgen-theme-conflict"; StateDirty(); return; }
        if (_entities.System<KsProcgenThemeLibrarySystem>().TrySave(_documentId, _revision + 1, save.Values, out var key, out var issue))
        {
            _revision++; _values = save.Values.Copy(); _status = "ks-procgen-theme-saved"; _detail = key!;
        }
        else { _status = "ks-procgen-theme-error"; _detail = issue!.Code; }
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        const int maximumChoices = 256;
        string[] Choices<T>() where T : class, IPrototype => _prototypes.EnumeratePrototypes<T>()
            .Select(prototype => prototype.ID).OrderBy(id => id, StringComparer.Ordinal).Take(maximumChoices).ToArray();
        return new KsProcgenThemeEditorState
        {
            DocumentId = _documentId, Revision = _revision, Values = _values.Copy(),
            TilePacks = Choices<KsProcgenTilePackPrototype>(), WallPacks = Choices<KsProcgenWallPackPrototype>(),
            LightPacks = Choices<KsProcgenLightingPackPrototype>(), EntityPacks = Choices<KsProcgenEntityPackPrototype>(),
            AtmosphereProfiles = Choices<KsProcgenAtmosphereProfilePrototype>(),
            ElectricalProfiles = Choices<KsProcgenElectricalProfilePrototype>(),
            Status = _status, Detail = _detail,
            CatalogTruncated = _prototypes.EnumeratePrototypes<KsProcgenTilePackPrototype>().Count() > maximumChoices ||
                _prototypes.EnumeratePrototypes<KsProcgenWallPackPrototype>().Count() > maximumChoices ||
                _prototypes.EnumeratePrototypes<KsProcgenLightingPackPrototype>().Count() > maximumChoices ||
                _prototypes.EnumeratePrototypes<KsProcgenEntityPackPrototype>().Count() > maximumChoices ||
                _prototypes.EnumeratePrototypes<KsProcgenAtmosphereProfilePrototype>().Count() > maximumChoices ||
                _prototypes.EnumeratePrototypes<KsProcgenElectricalProfilePrototype>().Count() > maximumChoices,
        };
    }
}
