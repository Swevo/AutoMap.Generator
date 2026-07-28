# AutoMap.Generator Demo Template

A minimal, runnable console project demonstrating **every** AutoMap.Generator
attribute in one file — perfect for kicking the tires without touching an
existing codebase.

## Try it without installing anything

```bash
git clone https://github.com/Swevo/AutoMap.Generator.git
cd AutoMap.Generator/templates/content/AutoMap.Demo
dotnet run
```

## Install as a `dotnet new` template

From the repo root:

```bash
dotnet pack templates/AutoMap.Generator.Templates.csproj -o ./nupkgs
dotnet new install ./nupkgs/AutoMap.Generator.Templates.*.nupkg
dotnet new automap-demo -o MyAutoMapDemo
cd MyAutoMapDemo
dotnet run
```

Uninstall with:

```bash
dotnet new uninstall AutoMap.Generator.Templates
```

## What it covers

The generated `Program.cs` exercises, in order:

1. Basic `[Map]` mapping + `[TrimStrings]`
2. Automatic nested object + collection mapping
3. Cross-enum mapping by member name
4. `[MapWith]` custom expressions and `[MapFormat]`
5. `[MapWhen]` conditional mapping
6. Reverse mapping (`Reverse = true`)
7. `IMapFrom<T>` convention-based mapping
8. Constructor mapping for positional records
9. Partial method hooks (`On{MethodName}`)
10. `IQueryable` projection (`GenerateProjection = true`)

Every line prints its own output, so you can read `Program.cs` top-to-bottom
alongside the console output to see exactly which attribute produced which
result.
