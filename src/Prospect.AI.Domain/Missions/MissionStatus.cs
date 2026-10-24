namespace Prospect.AI.Domain.Missions;

public enum MissionStatus
{
    /// <summary>Collected, waiting for AI qualification (phase 2).</summary>
    ToQualify,

    /// <summary>Analyzed and scored.</summary>
    Qualified,

    /// <summary>Set aside by the user or by a disqualifying rule.</summary>
    Dismissed,
}
