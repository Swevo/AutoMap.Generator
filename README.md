# AutoMap.Generator

[![NuGet](https://img.shields.io/nuget/v/AutoMap.Generator.svg)](https://www.nuget.org/packages/AutoMap.Generator)
[![NuGet Downloads](https://img.shields.io/nuget/dt/AutoMap.Generator.svg)](https://www.nuget.org/packages/AutoMap.Generator)
[![CI](https://github.com/Swevo/AutoMap.Generator/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/AutoMap.Generator/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**[📖 Documentation site](https://swevo.github.io/AutoMap.Generator/) &nbsp;·&nbsp; [NuGet](https://www.nuget.org/packages/AutoMap.Generator) &nbsp;·&nbsp; [Changelog](CHANGELOG.md)**

**Compile-time object mapping for .NET via Roslyn source generators.**

Add `[Map(typeof(OrderDto))]` to your class — AutoMap generates a strongly-typed `ToOrderDto()` extension method at build time. No reflection. No runtime overhead. AOT-safe.

```csharp
[Map(typeof(OrderDto))]
public class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
}

// Generated automatically:
public static partial class AutoMapExtensions
{
    public static OrderDto ToOrderDto(this Order src)
    {
        if (src is null) throw new ArgumentNullException(nameof(src));
        return new OrderDto
        {
            Id       = src.Id,
            Customer = src.Customer,
            Total    = src.Total,
        };
    }
}

// Usage:
var dto = order.ToOrderDto();
```

---

## Installation

```
dotnet add package AutoMap.Generator
```

Targets `netstandard2.0` — works with .NET 6, 7, 8, 9, and MAUI.

---

## Quick start

### 1. `[Map]` — attribute on the source type

Place `[Map(typeof(Destination))]` on the class you want to map **from**:

```csharp
using AutoMap;

[Map(typeof(UserDto))]
public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";  // no matching dest → silently omitted
}

public class UserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
}
```

Generated: `user.ToUserDto()`

---

### 2. `[MapFrom]` — attribute on the destination type

Place `[MapFrom(typeof(Source))]` on the DTO when you want to keep source types clean:

```csharp
using AutoMap;

public class Order { public int Id { get; set; } public string Customer { get; set; } = ""; }

[MapFrom(typeof(Order))]
public class OrderDto
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
}
```

Generated: `order.ToOrderDto()` — extension method is on `Order`, returning `OrderDto`.

---

### 3. Multiple mappings on one class

Both directions work. Stack `[Map]` for multiple destinations:

```csharp
[Map(typeof(OrderDto))]
[Map(typeof(OrderSummary))]
public class Order { ... }
```

---

### 4. Override the method name

```csharp
[Map(typeof(OrderDto), MethodName = "AsDto")]
public class Order { ... }

// Generated:
order.AsDto()
```

---

### 5. Reverse mapping in one line

```csharp
[Map(typeof(OrderDto), Reverse = true)]
public class Order { ... }

// Generated both:
order.ToOrderDto()
dto.ToOrder()
```

---

## Controlling properties

### `[MapIgnore]` — exclude a destination property

```csharp
[MapFrom(typeof(Order))]
public class OrderDto
{
    public int Id { get; set; }
    [MapIgnore] public string InternalNote { get; set; } = ""; // ← never mapped
}
```

### `[MapProperty("SourceName")]` — map from a differently-named source property

```csharp
public class Order { public string CustomerName { get; set; } = ""; }

[MapFrom(typeof(Order))]
public class OrderDto
{
    [MapProperty("CustomerName")]
    public string Client { get; set; } = "";
    // Generated: Client = src.CustomerName
}
```

---

## Nested object mapping

When a destination property type differs from the source, AutoMap.Generator checks whether a `[Map]` relationship exists between the two types and emits a null-safe chained call automatically:

```csharp
[Map(typeof(AddressDto))]
public class Address { public string City { get; set; } = ""; }

[Map(typeof(OrderDto))]
public class Order { public int Id { get; set; } public Address? Address { get; set; } }

public class OrderDto { public int Id { get; set; } public AddressDto? Address { get; set; } }

// Generated:
return new OrderDto
{
    Id      = src.Id,
    Address = src.Address?.ToAddressDto(),  // ← resolved automatically
};
```

No configuration needed — as long as the `[Map]` for the nested type exists anywhere in the compilation, AutoMap.Generator wires it up.

---

## Collection mapping

`List<T>`, `T[]`, `IEnumerable<T>`, `ICollection<T>`, and other standard collection types are mapped automatically when the element type has a registered `[Map]`:

```csharp
[Map(typeof(ItemDto))]
public class Item { public int Id { get; set; } }

[Map(typeof(OrderDto))]
public class Order { public int Id { get; set; } public List<Item> Items { get; set; } = new(); }

public class OrderDto { public int Id { get; set; } public List<ItemDto> Items { get; set; } = new(); }

// Generated (using System.Linq added automatically):
return new OrderDto
{
    Id    = src.Id,
    Items = src.Items?.Select(x => x.ToItemDto()).ToList(),
};
```

| Source collection | Destination collection | Emitted expression |
|---|---|---|
| `List<T>` / `IEnumerable<T>` / `ICollection<T>` | `List<TDto>` | `.Select(x => x.To...()).ToList()` |
| `T[]` | `TDto[]` | `.Select(x => x.To...()).ToArray()` |

---

## Reverse mapping

Set `Reverse = true` on `[Map]` or `[MapFrom]` to generate both directions at once:

```csharp
[Map(typeof(OrderDto), Reverse = true)]
public class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
}

public class OrderDto
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
}

// Generated:
order.ToOrderDto()    // Order → OrderDto (forward)
dto.ToOrder()         // OrderDto → Order (reverse)
```

Both directions are registered in the mapping registry, so nested and collection resolution works bidirectionally too.

---

## `IAutoMapper<TSource, TResult>` interface

AutoMap.Generator emits an `IAutoMapper<in TSource, out TResult>` interface into your compilation alongside a concrete sealed mapper class for every registered mapping:

```csharp
// Interface (emitted into your compilation automatically):
public interface IAutoMapper<in TSource, out TResult>
{
    TResult Map(TSource source);
}

// For [Map(typeof(OrderDto))] on Order, the following is generated:
public sealed class OrderToOrderDtoMapper : IAutoMapper<Order, OrderDto>
{
    public static readonly OrderToOrderDtoMapper Instance = new OrderToOrderDtoMapper();
    public OrderDto Map(Order source) => source.ToOrderDto();
}
```

Use `Instance` to avoid allocations, or inject `IAutoMapper<Order, OrderDto>` into your services for testability:

```csharp
// DI registration:
services.AddSingleton<IAutoMapper<Order, OrderDto>>(AutoMapExtensions.OrderToOrderDtoMapper.Instance);

// Service:
public class OrderService(IAutoMapper<Order, OrderDto> mapper) { ... }
```

### `[MapWith("expression")]` — custom expression

Use `[MapWith]` when you need a computed or transformed value rather than a direct property copy. Write any valid C# expression using `src` to reference the source object:

```csharp
public class Order
{
    public int Id { get; set; }
    public decimal Price { get; set; }
    public List<string> Tags { get; set; } = new();
}

[MapFrom(typeof(Order))]
public class OrderDto
{
    public int Id { get; set; }

    [MapWith("src.Price.ToString(\"C2\")")]
    public string PriceFormatted { get; set; } = "";

    [MapWith("src.Tags.Count")]
    public int TagCount { get; set; }

    [MapWith("src.Id > 1000 ? \"Premium\" : \"Standard\"")]
    public string Tier { get; set; } = "";
}
```

Generated:
```csharp
return new OrderDto
{
    Id             = src.Id,
    PriceFormatted = src.Price.ToString("C2"),
    TagCount       = src.Tags.Count,
    Tier           = src.Id > 1000 ? "Premium" : "Standard",
};
```

`[MapWith]` does **not** require a source property with a matching name — it is injected verbatim. If both `[MapWith]` and `[MapIgnore]` are on the same property, `[MapIgnore]` wins.

---

## Enum mapping

When source and destination properties are **different enum types**, AutoMap.Generator generates a compile-time `switch` expression mapping values by name automatically:

```csharp
public enum OrderStatus    { Pending, Active, Cancelled }
public enum OrderStatusDto { Pending, Active, Cancelled }

[Map(typeof(OrderDto))]
public class Order { public OrderStatus Status { get; set; } }
public class OrderDto { public OrderStatusDto Status { get; set; } }

// Generated:
Status = src.Status switch
{
    global::MyApp.OrderStatus.Pending   => global::MyApp.OrderStatusDto.Pending,
    global::MyApp.OrderStatus.Active    => global::MyApp.OrderStatusDto.Active,
    global::MyApp.OrderStatus.Cancelled => global::MyApp.OrderStatusDto.Cancelled,
    _ => default
},
```

Same-type enum properties are mapped directly (`Status = src.Status`) — no switch needed.

### `[MapEnum("DestValueName")]` — rename a value

Place `[MapEnum]` on a **source** enum member to redirect it to a differently-named destination member:

```csharp
public enum SrcStatus
{
    [MapEnum("Running")]   // ← maps to DstStatus.Running
    Active,
    Done
}
public enum DstStatus { Running, Done }
```

### AM006 — unmatched enum member

When a source enum member has no matching destination member and no `[MapEnum]` redirect, **AM006** is reported and the `_ => default` fallback is used so the build succeeds:

```csharp
// ⚠ AM006: Source enum member 'Unknown' on 'SrcStatus' has no matching member in 'DstStatus'.
public enum SrcStatus { Active, Unknown }
public enum DstStatus { Active }         // ← no Unknown
// Generated: _ => default (covers Unknown at runtime)
```

---

## Flattening

When a destination property has no direct source match, AutoMap.Generator automatically tries to resolve it by splitting the name at PascalCase boundaries and walking the source type tree — up to 3 levels deep.

```csharp
public class Address  { public string City  { get; set; } = ""; }
public class Customer { public Address? Address { get; set; } public string Name { get; set; } = ""; }
public class Order    { public int Id { get; set; } public Customer? Customer { get; set; } }

[MapFrom(typeof(Order))]
public class OrderDto
{
    public int Id                  { get; set; }  // direct match
    public string CustomerName     { get; set; } = "";  // → src.Customer?.Name
    public string CustomerAddressCity { get; set; } = "";  // → src.Customer?.Address?.City
}
```

Generated:
```csharp
return new OrderDto
{
    Id                  = src.Id,
    CustomerName        = src.Customer?.Name,
    CustomerAddressCity = src.Customer?.Address?.City,
};
```

**Rules:**
- Direct name matches always take priority over flattening
- Value-type intermediates use `.` instead of `?.` (structs can't be null)
- Flattening is attempted before `AM004` is reported

---

## `[MapDefault]` — null substitution

Place `[MapDefault("expression")]` on any destination property to substitute the provided expression when the source value is null. The expression is appended as `?? expr` and works with both direct and flattened paths:

```csharp
public class Order { public string? Region { get; set; } public Customer? Customer { get; set; } }

[MapFrom(typeof(Order))]
public class OrderDto
{
    [MapDefault("\"Global\"")]
    public string Region { get; set; } = "";          // → src.Region ?? "Global"

    [MapDefault("\"Guest\"")]
    public string CustomerName { get; set; } = "";    // → src.Customer?.Name ?? "Guest"

    [MapDefault("0")]
    public int CustomerOrderCount { get; set; }       // → src.Customer?.OrderCount ?? 0
}
```

`[MapIgnore]` takes precedence when both are on the same property. `[MapDefault]` has no effect on `[MapWith]` — write the full expression there instead.

---

## Constructor mapping

AutoMap.Generator automatically detects when the destination type has no public parameterless constructor and switches to constructor-call syntax — no configuration needed.

### Positional records (automatic)

```csharp
// Destination: positional record (no parameterless ctor)
public record OrderDto(int Id, string Customer);

[Map(typeof(OrderDto))]
public class Order { public int Id { get; set; } public string Customer { get; set; } = ""; }

// Generated:
return new global::MyApp.OrderDto(src.Id, src.Customer);
```

Parameter names are matched to source properties case-insensitively.

### `[MapConstructor]` — explicit opt-in

Use `[MapConstructor]` on the destination type to force constructor mapping even when a parameterless constructor exists, or to select the primary constructor among several:

```csharp
[MapConstructor]       // ← force ctor mapping
public class OrderDto
{
    public int Id { get; }
    public string Name { get; }

    public OrderDto() { }                              // parameterless exists, but ignored
    public OrderDto(int id, string name) { ... }       // ← selected (longest)
}

[Map(typeof(OrderDto))]
public class Order { public int Id { get; set; } public string Name { get; set; } = ""; }

// Generated:
return new global::MyApp.OrderDto(src.Id, src.Name);
```

### Mixed: ctor params + init properties

When the selected constructor covers only some properties, remaining writable properties are mapped in an object-initializer block:

```csharp
public class OrderDto
{
    public int Id { get; }
    public string Tag { get; set; } = "";
    public OrderDto(int id) { Id = id; }
}

// Generated:
return new global::MyApp.OrderDto(src.Id)
{
    Tag = src.Tag,
};
```

### AM005 — unmatched constructor parameter

If a constructor parameter has no matching source property, **AM005** is reported and `default` is emitted so the build still succeeds:

```csharp
// ⚠ AM005: Constructor parameter 'Missing' on 'OrderDto' has no matching property on 'Order'.
public record OrderDto(int Id, string Missing);

[Map(typeof(OrderDto))]
public class Order { public int Id { get; set; } }
// Generated: new OrderDto(src.Id, default)
```

---

## Property matching rules

| Rule | Behaviour |
|---|---|
| Name match | Case-insensitive name comparison |
| Type match | Source type must be **identical or implicitly convertible** to destination type |
| `[MapIgnore]` on dest | Property is skipped |
| `[MapProperty("X")]` on dest | Looks up `X` on the source instead |
| Readonly dest | Properties with no public setter/init are skipped |
| Static/indexer | Always skipped |
| Inherited properties | Source **and** destination inheritance chains are walked |

> Nested object mapping and collection mapping are resolved automatically when the related `[Map]` exists anywhere in the compilation.

---

## Attribute reference

### `[Map]` / `[MapFrom]`

| Property | Type | Description |
|---|---|---|
| *(constructor)* | `Type` | Destination type (`[Map]`) or source type (`[MapFrom]`) |
| `MethodName` | `string?` | Override the generated method name. Default: `To{TypeName}` |
| `Reverse` | `bool` | Also generate the opposite-direction mapping. Default: `false` |

### `[MapProperty]`

| Property | Type | Description |
|---|---|---|
| *(constructor)* | `string` | Name of the source property to read from |

### `[MapWith]`

| Property | Type | Description |
|---|---|---|
| *(constructor)* | `string` | C# expression using `src` as the source variable; emitted verbatim as the property assignment RHS |

### `[MapDefault]`

| Property | Type | Description |
|---|---|---|
| *(constructor)* | `string` | C# expression appended as `?? expr` after the source value; applied to direct and flattened paths |

### `[MapIgnore]`

No properties — applies to any destination property to exclude it from all mappings.

---

## Diagnostics

AutoMap.Generator ships **six built-in diagnostics** that surface problems at build time.

| ID | Severity | Meaning |
|---|---|---|
| AM001 | ⚠ Warning | No properties matched between source and destination — the mapping would be empty |
| AM002 | ❌ Error | `[MapProperty("X")]` references a source property that does not exist |
| AM003 | ❌ Error | The type passed to `[Map]` or `[MapFrom]` could not be resolved |
| AM004 | ⚠ Warning | A destination property with a matching name was skipped — incompatible types with no registered mapping |
| AM005 | ⚠ Warning | A required constructor parameter has no matching source property — `default` is emitted |
| AM006 | ⚠ Warning | A source enum member has no matching destination enum member — `_ => default` fallback used |

### AM001 example

```csharp
// ⚠ AM001: Mapping from 'Order' to 'ProductDto' produced no property matches.
[Map(typeof(ProductDto))]
public class Order { public int Id { get; set; } }
public class ProductDto { public string Sku { get; set; } = ""; }  // ← no common names

// ✅ Fix: ensure source and destination share property names, or use [MapProperty].
```

### AM002 example

```csharp
// ❌ AM002: [MapProperty("Foo")] on 'OrderDto.Name' references a property
//           that does not exist on source type 'Order'.
public class Order { public string CustomerName { get; set; } = ""; }

[MapFrom(typeof(Order))]
public class OrderDto
{
    [MapProperty("Foo")]   // ← typo!
    public string Name { get; set; } = "";
}

// ✅ Fix:
[MapProperty("CustomerName")]
public string Name { get; set; } = "";
```

---

## Records and structs

**Structs** — the null-guard is omitted since value types can't be null:

```csharp
[Map(typeof(PointDto))]
public struct Point { public int X { get; set; } public int Y { get; set; } }
// Generated: return new PointDto { X = src.X, Y = src.Y };   ← no null check
```

**Records** — `init`-only properties work out of the box with object initialiser syntax. Positional records (primary constructor parameters) require the destination type to have either a parameterless constructor or explicit `init` properties:

```csharp
// ✅ Works — standard record with init properties
public record OrderDto { public int Id { get; init; } public string Name { get; init; } = ""; }

[Map(typeof(OrderDto))]
public class Order { public int Id { get; set; } public string Name { get; set; } = ""; }
```

---

## FAQ

**Q: Does AutoMap support collection properties (List\<T\>, arrays)?**
Not in v1.0.0 — collection properties are skipped if the types don't match exactly. Map them manually with `[MapIgnore]` + a custom post-processing step.

**Q: Can I map to a type in a different assembly?**
Yes. The destination type just needs to be accessible (public, or internal with `InternalsVisibleTo`).

**Q: Does it work with nullable reference types?**
Yes. `string` → `string?` (and vice versa) maps correctly since the underlying type is the same.

**Q: Is it AOT-safe?**
Yes. All code is generated at build time — zero reflection at runtime.

**Q: Why not just use AutoMapper?**
AutoMapper is powerful but relies on runtime reflection, is not AOT-safe, and requires a `MapperConfiguration` setup. AutoMap is a build-time generator: if it compiles, it maps correctly.

---

## Also by the same author

| Package | Description |
|---|---|
| [**AutoWire**](https://github.com/Swevo/AutoWire) | Compile-time DI auto-registration — `[Scoped]`/`[Singleton]`/`[Transient]` generates `IServiceCollection` code. Zero reflection. |

---

## Contributing

Issues and PRs welcome at [github.com/Swevo/AutoMap.Generator](https://github.com/Swevo/AutoMap.Generator).

## License

MIT
