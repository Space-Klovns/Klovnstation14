using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Serialization;
using Robust.Shared.Toolshed.TypeParsers;

namespace Content.Shared._KS14.PopupLocalization;

/// <summary>A Fluent message kept unformatted until it reaches the recipient.</summary>
public readonly record struct KsPopupMessage(string Id, (string, object)[] Arguments, bool LowerCase = false)
{
    public static KsPopupMessage Create(string id, params (string, object)[] arguments) => new(id, arguments);
    public KsPopupMessage ToLower() => this with { LowerCase = true };

    public string Format(IEntityManager entities)
    {
        // Ordinary prediction keeps the original argument array and native entity
        // handles. Only deferred arguments require an additional formatting array.
        var arguments = Arguments;
        for (var index = 0; index < Arguments.Length; index++)
        {
            var value = Arguments[index].Item2;
            if (value is not KsPopupMessage && value is not KsPopupPrototypeName && value is not KsPopupMessageList)
                continue;
            if (ReferenceEquals(arguments, Arguments))
                arguments = (ValueTuple<string, object>[]) Arguments.Clone();
            arguments[index] = (Arguments[index].Item1, value switch
            {
                KsPopupMessage nested => nested.Format(entities),
                KsPopupPrototypeName prototype => prototype.Format(),
                KsPopupMessageList list => list.Format(entities),
                _ => value,
            });
        }
        var text = Loc.GetString(Id, arguments);
        return LowerCase ? text.ToLower() : text;
    }

    public KsPopupPayload ToPayload(IEntityManager entities)
    {
        if (Arguments.Length == 0)
            return new KsPopupPayload(Id, null, LowerCase);
        var arguments = new Dictionary<string, object>(Arguments.Length);
        foreach (var (name, value) in Arguments)
        {
            arguments.Add(name, value switch
            {
                EntityUid entity => entities.GetNetEntity(entity),
                IAsType<EntityUid> entity => entities.GetNetEntity(entity.AsType()),
                KsPopupMessage nested => nested.ToPayload(entities),
                KsPopupPrototypeName prototype => prototype,
                KsPopupMessageList list => list.ToPayload(entities),
                Color color => color.ToHex(),
                Enum enumeration => enumeration.ToString().ToLowerInvariant(),
                LocValueString text => text.Value,
                LocValueNumber number => number.Value,
                string or bool or byte or sbyte or short or ushort or int or uint or long or ulong
                    or float or double or DateTime or TimeSpan => value,
                _ => value.ToString() ?? "",
            });
        }
        return new KsPopupPayload(Id, arguments, LowerCase);
    }
}

[Serializable, NetSerializable]
public sealed class KsPopupPayload(string id, Dictionary<string, object>? arguments, bool lowerCase = false)
{
    public string Id = id;
    public Dictionary<string, object>? Arguments = arguments;
    public bool LowerCase = lowerCase;

    public string Format(IEntityManager entities)
    {
        if (Arguments == null)
        {
            var literal = Loc.GetString(Id);
            return LowerCase ? literal.ToLower() : literal;
        }
        var arguments = new (string, object)[Arguments.Count];
        var index = 0;
        foreach (var (name, value) in Arguments)
        {
            arguments[index++] = (name, value switch
            {
                NetEntity entity => entities.GetEntity(entity),
                KsPopupPayload nested => nested.Format(entities),
                KsPopupPrototypeName prototype => prototype.Format(),
                KsPopupPayloadList list => list.Format(entities),
                _ => value,
            });
        }
        var text = Loc.GetString(Id, arguments);
        return LowerCase ? text.ToLower() : text;
    }
}
