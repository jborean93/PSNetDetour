using System.Management.Automation.Runspaces;

namespace PSNetDetour;

public sealed class UseRunspaceValue
{
    public UseRunspaceValue(string value)
    {
        FreeForm = value;
    }

    public UseRunspaceValue(Runspace runspace)
    {
        Runspace = runspace;
    }

    public UseRunspaceValue(RunspacePool runspacePool)
    {
        RunspacePool = runspacePool;
    }

    internal Runspace? Runspace { get; }
    internal RunspacePool? RunspacePool { get; }
    internal string? FreeForm { get; }
}
