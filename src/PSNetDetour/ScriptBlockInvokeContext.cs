using System;
using System.Collections;
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
    private readonly Hashtable _usingVars;
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
        object? state,
        Hashtable usingVars,
        Runspace? runspace,
        RunspacePool? runspacePool,
        bool disposeRunspace)
    {
        _methodSignature = methodSignature;
        _detouredMethod = detouredMethod;
        _detourMetaType = detourMetaType;
        _myInvocation = myInvocation;
        _scriptBlock = scriptBlock;
        _usingVars = usingVars;
        _runspace = runspace;
        _runspacePool = runspacePool;
        _disposeRunspace = disposeRunspace;

        State = state;
    }

    internal object? State { get; }

    /// <summary>
    /// Called by Use-NetDetourContext to provide the cmdlet streams to
    /// forward output to.
    /// </summary>
    /// <param name="contextStreams">The cmdlet's PowerShell data streams to forward output to.</param>
    /// <param name="runspaceErrors">A list to capture errors from the runspace if the hook is run in a separate runspace/thread.</param>
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

        // If the hook is run in a separate runspace we need to capture any errors.
        List<ErrorRecord> uncapturedErrors = [];

        if (_contextStreams is not null)
        {
            ps.Streams.Error.DataAdded += (sender, e) =>
            {
                ErrorRecord error = ps.Streams.Error[e.Index];

                // We set the InvocationInfo to the cmdlet that created the
                // hook so the caller can see where it is from rather than the
                // Use-NetDetourContext cmdlet.
                ReflectionHelper.ErrorRecord_SetInvocationInfo(error, _myInvocation);

                // We set this property so Use-NetDetourContext doesn't
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

            // Remaining streams are forwarded automatically by PowerShell
            // to the cmdlet context unless we are in another Runspace. In
            // that case we need to hook them up manually and have the context
            // cmdlet process them at exit.
            if (isSeparateRunspace)
            {
                ps.Streams.Verbose = _contextStreams.Verbose;
                ps.Streams.Debug = _contextStreams.Debug;
                ps.Streams.Warning = _contextStreams.Warning;
                ps.Streams.Information = _contextStreams.Information;
                ps.Streams.Progress = _contextStreams.Progress;
            }
        }

        object? detourObj = self is null
            ? Activator.CreateInstance(_detourMetaType, [orig, State])
            : Activator.CreateInstance(_detourMetaType, [orig, State, self]);

        ps.AddScript("$Detour = $args[0]; $methArgs = $args[1]; & $args[2].Invoke($args[3]) @methArgs", true)
            .AddArgument(detourObj)
            .AddArgument(args)
            .AddArgument((object)ScriptBlockHelper.StripScriptBlockAffinity)
            .AddArgument(_scriptBlock);

        if (_usingVars.Count > 0)
        {
            ps.AddParameter("--%", _usingVars);
        }

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
                        $"Hook for '{_methodSignature}' produced multiple output values; only the first will be used."));
            }
        }

        if (_contextStreams is not null && _runspaceErrors is not null)
        {
            // We attempt to forward the ErrorRecord to the context cmdlet.
            // PSDataCollection will fail if we are in another thread so we
            // fallback to storing them in a List the cmdlet checks on exit.
            ForwardUncapturedStreams(uncapturedErrors, _contextStreams.Error, _runspaceErrors);
        }

        return LanguagePrimitives.ConvertTo<T>(outputValue);
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

    private static void ForwardUncapturedStreams<T>(
        IList<T> source,
        IList<T> destination,
        IList<T> backup)
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
            foreach (T record in source)
            {
                backup.Add(record);
            }
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
