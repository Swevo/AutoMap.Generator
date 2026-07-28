// AutoMap.Generator demo — showcases every attribute in one runnable console app.
// Run with: dotnet run

using System;
using System.Collections.Generic;
using System.Linq;
using AutoMap;

Console.WriteLine("=== AutoMap.Generator Demo ===\n");

// ── 1. Basic mapping: [Map] on the source type ─────────────────────────────
var order = new Order
{
    Id = 1,
    CustomerName = "  Ada Lovelace  ",
    Total = 199.99m,
    Status = OrderStatus.Active,
    ShippedAt = DateTime.UtcNow,
    Customer = new Customer { Id = 42, Name = "Ada Lovelace", Address = new Address { City = "London" } },
    Lines = new List<LineItem> { new() { Sku = "SKU-1", Quantity = 2 }, new() { Sku = "SKU-2", Quantity = 1 } },
};

var dto = order.ToOrderDto();
Console.WriteLine($"1. Basic + [TrimStrings]: CustomerName='{dto.CustomerName}'");

// ── 2. Nested + collection mapping (automatic via [Map] relationships) ────
Console.WriteLine($"2. Nested: CustomerCity flattened -> '{dto.CustomerAddressCity}'");
Console.WriteLine($"   Collection: {dto.Lines.Count} line items mapped");

// ── 3. Enum mapping (cross-enum by name) ───────────────────────────────────
Console.WriteLine($"3. Enum: {order.Status} -> {dto.Status}");

// ── 4. [MapWith] custom expression + [MapFormat] ───────────────────────────
Console.WriteLine($"4. MapWith/MapFormat: TotalFormatted='{dto.TotalFormatted}', ShippedAtLabel='{dto.ShippedAtLabel}'");

// ── 5. [MapWhen] conditional mapping ────────────────────────────────────────
Console.WriteLine($"5. MapWhen: StatusLabel='{dto.StatusLabel}'");

// ── 6. Reverse mapping ──────────────────────────────────────────────────────
var roundTripped = dto.ToOrder();
Console.WriteLine($"6. Reverse mapping: roundTripped.Id={roundTripped.Id}");

// ── 7. IMapFrom<T> convention interface ────────────────────────────────────
var summary = order.ToOrderSummary();
Console.WriteLine($"7. IMapFrom<T>: OrderSummary.Id={summary.Id}");

// ── 8. Constructor mapping (positional record) ─────────────────────────────
var receipt = order.ToReceipt();
Console.WriteLine($"8. Constructor mapping: Receipt={receipt}");

// ── 9. Partial method hook (see OnToOrderDto below) ────────────────────────
Console.WriteLine($"9. Partial hook: AuditTag='{dto.AuditTag}'");

// ── 10. IQueryable projection (dedicated flat type — nesting/TrimStrings
//         aren't expression-tree safe, see AM008) ─────────────────────────
var queryableProducts = new List<Product> { new() { Id = 1, Name = "Widget", Price = 9.99m } }.AsQueryable();
var projected = queryableProducts.ProjectToProductDto().ToList();
Console.WriteLine($"10. Projection: {projected.Count} product(s) projected via Expression<Func<Product, ProductDto>>");

Console.WriteLine("\nDone — see Program.cs for the source of every mapping above.");

// ─── Domain models ──────────────────────────────────────────────────────────

public enum OrderStatus { Pending, Active, Cancelled }
public enum OrderStatusDto { Pending, Active, Cancelled }

// Plain source-side substructures used only for flattening (Customer.Address.City);
// they don't need their own [Map]/DTO pair since OrderDto only exposes the
// flattened "CustomerAddressCity" leaf value, not a nested "Customer" property.
public class Address
{
    public string City { get; set; } = "";
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Address Address { get; set; } = new();
}

[Map(typeof(LineItemDto), Reverse = true)]
public class LineItem
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
}
public class LineItemDto
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
}

[Map(typeof(OrderDto), Reverse = true)]
[TrimStrings]
public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime? ShippedAt { get; set; }
    public Customer Customer { get; set; } = new();
    public List<LineItem> Lines { get; set; } = new();

    [MapIgnore]
    public string InternalNotes { get; set; } = "not exposed to DTO";
}

public class OrderDto
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";

    // Flattening: no direct "CustomerAddressCity" on Order, walks Customer -> Address -> City
    public string CustomerAddressCity { get; set; } = "";

    public decimal Total { get; set; }

    // Cross-enum mapping by member name
    public OrderStatusDto Status { get; set; }

    public List<LineItemDto> Lines { get; set; } = new();

    [MapWith("src.Total.ToString(\"C2\")")]
    public string TotalFormatted { get; set; } = "";

    [MapFormat("yyyy-MM-dd")]
    [MapProperty("ShippedAt")]
    public string ShippedAtLabel { get; set; } = "";

    [MapWhen("src.Status == global::OrderStatus.Active", Fallback = "\"Inactive\"")]
    [MapWith("src.Status.ToString()")]
    public string StatusLabel { get; set; } = "";

    [MapDefault("\"unassigned\"")]
    public string AuditTag { get; set; } = "";
}

// ── IMapFrom<T> convention-based mapping (no attribute needed on Order) ────
public class OrderSummary : IMapFrom<Order>
{
    public int Id { get; set; }
    public decimal Total { get; set; }
}

// ── Constructor mapping via positional record ──────────────────────────────
[MapFrom(typeof(Order), MethodName = "ToReceipt")]
public record Receipt(int Id, decimal Total);

// ── Dedicated flat type for the IQueryable projection demo (GenerateProjection
//    requires no null-conditional/switch/nested/collection expressions) ─────
[Map(typeof(ProductDto), GenerateProjection = true)]
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

// ── Partial method hook — customises OrderDto after generated mapping ──────
namespace AutoMap
{
    public static partial class AutoMapExtensions
    {
        static partial void OnToOrderDto(global::Order src, global::OrderDto result)
        {
            result.AuditTag = $"mapped-at-{DateTime.UtcNow:O}";
        }
    }
}
