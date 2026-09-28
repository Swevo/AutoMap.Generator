using System.IO;
using AutoMap.Cli;
using Xunit;

namespace AutoMap.Cli.Tests;

/// <summary>
/// Exercises <see cref="VerifyCommand"/> against a real on-disk fixture assembly
/// (<c>Fixtures/FixtureLib</c>) that hand-mimics AutoMap.Generator's always-emitted
/// <c>AutoMap.AutoMapGraph</c> class, so the reflection + diff pipeline is tested end-to-end
/// without needing to run the actual source generator.
/// </summary>
public class VerifyCommandTests
{
    private static string FixtureAssemblyPath => typeof(AutoMap.AutoMapGraph).Assembly.Location;

    [Fact]
    public void Verify_MissingArguments_ReturnsUsageError()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = VerifyCommand.Run(new[] { "verify" }, stdout, stderr);

        Assert.Equal(2, exitCode);
        Assert.Contains("required", stderr.ToString());
    }

    [Fact]
    public void Verify_UnknownCommand_ReturnsUsageError()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = VerifyCommand.Run(new[] { "bogus" }, stdout, stderr);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage", stderr.ToString());
    }

    [Fact]
    public void Verify_MissingAssembly_ReturnsError()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = VerifyCommand.Run(
            new[] { "verify", "--assembly", "does-not-exist.dll", "--snapshot", "snap.txt" },
            stdout, stderr);

        Assert.Equal(2, exitCode);
        Assert.Contains("not found", stderr.ToString());
    }

    [Fact]
    public void Verify_Update_WritesSnapshotFile_AndSucceeds()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"automap-snapshot-{Guid.NewGuid():N}.txt");
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = VerifyCommand.Run(
                new[] { "verify", "--assembly", FixtureAssemblyPath, "--snapshot", snapshotPath, "--update" },
                stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(snapshotPath));

            var lines = File.ReadAllLines(snapshotPath);
            Assert.Contains(lines, l => l.Contains("Order -> OrderDto : ToOrderDto"));
            Assert.Contains(lines, l => l.Contains("Customer -> CustomerDto : ToCustomerDto"));
        }
        finally
        {
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
        }
    }

    [Fact]
    public void Verify_MatchingSnapshot_Succeeds()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"automap-snapshot-{Guid.NewGuid():N}.txt");
        try
        {
            var updateStdout = new StringWriter();
            var updateStderr = new StringWriter();
            var updateExit = VerifyCommand.Run(
                new[] { "verify", "--assembly", FixtureAssemblyPath, "--snapshot", snapshotPath, "--update" },
                updateStdout, updateStderr);
            Assert.Equal(0, updateExit);

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = VerifyCommand.Run(
                new[] { "verify", "--assembly", FixtureAssemblyPath, "--snapshot", snapshotPath },
                stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Contains("verified", stdout.ToString());
        }
        finally
        {
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
        }
    }

    [Fact]
    public void Verify_DivergedSnapshot_FailsWithDiff()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"automap-snapshot-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(snapshotPath, new[]
            {
                "Order -> OrderDto : ToOrderDto",
                "Removed -> RemovedDto : ToRemovedDto",
            });

            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = VerifyCommand.Run(
                new[] { "verify", "--assembly", FixtureAssemblyPath, "--snapshot", snapshotPath },
                stdout, stderr);

            Assert.Equal(1, exitCode);
            var errorOutput = stderr.ToString();
            Assert.Contains("- Removed -> RemovedDto : ToRemovedDto", errorOutput);
            Assert.Contains("+ Customer -> CustomerDto : ToCustomerDto", errorOutput);
        }
        finally
        {
            if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
        }
    }

    [Fact]
    public void Verify_MissingSnapshotFile_ReturnsErrorSuggestingUpdate()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"automap-snapshot-{Guid.NewGuid():N}.txt");
        if (File.Exists(snapshotPath)) File.Delete(snapshotPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = VerifyCommand.Run(
            new[] { "verify", "--assembly", FixtureAssemblyPath, "--snapshot", snapshotPath },
            stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.Contains("--update", stderr.ToString());
    }
}
