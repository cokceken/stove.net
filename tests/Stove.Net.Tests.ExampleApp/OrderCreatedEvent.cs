namespace Stove.Net.Tests.ExampleApp;

public record OrderCreatedEvent(int OrderId, string ProductName, int Quantity, string Status);
