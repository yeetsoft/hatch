using System.Diagnostics;
using System.Text;

namespace Hatch.Cli;

/// <summary>
/// The spawn, generic over which agent's CLI is being run - see <see
/// cref="IAgentAdapter"/> for the half of this that differs by agent.
/// <paramref name="onError"/> takes each line the child writes to its standard
/// error, exactly as written; with none, those lines are dropped.
/// </summary>
public sealed class AgentSessionRunner(IAgentAdapter adapter, Action<string>? onError = null) : ISessionRunner, IUsageProbe
{
    public bool CanSpawn(out string refusal) => adapter.CanSpawn(out refusal);

    public async Task<SessionResult> RunAsync(SessionRequest request, Action<string>? onLine, CancellationToken ct)
    {
        var start = adapter.BuildHeadless(request);

        // The adapters build their own start info and none redirects this. Left
        // alone, the child's stderr is the runner's own console, under the
        // readout and outside its row count.
        start.RedirectStandardError = true;

        try
        {
            using var process = new Process { StartInfo = start };
            process.Start();

            // The prompt goes in on stdin rather than as an argument - it is long,
            // and an argument list is the one place where "long" has a limit worth
            // avoiding. An adapter that delivers it another way is left with a
            // stdin that reads EOF rather than the terminal.
            Task writing;
            if (adapter.PromptOnStdin)
            {
                writing = Task.Run(async () =>
                {
                    try
                    {
                        await process.StandardInput.WriteAsync(request.Prompt);
                        process.StandardInput.Close();
                    }
                    catch (IOException)
                    {
                        // The child ended - or was stopped - before it read the
                        // prompt. That is its exit code's to report, not a reason
                        // for the spawn to throw and lose it.
                    }
                }, CancellationToken.None);
            }
            else
            {
                if (start.RedirectStandardInput) process.StandardInput.Close();
                writing = Task.CompletedTask;
            }

            var collected = new StringBuilder();
            var reading = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
                {
                    if (onLine is null) collected.AppendLine(line);
                    else onLine(line);
                }
            }, CancellationToken.None);

            // Read beside stdout, so a child that fills either pipe cannot stall
            // on the other.
            var errors = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } line)
                    onError?.Invoke(line);
            }, CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                Stop(process);
            }

            // Whatever was written before the kill is still worth having, and the
            // reader ends of its own accord once the pipe closes.
            await Task.WhenAll(writing, reading, errors).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None);

            return new SessionResult(process.ExitCode, collected.ToString());
        }
        finally
        {
            adapter.Release(request);
        }
    }

    public async Task<int> AttachAsync(SessionRequest request, CancellationToken ct)
    {
        var start = adapter.BuildAttached(request);

        try
        {
            using var process = new Process { StartInfo = start };
            process.Start();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                Stop(process);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            return process.ExitCode;
        }
        finally
        {
            adapter.Release(request);
        }
    }

    public Task<IReadOnlyList<string>?> ProbeAsync(string tempDirectory, CancellationToken ct) =>
        adapter.Probe?.ProbeAsync(tempDirectory, ct) ?? Task.FromResult<IReadOnlyList<string>?>(null);

    /// <summary>
    /// The whole tree, because a session has spawned tools of its own and a
    /// lease that is over should not leave a `make test` running for four more
    /// minutes on a ticket somebody else now holds.
    /// </summary>
    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or SystemException)
        {
            // It ended between the question and the answer, which is the
            // outcome that was being asked for.
        }
    }
}
