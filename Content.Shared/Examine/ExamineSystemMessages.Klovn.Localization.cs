using Content.Shared.Localizations;
namespace Content.Shared.Examine;

public static partial class ExamineSystemMessages
{
    public sealed partial class RequestExamineInfoMessage
    {
        public string ClientLocale { get; set; } = ContentLocalizationManager.DefaultCultureName;
    }
}
