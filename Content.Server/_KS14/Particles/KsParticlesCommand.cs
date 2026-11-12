using System.Globalization;
using Content.Server.Administration;
using Content.Shared._KS14.Particles;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Server._KS14.Particles;

/// <summary>
///     <c>ksparticles &lt;prototype&gt; [netEntity] [-t seconds]</c>: spawns an emitter on an entity, yours by
///         default, to look at a particle prototype in game. <c>-t</c> gives it a timed despawn.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class KsParticlesCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private KsParticlesSystem _particlesSystem = default!;

    public override string Command => "ksparticles";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Loc.GetString("cmd-ksparticles-invalid-args"));
            return;
        }

        if (!_prototypeManager.TryIndex<KsParticlesPrototype>(args[0], out var prototype))
        {
            shell.WriteError(Loc.GetString("cmd-ksparticles-unknown-prototype", ("prototype", args[0])));
            return;
        }

        EntityUid? targetUid = shell.Player?.AttachedEntity;
        float? lifetime = null;

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "-t")
            {
                if (i + 1 >= args.Length ||
                    !float.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                    !float.IsFinite(seconds) ||
                    seconds <= 0f)
                {
                    shell.WriteError(Loc.GetString("cmd-ksparticles-bad-lifetime"));
                    return;
                }

                lifetime = seconds;
                continue;
            }

            if (!NetEntity.TryParse(args[i], out var netEntity) || !EntityManager.TryGetEntity(netEntity, out targetUid))
            {
                shell.WriteError(Loc.GetString("cmd-ksparticles-bad-target", ("target", args[i])));
                return;
            }
        }

        if (targetUid is not { } target || !EntityManager.EntityExists(target))
        {
            shell.WriteError(Loc.GetString("cmd-ksparticles-no-target"));
            return;
        }

        var uid = _particlesSystem.SpawnPvs(prototype.ID, target);
        if (uid is not { } emitterUid)
            return;

        if (lifetime is { } seconds2)
            EntityManager.EnsureComponent<TimedDespawnComponent>(emitterUid).Lifetime = seconds2;

        shell.WriteLine(Loc.GetString("cmd-ksparticles-success",
            ("prototype", prototype.ID),
            ("emitter", EntityManager.GetNetEntity(emitterUid)),
            ("target", EntityManager.ToPrettyString(target))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<KsParticlesPrototype>(proto: _prototypeManager), Loc.GetString("cmd-ksparticles-prototype-completion")),
            _ => CompletionResult.FromHint(Loc.GetString("cmd-ksparticles-target-completion")),
        };
    }
}

/// <summary>
///     <c>ksparticles_clear</c>: deletes every emitter entity made by <c>ksparticles</c> or the spawn helpers.
///         Components on other entities are left alone.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class KsParticlesClearCommand : LocalizedEntityCommands
{
    public override string Command => "ksparticles_clear";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var deleted = 0;
        var query = EntityManager.AllEntityQueryEnumerator<KsParticlesComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var metaDataComponent))
        {
            if (metaDataComponent.EntityPrototype?.ID != KsSharedParticlesSystem.ParticlesEntity.Id)
                continue;

            EntityManager.QueueDeleteEntity(uid);
            deleted++;
        }

        shell.WriteLine(Loc.GetString("cmd-ksparticles_clear-success", ("count", deleted)));
    }
}
