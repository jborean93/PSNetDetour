using System;
using System.Collections;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Reflection;
using MonoMod.RuntimeDetour;

namespace PSNetDetour.Commands;

[Alias("nethook")]
[Cmdlet(
    VerbsCommon.New,
    "PSNetDetourHook",
    DefaultParameterSetName = "Source")]
[OutputType(typeof(NetDetourHook))]
public sealed class NewPSNetDetourHook : PSCmdlet
{
    [Parameter(
        Mandatory = true,
        Position = 0,
        ParameterSetName = "Source"
    )]
    public ScriptBlock? Source { get; set; }

    [Parameter(
        Mandatory = true,
        Position = 0,
        ParameterSetName = "Method"
    )]
    public MethodBase? Method { get; set; }

    [Parameter(
        Mandatory = true,
        Position = 1
    )]
    public ScriptBlock? Hook { get; set; }

    [Parameter]
    public SwitchParameter FindNonPublic { get; set; }

    [Parameter]
    public SwitchParameter IgnoreConstructorNew { get; set; }

    [Parameter]
    public UseRunspaceValue? UseRunspace { get; set; }

    // UsingVariables

    protected override void EndProcessing()
    {
        Debug.Assert(Hook is not null);

        Runspace? hookRunspace = null;
        RunspacePool? hookRunspacePool = null;
        bool disposeRunspace = false;

        try
        {
            if (UseRunspace?.Runspace is not null)
            {
                hookRunspace = UseRunspace.Runspace;
            }
            else if (UseRunspace?.RunspacePool is not null)
            {
                hookRunspacePool = UseRunspace.RunspacePool;
            }
            else if (UseRunspace?.FreeForm is not null)
            {
                string value = UseRunspace.FreeForm.ToUpperInvariant();
                if (value == "NEW")
                {
                    disposeRunspace = true;
                    hookRunspace = RunspaceFactory.CreateRunspace();
                    hookRunspace.Open();
                }
                else if (value == "POOL")
                {
                    disposeRunspace = true;
                    hookRunspacePool = RunspaceFactory.CreateRunspacePool();
                    hookRunspacePool.Open();
                }
                else if (value != "CURRENT")
                {
                    ErrorRecord error = new(
                        new ArgumentException(
                            $"Invalid UseRunspace value '{UseRunspace.FreeForm}'. Valid values " +
                            "are 'Current', 'New', 'Pool', or a Runspace/RunspacePool instance."),
                        "InvalidUseRunspaceValue",
                        ErrorCategory.InvalidArgument,
                        UseRunspace.FreeForm
                    );
                    ThrowTerminatingError(error);
                    return;
                }
            }

            MethodBase sourceMethod;
            if (ParameterSetName == "Method")
            {
                Debug.Assert(Method is not null);
                sourceMethod = Method!;
            }
            else
            {
                Debug.Assert(Source is not null);

                try
                {
                    sourceMethod = Source.Ast switch
                    {
                        ScriptBlockAst sba => ScriptBlockParser.ParseScriptBlockMethod(
                            sba,
                            FindNonPublic.IsPresent,
                            IgnoreConstructorNew.IsPresent),
                        FunctionDefinitionAst fda => ScriptBlockParser.ParseScriptBlockMethod(
                            fda.Body,
                            FindNonPublic.IsPresent,
                            IgnoreConstructorNew.IsPresent),
                        _ => throw new RuntimeException($"Unexpected Ast type from ScriptBlock {Source.Ast.GetType().Name}.")
                    };
                }
                catch (ParseException e)
                {
                    ParseError error = e.Errors[0];
                    ErrorRecord rec = new(
                        e,
                        error.ErrorId,
                        ErrorCategory.ParserError,
                        new Hashtable()
                        {
                            { "Line", error.Extent.StartScriptPosition.LineNumber },
                            { "LineText", error.Extent.StartScriptPosition.Line },
                            // https://github.com/PowerShell/PowerShell/pull/26649
                            { "StartColumn", error.Extent.StartColumnNumber },
                            { "EndColumn", error.Extent.EndColumnNumber },
                        })
                    {
                        ErrorDetails = new($"Failed to parse Source method: {error.Message}")
                    };

                    ThrowTerminatingError(rec);
                    return;
                }
            }

            if (sourceMethod.IsGenericMethod || sourceMethod.DeclaringType is { IsGenericType: true })
            {
                ThrowTerminatingError(
                    new ErrorRecord(
                        new NotSupportedException("Detouring generic methods or methods on generic types is not supported."),
                        "GenericMethodsNotSupported",
                        ErrorCategory.NotImplemented,
                        sourceMethod));
                return;
            }

            string sourceSignature = MethodSignature.GetOverloadDefinition(sourceMethod);
            DetourInfo detourInfo = DetourInfo.CreateDetour(
                sourceMethod);

            ScriptBlockInvokeContext invokeContext = new(
                sourceSignature,
                sourceMethod,
                detourInfo.DetourMetaType,
                Hook, MyInvocation,
                hookRunspace,
                hookRunspacePool,
                disposeRunspace);
            Hook detourHook = new(
                sourceMethod,
                detourInfo.DetourTarget,
                invokeContext);

            WriteObject(new NetDetourHook(detourHook, invokeContext));
        }
        catch
        {
            if (disposeRunspace)
            {
                hookRunspace?.Dispose();
                hookRunspacePool?.Dispose();
            }
            throw;
        }
    }
}
