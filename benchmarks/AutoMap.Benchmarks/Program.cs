using AutoMap;
using AutoMapper;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Riok.Mapperly.Abstractions;

BenchmarkRunner.Run<MappingBenchmarks>();

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
