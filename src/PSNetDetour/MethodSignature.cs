using System;
using System.Reflection;
using System.Text;

namespace PSNetDetour;

internal static class MethodSignature
{
    public static string GetOverloadDefinition(MethodBase method)
    {
        return GetOverloadDefinition(
            method.DeclaringType!,
            method.IsConstructor ? "new" : method.Name,
            Array.ConvertAll(method.GetParameters(), p => p.ParameterType),
            method.IsStatic,
            method.IsConstructor,
            parameterNames: Array.ConvertAll(method.GetParameters(), p => p.Name),
            returnType: method is MethodInfo mi ? mi.ReturnType : null);
    }

    public static string GetOverloadDefinition(
        Type methodType,
        string methodName,
        Type[] parameterTypes,
        bool isStatic,
        bool isConstructor,
        string?[]? parameterNames = null,
        Type? returnType = null)
    {
        StringBuilder overload = new();

        if (isConstructor)
        {
            overload.AppendFormat("new {0}", methodType.Name);
        }
        else
        {
            overload.AppendFormat("{0} ", methodType.Name);

            if (isStatic)
            {
                overload.Append("static ");
            }

            if (returnType is not null)
            {
                overload.AppendFormat("{0} ", returnType.Name);
            }
            else
            {
                overload.Append("ReturnType ");
            }

            overload.AppendFormat(methodName);
        }

        overload.Append('(');
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            Type paramType = parameterTypes[i];
            string argName = $"arg{i}";
            if (parameterNames is not null && parameterNames.Length > i && !string.IsNullOrWhiteSpace(parameterNames[i]))
            {
                argName = parameterNames[i]!;
            }

            overload.AppendFormat("{0} {1}", paramType.Name, argName);
            if (i < parameterTypes.Length - 1)
            {
                overload.Append(", ");
            }
        }
        overload.Append(')');

        return overload.ToString();
    }
}
