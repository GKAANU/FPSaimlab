using System.Numerics;
using AimLab.Configuration;
using AimLab.Core;
using AimLab.Entities;
using AimLab.Mathematics;

namespace AimLab.Targeting;

/// <summary>A target that passed every filter, with the values used to rank it.</summary>
public sealed record TargetCandidate(
    PlayerEntity Entity,
    Vector3 AimPoint,
    ViewAngles AimAngles,
    float CrosshairDistance,
    float Distance);

public interface ITargetSelector
{
    /// <summary>All valid candidates, best first.</summary>
    IReadOnlyList<TargetCandidate> EvaluateCandidates(LocalPlayerState local, IEnumerable<PlayerEntity> players, AimSettings settings);

    TargetCandidate? SelectTarget(LocalPlayerState local, IEnumerable<PlayerEntity> players, AimSettings settings);
}

/// <summary>
/// Filters out the local player, teammates, eliminated players, hidden players (when the visibility
/// check is on) and anything outside the FOV cone, then ranks by the configured priority.
/// </summary>
public sealed class TargetSelector : ITargetSelector
{
    public IReadOnlyList<TargetCandidate> EvaluateCandidates(LocalPlayerState local, IEnumerable<PlayerEntity> players, AimSettings settings)
    {
        if (local.Entity is not { IsAlive: true } me)
        {
            return [];
        }

        var eye = local.EyePosition;
        var view = local.ViewAngles;
        var fovRadius = settings.Fov / 2f;
        var candidates = new List<TargetCandidate>();

        foreach (var p in players)
        {
            if (p.Id == me.Id || !p.IsAlive || p.Team == me.Team || p.Team == Team.None)
            {
                continue;
            }

            if (settings.VisibilityCheck && !p.IsVisible)
            {
                continue;
            }

            var aimPoint = p.GetAimPoint(settings.Region);
            var angles = AngleMath.CalculateAngles(eye, aimPoint);
            var crosshair = AngleMath.AngularDistance(view, angles);
            if (crosshair > fovRadius)
            {
                continue;
            }

            candidates.Add(new TargetCandidate(p, aimPoint, angles, crosshair, Vector3.Distance(eye, aimPoint)));
        }

        // Ties fall back to crosshair distance, then id, so ordering is deterministic.
        Comparison<TargetCandidate> primary = settings.Priority switch
        {
            TargetPriority.ClosestDistance => (a, b) => a.Distance.CompareTo(b.Distance),
            TargetPriority.LowestHealth => (a, b) => a.Entity.Health.CompareTo(b.Entity.Health),
            _ => (a, b) => a.CrosshairDistance.CompareTo(b.CrosshairDistance),
        };

        candidates.Sort((a, b) =>
        {
            var c = primary(a, b);
            if (c == 0) c = a.CrosshairDistance.CompareTo(b.CrosshairDistance);
            return c != 0 ? c : a.Entity.Id.CompareTo(b.Entity.Id);
        });
        return candidates;
    }

    public TargetCandidate? SelectTarget(LocalPlayerState local, IEnumerable<PlayerEntity> players, AimSettings settings)
    {
        var all = EvaluateCandidates(local, players, settings);
        return all.Count > 0 ? all[0] : null;
    }
}
