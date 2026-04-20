using Stove.Net.Core;

namespace Stove.Net.Xunit;

/// <summary>
/// Implemented by StoveFixture to expose the Stove instance for use in StoveTestBase.
/// </summary>
public interface IStoveFixture
{
    StoveInstance Stove { get; }
}
