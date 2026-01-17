// Ratbite - whole file
using Content.Shared.Anomaly;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.CorticalBorer.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class DarkPresenceComponent : Component
{
    [DataField]
    public float TakeControlAccumulator = 0f;

    /// <summary>
    /// If not null, we're undergoing the full control countdown.
    /// </summary>
    [DataField]
    public TimeSpan? TakeControlTime = null;

    [DataField]
    public DarkPresenceStage Stage = DarkPresenceStage.Begin;

    /// <summary>
    /// For how long have we been outside a host.
    /// </summary>
    [DataField]
    public float OutsideAccumulator = 0f;

    /// <summary>
    /// How long to be outside of a host to be able to infect another.
    /// </summary>
    [DataField]
    public TimeSpan ReinfectThreshold = TimeSpan.FromMinutes(5);

    [DataField]
    public ProtoId<DamageTypePrototype> AllowedDamage = "Holy";

    [DataField]
    public List<AnomalousParticleType> AllowedParticleTypes = new() { AnomalousParticleType.Delta, AnomalousParticleType.Epsilon, AnomalousParticleType.Zeta, AnomalousParticleType.Sigma };

    [DataField]
    public AnomalousParticleType DamagingType; // chosen on init
}

[Serializable, NetSerializable]
public enum DarkPresenceStage : Byte
{
    Begin,
    Percent50,
    Percent75
}
