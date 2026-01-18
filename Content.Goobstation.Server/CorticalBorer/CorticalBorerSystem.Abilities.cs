using System.Linq;
using Content.Goobstation.Common.Changeling;
using Content.Goobstation.Shared.CorticalBorer;
using Content.Goobstation.Shared.CorticalBorer.Components;
using Content.Goobstation.Shared.Devil;
using Content.Goobstation.Shared.SlaughterDemon;
using Content.Server.Body.Components;
using Content.Shared._Shitmed.Damage;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Part;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Speech.Muting;

namespace Content.Goobstation.Server.CorticalBorer;

public sealed partial class CorticalBorerSystem
{
    private void SubscribeAbilities()
    {
        SubscribeLocalEvent<CorticalBorerComponent, CorticalInfestEvent>(OnInfest);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalInfestDoAfterEvent>(OnInfestDoAfter);

        SubscribeLocalEvent<CorticalBorerComponent, CorticalEjectEvent>(OnEjectHost);
        SubscribeLocalEvent<CorticalBorerComponent, CorticalTakeControlEvent>(OnTakeControl);

        SubscribeLocalEvent<CorticalBorerComponent, CorticalCheckBloodEvent>(OnCheckBlood);

        SubscribeLocalEvent<CorticalBorerInfestedComponent, CorticalEndControlEvent>(OnEndControl);

        // Ratbite: Dark Presence action events
        // if you're porting cortical borer, make sure the other events ignore things cortical borer would normally need
        SubscribeLocalEvent<DarkPresenceComponent, DarkPresenceEvolveEvent>(OnDarkEvolve);
        SubscribeLocalEvent<DarkPresenceComponent, DarkPresenceDamageHostEvent>(OnDarkDamageHost);
        SubscribeLocalEvent<DarkPresenceComponent, DarkPresenceMuteHostEvent>(OnDarkMuteHost);
        SubscribeLocalEvent<DarkPresenceComponent, DarkPresenceTakeControlEvent>(OnDarkTakeControl);
        SubscribeLocalEvent<DarkPresenceComponent, DarkPresenceReattachEvent>(OnDarkReattach);
    }

    private void OnInfest(Entity<CorticalBorerComponent> ent, ref CorticalInfestEvent args)
    {
        var (uid, comp) = ent;
        var target = args.Target;
        var targetIdentity = Identity.Entity(target, EntityManager);

        if (comp.Host is not null)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-has-host"), uid, uid, PopupType.Medium);
            return;
        }

        if (HasComp<CorticalBorerInfestedComponent>(target))
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-host-already-infested", ("target", targetIdentity)), uid, uid, PopupType.Medium);
            return;
        }

        if (IsInvalidHost(target))
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-invalid-host", ("target", targetIdentity)), uid, uid, PopupType.Medium);
            return;
        }

        var infestAttempt = new InfestHostAttempt();
        RaiseLocalEvent(target, infestAttempt);

        if (infestAttempt.Cancelled)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-face-covered", ("target", targetIdentity)), uid, uid, PopupType.Medium);
            return;
        }

        Popup.PopupEntity(Loc.GetString("cortical-borer-start-infest", ("target", targetIdentity)), uid, uid, PopupType.Medium);

        var infestArgs = new DoAfterArgs(EntityManager, uid, TimeSpan.FromSeconds(3), new CorticalInfestDoAfterEvent(), uid, target)
        {
            DistanceThreshold = 1.5f,
            BreakOnDamage = true,
            BreakOnMove = true,
            BreakOnWeightlessMove = true,
            AttemptFrequency = AttemptFrequency.StartAndEnd,
            Hidden = true,
        };

        _doAfter.TryStartDoAfter(infestArgs);
    }

    // anything with bloodstream, BUT NOT THIS!!!
    private bool IsInvalidHost(EntityUid target)
    {
        return !HasComp<BloodstreamComponent>(target) ||
               HasComp<CorticalBorerComponent>(target) ||
               HasComp<DevilComponent>(target) ||
               HasComp<SlaughterDemonComponent>(target) ||
               HasComp<ChangelingComponent>(target);
    }

    private void OnInfestDoAfter(Entity<CorticalBorerComponent> ent, ref CorticalInfestDoAfterEvent args)
    {
        if (args.Handled)
            return;

        if (args.Args.Target is not { } target)
            return;

        if (args.Cancelled || HasComp<CorticalBorerInfestedComponent>(target))
            return;

        if (!HasComp<BloodstreamComponent>(target) || HasComp<CorticalBorerComponent>(target))
            return;

        if (TryComp<DarkPresenceComponent>(ent, out var dark) && !TerminatingOrDeleted(dark.ReinfestAction))
        {
            Actions.RemoveAction(ent, dark.ReinfestAction);
            dark.ReinfestAction = null;
        }

        InfestTarget(ent, target);
        args.Handled = true;
    }

    private void OnEjectHost(Entity<CorticalBorerComponent> ent, ref CorticalEjectEvent args)
    {
        if (args.Handled)
            return;

        var (uid, comp) = ent;

        if (comp.Host is null)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), uid, uid, PopupType.Medium);
            return;
        }

        if (TryEjectBorer(ent))
            args.Handled = true;
    }

    private void OnCheckBlood(Entity<CorticalBorerComponent> ent, ref CorticalCheckBloodEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Host is null)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), ent, ent, PopupType.Medium);
            return;
        }

        if (TryToggleCheckBlood(ent))
            args.Handled = true;
    }

    private void OnTakeControl(Entity<CorticalBorerComponent> ent, ref CorticalTakeControlEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Host is null)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), ent, ent, PopupType.Medium);
            return;
        }

        // Host is dead, you can't take control
        if (TryComp<MobStateComponent>(ent.Comp.Host, out var mobState) &&
            mobState.CurrentState == MobState.Dead)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-dead-host"), ent, ent, PopupType.Medium);
            return;
        }

        if (!TryComp<CorticalBorerInfestedComponent>(ent.Comp.Host, out var infestedComp))
            return;

        // idk how you would cause this...
        if (ent.Comp.ControlingHost)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-already-control"), ent, ent, PopupType.Medium);
            return;
        }

        TakeControlHost(ent, infestedComp);

        args.Handled = true;
    }

    private void OnEndControl(Entity<CorticalBorerInfestedComponent> host, ref CorticalEndControlEvent args)
    {
        if (args.Handled)
            return;

        EndControl(host);

        args.Handled = true;
    }

    private void OnDarkEvolve(Entity<DarkPresenceComponent> ent, ref DarkPresenceEvolveEvent args)
    {
        if (args.Handled)
            return;

        if (!_mind.TryGetMind(ent, out var mind, out var mindComp))
            return;

        // check if our objectives are completed
        foreach (var objUid in mindComp.Objectives)
        {
            if (!_objective.IsCompleted(objUid, (mind, mindComp)))
            {
                Popup.PopupEntity(Loc.GetString("dark-presence-need-objectives"), ent, ent, PopupType.MediumCaution);
                return;
            }
        }

        // proceed with evolution
        Actions.RemoveAction(ent, args.Action, action: args.Action.Comp);

        foreach (var protoId in args.ActionProtos)
            Actions.AddAction(ent, protoId);

        // grant a hand
        EnsureComp<HandsComponent>(ent);
        EnsureComp<ComplexInteractionComponent>(ent);
        var hand = Spawn("LeftHandHuman", Transform(ent).Coordinates);
        var part = Comp<BodyPartComponent>(hand);

        var attachAt = _body.GetBodyChildrenOfType(ent, BodyPartType.Arm).FirstOrDefault();
        if (attachAt == default)
            attachAt = _body.GetBodyChildren(ent).First();

        var slotId = $"{part.Symmetry.ToString().ToLower()} {part.GetHashCode().ToString()}";
        part.SlotId = part.GetHashCode().ToString();

        if (!_body.TryCreatePartSlotAndAttach(attachAt.Id, slotId, hand, BodyPartType.Hand, BodyPartSymmetry.Right, attachAt.Component, part))
            QueueDel(hand);

        Popup.PopupEntity(Loc.GetString("dark-presence-evolved"), ent, ent, PopupType.Large);
        args.Handled = true;
    }

    private void OnDarkDamageHost(Entity<DarkPresenceComponent> ent, ref DarkPresenceDamageHostEvent args)
    {
        if (args.Handled || !TryComp<CorticalBorerComponent>(ent, out var borer))
            return;

        if (borer.Host is not { } hostUid)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), ent, ent, PopupType.Medium);
            return;
        }

        _damageable.TryChangeDamage(hostUid, args.Amount, true, targetPart: TargetBodyPart.All, ignoreBlockers: true, splitDamage: SplitDamageBehavior.SplitEnsureAll, canMiss: false);

        Audio.PlayEntity(args.Sound, ent, ent);
        Audio.PlayEntity(args.Sound, hostUid, ent);

        args.Handled = true;
    }

    private void OnDarkMuteHost(Entity<DarkPresenceComponent> ent, ref DarkPresenceMuteHostEvent args)
    {
        if (args.Handled || !TryComp<CorticalBorerComponent>(ent, out var borer))
            return;

        if (borer.Host is not { } hostUid)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), ent, ent, PopupType.Medium);
            return;
        }

        _status.TryAddStatusEffect<MutedComponent>(hostUid, "Muted", args.Duration, true);

        args.Handled = true;
    }

    private void OnDarkTakeControl(Entity<DarkPresenceComponent> ent, ref DarkPresenceTakeControlEvent args)
    {
        if (args.Handled || !TryComp<CorticalBorerComponent>(ent, out var borer))
            return;

        if (borer.Host is not { } host)
        {
            Popup.PopupEntity(Loc.GetString("cortical-borer-no-host"), ent, ent, PopupType.Medium);
            return;
        }

        // cancel if active
        var active = ent.Comp.TakeControlTime != null;
        if (active)
        {
            Popup.PopupEntity(Loc.GetString("dark-presence-takeover-host-cancel"), host, host, PopupType.LargeCaution);
            Popup.PopupEntity(Loc.GetString("dark-presence-takeover-presence-cancel"), ent, ent, PopupType.LargeCaution);

            ent.Comp.TakeControlTime = null;
        }
        else
        {
            Popup.PopupEntity(Loc.GetString("dark-presence-takeover-host-begin"), host, host, PopupType.LargeCaution);
            Popup.PopupEntity(Loc.GetString("dark-presence-takeover-presence-begin"), ent, ent, PopupType.LargeCaution);

            ent.Comp.TakeControlTime = args.Duration;
        }

        args.Handled = true;
    }

    private void OnDarkReattach(Entity<DarkPresenceComponent> ent, ref DarkPresenceReattachEvent args)
    {
        if (!TryComp<CorticalBorerComponent>(ent, out var borer))
            return;

        if (borer.Host != null)
        {
            Popup.PopupEntity(Loc.GetString("dark-presence-reattach-in-host"), ent, ent, PopupType.SmallCaution);
            return;
        }

        if (ent.Comp.OriginalHost is not { } host || TerminatingOrDeleted(host))
        {
            Popup.PopupEntity(Loc.GetString("dark-presence-reattach-no-host"), ent, ent, PopupType.MediumCaution);
            return;
        }

        if (!Transform(host).Coordinates.TryDistance(EntityManager, Transform(ent).Coordinates, out var distance)
            || distance > args.Range)
        {
            Popup.PopupEntity(Loc.GetString("dark-presence-reattach-too-far"), ent, ent, PopupType.SmallCaution);
            return;
        }

        InfestTarget((ent, borer), host);
    }
}
