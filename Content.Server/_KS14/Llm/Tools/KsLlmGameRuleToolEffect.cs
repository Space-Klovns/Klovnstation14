using Content.Server.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Starts a game rule. Generic: which rule, and how the model is told about it, is entirely the tool
///         prototype's YAML. Two shapes:
///     <list type="bullet">
///         <item>
///             A fixed <see cref="Rule"/>: the tool takes no parameters, and calling it starts that rule.
///         </item>
///         <item>
///             A choice from <see cref="AllowedRules"/>, named by the model in <see cref="RuleArgument"/>. Give
///                 that parameter an <c>enum</c> of exactly the allowed rules, with an <c>enumDescriptions</c>
///                 entry each, so the model knows what it is choosing between.
///         </item>
///     </list>
/// </summary>
public sealed partial class KsLlmGameRuleToolEffect : KsLlmToolEffect
{
    /// <summary>
    ///     The one rule this tool starts. When set, <see cref="AllowedRules"/> and <see cref="RuleArgument"/> are
    ///         ignored.
    /// </summary>
    [DataField]
    public EntProtoId? Rule;

    [DataField]
    public string RuleArgument = "event";

    /// <summary>
    ///     The rules the model may choose from. Anything else is refused, whatever the parameter's enum says.
    /// </summary>
    [DataField]
    public List<EntProtoId> AllowedRules = [];

    /// <summary>
    ///     What the model is told on success, with <c>{0}</c> standing for the rule's ID. Worth overriding when
    ///         the rule has side effects the model should know about, such as announcing itself.
    /// </summary>
    [DataField]
    public string SuccessMessage = "{0} has been arranged.";

    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        EntProtoId rule;
        if (Rule is { } fixedRule)
        {
            rule = fixedRule;
        }
        else
        {
            var ruleName = context.Arguments.GetString(RuleArgument) ?? string.Empty;
            rule = AllowedRules.Find(allowedRule => string.Equals(allowedRule.Id, ruleName, StringComparison.OrdinalIgnoreCase));
            if (rule == default)
                return KsLlmToolOutcome.Error($"'{ruleName}' is not something you can arrange. Allowed: {string.Join(", ", AllowedRules)}.");
        }

        var ruleId = rule.Id;
        return context.EntityManager.System<GameTicker>().StartGameRule(ruleId)
            ? KsLlmToolOutcome.Ok(string.Format(SuccessMessage, ruleId))
            : KsLlmToolOutcome.Error($"{ruleId} could not be arranged right now.");
    }
}
