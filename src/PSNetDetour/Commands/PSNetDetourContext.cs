using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;

namespace PSNetDetour.Commands;

[Cmdlet(VerbsOther.Use, "PSNetDetourContext")]
public sealed class UsePSNetDetourContext : PSCmdlet
{
    [Parameter(
        Mandatory = true,
        Position = 0
    )]
    public ScriptBlock? ScriptBlock { get; set; }

    protected override void EndProcessing()
    {
        Debug.Assert(ScriptBlock is not null);

        using PowerShell ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        ps.AddScript("& $args[0].Ast.GetScriptBlock()")
            .AddArgument(ScriptBlock);

        List<NetDetourHook> hooks = [];
        try
        {
            PSDataCollection<PSObject?> output = [];
            output.DataAdding += (sender, e) =>
            {
                // Capture any NetDetourHook objects to dispose of later. We
                // also store some of this cmdlets state in the hook so it can
                // emit errors/warnings/etc back to this cmdlet.
                object? item = e.ItemAdded;
                if (item is not null && PSObject.AsPSObject(item).BaseObject is NetDetourHook hook)
                {
                    hook.InvokeContext.SetCmdletContext(Host, ps.Streams);
                    hooks.Add(hook);
                    return;
                }

                // All other items are ouput when received.
                WriteObject(e.ItemAdded);
            };
            ps.Streams.Error.DataAdded += (sender, e) =>
            {
                WriteError(ps.Streams.Error[e.Index]);
            };
            ps.Streams.Progress.DataAdded += (sender, e) =>
            {
                WriteProgress(ps.Streams.Progress[e.Index]);
            };
            ps.Streams.Verbose.DataAdded += (sender, e) =>
            {
                WriteVerbose(ps.Streams.Verbose[e.Index].Message);
            };
            ps.Streams.Debug.DataAdded += (sender, e) =>
            {
                WriteDebug(ps.Streams.Debug[e.Index].Message);
            };
            ps.Streams.Warning.DataAdded += (sender, e) =>
            {
                WriteWarning(ps.Streams.Warning[e.Index].Message);
            };
            ps.Streams.Information.DataAdded += (sender, e) =>
            {
                WriteInformation(ps.Streams.Information[e.Index]);
            };

            PSInvocationSettings settings = new()
            {
                Host = Host,
            };
            ps.Invoke(null, output, settings);
        }
        finally
        {
            foreach (NetDetourHook hook in hooks)
            {
                hook.InvokeContext.UnsetCmdletContext();
                hook.Dispose();
            }
        }
    }
}
