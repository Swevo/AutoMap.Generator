using System.Reflection;

namespace AutoMap.Cli;

/// <summary>
/// Implements <c>dotnet automap verify</c> — a CI gate that reflects into a built assembly's
/// generated <c>AutoMap.AutoMapGraph.Edges</c> field (always emitted by AutoMap.Generator; see
/// the "AutoMapGraph" section of the README) and diffs it against a checked-in snapshot file,
/// failing the build (non-zero exit code) when a mapping was added, removed, or renamed without
/// the snapshot being intentionally updated via <c>--update</c>.
///
/// This is AutoMap.Generator's answer to "how do I stop someone from silently deleting or
/// changing a mapping AutoMapper would have let slip through at runtime" — the whole point of a
/// compile-time mapper is that mistakes should be caught before merge, not in production logs.
/// </summary>
public static class VerifyCommand
{
    private const string GraphTypeName = "AutoMap.AutoMapGraph";
    private const string EdgesFieldName = "Edges";

    /// <summary>
    /// Runs the CLI. Kept separate from <see cref="Program.Main"/> so tests can exercise the
    /// full argument-parsing + reflection + diff pipeline against a real on-disk assembly without
    /// spawning a process.
    /// </summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args[0] != "verify")
        {
            stderr.WriteLine(Usage);
            return 2;
        }

        string? assemblyPath = null;
        string? snapshotPath = null;
        var update = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--assembly" or "-a":
                    if (++i >= args.Length) { stderr.WriteLine("Missing value for --assembly"); return 2; }
                    assemblyPath = args[i];
                    break;
                case "--snapshot" or "-s":
                    if (++i >= args.Length) { stderr.WriteLine("Missing value for --snapshot"); return 2; }
                    snapshotPath = args[i];
                    break;
                case "--update" or "-u":
                    update = true;
                    break;
                default:
                    stderr.WriteLine($"Unknown argument: {args[i]}");
                    stderr.WriteLine(Usage);
                    return 2;
            }
        }

        if (assemblyPath is null || snapshotPath is null)
        {
            stderr.WriteLine("Both --assembly and --snapshot are required.");
            stderr.WriteLine(Usage);
            return 2;
        }

        if (!File.Exists(assemblyPath))
        {
            stderr.WriteLine($"Assembly not found: {assemblyPath}");
            return 2;
        }

        IReadOnlyList<string> currentEdges;
        try
        {
            currentEdges = LoadEdges(assemblyPath);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Failed to load {GraphTypeName}.{EdgesFieldName} from '{assemblyPath}': {ex.Message}");
            return 2;
        }

        if (update)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(snapshotPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(snapshotPath, currentEdges);
            stdout.WriteLine($"Updated snapshot '{snapshotPath}' with {currentEdges.Count} mapping(s).");
            return 0;
        }

        if (!File.Exists(snapshotPath))
        {
            stderr.WriteLine($"Snapshot not found: {snapshotPath}. Run with --update to create it.");
            return 1;
        }

        var snapshotEdges = File.ReadAllLines(snapshotPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        var currentSet = new HashSet<string>(currentEdges);
        var snapshotSet = new HashSet<string>(snapshotEdges);

        var added = currentEdges.Where(e => !snapshotSet.Contains(e)).ToArray();
        var removed = snapshotEdges.Where(e => !currentSet.Contains(e)).ToArray();

        if (added.Length == 0 && removed.Length == 0)
        {
            stdout.WriteLine($"AutoMapGraph verified: {currentEdges.Count} mapping(s) match snapshot '{snapshotPath}'.");
            return 0;
        }

        stderr.WriteLine($"AutoMapGraph diverged from snapshot '{snapshotPath}':");
        foreach (var line in removed) stderr.WriteLine($"  - {line}");
        foreach (var line in added) stderr.WriteLine($"  + {line}");
        stderr.WriteLine("Run with --update if this change is intentional.");
        return 1;
    }

    /// <summary>
    /// Loads <c>AutoMap.AutoMapGraph.Edges</c> from the assembly on disk and formats each
    /// <c>(Source, Destination, MethodName)</c> tuple as a stable, sorted, human-diffable line.
    /// Uses <see cref="MetadataLoadContext"/>-free <see cref="Assembly.LoadFrom"/> because
    /// <c>System.ValueTuple</c> is a shared BCL type — the loaded assembly's
    /// <c>(string, string, string)[]</c> field can be cast directly without a custom binder.
    /// </summary>
    private static IReadOnlyList<string> LoadEdges(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        var graphType = assembly.GetType(GraphTypeName)
            ?? throw new InvalidOperationException($"Type '{GraphTypeName}' not found. Was this assembly built with AutoMap.Generator?");

        var field = graphType.GetField(EdgesFieldName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Field '{GraphTypeName}.{EdgesFieldName}' not found.");

        var edges = (ValueTuple<string, string, string>[])field.GetValue(null)!;

        return edges
            .Select(e => $"{e.Item1} -> {e.Item2} : {e.Item3}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
    }

    private const string Usage =
        """
        Usage: automap verify --assembly <path-to-dll> --snapshot <path-to-snapshot-file> [--update]

          --assembly, -a   Path to a built assembly containing an AutoMap.Generator-generated
                            AutoMap.AutoMapGraph class.
          --snapshot, -s   Path to a checked-in snapshot file listing expected mappings, one per line.
          --update, -u     Write the assembly's current mappings to the snapshot file instead of
                            diffing against it (use when a mapping change is intentional).
        """;
}
