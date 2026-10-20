namespace Hatch.Contracts;

/// <summary>
/// Which issue types run children the way an epic does - dispatch, WIP,
/// overnight and attention rules all key off this, not off "epic" by name.
/// </summary>
public static class IssueTypeKinds
{
    private static readonly HashSet<string> RunsChildrenTypes = ["epic", "reflection"];

    public static bool RunsChildren(string type) => RunsChildrenTypes.Contains(type);
}
