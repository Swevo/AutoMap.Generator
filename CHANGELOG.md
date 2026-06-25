# Changelog

All notable changes to AutoMap are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/); versions follow [Semantic Versioning](https://semver.org/).

---

## [1.2.0] — 2026-06-26

### Added
- **`Reverse = true`** on `[Map]` / `[MapFrom]` — automatically generates the opposite-direction mapping alongside the forward one (e.g. `[Map(typeof(OrderDto), Reverse = true)]` emits both `ToOrderDto()` on `Order` and `ToOrder()` on `OrderDto`)
- **`IAutoMapper<TSource, TResult>` interface** — emitted into the user's compilation via `AutoMapInterface.g.cs`; for every mapping a corresponding sealed class is generated inside `AutoMapExtensions` (e.g. `OrderToOrderDtoMapper`) with a static `Instance` property, allowing DI/factory patterns without reflection

---

## [1.1.0] — 2026-06-25

### Added
- **Nested object mapping** — when source has `Address Address` and dest has `AddressDto Address`, and `Address` has `[Map(typeof(AddressDto))]`, AutoMap.Generator emits `Address = src.Address?.ToAddressDto()` automatically
- **Collection mapping** — `List<T>` → `List<TDto>`, `T[]` → `TDto[]`, and other `IEnumerable<T>` variants emit `src.Items?.Select(x => x.ToItemDto()).ToList()` / `.ToArray()` when element types have a known `[Map]` relationship; `using System.Linq` added automatically
- AM004 diagnostic — warns when a destination property with a matching source name was skipped because the types are incompatible and no registered mapping can resolve them; add `[MapIgnore]` to suppress

---

## [1.0.0] — 2026-06-25

### Added
- `[Map(typeof(Dto))]` attribute — place on source type to generate `ToDto()` extension method
- `[MapFrom(typeof(Source))]` attribute — place on destination type to generate extension on source
- `[MapIgnore]` attribute — exclude a destination property from all mappings
- `[MapProperty("SourceName")]` attribute — map a destination property from a differently-named source property
- Convention-based matching: case-insensitive name match + implicit type conversion check
- Inherited property support — walks base type chain for both source and destination
- Struct source support — omits null-guard for value types
- `MethodName` override on `[Map]` / `[MapFrom]`
- Multiple `[Map]` attributes on one class — generates one extension per attribute
- AM001 diagnostic — warns when no properties match between source and destination
- AM002 diagnostic — error when `[MapProperty("X")]` references a non-existent source property
- AM003 diagnostic — error when the type passed to `[Map]`/`[MapFrom]` cannot be resolved
- Incremental source generator — no build-time perf overhead for unmodified files
