using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Reflection;

namespace PSNetDetour;

internal class ScriptBlockInvokeContext
{
    public MethodBase DetouredMethod { get; }
    public ScriptBlock ScriptBlock { get; }
    public InvocationInfo MyInvocation { get; }

    public PSDataStreams? ContextStreams { get; private set; }
    public PSHost? Host { get; private set; }

    public ScriptBlockInvokeContext(
        MethodBase detouredMethod,
        ScriptBlock scriptBlock,
        InvocationInfo myInvocation)
    {
        DetouredMethod = detouredMethod;
        ScriptBlock = scriptBlock;
        MyInvocation = myInvocation;
    }

    public void SetCmdletContext(PSHost? host, PSDataStreams contextStreams)
    {
        Host = host;
        ContextStreams = contextStreams;
    }

    public void UnsetCmdletContext()
    {
        Host = null;
        ContextStreams = null;
    }

    internal void InvokeScriptBlockVoid(Delegate orig, object? self, params object[] args)
        => InvokeScriptBlock<object>(orig, self, args);

    internal T InvokeScriptBlock<T>(Delegate orig, object? self, params object[] args)
    {
        using PowerShell ps = PowerShell.Create(RunspaceMode.CurrentRunspace);

        if (ContextStreams is not null)
        {
            ps.Streams.Error.DataAdded += (sender, e) =>
            {
                ErrorRecord error = ps.Streams.Error[e.Index];

                // We set the InvocationInfo the the cmdlet that created the
                // hook so the caller can see where it is from rather than the
                // Use-PSNetDetourContext cmdlet.
                ReflectionHelper.ErrorRecord_SetInvocationInfo(error, MyInvocation);

                // We set this property so Use-PSNetDetourContext doesn't
                // overwrite our InvocationInfo when it calls WriteError.
                ReflectionHelper.ErrorRecord_SetPreserveInvocationInfoOnce(error, true);

                ContextStreams.Error.Add(error);
            };
            ps.Streams.Progress = ContextStreams.Progress;
            ps.Streams.Verbose = ContextStreams.Verbose;
            ps.Streams.Debug = ContextStreams.Debug;
            ps.Streams.Warning = ContextStreams.Warning;
            ps.Streams.Information = ContextStreams.Information;
        }

        PSObject detourObj = new PSObject();
        detourObj.Properties.Add(new PSNoteProperty("Instance", self));
        detourObj.Properties.Add(new PSNoteProperty("Original", orig));
        detourObj.Methods.Add(new PSScriptMethod(
            "Invoke",
            ScriptBlock.Create("$this.Original.Invoke.Invoke(@(if ($this.Instance) { $this.Instance }; $args))")));

        ps.AddScript("$Detour = $args[0]; $methArgs = $args[2]; & $args[1].Ast.GetScriptBlock() @methArgs", true)
            .AddArgument(detourObj)
            .AddArgument(ScriptBlock)
            .AddArgument(args);

        PSInvocationSettings settings = new();
        if (Host is not null)
        {
            settings.Host = Host;
        }

        Collection<PSObject> output;
        try
        {
            output = ps.Invoke(null, settings);
        }
        catch (Exception e)
        {
            throw new MethodInvocationException(
                $"Exception occurred while invoking hook for {DetouredMethod.Name}: {e.Message}",
                e);
        }

        object? outputValue = null;
        if (output.Count > 0)
        {
            outputValue = output[0].BaseObject;
            if (output.Count > 1 && ContextStreams is not null)
            {
                ContextStreams.Warning.Add(
                    new WarningRecord(
                        "Multiple return values from ScriptBlock; only the first will be used.",
                        "MultipleReturnValues"));
            }
        }

        return LanguagePrimitives.ConvertTo<T>(outputValue);

    }
}
