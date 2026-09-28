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
/// writes them when it first sees the name and never again, so what a person
/// set at midnight survives the loop's own half-hourly restart. Sending them
/// each time is what makes the row show the truth from its first appearance
/// without the runner having to know whether it is new.</para>
/// </remarks>
public sealed class Runners(HatchClient client, string name)
{
    private readonly string _path = $"/api/hatch/runners/{Uri.EscapeDataString(name)}";

    /// <summary>
    /// Still here. Answers what the board would like, or the one refusal that
    /// is not weather.
    /// </summary>
    public async Task<RunnerBeat> BeatAsync(RunnerHeartbeatRequest beat, CancellationToken ct)
    {
        var answer = await client.Send(HttpMethod.Post, _path, beat, ct);

        // The one answer this is not: this name is already live somewhere else,
        // and going on would be two live runners heartbeating one row - which is
        // exactly the merge the board's own refusal exists to prevent.
        if (answer.Conflict) return new RunnerBeat(null, answer.Sentence);

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
/// this process would have read an instruction off.
/// </summary>
public readonly record struct RunnerBeat(RunnerInstructionDto? Instruction, string? Refusal);
