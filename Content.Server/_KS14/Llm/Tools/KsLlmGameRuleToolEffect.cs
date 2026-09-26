using Content.Server.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Starts one of a fixed set of game rules, named by the model. Generic: which rules, and how the model is
///         told about them, is entirely the tool prototype's YAML.
/// </summary>
/// <remarks>
///     Give the tool's parameter an <c>enum</c> of exactly <see cref="AllowedRules"/>, with an
///         <c>enumDescriptions</c> entry each, so the model knows what it is choosing between. Anything not in
///         <see cref="AllowedRules"/> is refused here regardless.
/// </remarks>
public sealed partial class KsLlmGameRuleToolEffect : KsLlmToolEffect
{
    [DataField]
    public string RuleArgument = "event";

    [DataField(required: true)]
    public List<EntProtoId> AllowedRules = [];

    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        var ruleName = context.Arguments.GetString(RuleArgument) ?? string.Empty;
        var rule = AllowedRules.Find(allowedRule => string.Equals(allowedRule.Id, ruleName, StringComparison.OrdinalIgnoreCase));
        if (rule == default)
            return KsLlmToolOutcome.Error($"'{ruleName}' is not something you can arrange. Allowed: {string.Join(", ", AllowedRules)}.");

        var ruleId = rule.Id;
        return context.EntityManager.System<GameTicker>().StartGameRule(ruleId)
            ? KsLlmToolOutcome.Ok($"{ruleId} has been arranged.")
            : KsLlmToolOutcome.Error($"{ruleId} could not be arranged right now.");
    }
}
