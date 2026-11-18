namespace AimLab.Core;

/// <summary>State of the user-controlled activation input.</summary>
public interface IInputState
{
    bool IsActivationHeld { get; }
}

/// <summary>Thread-safe mutable input state, written by the UI and read by the engine.</summary>
public sealed class ManualInputState : IInputState
{
    private volatile bool _held;

    public bool IsActivationHeld
    {
        get => _held;
        set => _held = value;
    }
}

/// <summary>Receives camera adjustments. Implemented only by the simulated game world.</summary>
public interface IAimSink
{
    ViewAngles CurrentView { get; }

    void ApplyView(ViewAngles view);
}
