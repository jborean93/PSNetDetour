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

    [Parameter]
    public SwitchParameter NoNewScope { get; set; }

    protected override void EndProcessing()
    {
        Debug.Assert(ScriptBlock is not null);

        List<ErrorRecord> uncaughtErrors = [];
        using PowerShell ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        ps.AddScript($"{(NoNewScope ? "." : "&")} $args[0].Invoke($args[1])")
            .AddArgument((object)ScriptBlockHelper.StripScriptBlockAffinity)
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
                    hook.InvokeContext.SetCmdletContext(ps.Streams, uncaughtErrors);
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

            ps.Invoke(null, output);

            // Check if there were any uncaught errors from hooks invoked in
            // other runspaces/threads that couldn't be written earlier.
            foreach (ErrorRecord error in uncaughtErrors)
            {
                WriteError(error);
            }

            // The remaining streams will be present in the PSDataStreams
            // if they weren't able to be forwarded earlier.
            foreach (VerboseRecord verbose in ps.Streams.Verbose)
            {
                WriteVerbose(verbose.ToString() ?? string.Empty);
            }
            foreach (DebugRecord debug in ps.Streams.Debug)
            {
                WriteDebug(debug.ToString() ?? string.Empty);
            }
            foreach (WarningRecord warning in ps.Streams.Warning)
            {
                WriteWarning(warning.ToString() ?? string.Empty);
            }
            foreach(ProgressRecord progress in ps.Streams.Progress)
            {
                WriteProgress(progress);
            }
            foreach (InformationRecord info in ps.Streams.Information)
            {
                WriteInformation(info);
            }
        }
        finally
        {
            foreach (NetDetourHook hook in hooks)
            {
                hook.Dispose();
            }
        }
    }
}
