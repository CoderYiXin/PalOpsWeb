namespace PalOps.Tooling.Infrastructure;

public sealed class ToolExitException : Exception
{
    public ToolExitException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }

    public static ToolExitException Verification(string message) => new(message, 1);
    public static ToolExitException Usage(string message) => new(message, 2);
}
