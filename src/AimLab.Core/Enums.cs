namespace AimLab.Core;

/// <summary>Team identifiers used by the simulation memory layout.</summary>
public enum Team
{
    None = 0,
    Attackers = 2,
    Defenders = 3,
}

/// <summary>Body region the aim assist steers toward.</summary>
public enum BodyRegion
{
    Head,
    Body,
}

/// <summary>Rule used to rank valid target candidates.</summary>
public enum TargetPriority
{
    ClosestToCrosshair,
    ClosestDistance,
    LowestHealth,
}
