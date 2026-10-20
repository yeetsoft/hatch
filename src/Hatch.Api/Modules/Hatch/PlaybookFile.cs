using System.Globalization;
using System.Text;
using Hatch.Contracts;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// <c>.hatch/playbooks.yaml</c> parsed into the same rows a
/// <see cref="PlaybookCreateRequest"/> would produce, or the one sentence
/// saying why not.
///
/// The only parser of the file - nothing in <c>Hatch.Cli</c> or the web touches
/// YAML - and every field rule a row is checked against lives here, shared
/// with <see cref="PlaybooksController"/> and <see cref="IssuePlaybookController"/>
/// rather than written a second time, so the file and the page can never
/// disagree about what is valid.
/// </summary>
/// <remarks>
/// A pure function of its two arguments. The trunk and the sha a refusal names
/// are not known here - the caller that reads the file off a repository's
/// checkout holds those - so <see cref="Parse"/> returns a problem already
/// carrying an entry's position and, where YamlDotNet's own exception or node
/// gives one, a line number; the caller prepends the file path, the trunk and
/// the sha.
/// </remarks>
public static class PlaybookFile
{
    /// <summary>Where this file lives in a repository's checkout - the route owns this path, and no runner spells it itself.</summary>
    public const string Path = ".hatch/playbooks.yaml";

    /// <summary>A playbooks file is a short list of rows; half a megabyte of it is a mistake, not a file.</summary>
    internal const int MaxFileBytes = 512 * 1024;

    private static readonly string[] EntryKeys =
        ["from", "to", "types", "shape", "prompt", "model", "effort", "budget", "briefLimit", "deniedTools", "isCheck"];

    public static (IReadOnlyList<PlaybookCreateRequest>? Rows, string? Problem) Parse(
        byte[] content, IReadOnlyList<EfHatchStatus> statuses, ModelFamilies? families = null)
    {
        if (content.Length > MaxFileBytes)
            return (null, $"is larger than {MaxFileBytes / 1024} KB");

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return (null, "is not valid UTF-8");
        }

        var yaml = new YamlStream();
        try
        {
            yaml.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            return (null, Fail("is not valid YAML - " + ex.Message, ex.Start));
        }

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            return (null, "needs a top-level \"playbooks\" key holding a list");

        if (!root.Children.TryGetValue(new YamlScalarNode("playbooks"), out var playbooksNode))
            return (null, "needs a top-level \"playbooks\" key holding a list");

        foreach (var keyNode in root.Children.Keys)
        {
            var key = ((YamlScalarNode)keyNode).Value ?? "";
            if (key != "playbooks")
                return (null, Fail($"a playbooks file has one key, \"playbooks\" - not \"{key}\" too", keyNode.Start));
        }

        if (playbooksNode is not YamlSequenceNode list)
            return (null, Fail("needs a top-level \"playbooks\" key holding a list", playbooksNode.Start));

        var rows = new List<PlaybookCreateRequest>();
        var seen = new HashSet<(int From, int To, string Types, string Shape)>();

        var position = 0;
        foreach (var node in list.Children)
        {
            position++;

            if (node is not YamlMappingNode entry)
                return (null, FailEntry(position, "a playbook is a mapping of fields - not a list or a scalar", node.Start));

            var (row, problem, problemAt) = ParseEntry(entry, statuses, families);
            if (problem is not null) return (null, FailEntry(position, problem, problemAt));

            var key = (row!.FromStatusId, row.ToStatusId, EfHatchPlaybook.NormalizeTypes(row.Types), row.Shape ?? EfHatchPlaybook.DefaultShape);
            if (!seen.Add(key))
                return (null, FailEntry(position, "there is already a playbook for that transition, those types and that shape", entry.Start));

            rows.Add(row);
        }

        return (rows, null);
    }

    private static (PlaybookCreateRequest? Row, string? Problem, Mark? At) ParseEntry(
        YamlMappingNode entry, IReadOnlyList<EfHatchStatus> statuses, ModelFamilies? families = null)
    {
        foreach (var (keyNode, _) in entry.Children)
        {
            var key = ((YamlScalarNode)keyNode).Value ?? "";
            if (!EntryKeys.Contains(key)) return (null, $"\"{key}\" is not a playbook field", keyNode.Start);
        }

        var fromNode = Scalar(entry, "from");
        var toNode = Scalar(entry, "to");

        if (fromNode is null) return (null, "a playbook needs a column to start from", entry.Start);
        if (toNode is null) return (null, "a playbook needs a column to end in", entry.Start);

        if (FindColumn(statuses, fromNode.Value ?? "") is not { } fromStatus)
            return (null, ColumnProblem(statuses, fromNode.Value ?? ""), fromNode.Start);
        if (FindColumn(statuses, toNode.Value ?? "") is not { } toStatus)
            return (null, ColumnProblem(statuses, toNode.Value ?? ""), toNode.Start);

        if (ReviewRefusal(fromStatus.Id, toStatus.Id, statuses) is { } reviewError)
            return (null, reviewError, entry.Start);

        var types = Sequence(entry, "types");
        var shapeNode = Scalar(entry, "shape");
        var shape = shapeNode?.Value;
        var effectiveShape = shape?.Trim() is { Length: > 0 } s ? s : EfHatchPlaybook.DefaultShape;
        if (InvalidShape(effectiveShape) is { } shapeError)
            return (null, shapeError, shapeNode?.Start ?? entry.Start);

        var promptNode = Scalar(entry, "prompt");
        var prompt = promptNode?.Value ?? "";
        if (InvalidPrompt(prompt.Trim()) is { } promptError)
            return (null, promptError, promptNode?.Start ?? entry.Start);

        var modelNode = Scalar(entry, "model");
        var model = modelNode?.Value;
        var effectiveModel = model?.Trim() is { Length: > 0 } m ? m : EfHatchPlaybook.DefaultModel;
        if (InvalidModel(effectiveModel, families) is { } modelError)
            return (null, modelError, modelNode?.Start ?? entry.Start);

        var effortNode = Scalar(entry, "effort");
        var effort = effortNode?.Value;
        var effectiveEffort = effort?.Trim() is { Length: > 0 } e ? e : EfHatchPlaybook.DefaultEffort;
        if (InvalidEffort(effectiveEffort) is { } effortError)
            return (null, effortError, effortNode?.Start ?? entry.Start);

        var budgetNode = Scalar(entry, "budget");
        if (budgetNode is not null && ParseBudget(budgetNode.Value ?? "").Error is { } budgetError)
            return (null, budgetError, budgetNode.Start);

        var briefLimitNode = Scalar(entry, "briefLimit");
        if (briefLimitNode is not null && ParseBriefLimit(briefLimitNode.Value ?? "").Error is { } briefLimitError)
            return (null, briefLimitError, briefLimitNode.Start);

        var deniedTools = Sequence(entry, "deniedTools");
        var normalizedDeniedTools = EfHatchPlaybook.NormalizeDeniedTools(deniedTools);
        if (InvalidDeniedTools(normalizedDeniedTools) is { } deniedToolsError)
            return (null, deniedToolsError, entry.Start);

        var isCheckNode = Scalar(entry, "isCheck");
        bool? isCheck = null;
        if (isCheckNode is not null)
        {
            if (!bool.TryParse(isCheckNode.Value, out var parsed))
                return (null, $"isCheck is true or false - not \"{isCheckNode.Value}\"", isCheckNode.Start);
            isCheck = parsed;
        }

        var row = new PlaybookCreateRequest(
            fromStatus.Id,
            toStatus.Id,
            types,
            prompt,
            model,
            effort,
            budgetNode?.Value,
            briefLimitNode?.Value,
            shape,
            deniedTools,
            ProjectId: null,
            IsCheck: isCheck);

        return (row, null, null);
    }

    /// <summary>
    /// The one case where <paramref name="from"/> and <paramref name="to"/> may
    /// be equal: the review column naming itself. Null when they differ, or
    /// when they are the review column's own id. Shared by
    /// <see cref="PlaybooksController.Refusal"/> (over the table's rows) and
    /// <see cref="Parse"/> (over the file's rows), so the two can never
    /// disagree about which transition that is.
    /// </summary>
    internal static string? ReviewRefusal(int from, int to, IReadOnlyList<EfHatchStatus> statuses)
    {
        if (from != to) return null;

        var review = Columns.AwaitingReview(statuses.ToList());
        return review?.Id == from
            ? null
            : "a playbook moves an issue between two columns - " +
              (review is null
                  ? "this board has no review column, so none may name itself"
                  : $"only the review column, \"{review.Name}\", may name itself, and that row is the review playbook");
    }

    private static EfHatchStatus? FindColumn(IReadOnlyList<EfHatchStatus> statuses, string name)
    {
        var matches = Matching(statuses, name);
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string ColumnProblem(IReadOnlyList<EfHatchStatus> statuses, string name) =>
        Matching(statuses, name).Count > 1
            ? $"two columns are both called \"{name}\""
            : $"there is no column named \"{name}\"";

    /// <summary>Every column the name folds onto. A name with no letters or digits in it names none.</summary>
    private static List<EfHatchStatus> Matching(IReadOnlyList<EfHatchStatus> statuses, string name)
    {
        var wanted = Normalise(name);
        return wanted.Length == 0 ? [] : statuses.Where(s => Normalise(s.Name) == wanted).ToList();
    }

    /// <summary>
    /// The letters and the digits, in any alphabet, lowercased - the same fold
    /// <c>Hatch.Cli</c>'s <c>Columns.Normalise</c> runs, copied rather than
    /// shared: <c>Hatch.Api</c> does not reference <c>Hatch.Cli</c>, and the
    /// file speaks the names the board's own CLI already folds this way.
    /// </summary>
    private static string Normalise(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        var written = 0;

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c)) buffer[written++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..written]);
    }

    private static YamlScalarNode? Scalar(YamlMappingNode entry, string key) =>
        entry.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value as YamlScalarNode : null;

    private static IReadOnlyList<string>? Sequence(YamlMappingNode entry, string key)
    {
        if (!entry.Children.TryGetValue(new YamlScalarNode(key), out var value)) return null;
        return value is YamlSequenceNode seq ? seq.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").ToList() : null;
    }

    private static string Fail(string problem, Mark at) => $"{problem} (line {at.Line})";

    private static string FailEntry(int position, string problem, Mark? at) =>
        at is { } mark ? $"entry {position}: {problem} (line {mark.Line})" : $"entry {position}: {problem}";

    // ---- Lifted from PlaybooksController, verbatim ----

    internal static string? InvalidPrompt(string prompt) => prompt switch
    {
        "" => "a playbook needs a prompt - it is the whole instruction the agent gets",
        { Length: > EfHatchPlaybook.MaxPromptLength } =>
            $"a prompt is at most {EfHatchPlaybook.MaxPromptLength} characters",
        _ => null,
    };

    /// <summary>
    /// Why this is not a model, in the sentence the operator reads. Shared
    /// with <see cref="PlaybooksController"/> and <see cref="IssuePlaybookController"/>
    /// rather than written twice: an issue's override holds a playbook's
    /// value and a file row holds one too, and three copies of a refusal are
    /// three things to keep in step. Per field, so a request naming only an
    /// effort is not judged on a model it never sent.
    /// </summary>
    internal static string? InvalidModel(string model, ModelFamilies? families = null)
    {
        if (EfHatchPlaybook.IsValidModel(model, families)) return null;

        var examples = string.Join("; ",
            (families ?? ModelFamilies.Default).Rows.Select(r => $"{r.Example} for {r.Agent}"));

        return $"a model is one of {string.Join(", ", EfHatchPlaybook.ModelAliases)}, " +
               $"or a pinned id of a supported agent ({examples}) - not \"{model}\"";
    }

    /// <summary>The same, for the thinking budget.</summary>
    internal static string? InvalidEffort(string effort) =>
        EfHatchPlaybook.IsValidEffort(effort)
            ? null
            : $"an effort is one of {string.Join(", ", EfHatchPlaybook.Efforts)} - not \"{effort}\"";

    /// <summary>The same, for the shape axis.</summary>
    internal static string? InvalidShape(string shape) =>
        EfHatchPlaybook.IsValidShape(shape)
            ? null
            : $"a shape is one of {string.Join(", ", EfHatchPlaybook.Shapes)} - not \"{shape}\"";

    /// <summary>
    /// The two size limits, then each rule's name - the part before an optional
    /// <c>(pattern)</c> - checked against <see cref="ClaudeTools.BuiltIn"/>, <c>mcp__…</c>
    /// and <c>*</c>, with any <c>(pattern)</c> suffix required to be single, balanced
    /// and non-empty.
    /// </summary>
    internal static string? InvalidDeniedTools(IReadOnlyList<string> tools)
    {
        if (tools.Count > EfHatchPlaybook.MaxDeniedTools)
            return $"a playbook denies at most {EfHatchPlaybook.MaxDeniedTools} tools - not {tools.Count}";

        foreach (var rule in tools)
        {
            if (rule.Length > EfHatchPlaybook.MaxToolRuleLength)
                return $"a denied tool rule is at most {EfHatchPlaybook.MaxToolRuleLength} characters";

            if (InvalidToolRule(rule))
                return "a tool rule is a built-in name, \"mcp__…\", or \"*\", each optionally followed " +
                       $"by one (pattern) - not \"{rule}\"";
        }

        return null;
    }

    /// <summary>The rule's name - the part before an optional (pattern) - names a tool, and any parens are balanced and non-empty.</summary>
    internal static bool InvalidToolRule(string rule)
    {
        var paren = rule.IndexOf('(');
        if (paren < 0) return InvalidToolName(rule);

        if (rule[^1] != ')' || rule.Count(c => c == '(') != 1 || rule.Count(c => c == ')') != 1) return true;
        if (paren == rule.Length - 2) return true; // "Name()" - zero characters inside

        return InvalidToolName(rule[..paren]);
    }

    internal static bool InvalidToolName(string name) =>
        name != "*" && !name.StartsWith("mcp__", StringComparison.Ordinal) && !ClaudeTools.BuiltIn.Contains(name);

    /// <summary>
    /// A non-null budget to either a cleared (blank) or set value, or the
    /// sentence saying why neither. Called only once the caller already knows
    /// <paramref name="budget"/> is not null - null means something different
    /// on create and on patch, so that distinction is made by the caller.
    /// </summary>
    internal static (int? Value, string? Error) ParseBudget(string budget)
    {
        var trimmed = budget.Trim();
        if (trimmed.Length == 0) return (null, null);

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
            return (parsed, null);

        return (null, $"a budget is a whole number of one or more, in millions of tokens - not \"{trimmed}\"");
    }

    /// <summary>The same, for the brief's limit.</summary>
    internal static (int? Value, string? Error) ParseBriefLimit(string briefLimit)
    {
        var trimmed = briefLimit.Trim();
        if (trimmed.Length == 0) return (null, null);

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
            return (parsed, null);

        return (null, $"a brief limit is a whole number of one or more, in characters - not \"{trimmed}\"");
    }

    /// <summary>
    /// A parsed <see cref="PlaybookCreateRequest"/> resolved into the stored
    /// row - the same shape/model/effort defaulting and type/denied-tools
    /// normalization <see cref="PlaybooksController.CreatePlaybook"/> applies
    /// before building an <c>EfHatchPlaybook</c>, copied rather than
    /// reinvented. <see cref="Parse"/> already validated every field, so
    /// <see cref="ParseBudget"/>/<see cref="ParseBriefLimit"/> cannot fail
    /// here - <c>.Value</c> is safe.
    /// </summary>
    internal static PlaybookFileRow Resolve(PlaybookCreateRequest row)
    {
        var shape = row.Shape?.Trim() is { Length: > 0 } s ? s : EfHatchPlaybook.DefaultShape;
        var model = row.Model?.Trim() is { Length: > 0 } m ? m : EfHatchPlaybook.DefaultModel;
        var effort = row.Effort?.Trim() is { Length: > 0 } e ? e : EfHatchPlaybook.DefaultEffort;
        var budget = row.Budget is null ? (int?)null : ParseBudget(row.Budget).Value;
        var briefLimit = row.BriefLimit is null ? (int?)null : ParseBriefLimit(row.BriefLimit).Value;
        var types = EfHatchPlaybook.SplitTypes(EfHatchPlaybook.NormalizeTypes(row.Types));
        var deniedTools = EfHatchPlaybook.NormalizeDeniedTools(row.DeniedTools);

        return new PlaybookFileRow(
            row.FromStatusId, row.ToStatusId, types, shape, row.Prompt.Trim(), model, effort,
            budget, briefLimit, deniedTools, row.IsCheck ?? false);
    }

    /// <summary>A stored row read back for the wire, its status ids resolved to the board's current names.</summary>
    internal static PlaybookFileRowDto ToDto(PlaybookFileRow row, IReadOnlyDictionary<int, string> names) => new(
        row.FromStatusId, names.GetValueOrDefault(row.FromStatusId, ""),
        row.ToStatusId, names.GetValueOrDefault(row.ToStatusId, ""),
        row.Types, row.Shape, row.Prompt, row.Model, row.Effort, row.Budget, row.BriefLimit, row.DeniedTools, row.IsCheck);
}
