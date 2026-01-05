using System.Management.Automation;
using System.Reflection;

namespace PSNetDetour;

internal static class ReflectionHelper
{
    private static MethodInfo? _errorRecord_SetInvocationInfo;

    public static void ErrorRecord_SetInvocationInfo(ErrorRecord errorRecord, InvocationInfo invocationInfo)
    {
        if (_errorRecord_SetInvocationInfo is null)
        {
            _errorRecord_SetInvocationInfo = typeof(ErrorRecord).GetMethod(
                "SetInvocationInfo",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                [typeof(InvocationInfo)],
                null)
                ?? throw new RuntimeException("Could not find ErrorRecord.SetInvocationInfo method via reflection.");
        }

        _errorRecord_SetInvocationInfo.Invoke(errorRecord, [ invocationInfo ]);
    }

    private static PropertyInfo? _errorRecord_PreserveInvocationInfoOnce;

    public static void ErrorRecord_SetPreserveInvocationInfoOnce(ErrorRecord errorRecord, bool value)
    {
        if (_errorRecord_PreserveInvocationInfoOnce is null)
        {
            _errorRecord_PreserveInvocationInfoOnce = typeof(ErrorRecord).GetProperty(
                "PreserveInvocationInfoOnce",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new RuntimeException("Could not find ErrorRecord.PreserveInvocationInfoOnce property via reflection.");
        }

        _errorRecord_PreserveInvocationInfoOnce.SetValue(errorRecord, value);
    }
}
