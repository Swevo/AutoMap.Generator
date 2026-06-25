# Changelog

All notable changes to AutoMap are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/); versions follow [Semantic Versioning](https://semver.org/).

---

## [1.6.0] — 2026-06-25

### Added
- **Cross-enum mapping** — when source and destination property types are different enum types, AutoMap.Generator automatically generates a compile-time `switch` expression mapping values by name; no configuration needed when names match

  ```csharp
  public enum OrderStatus    { Pending, Active, Cancelled }
  public enum OrderStatusDto { Pending, Active, Cancelled }

  // Generated automatically:
  Status = src.Status switch {
      global::MyApp.OrderStatus.Pending   => global::MyApp.OrderStatusDto.Pending,
      global::MyApp.OrderStatus.Active    => global::MyApp.OrderStatusDto.Active,
      global::MyApp.OrderStatus.Cancelled => global::MyApp.OrderStatusDto.Cancelled,
      _ => default
  },
  ```

- **`[MapEnum("DestValueName")]`** attribute — place on a source enum member to redirect it to a differently-named destination enum value

  ```csharp
  public enum SrcStatus { [MapEnum("Running")] Active, Done }
  public enum DstStatus { Running, Done }
  // Generated: SrcStatus.Active => DstStatus.Running
  ```

- **AM006 diagnostic** — warning when a source enum member has no matching destination member and no `[MapEnum]` redirect; the `_ => default` fallback still compiles but the warning flags the gap

---

## [1.5.0] — 2026-06-25

### Added
- **Automatic flattening** — when a destination property name has no direct source match, AutoMap.Generator walks the source type tree by splitting the name at PascalCase boundaries, up to 3 levels deep. No configuration needed.

  ```csharp
  // Source: Order → Customer → Name
  public class OrderDto
  {
      public string CustomerName { get; set; }     // → src.Customer?.Name
      public string CustomerAddressCity { get; set; } // → src.Customer?.Address?.City
  }
  ```
  - Struct intermediates use `.` (not `?.`) since structs can't be null
  - Direct property name matches always take priority over flattening

- **`[MapDefault("expr")]`** — null substitution: emits `?? expr` after the source expression when the value could be null; works with both direct and flattened paths

  ```csharp
  [MapDefault("\"Unknown\"")]
  public string CustomerName { get; set; }  // → src.Customer?.Name ?? "Unknown"

  [MapDefault("0")]
  public int? Count { get; set; }           // → src.Count ?? 0
  ```
  - `[MapIgnore]` takes precedence when both attributes are present
  - Has no effect on `[MapWith]` expressions (user controls those)

---

## [1.4.0] — 2026-06-25

### Added
- **`[MapWith("expression")]`** attribute — place on any destination property to supply a custom C# expression using `src` as the source variable; the expression is injected verbatim as the right-hand side of the property assignment at compile time
  - Works on both object-initializer and constructor-based mappings
  - Does not require a matching source property name — ideal for computed/derived values
  - `[MapIgnore]` takes precedence if both are applied to the same property

```csharp
[MapFrom(typeof(Order))]
public class OrderDto
{
    public int Id { get; set; }

    [MapWith("src.Price.ToString(\"C2\")")]
    public string PriceFormatted { get; set; } = "";

    [MapWith("src.Lines.Count")]
    public int LineCount { get; set; }
}
// Generated:
// PriceFormatted = src.Price.ToString("C2"),
// LineCount      = src.Lines.Count,
```

---

## [1.3.0] — 2026-06-25

### Added
- **Automatic constructor mapping** — when the destination type has no public parameterless constructor (e.g. a positional record `record OrderDto(int Id, string Name)` or a primary-constructor class), AutoMap.Generator automatically switches from object-initializer syntax to constructor-call syntax: `new OrderDto(src.Id, src.Name)`
- **`[MapConstructor]`** attribute — explicitly opt a destination type into constructor mapping even when a parameterless constructor exists; useful for disambiguating when multiple constructors are present (the longest public constructor wins)
- **Mixed ctor + init-property emission** — when the selected constructor covers only some properties, remaining `init`/`set` properties are filled via an object initializer block after the constructor arguments
- **AM005 diagnostic** — warning when a required constructor parameter has no matching property on the source type; the parameter receives `default` so the build still succeeds

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
