using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

public sealed partial class LobbyCharacterPreviewPanel
{
    /// <summary>
    ///     Sets the name and job lines shown under the character sprite. An empty value hides its line.
    /// </summary>
    public void SetCharacterInfo(string name, string job)
    {
        CharacterName.Text = name;
        CharacterName.Visible = !string.IsNullOrEmpty(name);
        CharacterJob.Text = job;
        CharacterJob.Visible = !string.IsNullOrEmpty(job);
    }

    /// <summary>
    ///     The localised name of the profile's highest-priority job: High, else the first Medium. Empty if none.
    /// </summary>
    public static string GetPreferredJobName(HumanoidCharacterProfile profile, IPrototypeManager prototypeManager)
    {
        ProtoId<JobPrototype>? mediumJobId = null;

        foreach (var (jobId, priority) in profile.JobPriorities)
        {
            if (priority == JobPriority.High)
                return GetJobName(jobId, prototypeManager);

            if (priority == JobPriority.Medium && mediumJobId == null)
                mediumJobId = jobId;
        }

        return mediumJobId == null ? string.Empty : GetJobName(mediumJobId.Value, prototypeManager);
    }

    private static string GetJobName(ProtoId<JobPrototype> jobId, IPrototypeManager prototypeManager)
    {
        return prototypeManager.TryIndex(jobId, out var jobPrototype) ? jobPrototype.LocalizedName : string.Empty;
    }
}
