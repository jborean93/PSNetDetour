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
    DefaultParameterSetName = "TargetScriptBlock")]
[OutputType(typeof(NetDetourHook))]
public sealed class NewPSNetDetourHook : PSCmdlet
{
    [Parameter(
        Mandatory = true,
        Position = 0,
        ParameterSetName = "TargetScriptBlock"
    )]
    public ScriptBlock? Target { get; set; }

    [Parameter(
        Mandatory = true,
        Position = 0,
        ParameterSetName = "TargetMethodInfo"
    )]
    public MethodBase? MethodInfo { get; set; }

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

        MethodBase targetMethod;
        if (ParameterSetName == "TargetMethodInfo")
        {
            Debug.Assert(MethodInfo is not null);
            targetMethod = MethodInfo!;
        }
        else
        {
            Debug.Assert(Target is not null);
            if (Target?.Ast is ScriptBlockAst targetAst)
            {
                try
                {
                    targetMethod = ScriptBlockParser.ParseScriptBlockMethod(
                        targetAst,
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
                        ErrorDetails = new($"Failed to parse Target method: {error.Message}")
                    };

                    ThrowTerminatingError(rec);
                    return;
                }
            }
            else
            {
                ThrowTerminatingError(
                    new ErrorRecord(
                        new ArgumentException("Target ScriptBlock Ast is not in the expected format."),
                        "TargetInvalidAst",
                        ErrorCategory.InvalidArgument,
                        Target));
                return;
            }
        }

        MethodInfo hookMethod = DetourBuilder.CreateDetourMethod(
            targetMethod);

        ScriptBlockInvokeContext invokeContext = new(Hook, MyInvocation);
        Hook detourHook = new(targetMethod, hookMethod, invokeContext);

        WriteObject(new NetDetourHook(detourHook, invokeContext));
    }
}
