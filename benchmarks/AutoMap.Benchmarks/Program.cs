using AutoMap;
using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Riok.Mapperly.Abstractions;

BenchmarkSwitcher.FromAssembly(typeof(MappingBenchmarks).Assembly).Run(args);

// ─── Domain models ────────────────────────────────────────────────────────────

[Map(typeof(OrderDto))]
public class Order
{
    public int      Id       { get; set; }
    public string   Customer { get; set; } = "";
    public decimal  Total    { get; set; }
    public string   Status   { get; set; } = "";
    public DateTime Created  { get; set; }
}

public class OrderDto
{
    public int      Id       { get; set; }
    public string   Customer { get; set; } = "";
    public decimal  Total    { get; set; }
    public string   Status   { get; set; } = "";
    public DateTime Created  { get; set; }
}

// ─── Mapperly mapper ─────────────────────────────────────────────────────────

[Mapper]
public partial class MapperlyOrderMapper
{
    public partial OrderDto Map(Order source);
}

// ─── Hand-written baseline ───────────────────────────────────────────────────

public static class HandWrittenMapper
{
    public static OrderDto Map(Order src) => new()
    {
        Id       = src.Id,
        Customer = src.Customer,
        Total    = src.Total,
        Status   = src.Status,
        Created  = src.Created,
    };
}

// ─── Nested + collection domain models ────────────────────────────────────────

[Map(typeof(CustomerDto))]
public class Customer
{
    public int    Id      { get; set; }
    public string Name    { get; set; } = "";
    public string City    { get; set; } = "";
}

public class CustomerDto
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
}

[Map(typeof(LineItemDto))]
public class LineItem
{
    public string Sku      { get; set; } = "";
    public int    Quantity { get; set; }
    public decimal Price   { get; set; }
}

public class LineItemDto
{
    public string  Sku      { get; set; } = "";
    public int     Quantity { get; set; }
    public decimal Price    { get; set; }
}

[Map(typeof(BigOrderDto))]
public class BigOrder
{
    public int               Id       { get; set; }
    public Customer           Customer { get; set; } = null!;
    public List<LineItem>     Lines    { get; set; } = new();
    public decimal            Total    { get; set; }
    public DateTime           Created  { get; set; }
}

public class BigOrderDto
{
    public int                Id       { get; set; }
    public CustomerDto        Customer { get; set; } = null!;
    public List<LineItemDto>  Lines    { get; set; } = new();
    public decimal            Total    { get; set; }
    public DateTime           Created  { get; set; }
}

[Mapper]
public partial class MapperlyBigOrderMapper
{
    public partial BigOrderDto Map(BigOrder source);
}

public static class HandWrittenBigOrderMapper
{
    public static BigOrderDto Map(BigOrder src) => new()
    {
        Id = src.Id,
        Customer = new CustomerDto { Id = src.Customer.Id, Name = src.Customer.Name, City = src.Customer.City },
        Lines = src.Lines.Select(l => new LineItemDto { Sku = l.Sku, Quantity = l.Quantity, Price = l.Price }).ToList(),
        Total = src.Total,
        Created = src.Created,
    };
}

// ─── Benchmarks ──────────────────────────────────────────────────────────────

[MemoryDiagnoser]
[SimpleJob]
public class MappingBenchmarks
{
    private Order               _order      = null!;
    private IMapper             _autoMapper = null!;
    private MapperlyOrderMapper _mapperly   = null!;

    [GlobalSetup]
    public void Setup()
    {
        _order = new Order
        {
            Id       = 1,
            Customer = "Acme Corp",
            Total    = 199.99m,
            Status   = "Active",
            Created  = DateTime.UtcNow,
        };

        var config = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>());
        _autoMapper = config.CreateMapper();

        _mapperly = new MapperlyOrderMapper();
    }

    [Benchmark(Baseline = true, Description = "Hand-written")]
    public OrderDto HandWritten() => HandWrittenMapper.Map(_order);

    [Benchmark(Description = "AutoMap.Generator")]
    public OrderDto AutoMapGenerator() => _order.ToOrderDto();

    [Benchmark(Description = "Mapperly")]
    public OrderDto Mapperly() => _mapperly.Map(_order);

    [Benchmark(Description = "AutoMapper")]
    public OrderDto AutoMapper() => _autoMapper.Map<OrderDto>(_order);
}

/// <summary>
/// Nested-object + collection mapping scenario (Order → Customer, List&lt;LineItem&gt;)
/// to measure overhead beyond flat property copies.
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class NestedCollectionMappingBenchmarks
{
    private BigOrder                  _order      = null!;
    private IMapper                   _autoMapper = null!;
    private MapperlyBigOrderMapper    _mapperly   = null!;

    [GlobalSetup]
    public void Setup()
    {
        _order = new BigOrder
        {
            Id       = 1,
            Customer = new Customer { Id = 1, Name = "Acme Corp", City = "Springfield" },
            Lines    = Enumerable.Range(1, 10)
                .Select(i => new LineItem { Sku = $"SKU-{i}", Quantity = i, Price = 9.99m * i })
                .ToList(),
            Total   = 199.99m,
            Created = DateTime.UtcNow,
        };

        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<Customer, CustomerDto>();
            cfg.CreateMap<LineItem, LineItemDto>();
            cfg.CreateMap<BigOrder, BigOrderDto>();
        });
        _autoMapper = config.CreateMapper();

        _mapperly = new MapperlyBigOrderMapper();
    }

    [Benchmark(Baseline = true, Description = "Hand-written")]
    public BigOrderDto HandWritten() => HandWrittenBigOrderMapper.Map(_order);

    [Benchmark(Description = "AutoMap.Generator")]
    public BigOrderDto AutoMapGenerator() => _order.ToBigOrderDto();

    [Benchmark(Description = "Mapperly")]
    public BigOrderDto Mapperly() => _mapperly.Map(_order);

    [Benchmark(Description = "AutoMapper")]
    public BigOrderDto AutoMapper() => _autoMapper.Map<BigOrderDto>(_order);
}
