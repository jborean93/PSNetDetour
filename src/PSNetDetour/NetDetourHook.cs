using System;
using MonoMod.RuntimeDetour;

namespace PSNetDetour;

public sealed class NetDetourHook : IDisposable
{
    private readonly Hook _hook;

    internal ScriptBlockInvokeContext InvokeContext { get; set; }

    public bool Enabled => _hook.IsApplied;

    internal NetDetourHook(Hook hook, ScriptBlockInvokeContext invokeContext)
    {
        _hook = hook;
        InvokeContext = invokeContext;
    }

    public void Dispose()
    {
        _hook.Dispose();
        InvokeContext.Dispose();
        GC.SuppressFinalize(this);
    }
    ~NetDetourHook() => Dispose();
}
