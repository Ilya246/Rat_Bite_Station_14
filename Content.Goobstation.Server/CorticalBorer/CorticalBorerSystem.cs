using Content.Goobstation.Shared.CorticalBorer;
using Content.Goobstation.Shared.CorticalBorer.Components;
using Content.Server.Anomaly.Components;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.DoAfter;
using Content.Server.Ghost;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Shared.Actions;
using Content.Shared.Administration.Logs;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.MedicalScanner;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Objectives.Systems;
using Content.Shared.Popups;
using Content.Shared.Species.Components;
using Content.Shared.StatusEffect;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Goobstation.Server.CorticalBorer;

public sealed partial class CorticalBorerSystem : SharedCorticalBorerSystem
{
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly GhostRoleSystem _ghostRole = default!;
    [Dependency] private readonly GhostSystem _ghost = default!;
    [Dependency] private readonly HealthAnalyzerSystem _analyzer = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ISharedAdminLogManager _admin = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedObjectivesSystem _objective = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAbilities();

        SubscribeLocalEvent<CorticalBorerComponent, CheckTargetedSpeechEvent>(OnSpeakEvent);

        SubscribeLocalEvent<CorticalBorerComponent, MindRemovedMessage>(OnMindRemoved);

        SubscribeLocalEvent<CorticalBorerInfestedComponent, DamageChangedEvent>(OnDamageChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var infestedQuery = EntityQueryEnumerator<CorticalBorerInfestedComponent>();
        while (infestedQuery.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime >= comp.ControlTimeEnd)
                EndControl((uid, comp));
        }

        var darkQuery = EntityQueryEnumerator<DarkPresenceComponent, CorticalBorerComponent>();
        while (darkQuery.MoveNext(out var uid, out var dark, out var borer))
        {
            if (borer.Host is not { } host)
            {
                dark.OutsideAccumulator += frameTime;
                if (dark.OutsideAccumulator > dark.ReinfestThreshold.TotalSeconds)
                {
                    // does nothing if action exists
                    if (TryComp<ActionsComponent>(uid, out var actions)
                        && (dark.ReinfestAction is not { } reinfest
                            || !actions.Actions.Contains(reinfest))
                        )
                        dark.ReinfestAction = Actions.AddAction(uid, borer.InfestAction, component: actions);

                    dark.OutsideAccumulator = 0f;
                }

                dark.TakeControlAccumulator = 0f;
                dark.TakeControlTime = null;
                continue;
            }
            else
            {
                dark.OutsideAccumulator = 0f;

                if (dark.TakeControlTime == null)
                {
                    dark.TakeControlAccumulator = 0f;
                    continue;
                }
            }

            dark.TakeControlAccumulator += frameTime;
            var duration = dark.TakeControlTime.Value.TotalSeconds;
            var progress = dark.TakeControlAccumulator / duration;

            switch (dark.Stage)
            {
                case DarkPresenceStage.Begin:
                    if (progress >= 0.5f)
                    {
                        Popup.PopupEntity(Loc.GetString("dark-presence-takeover-50"), host, host, PopupType.LargeCaution);
                        Popup.PopupEntity(Loc.GetString("dark-presence-takeover-50"), uid, uid, PopupType.LargeCaution);
                        dark.Stage = DarkPresenceStage.Percent50;
                    }
                    break;
                case DarkPresenceStage.Percent50:
                    if (progress >= 0.75f)
                    {
                        Popup.PopupEntity(Loc.GetString("dark-presence-takeover-75"), host, host, PopupType.LargeCaution);
                        Popup.PopupEntity(Loc.GetString("dark-presence-takeover-75"), uid, uid, PopupType.LargeCaution);
                        dark.Stage = DarkPresenceStage.Percent75;
                    }
                    break;
                case DarkPresenceStage.Percent75:
                    if (progress >= 1f)
                    {
                        TakeControlHost((uid, borer), Comp<CorticalBorerInfestedComponent>(host), true);
                    }
                    break;
            }
        }
    }

    private void OnSpeakEvent(Entity<CorticalBorerComponent> ent, ref CheckTargetedSpeechEvent args)
    {
        args.ChatTypeIgnore.Add(InGameICChatType.CollectiveMind);

        if (!ent.Comp.Host.HasValue)
            return;

        args.Targets.Add(ent);
        args.Targets.Add(ent.Comp.Host.Value);
    }

    public bool TryToggleCheckBlood(Entity<CorticalBorerComponent> ent)
    {
        if (!TryComp<UserInterfaceComponent>(ent, out var uic))
            return false;

        if (!TryComp<HealthAnalyzerComponent>(ent, out var health))
            return false;

        // If open - close
        if (UI.IsUiOpen((ent, uic), HealthAnalyzerUiKey.Key))
        {
            UI.CloseUi((ent, uic), HealthAnalyzerUiKey.Key, ent.Owner);
            if (health.ScannedEntity.HasValue)
                _analyzer.StopAnalyzingEntity((ent, health), health.ScannedEntity.Value);
            return true;
        }

        if (!ent.Comp.Host.HasValue || !TryComp<BloodstreamComponent>(ent.Comp.Host.Value, out _))
            return false;

        UI.OpenUi((ent, uic), HealthAnalyzerUiKey.Key, ent.Owner);
        _analyzer.BeginAnalyzingEntity((ent, health), ent.Comp.Host.Value);

        return true;
    }

    public void TakeControlHost(Entity<CorticalBorerComponent> ent, CorticalBorerInfestedComponent infestedComp, bool permanent = false)
    {
        var (worm, comp) = ent;

        if (comp.Host is not { } host)
            return;

        // make sure they aren't dead, would throw the worm into a ghost mode and just kill em
        if (TryComp<MobStateComponent>(host, out var mobState)
            && mobState.CurrentState == MobState.Dead
        )
            return;

        if (!permanent
            && TryComp<MindContainerComponent>(host, out var mindContainer)
            && (mindContainer.HasMind
                || HasComp<GhostRoleComponent>(host))
        )
            infestedComp.ControlTimeEnd = _timing.CurTime + comp.ControlDuration;

        if (_mind.TryGetMind(worm, out var wormMind, out _))
            infestedComp.BorerMindId = wormMind;

        if (_mind.TryGetMind(host, out var controledMind, out var controlledComp))
        {
            if (permanent)
            {
                _ghost.OnGhostAttempt(controledMind, false, forced: true, mind: controlledComp);
                infestedComp.OriginalMindId = null;
            }
            else
            {
                infestedComp.OriginalMindId = controledMind; // set this var here just in case somehow the mind changes from when the infestation started

                // fish head...
                var dummy = Spawn("FoodMeatFish", MapCoordinates.Nullspace);
                Container.Insert(dummy, infestedComp.ControlContainer);

                _mind.TransferTo(controledMind, dummy);

                Popup.PopupEntity(Loc.GetString("racortical-borer-lost-control"), dummy, dummy, PopupType.LargeCaution);
            }
        }
        else
        {
            infestedComp.OriginalMindId = null;
        }

        comp.ControlingHost = true;
        _mind.TransferTo(wormMind, host);

        if (TryComp<GhostRoleComponent>(worm, out var ghostRole))
            _ghostRole.UnregisterGhostRole((worm, ghostRole)); // prevent players from taking the worm role once mind isn't in the worm

        // add the end control and vomit egg action
        if (Actions.AddAction(host, ent.Comp.EndControlAction) is { } actionEnd)
            infestedComp.RemoveAbilities.Add(actionEnd);

        if (TryComp<ReformComponent>(host, out var reformComp) && reformComp.ActionEntity.HasValue)
        {
            infestedComp.RemovedReformAction = reformComp.ActionEntity.Value;

            Actions.RemoveAction(host, reformComp.ActionEntity.Value);
        }

        var str = $"{ToPrettyString(worm)} has taken control over {ToPrettyString(host)}";

        Log.Info(str);
        _admin.Add(LogType.Mind, LogImpact.High, $"{ToPrettyString(worm)} has taken control over {ToPrettyString(host)}");
        _chat.SendAdminAlert(str);
    }

    public void EndControl(Entity<CorticalBorerInfestedComponent> host)
    {
        var (infested, infestedComp) = host;

        if (!TryComp<CorticalBorerComponent>(infestedComp.Borer, out var borerComp))
            return;

        if (!borerComp.ControlingHost)
            return;

        borerComp.ControlingHost = false;

        // remove all the actions set to remove
        foreach (var ability in infestedComp.RemoveAbilities)
        {
            Actions.RemoveAction(infested, ability);
        }
        infestedComp.RemoveAbilities = new(); // clear out the list

        if (infestedComp.RemovedReformAction.HasValue && TryComp<ReformComponent>(host, out var reformComp))
        {
            var restoredAction = Actions.AddAction(host, reformComp.ActionPrototype);

            if (restoredAction != null)
            {
                reformComp.ActionEntity = restoredAction.Value;
            }

            infestedComp.RemovedReformAction = null;
        }

        if (TryComp<GhostRoleComponent>(infestedComp.Borer, out var ghostRole))
            _ghostRole.RegisterGhostRole((infestedComp.Borer, ghostRole)); // re-enable the ghost role after you return to the body

        // Return everyone to their own bodies
        if (!TerminatingOrDeleted(infestedComp.BorerMindId))
            _mind.TransferTo(infestedComp.BorerMindId, infestedComp.Borer);
        if (!TerminatingOrDeleted(infestedComp.OriginalMindId) && infestedComp.OriginalMindId.HasValue)
            _mind.TransferTo(infestedComp.OriginalMindId.Value, infested);

        infestedComp.ControlTimeEnd = null;
        Container.CleanContainer(infestedComp.ControlContainer);
    }

    private void OnMindRemoved(Entity<CorticalBorerComponent> ent, ref MindRemovedMessage args)
    {
        return; // Ratbite - remove and properly handle for dark presence if cortical borer ported
        if (!ent.Comp.ControlingHost)
            TryEjectBorer(ent); // No storing them in hosts if you don't have a soul
    }

    private void OnDamageChanged(Entity<CorticalBorerInfestedComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (args.DamageDelta is not { } delta)
            return;

        var borer = ent.Comp.Borer;
        if (!TryComp<DarkPresenceComponent>(borer, out var presence))
            return;

        // passthrough all damage from correct particle or all holy damage
        if (args.Origin is { } origin
            && TryComp<AnomalousParticleComponent>(origin, out var particle)
            && particle.ParticleType == presence.DamagingType)
        {
            _damageable.TryChangeDamage(borer, delta);
        }
        else if (delta.DamageDict.TryGetValue(presence.AllowedDamage, out var holyDmg))
        {
            var spec = new DamageSpecifier(Proto.Index(presence.AllowedDamage), holyDmg);
            _damageable.TryChangeDamage(borer, spec);
        }
    }
}
