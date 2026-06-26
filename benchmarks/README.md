# AutoMap.Generator Benchmarks

BenchmarkDotNet comparison of **AutoMap.Generator** vs **Mapperly**, **AutoMapper**, and a hand-written baseline.

## Running

```bash
cd benchmarks/AutoMap.Benchmarks
dotnet run -c Release
```

> Requires .NET 9 SDK. First run downloads BenchmarkDotNet, AutoMapper, and Mapperly packages (~30 s).

## Latest results

Mapping a 5-property class (`Order → OrderDto`) on .NET 9, AMD Ryzen 9 5900X:

| Method | Mean | Ratio | Alloc |
|---|---|---|---|
| Hand-written | 3.2 ns | 1.00 | 72 B |
| **AutoMap.Generator** | 3.3 ns | 1.03 | 72 B |
| Mapperly | 3.2 ns | 1.01 | 72 B |
| AutoMapper | 387 ns | 121x | 344 B |

AutoMap.Generator and Mapperly produce code that is functionally identical to hand-written property assignment — the tiny variation is within measurement noise. AutoMapper's reflection overhead is ~120× higher.

## What is being measured

- **Hand-written** — a static `Map(Order) → OrderDto` method with explicit property assignments (baseline).
- **AutoMap.Generator** — the generated `order.ToOrderDto()` extension method.
- **Mapperly** — a `[Mapper] partial class` with an auto-generated `Map(Order) → OrderDto` method.
- **AutoMapper** — `IMapper.Map<OrderDto>(order)` with a pre-configured `MapperConfiguration`.
