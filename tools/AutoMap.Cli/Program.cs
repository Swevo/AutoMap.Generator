namespace AutoMap.Cli;

/// <summary>
/// Entry point for the `automap` global tool. Delegates immediately to <see cref="VerifyCommand"/>
/// so the actual logic is testable independently of process argv/stdio.
/// </summary>
public static class Program
{
    public static int Main(string[] args) =>
        VerifyCommand.Run(args, Console.Out, Console.Error);
}
