using AimLab.Configuration;
using AimLab.Core;
using AimLab.Entities;
using AimLab.Mathematics;
using AimLab.Targeting;

namespace AimLab.AimAssist;

/// <summary>
/// Outcome of one aim update. Target analysis (<see cref="TargetId"/>, errors) is always filled when a target
/// exists, whether or not the assist moved the camera.
/// <see cref="ErrorBefore"/> is the angular error of <see cref="Before"/> (the view after manual input, before
/// the assist), <see cref="ErrorAfter"/> the error of <see cref="After"/> (the view at the end of the tick).
/// </summary>
public sealed record AimStep(
    ViewAngles Before,
    ViewAngles After,
    bool Applied,
    int? TargetId,
    float? ErrorBefore,
    float? ErrorAfter)
{
    public static AimStep Idle(ViewAngles view) => new(view, view, false, null, null, null);
}

public interface IAimAssistController
{
    AimStep Update(LocalPlayerState local, TargetCandidate? target, AimSettings settings, bool activationHeld);
}

/// <summary>Analyses the selected target and, only when enabled and activated, computes a smoothed step toward it.</summary>
public sealed class AimAssistController : IAimAssistController
{
    public AimStep Update(LocalPlayerState local, TargetCandidate? target, AimSettings settings, bool activationHeld)
    {
        var current = local.ViewAngles;
        if (!local.IsAlive || target is null)
        {
            return AimStep.Idle(current);
        }

        var errorBefore = AngleMath.AngularDistance(current, target.AimAngles);
        var apply = settings.Enabled && (!settings.RequireActivationKey || activationHeld);
        var next = apply ? AngleMath.Smooth(current, target.AimAngles, settings.Smoothing) : current;
        var errorAfter = apply ? AngleMath.AngularDistance(next, target.AimAngles) : errorBefore;
        return new AimStep(current, next, apply, target.Entity.Id, errorBefore, errorAfter);
    }
}
