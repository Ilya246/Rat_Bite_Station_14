using Content.Shared.Actions;
using Content.Shared.Damage;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Shared.CorticalBorer;

public sealed partial class CorticalInfestEvent : EntityTargetActionEvent;

public sealed partial class CorticalEjectEvent : InstantActionEvent;

public sealed partial class CorticalCheckBloodEvent : InstantActionEvent;

public sealed partial class CorticalTakeControlEvent : InstantActionEvent;

public sealed partial class CorticalEndControlEvent : InstantActionEvent;

public sealed partial class DarkPresenceEvolveEvent : InstantActionEvent
{
    /// <summary>
    /// Actions to grant on evolve.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> ActionProtos;
}

public sealed partial class DarkPresenceDamageHostEvent : InstantActionEvent
{
    [DataField(required: true)]
    public DamageSpecifier Amount;
}

public sealed partial class DarkPresenceMuteHostEvent : InstantActionEvent
{
    [DataField(required: true)]
    public TimeSpan Duration;
}

public sealed partial class DarkPresenceTakeControlEvent : InstantActionEvent
{
    /// <summary>
    /// How long to take to take control.
    /// </summary>
    [DataField(required: true)]
    public TimeSpan Duration = TimeSpan.FromMinutes(5);
}
