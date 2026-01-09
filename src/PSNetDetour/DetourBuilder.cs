using System;
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

    private static MethodInfo? _array_Empty;

    private static MethodInfo Array_Empty
    {
        get => _array_Empty ??= typeof(Array).GetMethod(
            "Empty",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [],
            null)
            ?? throw new RuntimeException("Failed to get Array.Empty<T>() method info.");
    }

    private static MethodInfo? _invokeScriptBlockVoidMethod;

    private static MethodInfo InvokeScriptBlockVoidMethod
    {
        get => _invokeScriptBlockVoidMethod ??= typeof(ScriptBlockInvokeContext).GetMethod(
            nameof(ScriptBlockInvokeContext.InvokeScriptBlockVoid),
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new RuntimeException("Failed to get InvokeScriptBlockVoid method info.");
    }

    private static MethodInfo? _invokeScriptBlockMethod;

    private static MethodInfo InvokeScriptBlockMethod
    {
        get => _invokeScriptBlockMethod ??= typeof(ScriptBlockInvokeContext).GetMethod(
            nameof(ScriptBlockInvokeContext.InvokeScriptBlock),
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new RuntimeException("Failed to get InvokeScriptBlock method info.");
    }

    private static FieldInfo? _invokeContext_InInvokeField;

    private static FieldInfo InvokeContext_InInvokeField
    {
        get => _invokeContext_InInvokeField ??= typeof(ScriptBlockInvokeContext).GetField(
            "InInvoke",
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new RuntimeException("Failed to get ScriptBlockInvokeContext.InInvoke field info.");
    }

    private static FieldInfo? _scriptBlockInvokeContext_StateField;

    private static FieldInfo ScriptBlockInvokeContext_StateField
    {
        get => _scriptBlockInvokeContext_StateField ??= typeof(ScriptBlockInvokeContext).GetField(
            "State",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new RuntimeException("Failed to get ScriptBlockInvokeContext.State field info.");
    }

    private static ConstructorInfo? _object_Ctor;

    private static ConstructorInfo Object_Ctor
    {
        get => _object_Ctor ??= typeof(object).GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [],
            null)
            ?? throw new RuntimeException("Failed to get object constructor method info.");
    }

    private static ConstructorInfo? _psReference_Ctor;

    private static ConstructorInfo PSReference_Ctor
    {
        get => _psReference_Ctor ??= typeof(PSReference).GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [ typeof(object) ],
            null)
            ?? throw new RuntimeException("Failed to get PSReference constructor method info.");
    }

    private static MethodInfo? _psReference_GetValue;

    private static MethodInfo PSReference_GetValue
    {
        get => _psReference_GetValue ??= typeof(PSReference).GetMethod(
            "get_Value",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [],
            null)
            ?? throw new RuntimeException("Failed to get PSReference.get_Value method info.");
    }

    private static MethodInfo? _languagePrimitives_ConvertTo;

    private static MethodInfo LanguagePrimitives_ConvertTo
    {
        get => _languagePrimitives_ConvertTo ??= typeof(LanguagePrimitives).GetMethod(
            "ConvertTo",
            BindingFlags.Public | BindingFlags.Static,
            null,
            [ typeof(object)  ],
            null)
            ?? throw new RuntimeException("Failed to get LanguagePrimitives.ConvertTo method info.");
    }

    /// <summary>
    /// Creates the required types and methods that can be used to detour and
    /// call our InvokeScriptBlock methods for the provided method.
    /// </summary>
    /// <param name="methodToDetour">The method to create a detour for.</param>
    /// <returns>The detour/hook target method.</returns>
    public static MethodInfo CreateDetour(
        MethodBase methodToDetour)
    {
        string typeName = $"{AssemblyName}.{methodToDetour.Name}-{Guid.NewGuid()}";

        bool isStatic = false;
        Type returnType = typeof(void);
        if (methodToDetour is MethodInfo methodInfo)
        {
            isStatic = methodInfo.IsStatic;
            returnType = methodInfo.ReturnType;
        }

        Type? instanceType = isStatic ? null : methodToDetour.DeclaringType;
        // ValueTypes are passed by ref in the detour entrypoint.
        Type? instanceDelegateType = instanceType?.IsValueType == true
            ? instanceType.MakeByRefType()
            : instanceType;

        ParameterInfo[] methodParams = methodToDetour.GetParameters();
        Type[] parameterTypes = Array.ConvertAll(methodParams, p => p.ParameterType);

        // If the method is not static we prepend the instance type
        // before the arguments.
        Type[] methodParameterTypes = instanceDelegateType is null
            ? parameterTypes
            : [ instanceDelegateType, .. parameterTypes ];

        // For methods that interact with PowerShell like InvokeScriptBlock and
        // the $Detour.Invoke(...) call we need to ensure that a pointer return
        // type is IntPtr.
        Type pwshSafeReturnType = returnType.IsPointer ? typeof(IntPtr) : returnType;

        MethodInfo invokeScriptBlock = returnType == typeof(void)
            ? InvokeScriptBlockVoidMethod
            : InvokeScriptBlockMethod.MakeGenericMethod(pwshSafeReturnType);

        // This is the delegate type and the method to invoke that delegate
        // that represents the original method signature. For instance methods
        // the first parameter is the instance type.
        (Type delegateType, MethodInfo delegateMethod) = CreateDelegateType(
            $"{typeName}Delegate",
            returnType,
            methodParameterTypes);

        // This is the type that will store the instance value, the delegate
        // to the original method, and the State object. It is created as part
        // of the hook entrypoint and passed to InvokeScriptBlock.
        (Type metaType, ConstructorInfo metaCtor, FieldInfo metaInstanceField) = CreateMetaType(
            $"{typeName}DetourMeta",
            instanceType,
            pwshSafeReturnType,
            methodParams,
            delegateType,
            delegateMethod);

        // This creates the method that is used as the immediate Hook for the
        // detour. It contains all the necessary logic needed to setup the
        // context for calling InvokeScriptBlock and creating the meta
        // instance at runtime.
        MethodInfo detourMethod = CreateDetourMethod(
            typeName,
            instanceType,
            returnType,
            [delegateType, ..methodParameterTypes],
            metaType,
            metaCtor,
            metaInstanceField,
            delegateMethod,
            invokeScriptBlock);

        return detourMethod;
    }

    private static (Type, MethodInfo) CreateDelegateType(
        string typeName,
        Type returnType,
        Type[] parameterTypes)
    {
        const string methodName = "Invoke";

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

    private static (Type, ConstructorInfo, FieldInfo) CreateMetaType(
        string typeName,
        Type? instanceType,
        Type returnType,
        ParameterInfo[] parameters,
        Type delegateType,
        MethodInfo delegateMethod)
    {
        const string instanceFieldName = "Instance";

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

        Type instanceSafeType = instanceType ?? typeof(object);
        FieldBuilder instanceField = builder.DefineField(
            instanceFieldName,
            instanceSafeType,
            FieldAttributes.Public);

        Type[] ctorTypes = [delegateType, typeof(object), instanceSafeType];
        ConstructorBuilder constructorBuilder = builder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            ctorTypes);

        ILGenerator il = constructorBuilder.GetILGenerator();

        // Call base() ctor.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, Object_Ctor);

        // Set this._delegate to the first parameter.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, delegateField);

        // Set this.State to the second parameter.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, stateField);

        // Set this.Instance to the third parameter.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, instanceField);

        il.Emit(OpCodes.Ret);

        // The Invoke method is called from PowerShell so pointer types need to
        // be exposed as IntPtr. The method will do the conversion back to a
        // pointer when calling the delegate.
        Type[] parameterTypes = Array.ConvertAll(parameters, p => p.ParameterType.IsPointer ? typeof(IntPtr) : p.ParameterType);
        MethodBuilder invokeMethod = builder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            returnType,
            parameterTypes);

        // We set the names so pwsh's overload string matches the original
        // method.
        foreach (ParameterInfo param in parameters)
        {
            invokeMethod.DefineParameter(
                param.Position + 1,
                param.Attributes,
                param.Name);
        }

        il = invokeMethod.GetILGenerator();
        LocalBuilder? returnValue = returnType == typeof(void)
            ? null
            : il.DeclareLocal(returnType);

        // If the instance is a ValueType it must be pased by ref to the
        // delegate. We use a local value so we can pass by ref and set it
        // back to the Instance field after the call.
        LocalBuilder? instanceValue = null;
        if (instanceType?.IsValueType == true)
        {
            instanceValue = il.DeclareLocal(instanceType);

            // T instance = this.Instance;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, instanceField);
            il.Emit(OpCodes.Stloc, instanceValue);
        }

        // this._delegate
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, delegateField);

        // Loads the instance value either through the local if ValueType or
        // directly from the Instance field for ref types.
        if (instanceValue is not null)
        {
            il.Emit(OpCodes.Ldloca_S, instanceValue);
        }
        else if (instanceType is not null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, instanceField);
        }

        // The remaining arguments to the delegate are passed through. IntPtr
        // from the pwsh arg is converted back to the pointer type used by
        // the delegate automatically.
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            il.Emit(OpCodes.Ldarg, i + 1);
        }

        il.Emit(OpCodes.Callvirt, delegateMethod);
        if (returnValue is not null)
        {
            il.Emit(OpCodes.Stloc, returnValue);
        }

        if (instanceValue is not null)
        {
            // If the instance is a ValueType we need to set it back to the
            // Instance field in case it was modified.
            // this.Instance = instance;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, instanceValue);
            il.Emit(OpCodes.Stfld, instanceField);
        }

        if (returnValue is not null)
        {
            il.Emit(OpCodes.Ldloc, returnValue);
        }
        il.Emit(OpCodes.Ret);

        Type metaType = builder.CreateType()
            ?? throw new RuntimeException("Failed to create detour meta type.");
        ConstructorInfo metaCtor = metaType.GetConstructor(ctorTypes)
            ?? throw new RuntimeException("Failed to get detour meta constructor.");
        FieldInfo metaInstance = metaType.GetField(instanceFieldName)
            ?? throw new RuntimeException("Failed to get detour meta Instance field.");

        return (metaType, metaCtor, metaInstance);
    }

    private static MethodInfo CreateDetourMethod(
        string typeName,
        Type? instanceType,
        Type returnType,
        Type[] parameterTypes,
        Type metaType,
        ConstructorInfo metaCtor,
        FieldInfo metaInstanceField,
        MethodInfo originalMethod,
        MethodInfo invokeScriptBlock)
    {
        const string methodName = "Hook";

        TypeBuilder typeBuilder = Builder.DefineType(
            typeName,
            TypeAttributes.Class | TypeAttributes.Public,
            typeof(object));

        MethodBuilder hookedMethod = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            returnType,
            parameterTypes);

        /*
        This is a complex set of IL that is designed to handle calling our
        InvokeScriptBlock method in a PowerShell friendly name. It is very
        dynamic as it is based on the method signature being detoured.

        While the type isn't defined under ScriptBlockInvokeContext, MonoMod
        Will set this to the instance of ScriptBlockInvokeContext provided
        when creating the Hook delegate. For static methods object instance
        is omitted as an arg and null is passed to InvokeScriptBlock. The args
        are treated as separate parameters and represented by ... to indicate
        that they are variable depending on the method signature.

        public ... Hook(Delegate originalMethod, [object instance], ... args)
        {
            if (InInvoke)
            {
                return originalMethod(... args);
            }

            InInvoke = true;
            try
            {
                DetourMeta detourObj = new DetourMeta(originalMethod, _state, instance ?? null);

                // Store each ref/out argument as a local PSReference variable
                object[] localArgs = new object[args.Length];
                foreach (... in args)
                {
                    localArgs[i] = ... is ByRef
                        ? new PSReference(...)
                        : ...;
                }

                ... res = InvokeScriptBlock(detourObj localArgs)

                foreach (l in localArgs)
                {
                    if (l is PSReference)
                    {
                        // Unbox/ref back to original ByRef argument
                        args[i] = LanguagePrimitives.ConvertTo<T>(l.Value);
                    }
                }

                if (instance.IsByRef)
                {
                    instance = detourObj.Instance;
                }

                return res;
            }
            finally
            {
                InInvoke = false;
            }
        }
        */

        // While this inherits from object, the hook is created with
        // ScriptBlockInvokeContext as the instance value so this/Ldarg_0
        // refers to the ScriptBlockInvokeContext instance and its members.
        ILGenerator il = hookedMethod.GetILGenerator();
        Label inInvokeFalseLabel = il.DefineLabel();
        Label returnLabel = il.DefineLabel();

        // The offset for the hook arguments depending on whether this is an
        // instance or static method.
        int hookParamOffset = instanceType is null ? 1 : 2;
        int hookLdargParamOffset = hookParamOffset + 1;
        int hookParamCount = parameterTypes.Length - hookParamOffset;

        LocalBuilder inInvokeLocal = il.DeclareLocal(typeof(bool));  // FIXME: See if this is needed
        LocalBuilder metaLocal = il.DeclareLocal(metaType);
        LocalBuilder? resultLocal = null;
        if (returnType != typeof(void))
        {
            resultLocal = il.DeclareLocal(returnType);
        }

        // We first check if this.InInvoke == true. If it is we call the
        // original method directly and return early. This prevents infinite
        // recursion anything in our ScriptBlock runner calls a method that
        // is also detoured.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, InvokeContext_InInvokeField);
        il.Emit(OpCodes.Stloc, inInvokeLocal);
        il.Emit(OpCodes.Ldloc, inInvokeLocal);
        il.Emit(OpCodes.Brfalse, inInvokeFalseLabel); // Skip to the normal hook path if not InInvoke.

        // Otherwise call the original method directly through the delegate
        // provided as the first argument.
        il.Emit(OpCodes.Ldarg_1);
        for (int i = 1; i < parameterTypes.Length; i++)
        {
            il.Emit(OpCodes.Ldarg, i + 1);
        }
        il.Emit(OpCodes.Callvirt, originalMethod);
        if (resultLocal is not null)
        {
            il.Emit(OpCodes.Stloc, resultLocal);
        }
        il.Emit(OpCodes.Br, returnLabel);

        // This is the continuation of this.InInvoke == false path.
        il.MarkLabel(inInvokeFalseLabel);

        // We set this.InInvoke = true for the duration of the hook.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, InvokeContext_InInvokeField);

        // We wrap the remaining code in try/finally to ensure InInvoke is set
        // back to false once complete. This is the beginning of the try block.
        il.BeginExceptionBlock();

        // Create the detour meta object to pass to our InvokeScriptBlock method.
        // This calls the contructor with (originalDelegate, State, instance).
        il.Emit(OpCodes.Ldarg_1);

        // Loads the this.State field
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ScriptBlockInvokeContext_StateField);

        // Loads the instance value, or null for static methods.
        if (instanceType is null)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_2);
            if (instanceType.IsValueType)
            {
                // ValueTypes are passed by ref so we need to load the value to
                // provide to the constructor.
                il.Emit(OpCodes.Ldobj, instanceType);
            }
        }

        il.Emit(OpCodes.Newobj, metaCtor);
        il.Emit(OpCodes.Stloc, metaLocal);

        // Once we've created the meta local value and stored it, we need to
        // invoke this.InvokeScriptBlock(detourObject, [.. args]); The args are
        // provided as an object[] so all ValueTypes need to be boxed and ByRef
        // parameters need to be a PSReference wrapping the value.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, metaLocal);

        // Scan all args to see if we have any ByRef parameters. These need to
        // stored as locals as PSReference instances so we can pass them to the
        // ScriptBlock and update the original ByRef parameters after the call.
        // We skip the instance parameter for instance methods as that has
        // already been handled by the meta object.

        (Type, LocalBuilder)?[] boxedRefLocal = new (Type, LocalBuilder)?[hookParamCount];
        for (int i = 0; i < hookParamCount; i++)
        {
            Type refType = parameterTypes[i + hookParamOffset];
            if (!refType.IsByRef)
            {
                continue;
            }

            Type paramType = refType.GetElementType()
                ?? throw new RuntimeException("Failed to get element type of ByRef parameter.");
            LocalBuilder localVal = il.DeclareLocal(typeof(PSReference));
            boxedRefLocal[i] = (paramType, localVal);

            il.Emit(OpCodes.Ldarg, i + hookLdargParamOffset);

            // Loads (and boxes value types) to create PSReference
            if (paramType.IsValueType)
            {
                il.Emit(OpCodes.Ldobj, paramType);
                il.Emit(OpCodes.Box, paramType);
            }
            else
            {
                il.Emit(OpCodes.Ldind_Ref);
            }

            il.Emit(OpCodes.Newobj, PSReference_Ctor);
            il.Emit(OpCodes.Stloc, localVal);
        }

        // We build the object[] array for the arguments to InvokeScriptBlock.
        if (hookParamCount > 0)
        {
            // Builds the object[] array for the arguments.
            il.Emit(OpCodes.Ldc_I4, hookParamCount);
            il.Emit(OpCodes.Newarr, typeof(object));

            // Stores the argument value into the array.
            for (int i = 0; i < hookParamCount; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);

                (Type, LocalBuilder)? refLocal = boxedRefLocal[i];
                Type paramType = parameterTypes[i + hookParamOffset];
                int ldargOffset = i + hookLdargParamOffset;

                if (refLocal is not null)
                {
                    // For by-ref types, we load the PSReference local.
                    il.Emit(OpCodes.Ldloc, refLocal.Value.Item2);
                }
                else if (paramType.IsPointer)
                {
                    // For pointer types we box as IntPtr before passing to
                    // pwsh as it cannot handle pointer types.
                    il.Emit(OpCodes.Ldarg, ldargOffset);
                    il.Emit(OpCodes.Box, typeof(IntPtr));
                }
                else if (paramType.IsValueType)
                {
                    // For value types we need to box as object.
                    il.Emit(OpCodes.Ldarg, ldargOffset);
                    il.Emit(OpCodes.Box, paramType);
                }
                else
                {
                    // Reference type, load directly.
                    il.Emit(OpCodes.Ldarg_S, ldargOffset);
                }

                il.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            // No parameters, so we load an empty array.
            il.Emit(OpCodes.Call, Array_Empty.MakeGenericMethod(typeof(object)));
        }

        // Calls this.InvokeScriptBlock(detourObj, args); and stores the return
        // value if required.
        il.Emit(OpCodes.Call, invokeScriptBlock);
        if (resultLocal is not null)
        {
            il.Emit(OpCodes.Stloc, resultLocal); // Store result
        }

        // After running our hook we need to set any ByRef parameters back to
        // the original arguments.
        for (int i = 0; i < boxedRefLocal.Length; i++)
        {
            (Type, LocalBuilder)? refLocal = boxedRefLocal[i];
            if (refLocal is null)
            {
                continue;
            }

            Type argType = refLocal.Value.Item1;
            LocalBuilder psRefLocal = refLocal.Value.Item2;

            // Unfortunately PSReference stores the value as object so there's
            // a chance the type no longer matches. We use
            // LanguagePrimitives.ConvertTo<T> to cast it back to the expected
            // type.
            MethodInfo convertMethod = LanguagePrimitives_ConvertTo.MakeGenericMethod(argType);

            // argX = LanguagePrimitivates.ConvertTo<T>(local.Value);
            il.Emit(OpCodes.Ldarg, i + hookLdargParamOffset);
            il.Emit(OpCodes.Ldloc, psRefLocal);
            il.Emit(OpCodes.Callvirt, PSReference_GetValue);
            il.Emit(OpCodes.Call, convertMethod);

            if (argType.IsValueType)
            {
                il.Emit(OpCodes.Stobj, argType);
            }
            else
            {
                il.Emit(OpCodes.Stind_Ref);
            }
        }

        // If the instance is ByRef we need to set it back to the original
        // argument in case it was modified.
        if (instanceType?.IsValueType == true)
        {
            // arg1 = meta.Instance;
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloc, metaLocal);
            il.Emit(OpCodes.Ldfld, metaInstanceField);
            il.Emit(OpCodes.Stobj, instanceType);
        }

        // End of the try block, we leave to the return label.
        il.Emit(OpCodes.Leave, returnLabel);

        // Start of the finally block to set this.InInvoke = false.
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, InvokeContext_InInvokeField);
        il.Emit(OpCodes.Endfinally);
        il.EndExceptionBlock();

        // Set the marker for anything wanting to return.
        il.MarkLabel(returnLabel);
        if (resultLocal is not null)
        {
            il.Emit(OpCodes.Ldloc, resultLocal);
        }
        il.Emit(OpCodes.Ret);

        Type createdType = typeBuilder.CreateType()
            ?? throw new RuntimeException("Failed to create detour type.");

        MethodInfo detourMethod = createdType.GetMethod(methodName)
            ?? throw new RuntimeException("Failed to get detour Hook method.");

        return detourMethod;
    }
}
