using System;
using System.Collections;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Language;
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

    // UseNewRunspace
    // UsingVariables

    protected override void EndProcessing()
    {
        Debug.Assert(Hook is not null);

        MethodBase sourceMethod;
        if (ParameterSetName == "Method")
        {
            Debug.Assert(Method is not null);
            sourceMethod = Method!;
        }
        else
        {
            Debug.Assert(Source is not null);
            if (Source?.Ast is ScriptBlockAst sourceAst)
            {
                try
                {
                    sourceMethod = ScriptBlockParser.ParseScriptBlockMethod(
                        sourceAst,
                        FindNonPublic.IsPresent,
                        IgnoreConstructorNew.IsPresent);
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
            else
            {
                ThrowTerminatingError(
                    new ErrorRecord(
                        new ArgumentException("Source ScriptBlock Ast is not in the expected format."),
                        "SourceInvalidAst",
                        ErrorCategory.InvalidArgument,
                        Source));
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

        MethodInfo hookMethod = DetourBuilder.CreateDetourMethod(
            sourceMethod);

        ScriptBlockInvokeContext invokeContext = new(sourceMethod, Hook, MyInvocation);
        Hook detourHook = new(sourceMethod, hookMethod, invokeContext);

        WriteObject(new NetDetourHook(detourHook, invokeContext));
    }
}
