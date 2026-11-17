using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Cargo.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Tgui.Windows.Cargo;

/// <summary>
///     The order form the catalog opens for one product (stock <c>CargoConsoleOrderMenu</c>), laid out as a tgui section in a
///         <see cref="KsTguiModal"/> over the console window, as tgui shows its pack contents. tgui adds an item straight to
///         the cart; here the player also says who is asking, why, and how many.
/// </summary>
public sealed class KsTguiCargoOrderModal : KsTguiModal
{
    private readonly Label _productLabel;
    private readonly RichTextLabel _descriptionLabel;
    private readonly Label _costLabel;
    private readonly LineEdit _requesterEdit;
    private readonly LineEdit _reasonEdit;
    private readonly KsTguiNumberInput _amountSpinBox;
    private readonly KsTguiButton _submitButton;

    private ProtoId<CargoProductPrototype>? _product;

    /// <summary>
    ///     Requester, reason, product and amount of an accepted form.
    /// </summary>
    public event Action<string, string, ProtoId<CargoProductPrototype>, int>? OnSubmitted;

    /// <summary>
    ///     How many the console can still take; the form refuses more.
    /// </summary>
    public int Capacity { get; set; }

    public KsTguiCargoOrderModal()
    {
        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = KsTguiSection.Gap,
            MinSize = new Vector2(420f, 0f),
        };

        var closeButton = new KsTguiButton { Icon = KsTguiIcons.Times, Color = KsTguiColor.Danger, Compact = true };
        closeButton.OnPressed += _ => Close();
        var section = new KsTguiSection { Title = Loc.GetString("cargo-console-order-menu-title"), Buttons = closeButton };
        var list = new KsTguiLabeledList();

        _productLabel = new Label();
        list.AddItem(Loc.GetString("cargo-console-order-menu-product-label")).AddChild(_productLabel);

        _descriptionLabel = new RichTextLabel { HorizontalExpand = true };
        list.AddItem(Loc.GetString("cargo-console-order-menu-description-label")).AddChild(_descriptionLabel);

        _costLabel = new Label();
        list.AddItem(Loc.GetString("cargo-console-order-menu-cost-label")).AddChild(_costLabel);

        _requesterEdit = new LineEdit { HorizontalExpand = true };
        list.AddItem(Loc.GetString("cargo-console-order-menu-requester-label")).AddChild(_requesterEdit);

        _reasonEdit = new LineEdit { HorizontalExpand = true };
        list.AddItem(Loc.GetString("cargo-console-order-menu-reason-label")).AddChild(_reasonEdit);

        _amountSpinBox = new KsTguiNumberInput { Format = "0", WidthPixels = 75f, MinValue = 1f };
        _amountSpinBox.SetValue(1f);
        list.AddItem(Loc.GetString("cargo-console-order-menu-amount-label")).AddChild(_amountSpinBox);

        section.AddChild(list);
        root.AddChild(section);

        _submitButton = new KsTguiButton
        {
            Text = Loc.GetString("cargo-console-order-menu-submit-button"),
            Icon = KsTguiIcons.Check,
            Color = KsTguiColor.Green,
            Fluid = true,
        };
        _submitButton.OnPressed += _ => Submit();
        root.AddChild(_submitButton);

        Body = root;
    }

    /// <summary>
    ///     Fills the form for a product and shows it.
    /// </summary>
    public void Show(CargoProductPrototype product, string productName, string description, string cost, string requester)
    {
        _product = product.ID;
        _productLabel.Text = productName;
        _costLabel.Text = cost;

        var message = new FormattedMessage();
        message.AddText(description);
        _descriptionLabel.SetMessage(message);

        _requesterEdit.Text = requester;
        _reasonEdit.Text = "";
        _amountSpinBox.MaxValue = MathF.Max(1f, (float)Capacity);
        _amountSpinBox.SetValue(1f);

        Open();
    }

    private void Submit()
    {
        if (_product == null)
            return;

        if (_amountSpinBox.IsEditing)
            _amountSpinBox.Commit();

        var amount = (int)_amountSpinBox.Value;
        if (amount < 1 || amount > Capacity)
            return;

        OnSubmitted?.Invoke(_requesterEdit.Text, _reasonEdit.Text, _product.Value, amount);
        Close();
    }
}
