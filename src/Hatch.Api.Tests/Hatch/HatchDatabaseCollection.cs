namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The one scratch database <see cref="HatchDatabase"/> hands out, which every
/// test that takes it empties first.
///
/// <para>xUnit gives each test class its own collection and runs collections in
/// parallel, so two classes that both call <c>PrepareAsync</c> truncate each
/// other's rows mid-test: a row one has just saved is gone when it reads it
/// back, and which test fails changes from run to run. Naming the classes into
/// one collection is where xUnit enforces that they take turns.</para>
/// </summary>
[CollectionDefinition(Name)]
public class HatchDatabaseCollection
{
    public const string Name = "Hatch database";
}
