using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using PSNetDetour;

[assembly: InternalsVisibleTo(DetourBuilder.AssemblyName)]

namespace PSNetDetour;

internal static class DetourBuilder
{
    internal const string AssemblyName = "PSNetDetour.Dynamic";

    private static ModuleBuilder? _builder;

    private static ModuleBuilder Builder
    {
        get
        {
            if (_builder is null)
            {
                AssemblyBuilder asmBuilder = AssemblyBuilder.DefineDynamicAssembly(
                    new(AssemblyName),
                    AssemblyBuilderAccess.Run);

                _builder = asmBuilder.DefineDynamicModule(AssemblyName);
            }

            return _builder;
        }
    }

    private static MethodInfo? _arrayEmptyMethod;

    private static MethodInfo ArrayEmptyMethod
    {
        get
        {
            if (_arrayEmptyMethod is null)
            {
                _arrayEmptyMethod = typeof(Array).GetMethod(
                    "Empty",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [],
                    null)
                    ?? throw new RuntimeException("Failed to get Array.Empty<T>() method info.");
            }

            return _arrayEmptyMethod;
        }
    }

    private static MethodInfo? _invokeScriptBlockVoidMethod;

    private static MethodInfo InvokeScriptBlockVoidMethod
    {
        get
        {
            if (_invokeScriptBlockVoidMethod is null)
            {
                _invokeScriptBlockVoidMethod = typeof(ScriptBlockInvokeContext).GetMethod(
                    nameof(ScriptBlockInvokeContext.InvokeScriptBlockVoid),
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new RuntimeException("Failed to get InvokeScriptBlockVoid method info.");
            }

            return _invokeScriptBlockVoidMethod;
        }
    }

    private static MethodInfo? _invokeScriptBlockMethod;

    private static MethodInfo InvokeScriptBlockMethod
    {
        get
        {
            if (_invokeScriptBlockMethod is null)
            {
                _invokeScriptBlockMethod = typeof(ScriptBlockInvokeContext).GetMethod(
                    nameof(ScriptBlockInvokeContext.InvokeScriptBlock),
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new RuntimeException("Failed to get InvokeScriptBlock method info.");
            }

            return _invokeScriptBlockMethod;
        }
    }

    public static MethodInfo CreateDetourMethod(
        MethodBase methodToDetour)
    {
        bool isStatic = false;
        Type returnType = typeof(void);
        if (methodToDetour is MethodInfo methodInfo)
        {
            isStatic = methodInfo.IsStatic;
            returnType = methodInfo.ReturnType;
        }
        Type? instanceType = isStatic ? null : methodToDetour.DeclaringType;  // FIXME: Handle nullable DeclaringType

        Type[] parameterTypes = [.. methodToDetour.GetParameters().Select(p => p.ParameterType)];
        string typeName = $"{AssemblyName}.{methodToDetour.Name}-{Guid.NewGuid()}";

        TypeBuilder typeBuilder = Builder.DefineType(
            typeName,
            TypeAttributes.Class | TypeAttributes.Public,
            typeof(object));

        List<Type> parameterTypeList = [.. parameterTypes];
        int numParameters = parameterTypeList.Count;

        if (!isStatic && instanceType is not null)
        {
            // If the method is not static we prepend the instance type
            // before the arguments.
            parameterTypeList.Insert(0, instanceType);
        }

        Type originalDelegate = CreateDelegateType(
            $"{typeName}Delegate",
            returnType,
            parameterTypeList.ToArray());

        // Need to build the original method delegate and insert it as the first
        // argument in our detour method.
        MethodInfo targetDelegate = returnType == typeof(void)
            ? InvokeScriptBlockVoidMethod
            : InvokeScriptBlockMethod.MakeGenericMethod(returnType);
        parameterTypeList.Insert(0, originalDelegate);

        MethodBuilder hookedMethod = typeBuilder.DefineMethod(
            "Hook",
            MethodAttributes.Public,
            returnType,
            [.. parameterTypeList]);

        /*
        Creates the following function dependent on whether the method to
        detour is static/instance and has a return type or not.

        While the type isn't defined under ScriptBlockInvokeContext, MonoMod
        Will set this to the instance of ScriptBlockInvokeContext provided
        when creating the Hook delegate.

        public void Hook(Action<...> originalMethod, ...)
            => this.InvokeScriptBlockVoid(originalMethod, null, ...);

        public void Hook(Action<InstanceType, ...> originalMethod, InstanceType instance, ...)
            => this.InvokeScriptBlockVoid(originalMethod, instance, ...);

        public ReturnType Hook(Func<..., ReturnType> originalMethod, ...)
            => this.InvokeScriptBlock(originalMethod, null, ...);

        public ReturnType Hook(Func<InstanceType, ..., ReturnType> originalMethod, InstanceType instance, ...)
            => this.InvokeScriptBlock(originalMethod, instance, ...);
        */

        ILGenerator ilGen = hookedMethod.GetILGenerator();

        ilGen.Emit(OpCodes.Ldarg_0); // Load ScriptBlockInvokeContext (this)

        ilGen.Emit(OpCodes.Ldarg_1); // Load original delegate (arg0)

        int argIndexOffset = 2;
        if (!isStatic)
        {
            argIndexOffset++;
            ilGen.Emit(OpCodes.Ldarg_2); // Load the instance of the original method (arg1)
        }
        else
        {
            ilGen.Emit(OpCodes.Ldnull); // Load null for static methods to provide as the instance
        }

        if (parameterTypes.Length > 0)
        {
            // Builds the object[] array for the remaining parameters and box value type arguments
            ilGen.Emit(OpCodes.Ldc_I4, parameterTypes.Length); // Array size
            ilGen.Emit(OpCodes.Newarr, typeof(object)); // Create object[] array
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                ilGen.Emit(OpCodes.Dup);
                ilGen.Emit(OpCodes.Ldc_I4, i); // Array index

                if (parameterTypes[i].IsValueType)
                {
                    ilGen.Emit(OpCodes.Ldarg, i + argIndexOffset); // Load argument
                    ilGen.Emit(OpCodes.Box, parameterTypes[i]);
                }
                else
                {
                    ilGen.Emit(OpCodes.Ldarg_S, i + argIndexOffset); // Load argument by reference
                }

                ilGen.Emit(OpCodes.Stelem_Ref); // Store in array
            }
        }
        else
        {
            ilGen.Emit(OpCodes.Call, ArrayEmptyMethod.MakeGenericMethod(typeof(object))); // Call Array.Empty<object>()
        }

        ilGen.Emit(OpCodes.Call, targetDelegate); // Call InvokeScriptBlock
        ilGen.Emit(OpCodes.Ret);

        Type createdType = typeBuilder.CreateType()!;
        return createdType.GetMethod("Hook")!;
    }

    private static Type CreateDelegateType(
        string typeName,
        Type returnType,
        Type[] parameterTypes)
    {
        TypeBuilder delegateBuilder = Builder.DefineType(
            typeName,
            TypeAttributes.Sealed | TypeAttributes.Public,
            typeof(MulticastDelegate));

        ConstructorBuilder constructorBuilder = delegateBuilder.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            [ typeof(object), typeof(IntPtr) ]);
        constructorBuilder.SetImplementationFlags(
            MethodImplAttributes.CodeTypeMask);

        MethodBuilder delegateMethod = delegateBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            returnType,
            parameterTypes);
        delegateMethod.SetImplementationFlags(
            MethodImplAttributes.CodeTypeMask);

        return delegateBuilder.CreateType()
            ?? throw new RuntimeException("Failed to create delegate type.");
    }
}
