using System.Runtime.CompilerServices;

namespace Stove.Net.Core.Testing;

/// <summary>
/// Optional base class for Stove test classes.
/// Provides convenience methods that delegate to the <see cref="StoveInstance"/>,
/// reducing boilerplate in test classes.
///
/// <para>
/// Since <see cref="StoveInstance.Validate"/> already manages the full test lifecycle
/// (NotifyTestStarted/Ended, trace context, failure detection), this class is a thin
/// wrapper that removes repetitive <c>fixture.Stove.Xxx()</c> calls.
/// </para>
///
/// <example>
/// <code>
/// public class MyTests(MyFixture fixture)
///     : StoveTestBase(fixture.Stove), IClassFixture&lt;MyFixture&gt;, IAsyncLifetime
/// {
///     public async ValueTask InitializeAsync() => await CleanupAsync();
///     public ValueTask DisposeAsync() => ValueTask.CompletedTask;
///
///     [Fact]
///     public async Task My_test() => await Validate(async s =>
///     {
///         await s.Http(async http => { /* assertions */ });
///     });
/// }
/// </code>
/// </example>
/// </summary>
public abstract class StoveTestBase
{
    /// <summary>The <see cref="StoveInstance"/> to use for test execution.</summary>
    protected StoveInstance Stove { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="StoveTestBase"/> with the given
    /// <see cref="StoveInstance"/>.
    /// </summary>
    /// <param name="stove">
    /// The Stove instance, typically obtained from a shared fixture
    /// (e.g., <c>fixture.Stove</c>).
    /// </param>
    protected StoveTestBase(StoveInstance stove)
    {
        Stove = stove;
    }

    /// <summary>
    /// Execute a test validation block with automatic lifecycle management.
    /// Delegates to <see cref="StoveInstance.Validate"/>, which handles
    /// NotifyTestStarted/Ended, trace context propagation, and failure detection.
    /// </summary>
    /// <param name="validation">
    /// The validation callback that receives a <see cref="ValidationDsl"/> for
    /// accessing registered systems (HTTP, PostgreSQL, WireMock, etc.).
    /// </param>
    /// <param name="testName">
    /// The test name, automatically captured from the calling method via
    /// <see cref="CallerMemberNameAttribute"/>.
    /// </param>
    protected Task Validate(
        Func<ValidationDsl, Task> validation,
        [CallerMemberName] string testName = "")
        => Stove.Validate(validation, testName);

    /// <summary>
    /// Clean up all registered systems between tests.
    /// Call from <c>IAsyncLifetime.InitializeAsync()</c> to reset state before each test.
    /// </summary>
    protected Task CleanupAsync() => Stove.CleanupAsync();
}
