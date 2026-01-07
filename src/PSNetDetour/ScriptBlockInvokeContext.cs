using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;

namespace PSNetDetour;

internal sealed class ScriptBlockInvokeContext : IDisposable
{
    private readonly string _methodSignature;
    private readonly MethodBase _detouredMethod;
    private readonly Type _detourMetaType;
    private readonly InvocationInfo _myInvocation;
    private readonly ScriptBlock _scriptBlock;
    private readonly Runspace? _runspace;
    private readonly RunspacePool? _runspacePool;
    private readonly bool _disposeRunspace;

    private PSDataStreams? _contextStreams;
    private List<ErrorRecord>? _runspaceErrors;

    public ScriptBlockInvokeContext(
        string methodSignature,
        MethodBase detouredMethod,
        Type detourMetaType,
        ScriptBlock scriptBlock,
        InvocationInfo myInvocation,
        Runspace? runspace,
        RunspacePool? runspacePool,
        bool disposeRunspace)
    {
        _methodSignature = methodSignature;
        _detouredMethod = detouredMethod;
        _detourMetaType = detourMetaType;
        _myInvocation = myInvocation;
        _scriptBlock = scriptBlock;
        _runspace = runspace;
        _runspacePool = runspacePool;
        _disposeRunspace = disposeRunspace;
    }

    public void SetCmdletContext(PSDataStreams contextStreams, List<ErrorRecord> runspaceErrors)
    {
        _contextStreams = contextStreams;
        _runspaceErrors = runspaceErrors;
    }

    internal void InvokeScriptBlockVoid(Delegate orig, object? self, params object[] args)
        => InvokeScriptBlock<object>(orig, self, args);

    internal T InvokeScriptBlock<T>(Delegate orig, object? self, params object[] args)
    {
        using PowerShell ps = CreatePowerShell(out bool isSeparateRunspace);

        // If the hook is run in a separate runspace we need to capture any errors
        // that cannot be forwarded directly to the cmdlet context.
        List<ErrorRecord> uncapturedErrors = [];

        if (_contextStreams is not null)
        {
            ps.Streams.Error.DataAdded += (sender, e) =>
            {
                ErrorRecord error = ps.Streams.Error[e.Index];

                // We set the InvocationInfo the the cmdlet that created the
                // hook so the caller can see where it is from rather than the
                // Use-PSNetDetourContext cmdlet.
                ReflectionHelper.ErrorRecord_SetInvocationInfo(error, _myInvocation);

                // We set this property so Use-PSNetDetourContext doesn't
                // overwrite our InvocationInfo when it calls WriteError.
                ReflectionHelper.ErrorRecord_SetPreserveInvocationInfoOnce(error, true);

                if (isSeparateRunspace)
                {
                    // We cannot forward the error if it was raised in another
                    // Runspace as the context Runspace. We try after the hook
                    // is invoked.
                    uncapturedErrors.Add(error);
                }
                else
                {
                    _contextStreams.Error.Add(error);
                }
            };
        }

        object? detourObj = self is null
            ? Activator.CreateInstance(_detourMetaType, [orig])
            : Activator.CreateInstance(_detourMetaType, [orig, self]);

        ps.AddScript("$Detour = $args[0]; $methArgs = $args[1]; & $args[2].Invoke($args[3]) @methArgs", true)
            .AddArgument(detourObj)
            .AddArgument(args)
            .AddArgument((object)ScriptBlockHelper.StripScriptBlockAffinity)
            .AddArgument(_scriptBlock);

        Collection<PSObject?> results;
        try
        {
            results = ps.Invoke();
        }
        catch (Exception e)
        {
            throw new MethodInvocationException(
                $"Exception occurred while invoking hook for {_detouredMethod.Name}: {e.Message}",
                e);
        }

        PSObject? outputValue = null;
        if (results.Count > 0)
        {
            outputValue = results[0];
            if (results.Count > 1)
            {
                _contextStreams?.Warning.Add(
                    new WarningRecord(
                        "MultipleHookOutputValues",
                        "Received multiple output values from hook; only the first will be used."));
            }
        }

        if (_contextStreams is not null)
        {
            // Error is special in that PSDataCollection.Add will throw if the
            // calling from another thread in which it is hooked up to. We have
            // a backup mechanism which can smuggle these errors back to the
            // cmdlet context after it ends.
            ForwardUncapturdStreams(uncapturedErrors, _contextStreams.Error, backup: _runspaceErrors);

            // Other streams can be forwarded directly even from another thread.
            ForwardUncapturdStreams(ps.Streams.Progress, _contextStreams.Progress);
            ForwardUncapturdStreams(ps.Streams.Verbose, _contextStreams.Verbose);
            ForwardUncapturdStreams(ps.Streams.Debug, _contextStreams.Debug);
            ForwardUncapturdStreams(ps.Streams.Warning, _contextStreams.Warning);
            ForwardUncapturdStreams(ps.Streams.Information, _contextStreams.Information);
        }

        return LanguagePrimitives.ConvertTo<T>(outputValue);
    }

    private static void ForwardUncapturdStreams<T>(
        IList<T> source,
        IList<T> destination,
        IList<T>? backup = null)
    {
        if (source.Count == 0)
        {
            return;
        }

        try
        {
            foreach (T record in source)
            {
                destination.Add(record);
            }
        }
        catch (PSInvalidOperationException)
        {
            if (backup is not null)
            {
                foreach (T record in source)
                {
                    backup.Add(record);
                }
            }
            else
            {
                throw;
            }
        }
    }

    private PowerShell CreatePowerShell(out bool isSeparateRunspace)
    {
        isSeparateRunspace = false;
        if (_runspace is not null)
        {
            isSeparateRunspace = true;
            PowerShell ps = PowerShell.Create();
            ps.Runspace = _runspace;
            return ps;
        }
        else if (_runspacePool is not null)
        {
            isSeparateRunspace = true;
            PowerShell ps = PowerShell.Create();
            ps.RunspacePool = _runspacePool;
            return ps;
        }
        else if (Runspace.DefaultRunspace is null)
        {
            throw new RuntimeException(
                $"Hook for '{_methodSignature}' is being invoked in a thread with no active Runspace. " +
                "Create hook with -UseRunspace New or -UseRunspace Pool to invoke the " +
                "hook in a separate Runspace or RunspacePool.");
        }
        else
        {
            isSeparateRunspace = false;
            return PowerShell.Create(RunspaceMode.CurrentRunspace);
        }
    }

    public void Dispose()
    {
        if (_disposeRunspace)
        {
            _runspace?.Dispose();
            _runspacePool?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
    ~ScriptBlockInvokeContext() { Dispose(); }
}
