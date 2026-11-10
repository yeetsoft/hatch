namespace Hatch.Cli;

/// <summary>
/// The runner's own voice on the board: one call, sent between increments,
/// saying what this process is and reading back what the board would like it to
/// do next.
/// </summary>
/// <remarks>
/// <para>Nothing here can end a night. A heartbeat that does not answer - an
/// origin that is down, a Hatch too old to have the route at all - comes back
/// as no instruction, and a loop with no instruction is a loop running on the
/// flags it was started with, which is exactly what it did before this existed.
/// That is the same judgement <see cref="Claim.BeatAsync"/> makes about
/// weather, and it is why this is not built out of the throwing lane.</para>
///
/// <para>The four bounds go up on every beat and are read on none: the server
/// writes them when it first sees the session and never again, so what a person
/// set at midnight survives the loop's own half-hourly restart. Sending them
/// each time is what makes the row show the truth from its first appearance
/// without the runner having to know whether it is new.</para>
/// </remarks>
public sealed class Runners(HatchClient client)
{
    private const string Path = "/api/hatch/runners/sessions";

    private static RunnerSessionRefusalDto? HeldBy(string body)
    {
        try
        {
            var held = System.Text.Json.JsonSerializer.Deserialize(body, HatchJson.Default.RunnerSessionRefusalDto);
            return held is { Error: not null } ? held : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// This process is leaving: ends its own session so a fresh loop started
    /// straight afterwards in the same checkout is not refused for it. Never
    /// throws and never ends anything - a session that could not be ended is
    /// marked gone by the server once it has been quiet long enough, which is
    /// what happened before this existed.
    /// </summary>
    public async Task EndAsync(int sessionId, CancellationToken ct)
    {
        try
        {
            await client.Send(HttpMethod.Post, $"{Path}/{sessionId}/end", null, ct);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or HatchException)
        {
        }
    }

    /// <summary>
    /// Still here. Answers what the board would like, or the one refusal that
    /// is not weather.
    /// </summary>
    public async Task<RunnerBeat> BeatAsync(RunnerHeartbeatRequest beat, CancellationToken ct)
    {
        var answer = await client.Send(HttpMethod.Post, Path, beat, ct);

        // The one answer this is not: a live loop is already at this `where`,
        // and going on would be two live runners heartbeating one row - which is
        // exactly the merge the board's own refusal exists to prevent.
        if (answer.Conflict) return new RunnerBeat(null, answer.Sentence, HeldBy(answer.Body));

        if (!answer.Ok || answer.Body.Trim().Length == 0) return new RunnerBeat(null, null);

        try
        {
            var instruction =
                System.Text.Json.JsonSerializer.Deserialize(answer.Body, HatchJson.Default.RunnerInstructionDto);
            return new RunnerBeat(instruction, null);
        }
        catch (System.Text.Json.JsonException)
        {
            // An answer this version cannot read is the same as no answer. A
            // night is not worth losing to a field somebody added.
            return new RunnerBeat(null, null);
        }
    }
}

/// <summary>
/// What a heartbeat came back with: an instruction to fold in, or the sentence
/// that ends a run - never both, since a refused beat never reached the row
/// this process would have read an instruction off. <paramref name="Held"/> is
/// the refusal's structured body - when the held session last beat and how long
/// until it is marked gone - or null for an older Hatch whose <c>409</c> is a
/// bare sentence, which is refused at once as it always was.
/// </summary>
public readonly record struct RunnerBeat(
    RunnerInstructionDto? Instruction, string? Refusal, RunnerSessionRefusalDto? Held = null);
