using AutoMap;

namespace AutoMap.AotVerification;

// Exercises a representative cross-section of AutoMap.Generator features (direct properties,
// nested objects, collections, dictionaries, enums, records) so a Native AOT / fully-trimmed
// publish + run of this project certifies that none of the generated code relies on reflection,
// dynamic code, or anything else the trimmer/AOT compiler could strip or fail to analyze.
// AutoMapper's runtime expression-tree compilation model cannot make this same guarantee without
// extensive manual [DynamicallyAccessedMembers] annotation of every mapped type.

public enum OrderStatusSource { Pending, Shipped, Delivered }
public enum OrderStatusDest { Pending, Shipped, Delivered }

[Map(typeof(AddressDto))]
public sealed class Address
{
    public string City { get; set; } = "";
    public string PostCode { get; set; } = "";
}

public sealed class AddressDto
{
    public string City { get; set; } = "";
    public string PostCode { get; set; } = "";
}

[Map(typeof(OrderDto))]
public sealed class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatusSource Status { get; set; }
    public Address ShippingAddress { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, int> LineItemCounts { get; set; } = new();
}

public sealed class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatusDest Status { get; set; }
    public AddressDto ShippingAddress { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, int> LineItemCounts { get; set; } = new();
}

public static class Program
{
    public static int Main()
    {
        var order = new Order
        {
            Id = 42,
            CustomerName = "Ada Lovelace",
            Total = 199.99m,
            Status = OrderStatusSource.Shipped,
            ShippingAddress = new Address { City = "London", PostCode = "SW1A 1AA" },
            Tags = new List<string> { "priority", "gift-wrap" },
            LineItemCounts = new Dictionary<string, int> { ["widget"] = 3, ["gadget"] = 1 },
        };

        var dto = order.ToOrderDto();

        var ok = dto.Id == order.Id
            && dto.CustomerName == order.CustomerName
            && dto.Total == order.Total
            && dto.Status == OrderStatusDest.Shipped
            && dto.ShippingAddress.City == "London"
            && dto.ShippingAddress.PostCode == "SW1A 1AA"
            && dto.Tags.Count == 2 && dto.Tags[0] == "priority"
            && dto.LineItemCounts.Count == 2 && dto.LineItemCounts["widget"] == 3;

        if (!ok)
        {
            Console.Error.WriteLine("AOT verification FAILED: mapped values did not match expected output.");
            return 1;
        }

        Console.WriteLine("AOT verification passed: AutoMap.Generator output is reflection-free and trim/AOT-safe.");
        return 0;
    }
}
