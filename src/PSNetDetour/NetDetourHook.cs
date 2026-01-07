using System;
using System.Reflection;
using MonoMod.RuntimeDetour;

namespace PSNetDetour;

public sealed class NetDetourHook : IDisposable
{
    private readonly Hook _hook;

    internal NetDetourHook(
        Hook hook,
        ScriptBlockInvokeContext invokeContext)
    {
        _hook = hook;
        InvokeContext = invokeContext;
    }

    /// <summary>
    /// Gets the method that is being hooked.
    /// </summary>
    public MethodBase SourceMethod => _hook.Source;

    /// <summary>
    /// Gets the state object associated with the hook.
    /// </summary>
    public object? State => InvokeContext.State;

    /// <summary>
    /// Gets the invoke context for the hook, the state of this context is
    /// tied to the lifetime of the hook and this.
    /// </summary>
    internal ScriptBlockInvokeContext InvokeContext { get; set; }

    public void Dispose()
    {
        _hook.Dispose();
        InvokeContext.Dispose();
        GC.SuppressFinalize(this);
    }
    ~NetDetourHook() => Dispose();
}
