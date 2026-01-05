using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using PSNetDetour;

[assembly: InternalsVisibleTo(DetourInfo.AssemblyName)]

namespace PSNetDetour;

internal sealed class DetourInfo
{
    internal const string AssemblyName = "PSNetDetour.Dynamic";

    public MethodInfo DetourTarget { get; }
    public Type DetourMetaType { get; }

    public DetourInfo(
        MethodInfo detourTarget,
        Type detourMetaType)
    {
        DetourTarget = detourTarget;
        DetourMetaType = detourMetaType;
    }

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

    private static ConstructorInfo? _objectCtor;

    private static ConstructorInfo ObjectCtor
    {
        get
        {
            if (_objectCtor is null)
            {
                _objectCtor = typeof(object).GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    [],
                    null)
                    ?? throw new RuntimeException("Failed to get object constructor method info.");
            }

            return _objectCtor;
        }
    }

    public static DetourInfo CreateDetour(
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

        ParameterInfo[] methodParams = methodToDetour.GetParameters();
        Type[] parameterTypes = [.. methodParams.Select(p => p.ParameterType)];
        string typeName = $"{AssemblyName}.{methodToDetour.Name}-{Guid.NewGuid()}";

        TypeBuilder typeBuilder = Builder.DefineType(
            typeName,
            TypeAttributes.Class | TypeAttributes.Public,
            typeof(object));

        // If the method is not static we prepend the instance type
        // before the arguments.
        Type[] methodParameterTypes = isStatic || instanceType is null
            ? parameterTypes
            : [ instanceType, .. parameterTypes ];

        (Type originalDelegate, MethodInfo delegateMethod) = CreateDelegateType(
            $"{typeName}Delegate",
            returnType,
            methodParameterTypes);

        Type detourMetaType = CreateDetourMetaType(
            $"{typeName}DetourMeta",
            returnType,
            methodParams,
            originalDelegate,
            delegateMethod,
            instanceType);

        // Need to build the original method delegate and insert it as the first
        // argument in our detour method.
        MethodInfo targetDelegate = returnType == typeof(void)
            ? InvokeScriptBlockVoidMethod
            : InvokeScriptBlockMethod.MakeGenericMethod(returnType);

        MethodBuilder hookedMethod = typeBuilder.DefineMethod(
            "Hook",
            MethodAttributes.Public,
            returnType,
            [originalDelegate, .. methodParameterTypes]);

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

        Type createdType = typeBuilder.CreateType()
            ?? throw new RuntimeException("Failed to create detour type.");

        MethodInfo detourMethod = createdType.GetMethod("Hook")
            ?? throw new RuntimeException("Failed to get detour Hook method.");

        return new(detourMethod, detourMetaType);
    }

    private static (Type, MethodInfo) CreateDelegateType(
        string typeName,
        Type returnType,
        Type[] parameterTypes)
    {
        TypeBuilder builder = Builder.DefineType(
            typeName,
            TypeAttributes.Sealed | TypeAttributes.Public,
            typeof(MulticastDelegate));

        ConstructorBuilder constructorBuilder = builder.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            [ typeof(object), typeof(IntPtr) ]);
        constructorBuilder.SetImplementationFlags(
            MethodImplAttributes.CodeTypeMask);

        const string methodName = "Invoke";
        MethodBuilder delegateMethod = builder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            returnType,
            parameterTypes);
        delegateMethod.SetImplementationFlags(
            MethodImplAttributes.CodeTypeMask);

        Type delegateType = builder.CreateType()
            ?? throw new RuntimeException("Failed to create delegate type.");
        MethodInfo method = delegateType.GetMethod(methodName)
            ?? throw new RuntimeException("Failed to get delegate Invoke method.");

        return (delegateType, method);
    }

    private static Type CreateDetourMetaType(
        string typeName,
        Type returnType,
        ParameterInfo[] parameters,
        Type delegateType,
        MethodInfo delegateMethod,
        Type? instanceType)
    {
        TypeBuilder builder = Builder.DefineType(
            typeName,
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed,
            typeof(object));

        FieldBuilder delegateField = builder.DefineField(
            "_delegate",
            delegateType,
            FieldAttributes.Private);

        List<Type> constructorParameterTypes = [ delegateType ];
        FieldBuilder? instanceField = null;
        if (instanceType is not null)
        {
            constructorParameterTypes.Add(instanceType);
            instanceField = builder.DefineField(
                "Instance",
                instanceType,
                FieldAttributes.Public);
        }

        ConstructorBuilder constructorBuilder = builder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [.. constructorParameterTypes]);

        ILGenerator ctorIl = constructorBuilder.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0); // Load this
        ctorIl.Emit(OpCodes.Call, ObjectCtor); // Call base constructor
        ctorIl.Emit(OpCodes.Ldarg_0); // Load this
        ctorIl.Emit(OpCodes.Ldarg_1); // Load delegate argument
        ctorIl.Emit(OpCodes.Stfld, delegateField); // Store delegate in field

        if (instanceField is not null)
        {
            ctorIl.Emit(OpCodes.Ldarg_0); // Load this
            ctorIl.Emit(OpCodes.Ldarg_2); // Load instance argument
            ctorIl.Emit(OpCodes.Stfld, instanceField); // Store instance in field
        }

        ctorIl.Emit(OpCodes.Ret);

        Type[] parameterTypes = [.. parameters.Select(p => p.ParameterType)];
        MethodBuilder invokeMethod = builder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            returnType,
            parameterTypes);

        foreach (ParameterInfo param in parameters)
        {
            invokeMethod.DefineParameter(
                param.Position + 1,
                param.Attributes,
                param.Name);
        }

        ILGenerator invokeIl = invokeMethod.GetILGenerator();
        invokeIl.Emit(OpCodes.Ldarg_0); // Load this
        invokeIl.Emit(OpCodes.Ldfld, delegateField); // Load delegate field

        if (instanceField is not null)
        {
            invokeIl.Emit(OpCodes.Ldarg_0); // Load this
            invokeIl.Emit(OpCodes.Ldfld, instanceField); // Load instance field
        }

        for (int i = 0; i < parameterTypes.Length; i++)
        {
            invokeIl.Emit(OpCodes.Ldarg, i + 1); // Load each argument
        }
        invokeIl.Emit(OpCodes.Callvirt, delegateMethod); // Call delegate Invoke
        invokeIl.Emit(OpCodes.Ret);

        return builder.CreateType()
            ?? throw new RuntimeException("Failed to create detour type.");
    }
}
