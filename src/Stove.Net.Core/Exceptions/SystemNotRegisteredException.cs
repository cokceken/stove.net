namespace Stove.Net.Core.Exceptions;

public class SystemNotRegisteredException(Type systemType, string name) : InvalidOperationException(
    name == SystemKey.DefaultName
        ? $"System '{systemType.Name}' is not registered. " +
          $"Register it during setup using the appropriate .With*() method on StoveBuilder."
        : $"System '{systemType.Name}' with name '{name}' is not registered. " +
          $"Register it during setup using the appropriate .With*() method on StoveBuilder.");