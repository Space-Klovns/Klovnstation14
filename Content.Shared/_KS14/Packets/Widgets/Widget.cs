using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Packets.Widgets;

[Serializable, NetSerializable]
public sealed class Widget : WidgetElement
{
    public WidgetElement[] Elements = [];

    public Widget(string id)
    {
        Id = id;
    }
}

[Serializable] [NetSerializable]
public abstract class WidgetElement
{
    public string Id = "";

    public int[] Margin = [0, 0, 0, 0];
    public int[] Size = [150, 50];

    public string HorizontalAligment = "Stretch";
    public string VerticalAligment = "Stretch";
}

[Serializable, NetSerializable]
public sealed class WidgetButton : WidgetElement
{
    public string Name;
    public string MethodName;

    public WidgetButton(string name, string id, string methodName)
    {
        Name = name;
        Id = id;
        MethodName = methodName;
    }
}

[Serializable, NetSerializable]
public sealed class WidgetContainer : WidgetElement
{
    public WidgetElement[] Elements = [];

    public WidgetContainer(string id)
    {
        Id = id;
    }
}

[Serializable] [NetSerializable]
public sealed class WidgetLabel : WidgetElement
{
    public string Text;

    public WidgetLabel(string name, string id, string methodName)
    {
        Text = name;
        Id = id;
    }
}

[Serializable] [NetSerializable]
public sealed class WidgetTextEdit : WidgetElement
{
    public bool ReadOnly;

    public WidgetTextEdit(bool readOnly, string id)
    {
        Id = id;
        ReadOnly = readOnly;
    }
}
