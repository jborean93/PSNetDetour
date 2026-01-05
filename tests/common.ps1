using namespace System.IO

$moduleName = (Get-Item ([Path]::Combine($PSScriptRoot, '..', 'module', '*.psd1'))).BaseName
$manifestPath = [Path]::Combine($PSScriptRoot, '..', 'output', $moduleName)

if (-not (Get-Module -Name $moduleName -ErrorAction SilentlyContinue)) {
    Import-Module $manifestPath
}

if (-not (Get-Variable IsWindows -ErrorAction SilentlyContinue)) {
    # Running WinPS so guaranteed to be Windows.
    Set-Variable -Name IsWindows -Value $true -Scope Global
}

Add-Type -TypeDefinition @'
using System;
using System.Collections;
using System.Collections.Generic;

namespace PSNetDetour.Tests
{
    public class TestClass
    {
        public int SomeProperty { get; set; }

        public TestClass()
        {
            SomeProperty = 1;
        }

        public TestClass(int arg1)
        {
            SomeProperty = arg1;
        }

        public static int StaticVoidCalled = 0;

        public static void StaticVoidNoArgs()
        {
            StaticVoidCalled++;
        }

        public static void StaticVoidWithArgs(string a, int b)
        {
            StaticVoidCalled += int.Parse(a) + b;
        }

        public static int StaticIntNoArgs()
        {
            return 1;
        }

        public static int StaticIntArgs(int a)
        {
            return 1 + a;
        }

        public static Hashtable StaticRefReturn()
        {
            return new Hashtable() { { "key", "value" } };
        }

        public static int New(int arg1)
        {
            return arg1 + 1;
        }

        public void InstanceVoidNoArgs()
        {
            SomeProperty++;
        }

        public void InstanceVoidWithArgs(string a, int b)
        {
            SomeProperty += int.Parse(a) + b;
        }

        public int InstanceIntNoArgs()
        {
            return SomeProperty;
        }

        public int InstanceIntWithArgs(int a)
        {
            return SomeProperty + a;
        }

        public static int InvokeInternalMethod()
        {
            return InternalMethod();
        }

        internal static int InternalMethod()
        {
            return 42;
        }

        public static int CaseCheck()
        {
            return 1;
        }

        public static int casecheck()
        {
            return 2;
        }

        public static T StaticGenericMethod<T>(T input)
        {
            return input;
        }

        public static int StaticWithFixedGenericArg(List<int> input)
        {
            return input[0];
        }
    }

    public class BaseClass
    {
        public BaseClass(int prop1) : this(prop1, 10)
        { }

        public BaseClass(int prop1, int prop2)
        {
            Prop1 = prop1;
            Prop2 = prop2;
        }

        public int Prop1 { get; }
        public int Prop2 { get; }
    }

    public class SubClass : BaseClass
    {
        public SubClass(int prop1) : base(prop1 + 10)
        {
            Prop3 = 50;
        }

        public SubClass(int prop1, int prop2) : base(prop1, prop2)
        {
            Prop3 = 100;
        }

        public int Prop3 { get; }
    }

    public class GenericClass<T>
    {
        public T Echo(T input)
        {
            return input;
        }
    }
}
'@
