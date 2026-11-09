using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Robust.Client.Graphics;
using Content.Shared._KS14.GhostRoleNotice;
using JetBrains.Annotations;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.GhostRoleNotice;

/// <summary>
///     <c>[ksghostrolefollow="text" roles="12,13"/]</c>: a link that asks the server to make this ghost follow the
///         first of the listed ghost roles that is still free. Like a command link, but it raises
///         <see cref="KsGhostRoleFollowRequestEvent"/> rather than a console command, which the console would echo.
/// </summary>
[UsedImplicitly]
public sealed partial class KsGhostRoleFollowTag : IMarkupTagHandler
{
    public const string TagName = "ksghostrolefollow";

    /// <summary>
    ///     The link's size. A link control ignores the surrounding font tags, so it sets its own font.
    /// </summary>
    public const int FontSize = 15;

    private const string BoldFontId = "DefaultBold";

    [Dependency] private IEntityNetworkManager _entityNetworkManager = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public string Name => TagName;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        // Not TryGetString: a lone role id ("12") is parsed as a number rather than a string.
        if (!node.Attributes.TryGetValue("roles", out var rolesParameter)
            || !TryParseRoles(ParameterText(rolesParameter), out var roles))
        {
            control = null;
            return false;
        }

        var text = ParameterText(node.Value);
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterMode.Stop,
            FontColorOverride = Color.LightBlue,
            DefaultCursorShape = Control.CursorShape.Hand,
        };

        var fontPrototype = _prototypeManager.Index<FontPrototype>(BoldFontId);
        label.FontOverride = new VectorFont(_resourceCache.GetResource<FontResource>(fontPrototype.Path), FontSize);

        label.OnMouseEntered += _ => label.FontColorOverride = Color.Blue;
        label.OnMouseExited += _ => label.FontColorOverride = Color.LightBlue;
        label.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            _entityNetworkManager.SendSystemNetworkMessage(new KsGhostRoleFollowRequestEvent(roles));
        };

        control = label;
        return true;
    }

    private static string ParameterText(MarkupParameter parameter)
    {
        return parameter.StringValue ?? parameter.LongValue?.ToString() ?? string.Empty;
    }

    /// <summary>
    ///     The attribute value of a link to <paramref name="roles"/>.
    /// </summary>
    public static string FormatRoles(IEnumerable<NetEntity> roles)
    {
        return string.Join(',', roles.Select(role => role.Id));
    }

    private static bool TryParseRoles(string text, [NotNullWhen(true)] out List<NetEntity>? roles)
    {
        roles = new List<NetEntity>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out var id))
            {
                roles = null;
                return false;
            }

            roles.Add(new NetEntity(id));
        }

        return roles.Count > 0;
    }
}
