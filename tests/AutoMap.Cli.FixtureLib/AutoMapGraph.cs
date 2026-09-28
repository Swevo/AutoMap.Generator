namespace AutoMap;

/// <summary>
/// Hand-written stand-in for the <c>AutoMap.AutoMapGraph</c> class AutoMap.Generator always
/// emits into a consuming compilation (see <c>AutoMapGenerator.GenerateSource</c>). Used by
/// <c>AutoMap.Cli.Tests</c> to exercise <c>VerifyCommand</c>'s reflection logic against a real
/// on-disk assembly without needing to run the actual source generator in the test project.
/// </summary>
public static class AutoMapGraph
{
    public const string Mermaid = @"graph LR
    Order -->|ToOrderDto| OrderDto
    Customer -->|ToCustomerDto| CustomerDto
";

    public static readonly (string Source, string Destination, string MethodName)[] Edges =
    {
        ("Order", "OrderDto", "ToOrderDto"),
        ("Customer", "CustomerDto", "ToCustomerDto"),
    };
}
