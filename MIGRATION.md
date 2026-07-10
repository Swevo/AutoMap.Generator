# Migrating from AutoMapper to AutoMap.Generator

AutoMap.Generator covers the vast majority of everyday AutoMapper usage with zero reflection, zero startup overhead, and full AOT / MAUI support. This guide walks through the most common AutoMapper patterns and their AutoMap.Generator equivalents.

> Tip: AutoMap.Generator also ships an **AM009** analyzer + code fix that spots AutoMapper-style `CreateMap<TSource, TDest>()` calls and can add `[Map(typeof(TDest))]` to the source type for you. Fluent member configuration still needs to be migrated manually using the patterns below.

---

## Why migrate?

| | AutoMapper | AutoMap.Generator |
|---|---|---|
| **Overhead** | Reflection + startup cost | None — plain C# generated at build time |
| **Errors** | Runtime exceptions | Build-time compiler errors and warnings |
| **AOT / MAUI** | ❌ | ✅ |
| **Debuggable** | Black-box reflection | Generated code you can read and step into |
| **DI required** | Yes (`IMapper`) | No — extension methods, no setup |

---

## Quick comparison

### 1. Basic mapping

**AutoMapper**
```csharp
// Startup
var config = new MapperConfiguration(cfg => cfg.CreateMap<Order, OrderDto>());
var mapper = config.CreateMapper();

// Usage
var dto = mapper.Map<OrderDto>(order);
```

**AutoMap.Generator**
```csharp
// Attribute on the source class
[Map(typeof(OrderDto))]
public class Order { ... }

// Usage (generated extension method — no setup needed)
var dto = order.ToOrderDto();
```

---

### 2. Reverse mapping

**AutoMapper**
```csharp
cfg.CreateMap<Order, OrderDto>().ReverseMap();
```

**AutoMap.Generator**
```csharp
[Map(typeof(OrderDto), Reverse = true)]
public class Order { ... }
// Generates both order.ToOrderDto() and dto.ToOrder()
```

---

### 3. Ignore a property

**AutoMapper**
```csharp
cfg.CreateMap<Order, OrderDto>()
   .ForMember(d => d.InternalCode, opt => opt.Ignore());
```

**AutoMap.Generator** — place on the *destination* property:
```csharp
[MapFrom(typeof(Order))]
public class OrderDto
{
    [MapIgnore]
    public string InternalCode { get; set; } = "";
}
```

---

### 4. Map from a differently-named source property

**AutoMapper**
```csharp
cfg.CreateMap<Order, OrderDto>()
   .ForMember(d => d.Client, opt => opt.MapFrom(s => s.CustomerName));
```

**AutoMap.Generator** — place on the *destination* property:
```csharp
[MapFrom(typeof(Order))]
public class OrderDto
{
    [MapProperty("CustomerName")]
    public string Client { get; set; } = "";
}
```

---

### 5. Custom value resolver / expression

**AutoMapper**
```csharp
cfg.CreateMap<Order, OrderDto>()
   .ForMember(d => d.PriceFormatted, opt => opt.MapFrom(s => s.Price.ToString("C2")));
```

**AutoMap.Generator** — use `[MapWith]` with any C# expression:
```csharp
[MapFrom(typeof(Order))]
public class OrderDto
{
    [MapWith("src.Price.ToString(\"C2\")")]
    public string PriceFormatted { get; set; } = "";
}
```

---

### 6. Null substitution

**AutoMapper**
```csharp
cfg.CreateMap<Order, OrderDto>()
   .ForMember(d => d.Region, opt => opt.NullSubstitute("Global"));
```

**AutoMap.Generator** — use `[MapDefault]` with a C# expression:
```csharp
[MapFrom(typeof(Order))]
public class OrderDto
{
    [MapDefault("\"Global\"")]
    public string Region { get; set; } = "";
    // Generated: Region = src.Region ?? "Global",
}
```

---

### 7. Conditional mapping

**AutoMapper**
```csharp
cfg.CreateMap<Order, OrderDto>()
   .ForMember(d => d.Name, opt => opt.Condition(s => s.IsActive));
```

**AutoMap.Generator** — use `[MapWhen]`:
```csharp
[MapFrom(typeof(Order))]
public class OrderDto
{
    [MapWhen("src.IsActive")]
    public string Name { get; set; } = "";
    // Generated: Name = src.IsActive ? src.Name : default,

    // With a fallback value:
    [MapWhen("src.IsActive", Fallback = "\"Inactive\"")]
    public string Status { get; set; } = "";
}
```

---

### 8. Flattening

**AutoMapper** — built-in, automatic for `CustomerName` → `Customer.Name`.

**AutoMap.Generator** — also automatic, up to 3 levels deep:
```csharp
// Source: Order.Customer.Name
[MapFrom(typeof(Order))]
public class OrderDto
{
    public string CustomerName { get; set; } = "";
    // Generated: CustomerName = src.Customer?.Name,

    public string CustomerAddressCity { get; set; } = "";
    // Generated: CustomerAddressCity = src.Customer?.Address?.City,
}
```

---

### 9. Nested object mapping

**AutoMapper** — resolved automatically from registered maps.

**AutoMap.Generator** — mark each nested type with `[Map]`; resolution is automatic:
```csharp
[Map(typeof(AddressDto))]
public class Address { public string City { get; set; } = ""; }

[Map(typeof(OrderDto))]
public class Order
{
    public int     Id      { get; set; }
    public Address Address { get; set; }
}
// Generated: Address = src.Address?.ToAddressDto(),
```

---

### 10. Collection mapping

**AutoMapper** — automatic.

**AutoMap.Generator** — automatic when element type has a `[Map]`:
```csharp
[Map(typeof(ItemDto))]
public class Item { ... }

[Map(typeof(OrderDto))]
public class Order { public List<Item> Items { get; set; } = new(); }
// Generated: Items = src.Items?.Select(x => x.ToItemDto()).ToList(),
```

---

### 11. Enum mapping

**AutoMapper**
```csharp
cfg.CreateMap<OrderStatus, OrderStatusDto>();
```

**AutoMap.Generator** — automatic when property types are different enums; matched by name:
```csharp
// No configuration needed — detected automatically
// Generated switch expression:
// Status = src.Status switch {
//     OrderStatus.Pending => OrderStatusDto.Pending,
//     OrderStatus.Active  => OrderStatusDto.Active,
//     _ => default
// },

// Rename a value:
public enum OrderStatus { [MapEnum("Running")] Active, Done }
```

---

### 12. Constructor mapping (records / immutable types)

**AutoMapper**
```csharp
cfg.CreateMap<Order, OrderDto>().ConstructUsing(s => new OrderDto(s.Id, s.Name));
```

**AutoMap.Generator** — automatic for types without a parameterless constructor:
```csharp
public record OrderDto(int Id, string Name);  // positional record

[Map(typeof(OrderDto))]
public class Order { public int Id { get; set; } public string Name { get; set; } = ""; }
// Generated: return new OrderDto(src.Id, src.Name);
```

---

### 13. Using `IMapper` in services

**AutoMapper**
```csharp
services.AddAutoMapper(typeof(OrderProfile));

public class OrderService(IMapper mapper)
{
    public OrderDto GetDto(Order order) => mapper.Map<OrderDto>(order);
}
```

**AutoMap.Generator** — use the generated `IAutoMapper<T, R>` interface:
```csharp
// Register:
services.AddSingleton<IAutoMapper<Order, OrderDto>>(
    AutoMapExtensions.OrderToOrderDtoMapper.Instance);

// Inject:
public class OrderService(IAutoMapper<Order, OrderDto> mapper)
{
    public OrderDto GetDto(Order order) => mapper.Map(order);
}

// Or skip DI entirely — just call the extension method:
public OrderDto GetDto(Order order) => order.ToOrderDto();
```

---

## Step-by-step migration

1. **Install** `AutoMap.Generator`:
   ```
   dotnet add package AutoMap.Generator
   ```

2. **Add attributes** to your domain/source classes:
   ```csharp
   [Map(typeof(OrderDto))]
   public class Order { ... }
   ```
   Or on the DTO side if you prefer keeping domain classes clean:
   ```csharp
   [MapFrom(typeof(Order))]
   public class OrderDto { ... }
   ```

3. **Replace `mapper.Map<Dto>(entity)`** with the generated extension method:
   ```csharp
   // Before
   var dto = _mapper.Map<OrderDto>(order);

   // After
   var dto = order.ToOrderDto();
   ```

4. **Move customisations** from `CreateMap` profiles to property attributes (`[MapIgnore]`, `[MapProperty]`, `[MapWith]`, `[MapWhen]`, `[MapDefault]`).

5. **Build** — AM001–AM006 diagnostics will flag any gaps at compile time, not runtime.

6. **Remove AutoMapper** once all profiles are migrated:
   ```
   dotnet remove package AutoMapper
   dotnet remove package AutoMapper.Extensions.Microsoft.DependencyInjection
   ```

---

## What AutoMap.Generator doesn't support

Be aware of these before migrating:

| Feature | Notes |
|---|---|
| `IQueryable` projection (`ProjectTo<T>`) | Requires expression trees — inherently runtime. Use a dedicated projection library or hand-write LINQ instead. |
| `BeforeMap` / `AfterMap` hooks | Use a wrapper method or a custom service layer. |
| Polymorphic / `Include<Derived>()` | No equivalent. Map each subtype individually. |
| Runtime conditional mapping | `[MapWhen]` covers compile-time conditions. For runtime logic, use `[MapWith("condition ? expr : expr2")]`. |
| Open generics mapping | Not supported. |

For projects using any of the above heavily, a full migration may not be appropriate.

---

## Need help?

Open an issue at [github.com/Swevo/AutoMap.Generator](https://github.com/Swevo/AutoMap.Generator/issues) — include your AutoMapper `CreateMap` / `ForMember` setup and we'll help you find the equivalent.
