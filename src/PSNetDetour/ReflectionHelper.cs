using System;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;

namespace PSNetDetour;

internal static class ReflectionHelper
{
    private static ConstructorInfo? _scriptBlock_Ctor_IParameterMetadataProvider;
    private static Type? _iParameterMetadataProvider_Type;

    public static ScriptBlock GetScriptBlockFromFunctionDefinitionAst(FunctionDefinitionAst fda)
    {
        // It is not possible to get the ScriptBlockAst from a
        // FunctionDefinitionAst using public APIs. As we need the SBK AST to
        // rebuild our ScriptBlocks with no Runspace affinity we have to use
        // what PowerShell does internally.
        // We cannot use fda.Body.GetScriptBlock() as it won't have the params
        // or other metadata defined on the function itself.

        if (_scriptBlock_Ctor_IParameterMetadataProvider is null)
        {
            _iParameterMetadataProvider_Type ??= typeof(FunctionDefinitionAst).Assembly.GetType(
                "System.Management.Automation.Language.IParameterMetadataProvider")
                ?? throw new RuntimeException("Could not find IParameterMetadataProvider type via reflection.");

            _scriptBlock_Ctor_IParameterMetadataProvider = typeof(ScriptBlock).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                [ _iParameterMetadataProvider_Type, typeof(bool) ],
                null)
                ?? throw new RuntimeException("Could not find ScriptBlock IParameterMetadataProvider constructor via reflection.");
        }

        return (ScriptBlock)_scriptBlock_Ctor_IParameterMetadataProvider.Invoke([ fda, fda.IsFilter ]);
    }

    private static MethodInfo? _errorRecord_SetInvocationInfo;

    public static void ErrorRecord_SetInvocationInfo(ErrorRecord errorRecord, InvocationInfo invocationInfo)
    {
        _errorRecord_SetInvocationInfo ??= typeof(ErrorRecord).GetMethod(
            "SetInvocationInfo",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(InvocationInfo)],
            null)
            ?? throw new RuntimeException("Could not find ErrorRecord.SetInvocationInfo method via reflection.");

        _errorRecord_SetInvocationInfo.Invoke(errorRecord, [ invocationInfo ]);
    }

    private static PropertyInfo? _errorRecord_PreserveInvocationInfoOnce;

    public static void ErrorRecord_SetPreserveInvocationInfoOnce(ErrorRecord errorRecord, bool value)
    {
        _errorRecord_PreserveInvocationInfoOnce ??= typeof(ErrorRecord).GetProperty(
            "PreserveInvocationInfoOnce",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new RuntimeException("Could not find ErrorRecord.PreserveInvocationInfoOnce property via reflection.");

        _errorRecord_PreserveInvocationInfoOnce.SetValue(errorRecord, value);
    }
}
