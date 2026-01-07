using System;
using System.Collections;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PSNetDetour;

internal sealed class UseRunspaceCompleter : IArgumentCompleter
{
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters)
    {
        (string, string)[] validValues = [
            ("Current", "Use the current Runspace for invoking the hook."),
            ("New", "Create a new Runspace for invoking the hook."),
            ("Pool", "Create a new RunspacePool for invoking the hook."),
        ];

        WildcardPattern pattern = new($"{wordToComplete}*", WildcardOptions.IgnoreCase);
        foreach ((string value, string description) in validValues)
        {
            if (
                value.StartsWith(wordToComplete, StringComparison.OrdinalIgnoreCase) ||
                pattern.IsMatch(value)
            )
            {
                yield return new(
                    value,
                    value,
                    CompletionResultType.ParameterValue,
                    toolTip: description);
            }
        }
    }
}
