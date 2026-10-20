namespace Hatch.Contracts;

/// <summary>
/// Which issue types run children the way an epic does - dispatch, WIP,
/// overnight and attention rules all key off this, not off "epic" by name.
/// </summary>
public static class IssueTypeKinds
{
    private static readonly HashSet<string> RunsChildrenSet = ["epic", "reflection"];

    /// <summary>The same types as <see cref="RunsChildren"/>, for a database query, which cannot translate the method.</summary>
    public static readonly IReadOnlyList<string> RunsChildrenTypes = ["epic", "reflection"];

    public static bool RunsChildren(string type) => RunsChildrenSet.Contains(type);
}
