// SPDX-FileCopyrightText: 2023 Slava0135
// SPDX-FileCopyrightText: 2024 KrasnoshchekovPavel
// SPDX-FileCopyrightText: 2024 Preston Smith
// SPDX-FileCopyrightText: 2024 beck-thompson
// SPDX-FileCopyrightText: 2026 github_actions[bot]
// SPDX-FileCopyrightText: 2026 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Damage.Components;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Damage.Systems;

public sealed class DamageExamineSystem : EntitySystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageExaminableComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
    }

    private void OnGetExamineVerbs(EntityUid uid, DamageExaminableComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var ev = new DamageExamineEvent(new FormattedMessage(), args.User);
        RaiseLocalEvent(uid, ref ev);
        if (!ev.Message.IsEmpty)
        {
            _examine.AddDetailedExamineVerb(args, component, ev.Message,
                Loc.GetString("damage-examinable-verb-text"),
                "/Textures/Interface/VerbIcons/smite.svg.192dpi.png",
                Loc.GetString("damage-examinable-verb-message")
            );
        }
    }

    public void AddDamageExamine(FormattedMessage message, DamageSpecifier damageSpecifier, string? type = null)
    {
        var markup = GetDamageExamine(damageSpecifier, type);
        if (!message.IsEmpty)
        {
            message.PushNewline();
        }
        message.AddMessage(markup);
    }

    /// <summary>
    /// Retrieves the damage examine values.
    /// </summary>
    private FormattedMessage GetDamageExamine(DamageSpecifier damageSpecifier, string? type = null)
    {
        var msg = new FormattedMessage();

        if (string.IsNullOrEmpty(type))
        {
            msg.AddMarkupOrThrow(Loc.GetString("damage-examine"));
        }
        else
        {
            if (damageSpecifier.GetTotal() == FixedPoint2.Zero && !damageSpecifier.AnyPositive())
            {
                msg.AddMarkupOrThrow(Loc.GetString("damage-none"));
                return msg;
            }

            msg.AddMarkupOrThrow(Loc.GetString("damage-examine-type", ("type", type)));
        }

        foreach (var damage in damageSpecifier.DamageDict)
        {
            if (damage.Value != FixedPoint2.Zero)
            {
                msg.PushNewline();
                msg.AddMarkupOrThrow(Loc.GetString("damage-value", ("type", _prototype.Index<DamageTypePrototype>(damage.Key).LocalizedName), ("amount", damage.Value)));
            }
        }

        // KS14 START
        // Show percentile penetration per damage type if present (KS14)
        if (damageSpecifier.PercentilePenetration is { } percentPen && percentPen.Count > 0)
        {
            foreach (var (key, val) in percentPen)
            {
                var typeName = _prototype.Index<DamageTypePrototype>(key).LocalizedName;
                var perc = val * 100f;
                var ap = (int)Math.Round(perc);
                var abs = Math.Abs(ap);
                var arg = abs == 0 ? 0 : ap / abs; // yields 1 for positive, -1 for negative
                msg.PushNewline();
                msg.AddMarkupOrThrow(Loc.GetString("armor-penetration-percentile", ("type", typeName), ("arg", arg), ("abs", abs)));
            }
        }

        // Show flat penetration per damage type if present (KS14)
        if (damageSpecifier.FlatPenetration is { } flatPen && flatPen.Count > 0)
        {
            foreach (var (key, val) in flatPen)
            {
                var typeName = _prototype.Index<DamageTypePrototype>(key).LocalizedName;
                var ap = (int)Math.Round(val);
                var abs = Math.Abs(ap);
                var arg = abs == 0 ? 0 : ap / abs; // yields 1 for positive, -1 for negative
                msg.PushNewline();
                msg.AddMarkupOrThrow(Loc.GetString("armor-penetration-flat", ("type", typeName), ("arg", arg), ("abs", abs)));
            }
        }
        // KS14 END

        return msg;
    }
}
