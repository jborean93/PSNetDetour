using namespace System.IO
using namespace System.Management.Automation

$moduleName = (Get-Item ([Path]::Combine($PSScriptRoot, '..', 'module', '*.psd1'))).BaseName
$manifestPath = [Path]::Combine($PSScriptRoot, '..', 'output', $moduleName)

if (-not (Get-Module -Name $moduleName -ErrorAction SilentlyContinue)) {
    Import-Module $manifestPath
}

if (-not (Get-Variable IsWindows -ErrorAction SilentlyContinue)) {
    # Running WinPS so guaranteed to be Windows.
    Set-Variable -Name IsWindows -Value $true -Scope Global
}

Function Global:Complete {
    [OutputType([System.Management.Automation.CompletionResult])]
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, Position = 0)]
        [string]
        $Expression
    )

    [CommandCompletion]::CompleteInput(
        $Expression,
        $Expression.Length,
        $null).CompletionMatches
}

$addTypeParams = @{}
$addTypeCommand = Get-Command -Name Add-Type
if ('CompilerParameters' -in $addTypeCommand.Parameters.Keys) {
    $referencedAssemblies = @(
        [Object].Assembly.Location
        [System.Dynamic.DynamicObject].Assembly.Location
        [PSObject].Assembly.Location
    )
    $addTypeParams.CompilerParameters = [CodeDom.Compiler.CompilerParameters]@{
        CompilerOptions = "/unsafe -reference:$($referencedAssemblies -join ",")"
    }
}
else {
    $addTypeParams.CompilerOptions = '/unsafe'
}

Add-Type @addTypeParams -TypeDefinition @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

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

        public static int RecursiveCall(int count)
        {
            while (count != -1)
            {
                if (count > 10)
                {
                    throw new Exception("Failure, expected exit already");
                }

                count = RecursiveCall(count + 1);
            }

            return count;
        }

        public static int RunInAnotherThread()
        {
            return Task.Run(() => TaskAsync()).GetAwaiter().GetResult();
        }

        public static async Task<int> TaskAsync()
        {
            await Task.Delay(1);
            return StaticIntArgs(Thread.CurrentThread.ManagedThreadId);
        }

        public static async Task<int> RunTaskAsync(int input)
        {
            await Task.Delay(1);
            TestClass.StaticVoidCalled = await TaskAsync();
            return input + 1;
        }

        public static object StaticReturnAnything()
        {
            return null;
        }

        public static int CallStaticVoidWithPointerArg(int value)
        {
            unsafe
            {
                StaticVoidWithPointerArg(&value);
            }

            return value;
        }

        public unsafe static void StaticVoidWithPointerArg(int* ptr)
        {
            (*ptr) += 1;
        }

        public static IntPtr CallStaticPointerReturn(IntPtr value, out char newValue)
        {
            unsafe
            {
                char* ptr = (char*)value;
                char* newPtr = StaticPointerReturn(ptr);
                newValue = *newPtr;

                return (IntPtr)newPtr;
            }
        }

        public unsafe static char* StaticPointerReturn(char* input)
        {
            return input + 1;
        }

        public int CallInstanceVoidWithPointerArg(int value)
        {
            unsafe
            {
                InstanceVoidWithPointerArg(&value);
            }

            return value;
        }

        public unsafe void InstanceVoidWithPointerArg(int* ptr)
        {
            SomeProperty = 2;
            (*ptr) += 1;
        }

        public IntPtr CallInstancePointerReturn(IntPtr value, out char newValue)
        {
            unsafe
            {
                char* ptr = (char*)value;
                char* newPtr = InstancePointerReturn(ptr);
                newValue = *newPtr;

                return (IntPtr)newPtr;
            }
        }

        public unsafe char* InstancePointerReturn(char* input)
        {
            SomeProperty = 4;
            return input + 1;
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

    public struct TestStruct
    {
        public int IntValue;

        public TestStruct(int value)
        {
            IntValue = value;
        }

        public static void StaticIncrement(ref TestStruct str)
        {
            str.IntValue++;
        }

        public static TestStruct Create()
        {
            return new TestStruct(1);
        }

        public static TestStruct Create(int value)
        {
            return new TestStruct(value);
        }

        public void Increment()
        {
            IntValue++;
        }

        public void Increment(int addValue)
        {
            IntValue += addValue;
        }

        public int IncrementAndReturn()
        {
            IntValue++;
            return IntValue;
        }

        public int IncrementAndReturn(int addValue)
        {
            IntValue += addValue;
            return IntValue;
        }
    }

    public class CapturingHost : PSHost
    {
        private readonly CapturingHostUI HostUI;

        public CapturingHost()
        {
            HostUI = new CapturingHostUI();
        }

        public override CultureInfo CurrentCulture
        {
            get { return CultureInfo.InvariantCulture; }
        }

        public override CultureInfo CurrentUICulture
        {
            get { return CultureInfo.InvariantCulture; }
        }

        public override Guid InstanceId
        {
            get { return Guid.NewGuid(); }
        }

        public override string Name
        {
            get { return "CapturingHost"; }
        }

        public override PSHostUserInterface UI
        {
            get { return HostUI; }
        }

        public override Version Version
        {
            get { return new Version("1.2.3"); }
        }

        public override void EnterNestedPrompt()
        {}

        public override void ExitNestedPrompt()
        {}

        public override void NotifyBeginApplication()
        {}

        public override void NotifyEndApplication()
        {}

        public override void SetShouldExit(int exitCode)
        {}
    }

    public class CapturingHostUI : PSHostUserInterface
    {
        private readonly List<string> _callHistory = new List<string>();

        public CapturingHostUI()
        {}

        public string[] CallHistory
        {
            get { return _callHistory.ToArray(); }
        }

        public override PSHostRawUserInterface RawUI
        {
            get { return null; }
        }

        public override Dictionary<string, PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions)
        {
            throw new NotImplementedException();
        }

        public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice)
        {
            throw new NotImplementedException();
        }

        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
        {
            throw new NotImplementedException();
        }

        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName)
        {
            throw new NotImplementedException();
        }

        public override string ReadLine()
        {
            throw new NotImplementedException();
        }

        public override SecureString ReadLineAsSecureString()
        {
            throw new NotImplementedException();
        }

        public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
        {
            _callHistory.Add(string.Format("Write: FG:{0} BG:{1} VAL:{2}", foregroundColor, backgroundColor, value));
        }

        public override void Write(string value)
        {
            _callHistory.Add(string.Format("Write: VAL:{0}", value));
        }

        public override void WriteDebugLine(string message)
        {
            _callHistory.Add(string.Format("WriteDebugLine: MSG:{0}", message));
        }

        public override void WriteErrorLine(string value)
        {
            _callHistory.Add(string.Format("WriteErrorLine: VAL:{0}", value));
        }

        public override void WriteLine(string value)
        {
            _callHistory.Add(string.Format("WriteLine: VAL:{0}", value));
        }

        public override void WriteProgress(long sourceId, ProgressRecord record)
        {
            _callHistory.Add(string.Format("WriteProgress: ID:{0} REC:{1}", sourceId, record.ToString()));
        }

        public override void WriteVerboseLine(string message)
        {
            _callHistory.Add(string.Format("WriteVerboseLine: MSG:{0}", message));
        }

        public override void WriteWarningLine(string message)
        {
            _callHistory.Add(string.Format("WriteWarningLine: MSG:{0}", message));
        }

        public override void WriteInformation(InformationRecord record)
        {
            _callHistory.Add(string.Format("WriteInformation: REC:{0}", record.ToString()));
        }
    }
}
'@
