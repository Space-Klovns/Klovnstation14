using System.Linq;
using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.UserInterface.Controls;
using Content.Shared.Materials.OreSilo;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Materials;

/// <summary>
///     The material silo after tgui's Ore Silo: the machines that can draw from it, each a button that links or unlinks it,
///         over the stored materials in a grid. tg's logs and ID restriction have no counterpart here and are left out.
/// </summary>
public sealed partial class KsTguiOreSiloWindow : FancyWindow, IKsOreSiloWindow
{
    [Dependency] private IEntityManager _entityManager = default!;

    private readonly KsTguiMaterialList _materials;
    private readonly BoxContainer _clientList;
    private readonly KsTguiNoticeBox _noClientsNotice;

    private EntityUid _silo;

    public event Action<NetEntity>? OnClientEntryPressed;

    public KsTguiOreSiloWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("ore-silo-ui-title");
        MinSize = new Vector2(400f, 260f);
        SetSize = new Vector2(520f, 520f);

        _noClientsNotice = new KsTguiNoticeBox { Text = Loc.GetString("ks-tgui-ore-silo-no-clients"), NoticeColor = KsTguiNoticeColor.Danger };
        _clientList = new KsTguiCandystripe { Orientation = BoxContainer.LayoutOrientation.Vertical };

        var clientsBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };
        clientsBody.AddChild(_noClientsNotice);
        clientsBody.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _clientList } });
        var clientsSection = new KsTguiSection { Title = Loc.GetString("ore-silo-ui-label-clients"), Fill = true };
        clientsSection.AddChild(clientsBody);

        _materials = new KsTguiMaterialList();
        var materialsSection = new KsTguiSection { Title = Loc.GetString("ore-silo-ui-label-mats") };
        materialsSection.AddChild(_materials);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = KsTguiSection.Gap, Margin = new Thickness(6f) };
        root.AddChild(clientsSection);
        root.AddChild(materialsSection);
        ContentsContainer.AddChild(root);
    }

    public void SetEntity(EntityUid uid)
    {
        _silo = uid;
        _materials.SetOwner(uid);
    }

    public void Update(OreSiloBuiState state)
    {
        _clientList.RemoveAllChildren();

        // The stock list is in the order of the entities' ids once it has them.
        foreach (var (client, text, _) in state.Clients.OrderBy(entry => entry.Item1))
        {
            var clientEntity = client;
            var button = new KsTguiButton
            {
                Text = text,
                Fluid = true,
                LeftAligned = true,
                Selected = IsLinked(clientEntity),
            };
            button.OnPressed += _ => OnClientEntryPressed?.Invoke(clientEntity);
            _clientList.AddChild(button);
        }

        _noClientsNotice.Visible = state.Clients.Count == 0;
    }

    /// <summary>
    ///     Whether the client is linked to this silo, where the client is known. The text of every row says so as well.
    /// </summary>
    private bool IsLinked(NetEntity client)
    {
        return _entityManager.TryGetEntity(client, out var clientUid)
            && _entityManager.TryGetComponent<OreSiloClientComponent>(clientUid, out var clientComponent)
            && clientComponent.Silo == _silo;
    }
}
