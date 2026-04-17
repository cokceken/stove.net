namespace Stove.Net.Core;

/// <summary>
/// Implemented by systems that need to expose configuration to the application under test.
/// For example, a PostgreSQL system exposes its connection string so the app can connect
/// to the Testcontainer instead of a real database.
/// </summary>
public interface IExposesConfiguration
{
    IEnumerable<KeyValuePair<string, string>> Configuration();
}
