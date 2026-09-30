using System.Runtime.CompilerServices;

namespace Hatch.Api.Tests;

/// <summary>
/// Raises the process's minimum thread pool size before the first test runs -
/// the fix for HA-169's build failure, which was not a hang but a stall this
/// close to one: <c>WebApplicationFactory.CreateClient()</c> starts its host
/// with a blocking <c>Task.Wait()</c>, Microsoft's own choice inside
/// <c>HostingAbstractionsHostExtensions.Start</c>, not anything under this
/// assembly's control. That wait needs a worker to run every hosted service's
/// <c>StartAsync</c> - Quartz's among them - and the pool's default floor is
/// <see cref="Environment.ProcessorCount"/>, which on CI's two-core runner is
/// as small as two. A run with this many parallel collections keeps every
/// floor-level worker busy often enough that the one blocked in
/// <see cref="MinimalStartupTests"/> can find nothing free to run its own
/// continuation on - and the pool's injector only adds one more roughly every
/// half second once it notices, which is the gap that became the 106-second
/// silence before HA-169's CI run was killed and dumped.
///
/// <para>A module initializer so it runs once per process, ahead of every
/// test in this assembly rather than only <see cref="MinimalStartupTests"/>'s
/// own - the same stall is latent anywhere a test blocks a pool thread
/// waiting on work that needs another one.</para>
/// </summary>
internal static class ThreadPoolFloor
{
    [ModuleInitializer]
    internal static void RaiseIt()
    {
        var floor = Environment.ProcessorCount * 8;
        ThreadPool.SetMinThreads(floor, floor);
    }
}
