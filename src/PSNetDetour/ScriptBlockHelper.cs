using System.Management.Automation;
using System.Management.Automation.Language;

namespace PSNetDetour;

internal static class ScriptBlockHelper
{
    public static ScriptBlock StripScriptBlockAffinity(ScriptBlock scriptBlock) => scriptBlock.Ast switch
    {
        ScriptBlockAst sba => sba.GetScriptBlock(),
        FunctionDefinitionAst fda => ReflectionHelper.GetScriptBlockFromFunctionDefinitionAst(fda),
        _ => throw new RuntimeException($"Unexpected Ast type from ScriptBlock {scriptBlock.Ast.GetType().Name}.")
    };
}
