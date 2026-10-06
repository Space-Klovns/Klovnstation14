using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.PopupLocalization;

/// <summary>A short list of translated labels joined after recipient formatting.</summary>
public readonly record struct KsPopupMessageList(string Separator, KsPopupMessage[] Messages)
{
    public string Format(IEntityManager entities)
    {
        var values = new string[Messages.Length];
        for (var index = 0; index < Messages.Length; index++)
            values[index] = Messages[index].Format(entities);
        return string.Join(Separator, values);
    }

    public KsPopupPayloadList ToPayload(IEntityManager entities)
    {
        var messages = new KsPopupPayload[Messages.Length];
        for (var index = 0; index < Messages.Length; index++)
            messages[index] = Messages[index].ToPayload(entities);
        return new KsPopupPayloadList(Separator, messages);
    }
}

[Serializable, NetSerializable]
public sealed class KsPopupPayloadList(string separator, KsPopupPayload[] messages)
{
    public string Separator = separator;
    public KsPopupPayload[] Messages = messages;

    public string Format(IEntityManager entities)
        => TryFormat(entities, out var text) ? text : "";

    public bool TryFormat(IEntityManager entities, out string text)
    {
        text = "";
        var values = new string[Messages.Length];
        for (var index = 0; index < Messages.Length; index++)
        {
            if (!Messages[index].TryFormat(entities, out values[index]))
                return false;
        }
        text = string.Join(Separator, values);
        return true;
    }
}
