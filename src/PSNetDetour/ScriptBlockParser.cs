using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using System.Text;

namespace PSNetDetour;

internal static class ScriptBlockParser
{
    private class DefaultVariableValue
    {
        private static DefaultVariableValue? _instance;

        private DefaultVariableValue()
        { }

        public static DefaultVariableValue Value => _instance ??= new();
    }

    /// <summary>
    /// Parses a ScriptBlockAst to extract the MethodBase it represents.
    /// </summary>
    /// <param name="ast">The ScriptBlockAst to parse.</param>
    /// <param name="findNonPublic">Whether to find non-public methods.</param>
    /// <param name="ignoreConstructorNew">Whether to ignore constructor new and find a static method called new instead.</param>
    /// <returns>The MethodBase represented by the ScriptBlockAst.</returns>
    public static MethodBase ParseScriptBlockMethod(
        ScriptBlockAst ast,
        bool findNonPublic,
        bool ignoreConstructorNew)
    {
        if (ast.ParamBlock is not null)
        {
            throw CreateParseError(
                ast.ParamBlock.Extent,
                "ScriptBlockContainsParams",
                "ScriptBlock must not contain the param block.");
        }

        if (ast.DynamicParamBlock is not null)
        {
            throw CreateParseError(
                ast.DynamicParamBlock.Extent,
                "ScriptBlockContainsDynamicParams",
                "ScriptBlock must not contain the dynamicparam block.");
        }

        if (ast.BeginBlock is not null)
        {
            throw CreateParseError(
                ast.BeginBlock.Extent,
                "ScriptBlockContainsBeginBlock",
                "ScriptBlock must not contain a begin block.");
        }

        if (ast.ProcessBlock is not null)
        {
            throw CreateParseError(
                ast.ProcessBlock.Extent,
                "ScriptBlockContainsProcessBlock",
                "ScriptBlock must not contain a process block.");
        }

#if NET8_0_OR_GREATER
        if (ast.CleanBlock is not null)
        {
            throw CreateParseError(
                ast.CleanBlock.Extent,
                "ScriptBlockContainsCleanBlock",
                "ScriptBlock must not contain a clean block.");
        }
#endif

        if (ast.EndBlock.Statements.Count != 1)
        {
            throw CreateParseError(
                ast.EndBlock.Extent,
                "ScriptBlockEmptyEndBlock",
                "ScriptBlock end block must have only one statement.");
        }

        if (ast.EndBlock.Statements[0] is not PipelineAst pipelineAst)
        {
            throw CreateParseError(
                ast.EndBlock.Statements[0].Extent,
                "ScriptBlockEndBlockNotPipeline",
                "ScriptBlock end block statement must be a method call.");
        }

        if (pipelineAst.PipelineElements.Count != 1)
        {
            throw CreateParseError(
                pipelineAst.Extent,
                "ScriptBlockPipelineEmpty",
                "ScriptBlock pipeline must have only one element.");
        }

        if (pipelineAst.PipelineElements[0] is not CommandExpressionAst commandExpressionAst)
        {
            throw CreateParseError(
                pipelineAst.Extent,
                "ScriptBlockPipelineElementNotCommandExpression",
                "ScriptBlock pipeline element must be a method call.");
        }

        if (commandExpressionAst.Redirections.Count != 0)
        {
            throw CreateParseError(
                commandExpressionAst.Extent,
                "ScriptBlockCommandExpressionHasRedirections",
                "ScriptBlock command expression must not have any redirections.");
        }

        if (commandExpressionAst.Expression is not InvokeMemberExpressionAst invokeAst)
        {
            throw CreateParseError(
                commandExpressionAst.Expression.Extent,
                "ScriptBlockExpressionNotInvokeMember",
                "ScriptBlock expression must be a method call.");
        }

        if (invokeAst.Expression is not TypeExpressionAst typeExpressionAst)
        {
            throw CreateParseError(
                invokeAst.Expression.Extent,
                "ScriptBlockInvokeMemberNotTypeExpression",
                "ScriptBlock method call must be invoked on a type.");
        }

        if (invokeAst.Member is not StringConstantExpressionAst memberNameAst)
        {
            throw CreateParseError(
                invokeAst.Member.Extent,
                "ScriptBlockInvokeMemberNotStringConstant",
                "ScriptBlock method call method name must be a string constant.");
        }

        Type methodType = ResolveType(typeExpressionAst.TypeName);
        if (methodType.IsGenericType)
        {
            throw CreateParseError(
                typeExpressionAst.TypeName.Extent,
                "ScriptBlockInvokeMemberOnGenericType",
                "ScriptBlock method call type must not be a generic type, hooks do not work on generics.");
        }

        string methodName = memberNameAst.Value;
        Type[] parameterTypes = [];
        if (invokeAst.Arguments is not null)
        {
            parameterTypes = [.. invokeAst.Arguments.Select(a =>
            {
                bool isRef = false;
                if (a is ConvertExpressionAst convertAst && convertAst.Attribute is TypeConstraintAst typeConstraint)
                {
                    if (typeConstraint.TypeName.GetReflectionType() != typeof(PSReference))
                    {
                        throw CreateParseError(
                            typeConstraint.Extent,
                            "ScriptBlockArgumentInvalidTypeConstraint",
                            "Unknown script argument type constraint, only [ref] is supported for ref/out arguments.");
                    }

                    a = convertAst.Child;
                    isRef = true;
                }

                if (a is not TypeExpressionAst typeArgAst)
                {
                    throw CreateParseError(
                        a.Extent,
                        "ScriptBlockArgumentNotTypeExpression",
                        "Method call argument entry must be a single type value or [ref][typehere].");
                }

                Type resolvedType = ResolveType(typeArgAst.TypeName);
                if (isRef)
                {
                    resolvedType = resolvedType.MakeByRefType();
                }

                return resolvedType;
            })];
        }

#if NET8_0_OR_GREATER
        if (invokeAst.NullConditional)
        {
            throw CreateParseError(
                invokeAst.Extent,
                "ScriptBlockInvokeMemberIsNullConditional",
                "ScriptBlock method call must not be null-conditional.");
        }

        if (invokeAst.GenericTypeArguments is not null && invokeAst.GenericTypeArguments.Count > 0)
        {
            throw CreateParseError(
                invokeAst.GenericTypeArguments[0].Extent,
                "ScriptBlockInvokeMemberHasGenericTypeArguments",
                "ScriptBlock method call must not have generic type arguments.");
        }
#endif

        bool searchConstructor = !ignoreConstructorNew && invokeAst.Static && methodName.Equals("new", StringComparison.OrdinalIgnoreCase);

        BindingFlags bindingFlags = BindingFlags.Public;
        if (findNonPublic)
        {
            bindingFlags |= BindingFlags.NonPublic;
        }

        if (invokeAst.Static && !searchConstructor)
        {
            bindingFlags |= BindingFlags.Static;
        }
        else
        {
            bindingFlags |= BindingFlags.Instance;
        }

        MethodBase? foundMethod = null;
        if (searchConstructor)
        {
            foundMethod = methodType.GetConstructor(bindingFlags, null, parameterTypes, null);
        }
        else
        {
            foundMethod = methodType.GetMethod(methodName, bindingFlags, null, parameterTypes, null)
                ?? methodType.GetMethod(methodName, bindingFlags | BindingFlags.IgnoreCase, null, parameterTypes, null);
        }

        if (foundMethod is null)
        {
            string overload = MethodSignature.GetOverloadDefinition(
                methodType,
                methodName,
                parameterTypes,
                invokeAst.Static,
                searchConstructor);

            throw CreateParseError(
                ast.Extent,
                "ScriptBlockMethodNotFound",
                $"Failed to find the method described by the ScriptBlock: {overload}");
        }

        return foundMethod;
    }

    /// <summary>
    /// Tries to extracts all $using: variables from a ScriptBlockAst and
    /// stores their values in the usingParams hashtable our parameter.
    /// </summary>
    /// <param name="cmdlet">The PSCmdlet to use for retrieving variable values or write errors.</param>
    /// <param name="ast">The ScriptBlockAst to extract $using variables from.</param>
    /// <param name="usingParams">A Hashtable containing all the using variables found.</param>
    /// <returns>True if the using variables were successfully extracted; otherwise, false.</returns>
    /// <remarks>
    /// If this method fails to extract a using variable it will write an error
    /// using the cmdlet provided.
    /// </remarks>
    public static bool TryGetUsingParameters(
        PSCmdlet cmdlet,
        ScriptBlockAst ast,
        [NotNullWhen(true)] out Hashtable? usingParams)
    {
        usingParams = new();

        bool successful = true;
        foreach (Ast? usingStatement in ast.FindAll((a) => a is UsingExpressionAst, true))
        {
            UsingExpressionAst usingAst = (UsingExpressionAst)usingStatement;
            VariableExpressionAst backingVariableAst = UsingExpressionAst.ExtractUsingVariable(usingAst);
            string varPath = backingVariableAst.VariablePath.UserPath;

            // The non-index $using variable ensures the key is lowercased.
            // The index variant is not in case the index itself is a string
            // as that could be case sensitive.
            string varText = usingAst.ToString();
            if (usingAst.SubExpression is VariableExpressionAst)
            {
                varText = varText.ToLowerInvariant();
            }
            string key = Convert.ToBase64String(Encoding.Unicode.GetBytes(varText));

            if (!usingParams.ContainsKey(key))
            {
                object? value = cmdlet.SessionState.PSVariable.GetValue(varPath, DefaultVariableValue.Value);
                if (value is DefaultVariableValue)
                {
                    string msg = $"The value of the using variable '{usingStatement}' cannot be retrieved because it has not been set in the local session.";
                    ErrorRecord err = new(
                        new ArgumentException(msg),
                        "UsingVariableIsUndefined",
                        ErrorCategory.InvalidArgument,
                        varPath);
                    cmdlet.WriteError(err);
                    successful = false;
                    continue;
                }

                value = ExtractUsingExpressionValue(value, usingAst.SubExpression);

                usingParams.Add(key, value);
            }
        }

        return successful;
    }

    private static object? ExtractUsingExpressionValue(
        object? value,
        ExpressionAst ast)
    {
        if (ast is not MemberExpressionAst && ast is not IndexExpressionAst)
        {
            // No need to extract the inner value for simple $using:var entries.
            return value;
        }

        // We need to replace the inner VariableExpressionAst that the
        // member/index expressions are pulling from with the constant value.
        VariableExpressionAst usingVariable = (VariableExpressionAst)ast.Find(a => a is VariableExpressionAst, false);

        // We go up the hierarchy replacing the index and member expressions
        // with the new constant value/the newly wrapped AST expressions.
        ExpressionAst lookupAst = new ConstantExpressionAst(ast.Extent, value);
        ExpressionAst currentAst = usingVariable;
        while (true)
        {
            if (currentAst.Parent is IndexExpressionAst indexAst)
            {
                lookupAst = new IndexExpressionAst(
                    indexAst.Extent,
                    lookupAst,
                    (ExpressionAst)indexAst.Index.Copy()
#if NET8_0_OR_GREATER
                    , indexAst.NullConditional
#endif
                );
                currentAst = indexAst;
            }
            else if (currentAst.Parent is MemberExpressionAst memberAst)
            {
                lookupAst = new MemberExpressionAst(
                    memberAst.Extent,
                    lookupAst,
                    (ExpressionAst)memberAst.Member.Copy(),
                    memberAst.Static
#if NET8_0_OR_GREATER
                    , memberAst.NullConditional
#endif
                );
                currentAst = memberAst;
            }
            else
            {
                break;
            }
        }

        // With the new ScriptBlock we can just run it to get the final value.
        ScriptBlock extractionScriptBlock = new ScriptBlockAst(
            ast.Extent,
            null,
            new StatementBlockAst(
                ast.Extent,
                [
                    new PipelineAst(
                        ast.Extent,
                        [
                            new CommandExpressionAst(
                                ast.Extent,
                                lookupAst,
                                null)
                        ])
                ],
                null),
            false).GetScriptBlock();
        return extractionScriptBlock.Invoke().FirstOrDefault();
    }

    private static Type ResolveType(ITypeName typeName)
    {
        if (typeName is not GenericTypeName genericTypeName)
        {
            Type resolvedType = typeName.GetReflectionType()
                ?? throw CreateParseError(
                    typeName.Extent,
                    "UnknownType",
                    $"Failed to resolve type {typeName.FullName}.");

            return resolvedType;
        }

        Type genericDefinition = GetGenericTypeDefinition(
            genericTypeName.TypeName,
            genericTypeName.GenericArguments.Count);

        var resolvedArguments = new Type[genericTypeName.GenericArguments.Count];
        for (int i = 0; i < resolvedArguments.Length; i++)
        {
            resolvedArguments[i] = ResolveType(genericTypeName.GenericArguments[i]);
        }

        return genericDefinition.MakeGenericType(resolvedArguments);
    }

    private static Type GetGenericTypeDefinition(ITypeName typeName, int arity)
    {
        Type? type = typeName.GetReflectionType();
        if ((type is null || !type.ContainsGenericParameters) && typeName.FullName.IndexOf('`') == -1)
        {
            type = new TypeName(
                typeName.Extent,
                $"{typeName.FullName}`{arity}")
                .GetReflectionType();
        }

        if (type is null)
        {
            throw CreateParseError(
                typeName.Extent,
                "UnknownType",
                $"Failed to resolve type {typeName.FullName}.");
        }

        return type;
    }

    private static ParseException CreateParseError(
        IScriptExtent extent,
        string errorId,
        string message) => new([new ParseError(extent, errorId, message)]);
}
