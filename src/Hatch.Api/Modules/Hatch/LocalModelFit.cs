using Hatch.Contracts;

namespace Hatch.Api.Modules.Hatch;

/// <summary>
/// Which rows of the board-wide local-models table fit a given runner's own
/// machine - read by <see cref="RunnersController"/> for both the keyed read
/// and every heartbeat answer, so the two never disagree.
/// </summary>
public static class LocalModelFit
{
    /// <summary>
    /// The <see cref="EfHatchLocalModel.OllamaId"/> of every row that fits,
    /// in the table's own order (oldest <see cref="EfHatchLocalModel.CreatedAt"/>
    /// then <see cref="EfHatchLocalModel.Id"/> first). Empty unless
    /// <paramref name="agent"/> is <c>"ollama"</c> and <paramref name="acceleratorMemoryBytes"/>
    /// is known - a runner that never said either holds nothing, whatever the
    /// table has. A row fits when its own footprint is no larger than the
    /// memory reported, and this runner does not refuse it per
    /// <see cref="RunnerModels.Refuses"/>.
    /// </summary>
    public static IReadOnlyList<string> Hold(
        IReadOnlyList<EfHatchLocalModel> models, string? agent, long? acceleratorMemoryBytes, IReadOnlyList<string>? refusedModels)
    {
        if (!string.Equals(agent, "ollama", StringComparison.Ordinal) || acceleratorMemoryBytes is not { } memory)
            return [];

        return models
            .Where(m => m.MemoryFootprintBytes <= memory && !RunnerModels.Refuses(refusedModels, m.OllamaId))
            .Select(m => m.OllamaId)
            .ToList();
    }
}
