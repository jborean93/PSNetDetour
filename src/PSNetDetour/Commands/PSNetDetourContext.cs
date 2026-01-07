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
            // We need to capture the errors from the pipeline so we can
            // forward them to the caller. All other streams are done
            // automatically by PowerShell except when the hook is run in
            // another Runspace. That is handled after Invoke() as there is no
            // public way to hook up the streams to the other Runspace.
            ps.Streams.Error.DataAdded += (sender, e) =>
            {
                WriteError(ps.Streams.Error[e.Index]);
            };

            ps.Invoke(null, output);

            // Check the other streams for any records emitted by a hook
            // invoking another Runspace. The error stream uses a custom list
            // as the PSDataStreams can only be added in the same thread
            // whereas the others do not have that restriction.
            foreach (ErrorRecord error in uncaughtErrors)
            {
                WriteError(error);
            }
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
