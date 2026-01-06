using System;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Reflection;

namespace PSNetDetour;

internal class ScriptBlockInvokeContext
{
    public MethodBase DetouredMethod { get; }
    public Type DetourMetaType { get; }
    public ScriptBlock ScriptBlock { get; }
    public InvocationInfo MyInvocation { get; }

    public PSDataStreams? ContextStreams { get; private set; }

    public ScriptBlockInvokeContext(
        MethodBase detouredMethod,
        Type detourMetaType,
        ScriptBlock scriptBlock,
        InvocationInfo myInvocation)
    {
        DetouredMethod = detouredMethod;
        DetourMetaType = detourMetaType;
        ScriptBlock = scriptBlock;
        MyInvocation = myInvocation;
    }

    public void SetCmdletContext(PSDataStreams contextStreams)
    {
        ContextStreams = contextStreams;
    }

    public void UnsetCmdletContext()
    {
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
        }

        object? detourObj = self is null
            ? Activator.CreateInstance(DetourMetaType, [orig])
            : Activator.CreateInstance(DetourMetaType, [orig, self]);

        ps.AddScript("$Detour = $args[0]; $methArgs = $args[2]; & $args[1].Ast.GetScriptBlock() @methArgs", true)
            .AddArgument(detourObj)
            .AddArgument(ScriptBlock)
            .AddArgument(args);

        Collection<PSObject?> results;
        try
        {
            results = ps.Invoke();
        }
        catch (Exception e)
        {
            throw new MethodInvocationException(
                $"Exception occurred while invoking hook for {DetouredMethod.Name}: {e.Message}",
                e);
        }

        PSObject? outputValue = null;
        if (results.Count > 0)
        {
            outputValue = results[0];
            if (results.Count > 1)
            {
                ContextStreams?.Warning.Add(
                    new WarningRecord(
                        "MultipleHookOutputValues",
                        "Received multiple output values from hook; only the first will be used."));
            }
        }

        return LanguagePrimitives.ConvertTo<T>(outputValue);
    }
}
