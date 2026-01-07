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

    private static MethodInfo? _array_Empty;

    private static MethodInfo Array_Empty
    {
        get
        {
            if (_array_Empty is null)
            {
                _array_Empty = typeof(Array).GetMethod(
                    "Empty",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [],
                    null)
                    ?? throw new RuntimeException("Failed to get Array.Empty<T>() method info.");
            }

            return _array_Empty;
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

    private static ConstructorInfo? _object_Ctor;

    private static ConstructorInfo Object_Ctor
    {
        get
        {
            if (_object_Ctor is null)
            {
                _object_Ctor = typeof(object).GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    [],
                    null)
                    ?? throw new RuntimeException("Failed to get object constructor method info.");
            }

            return _object_Ctor;
        }
    }

    private static ConstructorInfo? _psReference_Ctor;

    private static ConstructorInfo PSReference_Ctor
    {
        get
        {
            if (_psReference_Ctor is null)
            {
                _psReference_Ctor = typeof(PSReference).GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    [ typeof(object) ],
                    null)
                    ?? throw new RuntimeException("Failed to get PSReference constructor method info.");
            }

            return _psReference_Ctor;
        }
    }

    private static MethodInfo? _psReference_GetValue;

    private static MethodInfo PSReference_GetValue
    {
        get
        {
            if (_psReference_GetValue is null)
            {
                _psReference_GetValue = typeof(PSReference).GetMethod(
                    "get_Value",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    [],
                    null)
                    ?? throw new RuntimeException("Failed to get PSReference.get_Value method info.");
            }

            return _psReference_GetValue;
        }
    }

    private static MethodInfo? _psReference_SetValue;

    private static MethodInfo PSReference_SetValue
    {
        get
        {
            if (_psReference_SetValue is null)
            {
                _psReference_SetValue = typeof(PSReference).GetMethod(
                    "set_Value",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    [ typeof(object) ],
                    null)
                    ?? throw new RuntimeException("Failed to get PSReference.set_Value method info.");
            }

            return _psReference_SetValue;
        }
    }

    private static MethodInfo? _languagePrimitives_ConvertTo;

    private static MethodInfo LanguagePrimitives_ConvertTo
    {
        get
        {
            if (_languagePrimitives_ConvertTo is null)
            {
                _languagePrimitives_ConvertTo = typeof(LanguagePrimitives).GetMethod(
                    "ConvertTo",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [ typeof(object)  ],
                    null)
                    ?? throw new RuntimeException("Failed to get LanguagePrimitives.ConvertTo method info.");
            }

            return _languagePrimitives_ConvertTo;
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

        // This is the type that will be set to $Detour that can be used in the
        // ScriptBlock to access the Instance and original method to invoke.
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
        when creating the Hook delegate. For static methods object instance
        is omitted as an arg and null is passed to InvokeScriptBlock. The args
        are treated as separate parameters and represented by ... to indicate
        that they are variable depending on the method signature.

        public ... Hook(Delegate originalMethod, [object instance], ... args)
        {
            // Store each ref/out argument as a local PSReference variable
            object[] localArgs = new object[...];
            foreach (... in args)
            {
                localArgs[i] = ... is ByRef
                    ? new PSReference(...)
                    : ...;
            }

            ... res = InvokeScriptBlock(originalMethod, instance, localArgs)

            foreach (l in localArgs)
            {
                if (l is PSReference)
                {
                    // Unbox/ref back to original ByRef argument
                    args[i] = LanguagePrimitives.ConvertTo<T>(l.Value);
                }
            }

            return res;
        }
        */

        int ldargArgumentIndexOffset = isStatic ? 2 : 3;

        ILGenerator ilGen = hookedMethod.GetILGenerator();

        LocalBuilder? resultLocal = null;
        if (returnType != typeof(void))
        {
            resultLocal = ilGen.DeclareLocal(returnType);
        }

        // Scan all args to see if we have any ByRef parameters. These need to
        // stored as locals as PSReference instances so we can pass them to the
        // ScriptBlock and update the original ByRef parameters after the call.
        (Type, LocalBuilder)?[] boxedRefLocal = new (Type, LocalBuilder)?[parameterTypes.Length];
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            Type refType = parameterTypes[i];
            if (!refType.IsByRef)
            {
                continue;
            }

            Type paramType = refType.GetElementType()
                ?? throw new RuntimeException("Failed to get element type of ByRef parameter.");
            LocalBuilder localVal = ilGen.DeclareLocal(typeof(PSReference));
            boxedRefLocal[i] = (paramType, localVal);

            ilGen.Emit(OpCodes.Ldarg, i + ldargArgumentIndexOffset);

            // Loads (and boxes value types) to create PSReference
            if (paramType.IsValueType)
            {
                ilGen.Emit(OpCodes.Ldobj, paramType);
                ilGen.Emit(OpCodes.Box, paramType);
            }
            else
            {
                ilGen.Emit(OpCodes.Ldind_Ref);
            }

            ilGen.Emit(OpCodes.Newobj, PSReference_Ctor); // Create new PSReference
            ilGen.Emit(OpCodes.Stloc, localVal); // Store the PSReference in the local variable
        }

        ilGen.Emit(OpCodes.Ldarg_0); // Load ScriptBlockInvokeContext (this)
        ilGen.Emit(OpCodes.Ldarg_1); // Load original delegate (arg0)

        if (isStatic)
        {
            ilGen.Emit(OpCodes.Ldnull); // Load null for static methods to provide as the instance
        }
        else
        {

            ilGen.Emit(OpCodes.Ldarg_2); // Load the instance of the original method (arg1)
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

                (Type, LocalBuilder)? refLocal = boxedRefLocal[i];
                if (refLocal is not null)
                {
                    ilGen.Emit(OpCodes.Ldloc, refLocal.Value.Item2); // Load PSReference local
                }
                else if (parameterTypes[i].IsValueType)
                {
                    ilGen.Emit(OpCodes.Ldarg, i + ldargArgumentIndexOffset); // Load argument
                    ilGen.Emit(OpCodes.Box, parameterTypes[i]);
                }
                else
                {
                    ilGen.Emit(OpCodes.Ldarg_S, i + ldargArgumentIndexOffset); // Load argument by reference
                }

                ilGen.Emit(OpCodes.Stelem_Ref); // Store in array
            }
        }
        else
        {
            ilGen.Emit(OpCodes.Call, Array_Empty.MakeGenericMethod(typeof(object))); // Call Array.Empty<object>()
        }

        ilGen.Emit(OpCodes.Call, targetDelegate); // Call InvokeScriptBlock

        if (resultLocal is not null)
        {
            ilGen.Emit(OpCodes.Stloc, resultLocal); // Store result
        }

        // Unbox/ref back any ByRef parameters from the PSReference locals
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            (Type, LocalBuilder)? refLocal = boxedRefLocal[i];
            if (refLocal is null)
            {
                continue;
            }

            Type argType = refLocal.Value.Item1;
            LocalBuilder psRefLocal = refLocal.Value.Item2;
            MethodInfo convertMethod = LanguagePrimitives_ConvertTo.MakeGenericMethod(argType);

            ilGen.Emit(OpCodes.Ldarg, i + ldargArgumentIndexOffset); // Load original ByRef argument
            ilGen.Emit(OpCodes.Ldloc, psRefLocal); // Load PSReference local for the arg
            ilGen.Emit(OpCodes.Callvirt, PSReference_GetValue); // Call PSReference.get_Value
            ilGen.Emit(OpCodes.Call, convertMethod); // Convert to the proper type

            // Set the value back to the original ByRef argument
            if (argType.IsValueType)
            {
                ilGen.Emit(OpCodes.Stobj, argType);
            }
            else
            {
                ilGen.Emit(OpCodes.Stind_Ref);
            }
        }

        if (resultLocal is not null)
        {
            ilGen.Emit(OpCodes.Ldloc, resultLocal); // Load result for return
        }

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

        FieldBuilder stateField = builder.DefineField(
            "State",
            typeof(object),
            FieldAttributes.Public);

        List<Type> constructorParameterTypes = [ delegateType, typeof(object) ];
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
        ctorIl.Emit(OpCodes.Call, Object_Ctor); // Call base constructor
        ctorIl.Emit(OpCodes.Ldarg_0); // Load this
        ctorIl.Emit(OpCodes.Ldarg_1); // Load delegate argument
        ctorIl.Emit(OpCodes.Stfld, delegateField); // Store delegate in field

        ctorIl.Emit(OpCodes.Ldarg_0); // Load this
        ctorIl.Emit(OpCodes.Ldarg_2); // Load object argument
        ctorIl.Emit(OpCodes.Stfld, stateField); // Store object in field

        if (instanceField is not null)
        {
            ctorIl.Emit(OpCodes.Ldarg_0); // Load this
            ctorIl.Emit(OpCodes.Ldarg_3); // Load instance argument
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
