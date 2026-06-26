# Contributing to AutoMap.Generator

Thank you for considering a contribution! Here's everything you need to get started.

---

## Quick start

```bash
# Clone
git clone https://github.com/Swevo/AutoMap.Generator.git
cd AutoMap.Generator

# Build
dotnet build

# Test
dotnet test
```

All tests live in `tests/AutoMap.Tests/MappingTests.cs`.

---

## Project structure

```
src/AutoMap/
    AutoMapGenerator.cs      ← the Roslyn IIncrementalGenerator (all generation logic)
    AutoMapAnalyzer.cs       ← diagnostic analyser (AM001–AM007)
    AutoMapCodeFixProvider.cs← code-fix lightbulb actions
tests/AutoMap.Tests/
    MappingTests.cs          ← xUnit tests covering every feature
benchmarks/AutoMap.Benchmarks/
    Program.cs               ← BenchmarkDotNet comparison vs AutoMapper / Mapperly
docs/
    index.html               ← GitHub Pages documentation site
```

---

## How the generator works

`AutoMapGenerator` is an `IIncrementalGenerator`. It:

1. Finds all types decorated with `[Map]` or `[MapFrom]` attributes.
2. Resolves source/destination property pairs (handling nested objects, collections, enums, flattening, etc.).
3. Emits a single `AutoMapExtensions.g.cs` file containing all extension methods, partial hooks, and `IAutoMapper<T, R>` singletons.

The attributes themselves (`MapAttribute`, `MapFromAttribute`, etc.) are defined as inner `sealed class` types inside `AutoMapGenerator.cs` and injected into the consumer's compilation at build time.

---

## Making a change

1. Edit `AutoMapGenerator.cs` for new mapping behaviour, or `AutoMapAnalyzer.cs` for new diagnostics.
2. Add a test in `MappingTests.cs` — each scenario is a small xUnit `[Fact]`.
3. Run `dotnet test` to confirm nothing regresses.
4. Open a PR with a clear description of what you changed and why.

### Adding a new attribute

1. Define the attribute as a `sealed class` constant string near the top of `AutoMapGenerator.cs` (search for `MapIgnoreAttribute` as a reference).
2. Parse it in `BuildMappingModel` where properties are analysed.
3. Apply the parsed value during code emission in `EmitExtensions`.
4. Add at least one test per new behaviour.

---

## Running benchmarks

```bash
cd benchmarks/AutoMap.Benchmarks
dotnet run -c Release
```

Results compare AutoMap.Generator against Mapperly, AutoMapper, and hand-written code.

---

## Reporting a bug

Please open an [issue](https://github.com/Swevo/AutoMap.Generator/issues) with:

- A minimal repro (the class definitions + `[Map]` attributes you used)
- The error or incorrect generated code you observed
- Your .NET version and AutoMap.Generator version

---

## Code style

- Match the style of the surrounding code.
- No new external runtime dependencies.
- Keep generated code readable — a developer should be able to step through it in a debugger.

---

## Licence

By contributing, you agree that your contribution will be licensed under the MIT Licence.
