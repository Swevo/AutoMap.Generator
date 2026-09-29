# AutoMap.Cli

`AutoMap.Cli` is the CI verification tool for `AutoMap.Generator`.

It provides:

- `dotnet automap verify` — validates that your generated mapping graph still matches a checked-in snapshot
- deterministic failure when mappings are unexpectedly added, removed, or renamed
- an easy CI gate to prevent silent mapping drift

## Install

```bash
dotnet tool install --global AutoMap.Cli
```

## Usage

```bash
dotnet automap verify --assembly bin/Release/net9.0/MyApp.dll --baseline automap-baseline.json
```

Typical workflow:

1. Generate/update your baseline file when intentional mapping changes are made.
2. Run `dotnet automap verify` in CI.
3. Fail the build if the graph no longer matches.

## Works with

- [`AutoMap.Generator`](https://www.nuget.org/packages/AutoMap.Generator)
- [.NET SDK global/local tool workflows](https://learn.microsoft.com/dotnet/core/tools/global-tools)
