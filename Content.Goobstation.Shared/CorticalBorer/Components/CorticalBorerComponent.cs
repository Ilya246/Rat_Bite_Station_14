using Content.Shared._Starlight.CollectiveMind;
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
}
