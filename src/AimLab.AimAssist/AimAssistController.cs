using AimLab.Configuration;
using AimLab.Core;
using AimLab.Entities;
using AimLab.Mathematics;
using AimLab.Targeting;

namespace AimLab.AimAssist;

/// <summary>Outcome of one aim assist update.</summary>
public sealed record AimStep(ViewAngles Before, ViewAngles After, bool Applied, int? TargetId, float? ErrorBefore)
{
    public static AimStep Idle(ViewAngles view) => new(view, view, false, null, null);
}

public interface IAimAssistController
{
    AimStep Update(LocalPlayerState local, TargetCandidate? target, AimSettings settings, bool activationHeld);
}

/// <summary>Computes the next smoothed view toward the selected target. It does not apply it.</summary>
public sealed class AimAssistController : IAimAssistController
{
    public AimStep Update(LocalPlayerState local, TargetCandidate? target, AimSettings settings, bool activationHeld)
    {
        var current = local.ViewAngles;
        if (!settings.Enabled || !local.IsAlive || target is null)
        {
            return AimStep.Idle(current);
        }

        var error = AngleMath.AngularDistance(current, target.AimAngles);
        if (settings.RequireActivationKey && !activationHeld)
        {
            return new AimStep(current, current, false, target.Entity.Id, error);
        }

        var next = AngleMath.Smooth(current, target.AimAngles, settings.Smoothing);
        return new AimStep(current, next, true, target.Entity.Id, error);
    }
}
