namespace Stove.Net.Core.Exceptions;

/// <summary>
/// Wraps a test assertion failure with the Stove execution report.
/// The report is appended to the exception message so it's always visible
/// in test output, regardless of how the test runner handles stderr.
/// Inspired by Kotlin Stove's enrichIfFailed() pattern.
/// </summary>
public sealed class StoveTestFailureException : Exception
{
    public StoveTestFailureException(Exception inner, string report)
        : base(FormatMessage(inner, report), inner)
    {
        Report = report;
    }

    /// <summary>The rendered Stove execution report.</summary>
    public string Report { get; }

    private static string FormatMessage(Exception inner, string report)
        => $"{inner.Message}{Environment.NewLine}{Environment.NewLine}{report}";
}
