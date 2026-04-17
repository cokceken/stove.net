namespace Stove.Net.Core.Exceptions;

public class SystemNotRegisteredException : InvalidOperationException
{
    public SystemNotRegisteredException(Type systemType)
        : base($"System '{systemType.Name}' is not registered. " +
               $"Register it during setup using the appropriate .With*() method on StoveBuilder.")
    {
    }
}
