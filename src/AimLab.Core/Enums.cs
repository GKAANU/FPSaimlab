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

/// <summary>Training scenario driving target spawns and behaviour.</summary>
public enum TrainingMode
{
    /// <summary>Free-roaming bots on two teams (original research arena).</summary>
    Arena,

    /// <summary>One static target at a time; it respawns elsewhere after being hit.</summary>
    Flick,

    /// <summary>One strafing target that is never eliminated; measures continuous tracking.</summary>
    Tracking,

    /// <summary>Several targets at once; each hit target respawns, forcing switches.</summary>
    TargetSwitch,
}

/// <summary>How training targets become visible to the local player.</summary>
public enum VisibilityScenario
{
    /// <summary>No walls; every target is always exposed.</summary>
    AlwaysVisible,

    /// <summary>Arena walls block line of sight.</summary>
    WallOcclusion,

    /// <summary>No walls; each target alternates between exposed and hidden on a timer.</summary>
    Peek,
}
