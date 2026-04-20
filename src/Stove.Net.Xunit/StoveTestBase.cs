using System.Runtime.CompilerServices;
using Stove.Net.Core;
using Xunit;

namespace Stove.Net.Xunit;

/// <summary>
/// Optional base class for Stove test classes. Automatically notifies the Stove event bus
/// when each test starts and ends, enabling per-test grouping in event listeners
/// (e.g., console reporter and dashboard emitter).
///
/// Usage:
/// <code>
/// public class MyTests : StoveTestBase&lt;MyFixture&gt;, IClassFixture&lt;MyFixture&gt;
/// {
///     public MyTests(MyFixture fixture) : base(fixture) { }
///
///     [Fact]
///     public async Task Product_is_returned()
///         => await Validate(async ctx =>
///             await ctx.Http().GetAsync&lt;Product&gt;("/api/products/1",
///                 p => Assert.Equal("Widget", p.Name)));
/// }
/// </code>
/// </summary>
public abstract class StoveTestBase<TFixture>(TFixture fixture) : IAsyncLifetime
    where TFixture : IStoveFixture
{
    private readonly string _testId = Guid.NewGuid().ToString("N");
    private bool _testPassed = true;
    private string? _testError;

    /// <summary>The fixture that owns the Stove instance.</summary>
    protected TFixture Fixture { get; } = fixture;

    /// <summary>The Stove instance from the fixture.</summary>
    protected StoveInstance Stove => Fixture.Stove;

    /// <summary>
    /// Wraps Stove.Validate with failure tracking so OnTestEnded reports the correct status.
    /// Use this instead of Stove.Validate directly if you want accurate pass/fail in the dashboard.
    /// </summary>
    protected async Task Validate(Func<ValidationDsl, Task> test, [CallerMemberName] string testName = "")
    {
        try
        {
            await Stove.Validate(test, testName);
        }
        catch (Exception ex)
        {
            _testPassed = false;
            _testError = ex.Message;
            throw;
        }
    }

    public virtual ValueTask InitializeAsync()
    {
        Stove.NotifyTestStarted(_testId, GetTestName(), GetSpecName(), GetTestPath());
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask DisposeAsync()
    {
        Stove.NotifyTestEnded(_testPassed, _testError);
        return ValueTask.CompletedTask;
    }

    /// <summary>Returns the test method name using xUnit v3 TestContext when available.</summary>
    protected virtual string GetTestName()
        => TestContext.Current?.TestCase?.TestMethodName
           ?? TestContext.Current?.TestCase?.TestCaseDisplayName
           ?? GetType().Name;

    /// <summary>Returns the spec/class name shown in the dashboard.</summary>
    protected virtual string GetSpecName()
        => TestContext.Current?.TestCase?.TestClassName
           ?? GetType().FullName
           ?? GetType().Name;

    /// <summary>
    /// Builds a hierarchical test path from xUnit TestContext for dashboard visualization.
    /// Returns segments like ["Namespace", "ClassName", "MethodName"].
    /// </summary>
    protected virtual string[]? GetTestPath()
    {
        var className = TestContext.Current?.TestCase?.TestClassName;
        var methodName = TestContext.Current?.TestCase?.TestMethodName;
        if (className == null || methodName == null) return null;

        var parts = new List<string>();
        var lastDot = className.LastIndexOf('.');
        if (lastDot > 0)
        {
            parts.Add(className[..lastDot]); // namespace
            parts.Add(className[(lastDot + 1)..]); // class
        }
        else
        {
            parts.Add(className);
        }
        parts.Add(methodName);
        return parts.ToArray();
    }
}