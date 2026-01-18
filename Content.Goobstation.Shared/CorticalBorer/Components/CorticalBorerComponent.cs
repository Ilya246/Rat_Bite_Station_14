using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Shared.CorticalBorer.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class CorticalBorerComponent : Component
{
    /// <summary>
    /// Host of this Borer
    /// </summary>
    [ViewVariables]
    public EntityUid? Host = null;

    /// <summary>
    /// The max duration you can take control of your host
    /// </summary>
    [DataField]
    public TimeSpan ControlDuration = TimeSpan.FromSeconds(40);

    [DataField]
    public bool ControlingHost;

    [DataField]
    public ComponentRegistry? AddOnInfest;

    [DataField]
    public ComponentRegistry? RemoveOnInfest;

    [DataField]
    public EntProtoId EndControlAction = "ActionEndControlHost";

    [DataField]
    public EntProtoId InfestAction = "ActionCorticalBorerInfest";

    /// <summary>
    /// Respawn us as this prototype when infesting a host.
    /// </summary>
    [DataField]
    public EntProtoId? InfestPrototype = null;

    [DataField]
    public SoundSpecifier? InfestSound = new SoundPathSpecifier("/Audio/Effects/gib1.ogg");

    [DataField]
    public LocId? InfestPopupBorer = "cortical-borer-infest-borer";

    [DataField]
    public LocId? InfestPopupHost = "cortical-borer-infest-host";

    [DataField]
    public SoundSpecifier? DeathSound = new SoundPathSpecifier("/Audio/Effects/gib1.ogg");

    [DataField]
    public LocId? DeathPopup = "cortical-borer-death";

    /// <summary>
    /// For how long to stun the host on infest.
    /// </summary>
    [DataField]
    public TimeSpan InfestStunDuration = TimeSpan.FromSeconds(5);

    /// <summary>
    /// For how long to stun the host on eject.
    /// </summary>
    [DataField]
    public TimeSpan EjectStunDuration = TimeSpan.FromSeconds(5);
}
