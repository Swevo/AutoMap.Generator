# Changelog

All notable changes to AutoMap are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/); versions follow [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

### Added

- **`AutoMapGraph`** — a static `AutoMap.AutoMapGraph` class is now always emitted, summarizing every `[Map]`/`[MapFrom]`/`[MapExternal]` mapping registered in the compilation as both a Mermaid flowchart (`AutoMapGraph.Mermaid`) and a plain `(Source, Destination, MethodName)[]` array (`AutoMapGraph.Edges`). Unlike AutoMapper's `CreateMap<>` profiles, which are only inspectable at runtime via reflection over the built `MapperConfiguration`, this graph is baked in at build time — diffable in source control, embeddable in docs/CI artifacts, and usable in tests without spinning up the application.
- **AM011 "insert generated code preview" code fix** — `AutoMapGeneratedCodePreviewCodeFixProvider` now offers a code fix on the existing AM011 preview diagnostic that inserts the full generated method body as an idempotent comment block directly above the `[Map]`/`[MapFrom]`-decorated type, so the exact generated code can be reviewed inline in the IDE without opening `AutoMapExtensions.g.cs`.
- **`GenerateUpdate` / `UpdateFrom(TSource src)`** — set `GenerateUpdate = true` on `[Map]`, `[MapFrom]`, or `[MapExternal]` to also generate an `UpdateFrom(TSource src)` instance-extension method that patches an existing destination instance's settable properties in place, instead of allocating a new one. Equivalent to AutoMapper's `mapper.Map(source, existingDestination)` — useful for `PUT`/`PATCH` endpoints and EF Core `Update` scenarios. Respects all the same per-property attributes (`[MapIgnore]`, `[MapWith]`, `[MapConverter]`, etc.) and nested/collection/dictionary resolution as the regular `ToXxx()` method; constructor-only properties are left untouched.
- **Dictionary mapping** — `Dictionary<TKey, TValue>`, `IDictionary<TKey, TValue>`, and `IReadOnlyDictionary<TKey, TValue>` are now mapped automatically like `List<T>`/arrays, whenever the key types match. Keys are copied as-is; values are converted via `.ToXxx()` when the value type has a registered `[Map]`, or copied directly when the value types are identical.
- **`[MapNamingConvention]`** class-level attribute — enables separator- and case-insensitive property matching (e.g. a `customer_name` source property matches a `CustomerName` destination property), as a fallback before automatic flattening. Useful when mapping from snake_case/kebab-case sources such as deserialized JSON or raw DB rows.
- **`[MapConverter(typeof(Converter), "MethodName")]`** property-level attribute — calls a reusable static conversion method (`Converter.MethodName(src.Prop)`) instead of duplicating the same `[MapWith]` expression across multiple mappings. Bypasses the normal type-compatibility check, like `[MapFormat]`.
- **`[MapExternal(typeof(Source), typeof(Dest))]`** class-level attribute — registers a mapping between two types you don't own (e.g. types from a NuGet package or another assembly) by placing the attribute on any accessible placeholder type instead of on the source/destination type directly. Supports the same `MethodName`, `Reverse`, and `Strict` options as `[Map]`/`[MapFrom]`.

### Fixed

- **MIGRATION.md** — the "what AutoMap.Generator doesn't support" table incorrectly listed `IQueryable` projection (`ProjectTo<T>`) as unsupported; it has been supported since v1.12.0 via `GenerateProjection = true`.

---

## [1.15.0] — 2026-07-28

### Added

- **AM010 diagnostic — unused mapping detection** — `AutoMapUnusedMappingAnalyzer` performs a compilation-wide pass over every `[Map]`/`[MapFrom]` attribute and reports an `Info`-severity AM010 when the generated mapping method (its `ToXxx()` extension method, its `ToXxxs()` collection helper, and its `IAutoMapper<,>` singleton class) does not appear to be referenced anywhere in the project — as a plain identifier, a call, `nameof(...)`, or a DI registration. Generated (`*.g.cs`) files are excluded from the usage scan so the mapping's own collection-helper method never masks a truly unused mapping. This is a suggestion only — no automatic fix is offered, since the method could legitimately be used via reflection, dependency injection, or another project that isn't part of this compilation.
- **AM011 diagnostic — generated code preview** — `AutoMapPreviewAnalyzer` reports a `Hidden`-severity AM011 directly on the `[Map]`/`[MapFrom]` attribute, showing a one-line preview of the method the generator will emit (e.g. `Generates: public static OrderDto ToOrderDto(this Order src) — Flattened: CustomerAddressCity; Ignored: InternalNotes`). This surfaces via IDE hover/lightbulb/Error List without needing to open the generated `.g.cs` file — the closest in-package equivalent to a CodeLens annotation for a source-generator NuGet package. Reuses the exact same flattened/defaulted/ignored/custom summarization logic as the XML doc comments (extracted to a shared `AutoMapGenerator.GetDocCategories` helper), so the two can never drift apart.

---

## [1.14.0] — 2026-07-28

### Added

- **XML doc comments on generated mapping methods** — every generated `ToXxx()` method now gets a `/// <summary>` documenting which destination properties were flattened from a nested path, defaulted via `[MapDefault]`, skipped via `[MapIgnore]`, or use a custom `[MapWith]`/`[MapFormat]`/`[MapWhen]` expression. Only non-empty categories are emitted, so a plain 1:1 mapping just gets a one-line summary.
- **AM005 code fix** — `AutoMapAnalyzer` now also re-reports AM005 (unmatched constructor parameter) with a real location on the parameter itself. `AutoMapConstructorCodeFixProvider` offers **"Add [property: MapProperty(\"ClosestSourceName\")]"** on positional-record/primary-constructor parameters when a reasonably close source property name can be found (substring or Levenshtein-distance match); no fix is offered when nothing is close enough.
- **AM006 code fix** — `AutoMapAnalyzer` re-reports AM006 (unmatched enum member) on the source enum member's own declaration. `AutoMapEnumCodeFixProvider` offers one **"Add [MapEnum(\"DestValueName\")]"** action per destination enum member (up to 5), so you can pick the right redirect.
- **AM008 code fix** — `AutoMapAnalyzer` re-reports AM008 (projection unsupported) on the `[Map]`/`[MapFrom]` attribute itself. `AutoMapProjectionCodeFixProvider` offers **"Remove GenerateProjection = true"**, since the instance `ToXxx()` method is unaffected either way.

### Changed

- **Diagnostic messages now include an inline fix snippet** — AM001 through AM008 messages were rewritten to show a concrete, ready-to-paste code fix (e.g. `` [MapIgnore] ``/`` [MapProperty("X")] ``/`` [MapEnum("X")] ``) instead of just describing the problem in prose.

---

## [1.12.0] — 2026-07-09

### Added

- **`GenerateProjection` on `[Map]`/`[MapFrom`]** — generates a static `Expression<Func<TSource, TDest>>` plus an `IQueryable<TDest>` `ProjectToXxx()` extension method, so EF Core (or any `IQueryable` provider) can translate the mapping directly into SQL. Equivalent to AutoMapper's `ProjectTo<T>()`.
- **AM008 diagnostic** — warns when `GenerateProjection = true` is requested but the mapping requires the null-conditional operator (`?.`) or a `switch` expression, neither of which are supported inside C# expression trees. The regular instance `ToXxx()` method is unaffected.

---

## [1.11.0] — 2026-06-25

### Added

- **Reverse mapping generation** — set `Reverse = true` on `[Map]` or `[MapFrom]` to emit a second mapping method in the opposite direction (for example `dto.ToOrder()` alongside `order.ToOrderDto()`).
- **Reverse-aware property rules** — reverse generation now respects `[MapIgnore]`, `[MapProperty]`, constructor-mapped destinations, and only reverses nested/collection members when a matching reverse mapping exists.
- **AM007 diagnostic** — warns when `Reverse = true` is requested but no reverse properties can be generated.

---

## [1.9.0] — 2026-06-25

### Added

- **Collection-level extension methods** — for every `[Map]`, a companion `ToXDtos(this IEnumerable<Src>)` method is now generated. Lets you map whole sequences without `.Select()` boilerplate:

  ```csharp
  var dtos = orders.ToOrderDtos(); // IEnumerable<OrderDto>
  ```

- **`AutoMapAnalyzer`** — standalone `DiagnosticAnalyzer` that re-reports AM004 diagnostics with real property-level source locations, enabling IDE lightbulb suggestions.

- **`AutoMapCodeFixProvider`** — Roslyn code-fix provider for AM004. When a destination property is flagged for type incompatibility, the IDE offers **"Add [MapIgnore] to suppress this mapping"** as a one-click fix.

---



### Added

- **`[TrimStrings]`** class-level attribute — automatically wraps every mapped `string` property with `?.Trim()`. Place on either the source or destination type. `[MapWith]` still takes precedence per-property.

  ```csharp
  [Map(typeof(OrderDto))]
  [TrimStrings]
  public class Order { public string Name { get; set; } = ""; }
  // Generated: Name = src.Name?.Trim(),
  ```

- **`[MapFormat("format")]`** property-level attribute — calls `.ToString("format")` on the source value. Works across type mismatches (e.g. `decimal → string`). Emits `?.` for reference types and `Nullable<T>`.

  ```csharp
  [MapFormat("C2")]
  public string Price { get; set; } = "";   // src is decimal → src.Price.ToString("C2")

  [MapFormat("yyyy-MM-dd")]
  public string ShippedAt { get; set; } = "";   // src is DateTime? → src.ShippedAt?.ToString("yyyy-MM-dd")
  ```

  Composes with `[MapWhen]`:
  ```csharp
  [MapFormat("yyyy-MM-dd")]
  [MapWhen("src.IsShipped", Fallback = "\"N/A\"")]
  public string ShippedAt { get; set; } = "";
  // Generated: ShippedAt = src.IsShipped ? src.ShippedAt.ToString("yyyy-MM-dd") : "N/A",
  ```

- **`IMapFrom<TSource>` convention interface** — implement this interface on a DTO class to generate `TSource.ToDto()` without any attribute. Equivalent to `[MapFrom(typeof(TSource))]`. Deduplicates automatically when combined with `[MapFrom]`.

  ```csharp
  public class OrderDto : IMapFrom<Order>
  {
      public int Id { get; set; }
      public string Name { get; set; } = "";
  }
  // Auto-generates: Order.ToOrderDto() extension method
  ```

- **Partial method hooks** — every generated mapping method now calls `On{MethodName}(src, result)` before returning. The signature is emitted as a `static partial void` declaration in the same `AutoMapExtensions` class. Implement it in your own partial class for post-mapping customisation at zero cost when unimplemented.

  ```csharp
  // Generated (AutoMapExtensions.g.cs):
  static partial void OnToOrderDto(global::MyApp.Order src, global::MyApp.OrderDto result);

  // Your code (anywhere in your project):
  namespace AutoMap
  {
      public static partial class AutoMapExtensions
      {
          static partial void OnToOrderDto(Order src, OrderDto result)
          {
              result.AuditTag = $"mapped-at-{DateTime.UtcNow:O}";
          }
      }
  }
  ```

- **`Strict = true` mode** on `[Map]` and `[MapFrom]` — promotes AM001 (no properties mapped) and AM004 (type incompatibility) from warnings to **errors**. Use when you want compile failures rather than silent skips.

  ```csharp
  [Map(typeof(OrderDto), Strict = true)]
  public class Order { /* ... */ }
  ```

---

## [1.7.0] — 2026-06-25

### Added
- **`[MapWhen("condition")]`** attribute — wraps the property assignment in a ternary using the given C# boolean expression; `src` references the source object
  - Optional `Fallback` property sets the false-branch expression (defaults to `default`)
  - Composes with `[MapWith]` — the custom expression becomes the true branch
  - Composes with flattening — the flattened path becomes the true branch
  - `[MapIgnore]` takes precedence when both attributes are present

  ```csharp
  [MapWhen("src.IsActive")]
  public string Name { get; set; } = "";
  // Generated: Name = src.IsActive ? src.Name : default,

  [MapWhen("src.IsPremium", Fallback = "\"Standard\"")]
  public string Tier { get; set; } = "";
  // Generated: Tier = src.IsPremium ? src.Tier : "Standard",

  [MapWhen("src.IsKnown")]
  [MapWith("src.Price.ToString(\"C2\")")]
  public string PriceLabel { get; set; } = "";
  // Generated: PriceLabel = src.IsKnown ? src.Price.ToString("C2") : default,
  ```

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
