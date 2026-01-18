using Content.Goobstation.Common.CorticalBorer;
using Content.Goobstation.Shared.CorticalBorer.Components;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.MedicalScanner;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;

namespace Content.Goobstation.Shared.CorticalBorer;

public abstract class SharedCorticalBorerSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ISerializationManager _serManager = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    [Dependency] protected readonly IPrototypeManager Proto = default!;
    [Dependency] protected readonly SharedActionsSystem Actions = default!;
    [Dependency] protected readonly SharedAudioSystem Audio = default!;
    [Dependency] protected readonly SharedContainerSystem Container = default!;
    [Dependency] protected readonly SharedPopupSystem Popup = default!;
    [Dependency] protected readonly SharedUserInterfaceSystem UI = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CorticalBorerComponent, AttemptMeleeEvent>(OnAttempt);
        SubscribeLocalEvent<CorticalBorerComponent, MobStateChangedEvent>(OnMobStateChange);

        SubscribeLocalEvent<DarkPresenceComponent, MapInitEvent>(OnPresenceInit);

        SubscribeLocalEvent<CorticalBorerInfestedComponent, CheckCorticalBorerEvent>(OnCheck);
        SubscribeLocalEvent<CorticalBorerInfestedComponent, EjectCorticalBorerEvent>(OnEject);
    }

    private void OnEject(Entity<CorticalBorerInfestedComponent> ent, ref EjectCorticalBorerEvent args)
    {
        if (ent.Comp.InfestationContainer.ContainedEntities.Count != 0)
            TryEjectBorer(ent.Comp.Borer);
    }

    private void OnCheck(Entity<CorticalBorerInfestedComponent> ent, ref CheckCorticalBorerEvent args)
    {
        args.Found = true;
    }

    private void OnAttempt(Entity<CorticalBorerComponent> ent, ref AttemptMeleeEvent args)
    {
        if (ent.Comp.Host != null)
            args.Cancelled = true;
    }

    private void OnPresenceInit(Entity<DarkPresenceComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.DamagingType = _random.Pick(ent.Comp.AllowedParticleTypes);
    }

    private void OnMobStateChange(Entity<CorticalBorerComponent> ent, ref MobStateChangedEvent args)
    {
        // eject us if we die in host
        if (args.NewMobState != MobState.Alive)
        {
            TryEjectBorer(ent);

            Audio.PlayPvs(ent.Comp.DeathSound, ent);
            if (ent.Comp.DeathPopup is { } popup)
                Popup.PopupEntity(Loc.GetString(popup), ent, PopupType.Large);
        }
    }

    public void InfestTarget(Entity<CorticalBorerComponent> ent, EntityUid target)
    {
        var (uid, comp) = ent;

        if (ent.Comp.InfestPrototype is { } infestProto)
        {
            var newUid = Spawn(infestProto, Transform(uid).Coordinates);
            if (!_mind.TryGetMind(ent, out var borerMind, out _)
                || !TryComp<CorticalBorerComponent>(newUid, out var newComp))
            {
                Del(newUid);
                return;
            }

            _mind.TransferTo(borerMind, newUid);
            QueueDel(ent);

            ent = (uid, comp) = (newUid, newComp);
        }

        // Make sure the infected person is infected right
        var infestedComp = EnsureComp<CorticalBorerInfestedComponent>(target);

        // Make sure they get into the target
        if (!Container.Insert(uid, infestedComp.InfestationContainer))
        {
            // oh no it didn't work somehow so remove the comp you just added...
            RemCompDeferred<CorticalBorerInfestedComponent>(target);
            return;
        }

        // Set up the Borer
        infestedComp.Borer = ent;
        comp.Host = target;

        if (comp.AddOnInfest is not null)
        {
            foreach (var (_, compReg) in comp.AddOnInfest)
            {
                var compType = compReg.Component.GetType();
                if (HasComp(ent, compType))
                    continue;

                var newComp = (Component) _serManager.CreateCopy(compReg.Component, notNullableOverride: true);
                EntityManager.AddComponent(ent, newComp, true);
            }
        }

        if (comp.RemoveOnInfest is not null)
        {
            foreach (var (_, compReg) in comp.RemoveOnInfest)
            {
                RemCompDeferred(ent, compReg.Component.GetType());
            }
        }

        Audio.PlayEntity(ent.Comp.InfestSound, ent, ent);
        Audio.PlayEntity(ent.Comp.InfestSound, target, ent);
        if (ent.Comp.InfestPopupBorer is { } popupBorer)
            Popup.PopupEntity(Loc.GetString(popupBorer), ent, ent, PopupType.LargeCaution);
        if (ent.Comp.InfestPopupHost is { } popupHost)
            Popup.PopupEntity(Loc.GetString(popupHost), ent, target, PopupType.LargeCaution);

        if (ent.Comp.InfestStunDuration.TotalSeconds != 0)
            _stun.TryStun(target, ent.Comp.InfestStunDuration, true);

        if (TryComp<DamageableComponent>(ent, out var damComp))
            _damage.SetAllDamage(ent, damComp, 0);
    }

    public bool TryEjectBorer(Entity<CorticalBorerComponent> ent)
    {
        if (ent.Comp.Host is not { } host)
            return false;

        if (TerminatingOrDeleted(ent.Owner))
            return false;

        // Make sure they get out of the host
        if (!Container.TryRemoveFromContainer(ent.Owner))
            return false;

        // close all the UIs that relate to host
        if (TryComp<UserInterfaceComponent>(ent, out var uic))
        {
            UI.CloseUi((ent.Owner, uic), HealthAnalyzerUiKey.Key);
        }

        RemCompDeferred<CorticalBorerInfestedComponent>(ent.Comp.Host.Value);
        ent.Comp.Host = null;

        if (ent.Comp.RemoveOnInfest is not null)
        {
            foreach (var (_, compReg) in ent.Comp.RemoveOnInfest)
            {
                var compType = compReg.Component.GetType();
                if (HasComp(ent, compType))
                    continue;

                var newComp = (Component) _serManager.CreateCopy(compReg.Component, notNullableOverride: true);
                EntityManager.AddComponent(ent, newComp, true);
            }
        }

        if (ent.Comp.AddOnInfest is not null)
        {
            foreach (var (_, compReg) in ent.Comp.AddOnInfest)
            {
                RemCompDeferred(ent, compReg.Component.GetType());
            }
        }

        if (TryComp<DarkPresenceComponent>(ent, out var dark))
            dark.OriginalHost = host;

        if (ent.Comp.EjectStunDuration.TotalSeconds != 0)
            _stun.TryStun(host, ent.Comp.EjectStunDuration, true);

        return true;
    }
}

public sealed class InfestHostAttempt : CancellableEntityEventArgs
{
    /// <summary>
    ///     The equipment that is blocking the entrance
    /// </summary>
    public EntityUid? Blocker = null;
}
