namespace Stove.Net.Core;

/// <summary>
/// Implemented by systems that need access to the application's DI container
/// after the application has started. For example, to resolve services for
/// the bridge/using pattern.
/// </summary>
public interface IAfterRunAware
{
    Task AfterRunAsync(IServiceProvider serviceProvider);
}
