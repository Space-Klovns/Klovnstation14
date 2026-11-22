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
    private readonly KsTguiMaterialList _materials;
    private readonly BoxContainer _clientList;
    private readonly KsTguiNoticeBox _noClientsNotice;

    private readonly Dictionary<NetEntity, KsTguiButton> _clientButtons = new();

    /// <summary>
    ///     The row of each machine listed.
    /// </summary>
    public IReadOnlyDictionary<NetEntity, KsTguiButton> ClientButtons => _clientButtons;

    public event Action<NetEntity>? OnClientEntryPressed;

    public KsTguiOreSiloWindow()
    {
        Title = Loc.GetString("ore-silo-ui-title");
        MinSize = new Vector2(400f, 260f);
        SetSize = new Vector2(520f, 520f);

        _noClientsNotice = new KsTguiNoticeBox { Text = Loc.GetString("ks-tgui-ore-silo-no-clients"), NoticeColor = KsTguiNoticeColor.Danger };
        _clientList = new KsTguiCandystripe { Orientation = BoxContainer.LayoutOrientation.Vertical };

        var clientsBody = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };
        clientsBody.AddChild(_noClientsNotice);
        clientsBody.AddChild(new KsTguiScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _clientList } });
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
        _materials.SetOwner(uid);
    }

    public void Update(OreSiloBuiState state)
    {
        // Rows are kept for the machines still listed, so a row being clicked is not swapped for a new one under the mouse
        //     when a state arrives between the press and the release.
        var listed = new HashSet<NetEntity>();
        var position = 0;

        // The stock list is in the order of the entities' ids once it has them.
        foreach (var (client, text, _) in state.Clients.OrderBy(entry => entry.Item1))
        {
            listed.Add(client);

            if (!_clientButtons.TryGetValue(client, out var button))
            {
                var clientEntity = client;
                button = new KsTguiButton
                {
                    Fluid = true,
                    LeftAligned = true,
                };
                button.OnPressed += _ => OnClientEntryPressed?.Invoke(clientEntity);
                _clientButtons.Add(client, button);
                _clientList.AddChild(button);
            }

            button.Text = text;
            button.SetPositionInParent(position++);
        }

        foreach (var (client, button) in _clientButtons.ToList())
        {
            if (listed.Contains(client))
                continue;

            button.Orphan();
            _clientButtons.Remove(client);
        }

        _noClientsNotice.Visible = state.Clients.Count == 0;

        // Whether a machine is linked comes with its row's text, from the server. The machine's own link can't be read
        //     here: one out of the player's sight sends the client nothing, and one in sight can have its state applied
        //     after this one, leaving the row a colour that its text contradicts (#451).
        foreach (var (client, button) in _clientButtons)
        {
            button.Selected = state.KsLinked.Contains(client);
        }
    }
}
