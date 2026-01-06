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

        public static void StaticVoidWithBlittableRefArg(ref int input)
        {
            input += 1;
        }

        public static void StaticVoidWithBlittableOutArg(out int output)
        {
            output = 20;
        }

        public static void StaticVoidWithNonBlittableRefArg(ref NonBlittableStruct input)
        {
            input.IntValue += 10;
            input.StringValue += " Modified";
        }

        public static void StaticVoidWithNonBlittableOutArg(out NonBlittableStruct output)
        {
            output = new NonBlittableStruct { IntValue = 30, StringValue = "Hello" };
        }

        public static void StaticVoidWithRefRefArg(ref string input)
        {
            input = "Modified";
        }

        public static void StaticVoidWithOutRefArg(out string output)
        {
            output = "Replaced";
        }

        public static bool StaticBoolWithRefArg(ref int input)
        {
            input += 1;
            return input == 2;
        }

        public static bool StaticBoolWithOutArg(out int output)
        {
            output = 10;
            return false;
        }

        public static void StaticVoidWithMultipleRefArgs(string value, ref int refInt, out bool outBool)
        {
            refInt += value.Length;
            outBool = true;
        }

        public void InstanceVoidWithRefArg(string value, ref int refInf)
        {
            refInf += value.Length;
        }

        public void InstanceVoidWithOutArg(string value, out int outInt)
        {
            outInt = value.Length;
        }

        public bool InstanceBoolWithRefArg(ref int refInt)
        {
            refInt += 1;
            return refInt == 2;
        }

        public bool InstanceBoolWithOutArg(out int outInt)
        {
            outInt = 10;
            return false;
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

        public int Prop1 { get; set; }
        public int Prop2 { get; set; }
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

        public int Prop3 { get; set; }
    }

    public class GenericClass<T>
    {
        public T Echo(T input)
        {
            return input;
        }
    }

    public struct NonBlittableStruct
    {
        public int IntValue;
        public string StringValue;
    }
}
'@
