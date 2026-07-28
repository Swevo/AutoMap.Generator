# AutoMap.Generator Benchmarks

BenchmarkDotNet comparison of **AutoMap.Generator** vs **Mapperly**, **AutoMapper**, and a hand-written baseline.

## Running

```bash
cd benchmarks/AutoMap.Benchmarks
dotnet run -c Release
```

> Requires .NET 9 SDK. First run downloads BenchmarkDotNet, AutoMapper, and Mapperly packages (~30 s).

## Latest results

Mapping a 5-property class (`Order → OrderDto`) on Windows 11, AMD Ryzen 9 5900X, .NET 9.0.18:

| Method | Mean | Ratio | Alloc |
|---|---|---|---|
| Hand-written | 7.23 ns | 1.00 | 64 B |
| **AutoMap.Generator** | 6.64 ns | 0.93 | 64 B |
| Mapperly | 6.68 ns | 0.93 | 64 B |
| AutoMapper | 53.40 ns | 7.45x | 64 B |

Mapping a nested/collection scenario (`BigOrder → BigOrderDto`, one nested `Customer` object plus a 10-item `List<LineItem>`) on the same machine:

| Method | Mean | Ratio | Alloc |
|---|---|---|---|
| Hand-written | 113.5 ns | 1.00 | 824 B |
| **AutoMap.Generator** | 105.8 ns | 0.94 | 824 B |
| Mapperly | 118.6 ns | 1.05 | 792 B |
| AutoMapper | 234.2 ns | 2.07x | 944 B |

AutoMap.Generator and Mapperly both produce code equivalent to hand-written property assignment — differences are within measurement noise. AutoMapper's reflection overhead is ~2×–7.5× higher depending on mapping complexity, and its per-call allocations don't shrink with AOT/trimming since they route through cached delegate reflection.

> Regenerate these numbers yourself: `cd benchmarks/AutoMap.Benchmarks && dotnet run -c Release -- --filter '*'`. Raw BenchmarkDotNet output (including full statistics) is written to `BenchmarkDotNet.Artifacts/results/`.

## What is being measured

- **Hand-written** — a static `Map(Order) → OrderDto` method with explicit property assignments (baseline).
- **AutoMap.Generator** — the generated `order.ToOrderDto()` / `order.ToBigOrderDto()` extension method.
- **Mapperly** — a `[Mapper] partial class` with an auto-generated `Map(Order) → OrderDto` method.
- **AutoMapper** — `IMapper.Map<OrderDto>(order)` with a pre-configured `MapperConfiguration`.
- The nested/collection scenario additionally measures a `Customer → CustomerDto` object reference and a `List<LineItem> → List<LineItemDto>` collection copy, to show overhead beyond flat property copies.
