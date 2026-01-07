using namespace System.IO

. ([Path]::Combine($PSScriptRoot, 'common.ps1'))

Describe "New-PSNetDetourHook" {
    Context "Hooks" {
        It "Hooks static void method with no args" {
            $script:marker = $false
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs() } -Hook {
                $script:marker = $true

                [PSNetDetour.Tests.TestClass]::StaticVoidCalled | Should -Be 0
                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.State | Should -BeNullOrEmpty
                $Detour.Invoke()
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled | Should -Be 1
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled = 0

                [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs()
                $script:marker | Should -BeTrue
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled | Should -Be 1
            }
            finally {
                $h.Dispose()
            }

            # Verify dispose disables hook
            $script:marker = $false
            [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs()
            $script:marker | Should -BeFalse
        }

        It "Hooks static void method with args" {
            $script:marker = $false
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidWithArgs([string], [int]) } -Hook {
                param ($arg1, $arg2)
                $script:marker = $true

                $arg1 | Should -Be 2
                $arg1.GetType().Name | Should -Be 'String'
                $arg2 | Should -Be 3

                [PSNetDetour.Tests.TestClass]::StaticVoidCalled | Should -Be 0
                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1, $arg2)
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled | Should -Be 5
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled = 0

                [PSNetDetour.Tests.TestClass]::StaticVoidWithArgs('2', 3)
                $script:marker | Should -Be $true
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled | Should -Be 5
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static int method with no args" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke() + 2
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static int method with args" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                param ($arg1)

                $arg1 | Should -Be 5
                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1) + 2
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticIntArgs(5) | Should -Be 8
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks instance void with no args" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].InstanceVoidNoArgs() } -Hook {
                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance | Should -BeOfType ([PSNetDetour.Tests.TestClass])
                $Detour.Instance.SomeProperty | Should -Be 1
                $Detour.Invoke()
                $Detour.Instance.SomeProperty | Should -Be 2
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new()
                $i.SomeProperty | Should -Be 1
                $i.InstanceVoidNoArgs()
                $i.SomeProperty | Should -Be 2
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks instance void with args" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].InstanceVoidWithArgs([string], [int]) } -Hook {
                param ($arg1, $arg2)

                $arg1 | Should -Be 2
                $arg1 | Should -BeOfType 'String'
                $arg2 | Should -Be 3
                $arg2 | Should -BeOfType 'Int32'

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance | Should -BeOfType ([PSNetDetour.Tests.TestClass])
                $Detour.Instance.SomeProperty | Should -Be 1
                $Detour.Invoke($arg1, $arg2)
                $Detour.Instance.SomeProperty | Should -Be 6
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new()
                $i.SomeProperty | Should -Be 1
                $i.InstanceVoidWithArgs('2', 3)
                $i.SomeProperty | Should -Be 6
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks instance int with no args" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].InstanceIntNoArgs() } -Hook {
                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance | Should -BeOfType ([PSNetDetour.Tests.TestClass])
                $Detour.Invoke() + 2
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new()
                $i.SomeProperty = 2
                $i.InstanceIntNoArgs() | Should -Be 4
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks instance int with args" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].InstanceIntWithArgs([int]) } -Hook {
                param ($arg1)

                $arg1 | Should -Be 3
                $arg1 | Should -BeOfType 'Int32'

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance | Should -BeOfType ([PSNetDetour.Tests.TestClass])
                $Detour.Invoke($arg1) + 2
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new()
                $i.SomeProperty = 2
                $i.InstanceIntWithArgs(3) | Should -Be 7
            }
            finally {
                $h.Dispose()
            }
        }

        # FIXME: Figure out why WinPS fails
        It "Hooks constructor with no args" -Skip:(-not $IsCoreCLR) {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::new() } -Hook {
                $ErrorActionPreference = 'Stop'

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance.SomeProperty | Should -Be 0
                $Detour.Invoke()
                $Detour.Instance.SomeProperty | Should -Be 1

                $Detour.Instance.SomeProperty = 2
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new()
                $i.SomeProperty | Should -Be 2
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks constructor with args" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::new([int]) } -Hook {
                param ($arg1)

                $arg1 | Should -Be 2
                $arg1 | Should -BeOfType 'Int32'

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance.SomeProperty | Should -Be 0
                $Detour.Invoke($arg1 + 2)
                $Detour.Instance.SomeProperty | Should -Be 4

                $Detour.Instance.SomeProperty = 2
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new(2)
                $i.SomeProperty | Should -Be 2
            }
            finally {
                $h.Dispose()
            }
        }

        It "Finds private/internal method" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::InternalMethod() } -Hook {
                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke() + 1
            } -FindNonPublic
            try {
                [PSNetDetour.Tests.TestClass]::InvokeInternalMethod() | Should -Be 43
            }
            finally {
                $h.Dispose()
            }
        }

        It "Uses static method New over constructor" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::new([int]) } -Hook {
                param ($arg1)

                $arg1 | Should -Be 2
                $arg1 | Should -BeOfType 'Int32'

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1 + 2)
            } -IgnoreConstructorNew
            try {
                [PSNetDetour.Tests.TestClass]::New.Invoke(@(2)) | Should -Be 5
            }
            finally {
                $h.Dispose()
            }
        }

        It "Matches method by case" {
            $h1 = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::CaseCheck() } -Hook {
                $Detour.Invoke() + 1
            }
            $h2 = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::casecheck() } -Hook {
                $Detour.Invoke() + 10
            }
            try {
                [PSNetDetour.Tests.TestClass]::CaseCheck() | Should -Be 2
                [PSNetDetour.Tests.TestClass].GetMethod('casecheck').Invoke($null, @()) | Should -Be 12
            }
            finally {
                $h1.Dispose()
                $h2.Dispose()
            }
        }

        It "Falls back to case insensitive on no match" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::staticintnoargs() } -Hook {
                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke() + 2
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        # FIXME: Figure out why WinPS fails
        It "Hooks property getter" -Skip:(-not $IsCoreCLR) {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].get_SomeProperty() } -Hook {
                $ErrorActionPreference = 'Stop'

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Invoke() + 10
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new(5)
                $i.SomeProperty | Should -Be 15
            }
            finally {
                $h.Dispose()
            }
        }

        # FIXME: Figure out why WinPS fails
        It "Hooks property setter" -Skip:(-not $IsCoreCLR) {
            $i = [PSNetDetour.Tests.TestClass]::new()

            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].set_SomeProperty([int]) } -Hook {
                param ($arg1)

                $ErrorActionPreference = 'Stop'

                $arg1 | Should -Be 5
                $arg1 | Should -BeOfType 'Int32'

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance.SomeProperty | Should -Be 1
                $Detour.Invoke($arg1 + 10)
                $Detour.Instance.SomeProperty | Should -Be 15
            }
            try {
                $i.SomeProperty = 5
                $i.SomeProperty | Should -Be 15
            }
            finally {
                $h.Dispose()
            }
        }

        It "Uses MethodInfo for constructor parameter" {
            $meth = [PSNetDetour.Tests.TestClass].GetConstructor([type[]]@([int]))
            $h = New-PSNetDetourHook -Method $meth -Hook {
                param ($arg1)

                $arg1 | Should -Be 2
                $arg1 | Should -BeOfType 'Int32'

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance.SomeProperty | Should -Be 0
                $Detour.Invoke($arg1 + 1)
                $Detour.Instance.SomeProperty | Should -Be 3
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new(2)
                $i.SomeProperty | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Uses MethodInfo for method parameter" {
            $meth = [PSNetDetour.Tests.TestClass].GetMethod('StaticIntNoArgs')
            $h = New-PSNetDetourHook -Method $meth -Hook {
                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke() + 2
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Throws exception in hook" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
                throw "Test exception"
            }
            try {
                {
                    [PSNetDetour.Tests.TestClass]::StaticIntNoArgs()
                } | Should -Throw '*Exception occurred while invoking hook for StaticIntNoArgs: Test exception'
            }
            finally {
                $h.Dispose()
            }
        }

        It "Ignores errors in hook" {
            $ErrorActionPreference = 'Continue'

            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
                Write-Error "Test error"
                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke() + 2
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Casts no output to default value for value type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
                # No output
            }
            try {
                # Default int value is 0
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 0
            }
            finally {
                $h.Dispose()
            }
        }

        It "Casts no output to null for reference type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticRefReturn() } -Hook {
                # No output
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticRefReturn() | Should -Be $null
            }
            finally {
                $h.Dispose()
            }
        }

        It "Ignores remaining output values" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
                10
                "abc"
                return @{ key = "value" }
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 10
            }
            finally {
                $h.Dispose()
            }
        }

        It "Casts output to correct type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
                "5"
            }
            try {
                $actual = [PSNetDetour.Tests.TestClass]::StaticIntNoArgs()
                $actual | Should -BeOfType 'Int32'
                $actual | Should -Be 5
            }
            finally {
                $h.Dispose()
            }
        }

        It "Throws error when failing to cast to correct type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
                "abc"
            }
            try {
                {
                    [PSNetDetour.Tests.TestClass]::StaticIntNoArgs()
                } | Should -Throw '*Cannot convert value "abc" to type "System.Int32".*'
            }
            finally {
                $h.Dispose()
            }
        }

        It "Uses using namespace for type resolution" {
            # Uses ScriptBlock::Create to test with the using namespace directive
            & ([ScriptBlock]::Create(@'
using namespace PSNetDetour.Tests

$h = New-PSNetDetourHook -Source { [TestClass]::StaticIntNoArgs() } -Hook {
    $Detour.Instance | Should -BeNullOrEmpty
    $Detour.Invoke() + 2
}
try {
    [TestClass]::StaticIntNoArgs() | Should -Be 3
}
finally {
    $h.Dispose()
}
'@))
        }

        It "Hooks constructor with this call" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.BaseClass]::new([int]) } -Hook {
                param ($arg1)

                $arg1 | Should -Be 1
                $arg1 | Should -BeOfType 'Int32'

                $Detour.Instance.Prop1 | Should -Be 0
                $Detour.Instance.Prop2 | Should -Be 0
                $Detour.Invoke($arg1 + 2)
                $Detour.Instance.Prop1 | Should -Be 3
                $Detour.Instance.Prop2 | Should -Be 10
            }
            try {
                $i = [PSNetDetour.Tests.BaseClass]::new(1)
                $i.Prop1 | Should -Be 3
                $i.Prop2 | Should -Be 10
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks constructor with base call" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.SubClass]::new([int]) } -Hook {
                param ($arg1)

                $arg1 | Should -Be 1
                $arg1 | Should -BeOfType 'Int32'

                $Detour.Instance.Prop1 | Should -Be 0
                $Detour.Instance.Prop2 | Should -Be 0
                $Detour.Instance.Prop3 | Should -Be 0
                $Detour.Invoke($arg1 + 2)
                $Detour.Instance.Prop1 | Should -Be 13
                $Detour.Instance.Prop2 | Should -Be 10
                $Detour.Instance.Prop3 | Should -Be 50
            }
            try {
                $i = [PSNetDetour.Tests.SubClass]::new(1)
                $i.Prop1 | Should -Be 13
                $i.Prop2 | Should -Be 10
                $i.Prop3 | Should -Be 50
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks method with fixed generic type as argument" {
            $h = New-PSNetDetourHook -Source {
                [PSNetDetour.Tests.TestClass]::StaticWithFixedGenericArg([System.Collections.Generic.List[int]])
            } -Hook {
                param ($arg1)

                , $arg1 | Should -BeOfType 'System.Collections.Generic.List`1[System.Int32]'
                $arg1.Count | Should -Be 2
                $arg1[0] | Should -Be 5
                $arg1[1] | Should -Be 10

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1) + 1
            }
            try {
                $list = [System.Collections.Generic.List[int]]::new()
                $list.Add(5)
                $list.Add(10)

                [PSNetDetour.Tests.TestClass]::StaticWithFixedGenericArg($list) | Should -Be 6
            }
            finally {
                $h.Dispose()
            }
        }

        It "Fails with generic method" {
            $meth = [PSNetDetour.Tests.TestClass].GetMethod('StaticGenericMethod')
            {
                New-PSNetDetourHook -Method $meth -Hook {}
            } | Should -Throw '*Detouring generic methods or methods on generic types is not supported.'
        }

        It "Fails with constructed generic method" {
            $meth = [PSNetDetour.Tests.TestClass].GetMethod('StaticGenericMethod').MakeGenericMethod([int])
            {
                New-PSNetDetourHook -Method $meth -Hook {}
            } | Should -Throw '*Detouring generic methods or methods on generic types is not supported.'
        }

        It "Fails with generic class method" {
            $meth = [PSNetDetour.Tests.GenericClass[int]].GetMethod('Echo')
            {
                New-PSNetDetourHook -Method $meth -Hook {}
            } | Should -Throw '*Detouring generic methods or methods on generic types is not supported.'
        }

        It "Hooks static void with blittable ref value type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableRefArg([ref][int]) } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value | Should -Be 1

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1)

                $arg1.Value | Should -Be 2
                $arg1.Value = 3
            }
            try {
                $val = 1
                [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableRefArg([ref]$val)
                $val | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static void with blittable out value type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableOutArg([ref][int]) } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value | Should -Be 1

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1)

                $arg1.Value | Should -Be 20
                $arg1.Value = 3
            }
            try {
                $val = 1
                [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableOutArg([ref]$val)
                $val | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static void with non-blittable ref value type" {
            $h = New-PSNetDetourHook -Source {
                [PSNetDetour.Tests.TestClass]::StaticVoidWithNonBlittableRefArg([ref][PSNetDetour.Tests.NonBlittableStruct])
            } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value.IntValue | Should -Be 10
                $arg1.Value.StringValue | Should -Be 'Test'

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1)

                $arg1.Value.IntValue | Should -Be 20
                $arg1.Value.StringValue | Should -Be 'Test Modified'

                $arg1.Value.IntValue = 30
                $arg1.Value.StringValue = 'NewValue'
            }
            try {
                $val = [PSNetDetour.Tests.NonBlittableStruct]::new()
                $val.IntValue = 10
                $val.StringValue = 'Test'
                [PSNetDetour.Tests.TestClass]::StaticVoidWithNonBlittableRefArg([ref]$val)
                $val.IntValue | Should -Be 30
                $val.StringValue | Should -Be 'NewValue'
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static void with non-blittable out value type" {
            $h = New-PSNetDetourHook -Source {
                [PSNetDetour.Tests.TestClass]::StaticVoidWithNonBlittableOutArg([ref][PSNetDetour.Tests.NonBlittableStruct])
            } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value.IntValue | Should -Be 10
                $arg1.Value.StringValue | Should -Be 'Test'

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1)

                $arg1.Value.IntValue | Should -Be 30
                $arg1.Value.StringValue | Should -Be 'Hello'

                $arg1.Value.IntValue = 40
                $arg1.Value.StringValue = 'NewValue'
            }
            try {
                $val = [PSNetDetour.Tests.NonBlittableStruct]::new()
                $val.IntValue = 10
                $val.StringValue = 'Test'
                [PSNetDetour.Tests.TestClass]::StaticVoidWithNonBlittableOutArg([ref]$val)
                $val.IntValue | Should -Be 40
                $val.StringValue | Should -Be 'NewValue'
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static void with ref reference type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidWithRefRefArg([ref][string]) } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value | Should -Be 'Initial'

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1)

                $arg1.Value | Should -Be 'Modified'
                $arg1.Value = 'NewValue'
            }
            try {
                $val = 'Initial'
                [PSNetDetour.Tests.TestClass]::StaticVoidWithRefRefArg([ref]$val)
                $val | Should -Be 'NewValue'
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static void with out reference type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidWithOutRefArg([ref][string]) } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value | Should -Be 'Initial'

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1)

                $arg1.Value | Should -Be 'Replaced'
                $arg1.Value = 'NewValue'
            }
            try {
                $val = 'Initial'
                [PSNetDetour.Tests.TestClass]::StaticVoidWithOutRefArg([ref]$val)
                $val | Should -Be 'NewValue'
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static bool with ref arg" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticBoolWithRefArg([ref][int]) } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value | Should -Be 1

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1)

                $arg1.Value | Should -Be 2
                $arg1.Value = 3
            }
            try {
                $val = 1
                [PSNetDetour.Tests.TestClass]::StaticBoolWithRefArg([ref]$val) | Should -BeTrue
                $val | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static bool with out arg" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticBoolWithOutArg([ref][int]) } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value | Should -Be 1

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1)

                $arg1.Value | Should -Be 10
                $arg1.Value = 3
            }
            try {
                $val = 1
                [PSNetDetour.Tests.TestClass]::StaticBoolWithOutArg([ref]$val) | Should -BeFalse
                $val | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks instance void with ref arg" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].InstanceVoidWithRefArg([string], [ref][int]) } -Hook {
                param ($arg1, $arg2)

                $arg1 | Should -Be 'Hello'

                $arg2.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg2.Value | Should -Be 1

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance | Should -BeOfType ([PSNetDetour.Tests.TestClass])

                $Detour.Invoke($arg1, $arg2)

                $arg2.Value | Should -Be 6
                $arg2.Value = 3
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new()
                $val = 1
                $i.InstanceVoidWithRefArg('Hello', [ref]$val)
                $val | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks instance void with out arg" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].InstanceVoidWithOutArg([string], [ref][int]) } -Hook {
                param ($arg1, $arg2)

                $arg1 | Should -Be 'Hello'

                $arg2.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg2.Value | Should -Be 1

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance | Should -BeOfType ([PSNetDetour.Tests.TestClass])

                $Detour.Invoke($arg1, $arg2)

                $arg2.Value | Should -Be 5
                $arg2.Value = 3
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new()
                $val = 1
                $i.InstanceVoidWithOutArg('Hello', [ref]$val)
                $val | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks instance bool with ref arg" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].InstanceBoolWithRefArg([ref][int]) } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value | Should -Be 1

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance | Should -BeOfType ([PSNetDetour.Tests.TestClass])

                $Detour.Invoke($arg1)

                $arg1.Value | Should -Be 2
                $arg1.Value = 3
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new()
                $val = 1
                $i.InstanceBoolWithRefArg([ref]$val) | Should -BeTrue
                $val | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks instance bool with out arg" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass].InstanceBoolWithOutArg([ref][int]) } -Hook {
                param ($arg1)

                $arg1.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg1.Value | Should -Be 1

                $Detour.Instance | Should -Not -BeNullOrEmpty
                $Detour.Instance | Should -BeOfType ([PSNetDetour.Tests.TestClass])

                $Detour.Invoke($arg1)

                $arg1.Value | Should -Be 10
                $arg1.Value = 3
            }
            try {
                $i = [PSNetDetour.Tests.TestClass]::new()
                $val = 1
                $i.InstanceBoolWithOutArg([ref]$val) | Should -BeFalse
                $val | Should -Be 3
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks static method with many ref args" {
            $h = New-PSNetDetourHook -Source {
                [PSNetDetour.Tests.TestClass]::StaticVoidWithMultipleRefArgs([string], [ref][int], [ref][bool])
            } -Hook {
                param ($arg1, $arg2, $arg3)

                $arg1 | Should -Be 'Test'

                $arg2.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg2.Value | Should -Be 10

                $arg3.GetType().FullName | Should -Be 'System.Management.Automation.PSReference'
                $arg3.Value | Should -BeFalse

                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1, $arg2, $arg3)

                $arg1 | Should -Be 'Test'
                $arg2.Value | Should -Be 14
                $arg3.Value | Should -BeTrue

                $arg2.Value = 20
            }
            try {
                $intVal = 10
                $boolVal = $false
                [PSNetDetour.Tests.TestClass]::StaticVoidWithMultipleRefArgs('Test', [ref]$intVal, [ref]$boolVal)
                $intVal | Should -Be 20
                $boolVal | Should -BeTrue
            }
            finally {
                $h.Dispose()
            }
        }

        It "Casts ref value set in hook to correct type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableRefArg([ref][int]) } -Hook {
                param ($arg1)

                $arg1.Value = '5'
            }
            try {
                $val = 1
                [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableRefArg([ref]$val)
                $val | Should -Be 5
            }
            finally {
                $h.Dispose()
            }
        }

        It "Errors when failing to cast ref value set in hook to correct type" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableRefArg([ref][int]) } -Hook {
                param ($arg1)

                $arg1.Value = 'abc'
            }
            try {
                {
                    $val = 1
                    [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableRefArg([ref]$val)
                } | Should -Throw '*Cannot convert value "abc" to type "System.Int32".*'
            }
            finally {
                $h.Dispose()
            }
        }

        It "Handles exception for out parameter" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableOutArg([ref][int]) } -Hook {
                param ($arg1)

                throw "Test exception in hook"
            }
            try {
                $val = 1
                {
                    [PSNetDetour.Tests.TestClass]::StaticVoidWithBlittableOutArg([ref]$val)
                } | Should -Throw '*Exception occurred while invoking hook for StaticVoidWithBlittableOutArg: Test exception in hook*'
                $val | Should -Be 1
            }
            finally {
                $h.Dispose()
            }
        }

        It "Handles recursive calls" {
            $script:callCount = 0
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::RecursiveCall([int]) } -Hook {
                param ($arg1)

                $script:callCount++

                if ($arg1 -eq 4) {
                    $arg1 = -1
                }

                $Detour.Invoke($arg1)
            }
            try {
                [PSNetDetour.Tests.TestClass]::RecursiveCall(1) | Should -Be -1
                $script:callCount | Should -Be 4
            }
            finally {
                $h.Dispose()
            }
        }

        It "Sends other streams to PSHost of pipeline invoking hooked method" {
            $ps = [PowerShell]::Create([System.Management.Automation.RunspaceMode]::CurrentRunspace)

            $host1 = [PSNetDetour.Tests.CapturingHost]::new()
            $host2 = [PSNetDetour.Tests.CapturingHost]::new()

            $null = $ps.AddScript({
                New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs() } -Hook {
                    Write-Error "error"
                    Write-Verbose "verbose"
                    Write-Debug "debug"
                    Write-Warning "warning"
                    Write-Progress -Activity "Test Activity" -Status "Test Status" -Completed
                    Write-Information "information"
                    Write-Host "host"
                }
            })

            $h = $ps.Invoke($null, [System.Management.Automation.PSInvocationSettings]@{ Host = $host1 })
            try {
                $ps.Commands.Clear()
                $ps.Streams.ClearStreams()

                $null = $ps.AddScript({
                    $ErrorActionPreference = 'Continue'
                    $VerbosePreference = 'Continue'
                    $DebugPreference = 'Continue'
                    $WarningPreference = 'Continue'
                    $ProgressPreference = 'Continue'
                    $InformationPreference = 'Continue'

                    [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs()
                })

                $ps.Invoke($null, [System.Management.Automation.PSInvocationSettings]@{ Host = $host2 })
            }
            finally {
                $h.Dispose()
            }

            # As the hook runs in a .NET method it doesn't emit the records to
            # the PowerShell streams.
            $ps.Streams.Error.Count | Should -Be 0
            $ps.Streams.Verbose.Count | Should -Be 0
            $ps.Streams.Debug.Count | Should -Be 0
            $ps.Streams.Warning.Count | Should -Be 0
            $ps.Streams.Progress.Count | Should -Be 0
            $ps.Streams.Information.Count | Should -Be 0

            # Instead they are written to the PSHost that was used when
            # invoking the method (not when the hook was created).
            # Exception to this is the ErrorRecord which is only written by
            # the statement and it will never reach that in this case.
            $host1.UI.CallHistory.Length | Should -Be 0
            $host2.UI.CallHistory.Length | Should -Be 8
            $host2.UI.CallHistory[0] | Should -Be 'WriteVerboseLine: MSG:verbose'
            $host2.UI.CallHistory[1] | Should -Be 'WriteDebugLine: MSG:debug'
            $host2.UI.CallHistory[2] | Should -Be 'WriteWarningLine: MSG:warning'
            $host2.UI.CallHistory[3] | Should -Be 'WriteProgress: ID:0 REC:parent = -1 id = 0 act = Test Activity stat = Test Status cur =  pct = -1 sec = -1 type = Completed'
            $host2.UI.CallHistory[4] | Should -Be 'WriteInformation: REC:information'
            $host2.UI.CallHistory[5] | Should -Be 'WriteLine: VAL:information'
            $host2.UI.CallHistory[6] | Should -Be 'WriteInformation: REC:host'
            $host2.UI.CallHistory[7] | Should -Be 'WriteLine: VAL:host'

            $ps.Dispose()
        }

        It "Uses Function ScriptBlock a hook definition" {
            $ErrorActionPreference = 'Stop'

            Function source {
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs()
            }

            Function hook {
                100
            }

            $h = New-PSNetDetourHook -Source ${function:source} -Hook ${function:hook}
            try {
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 100
            }
            finally {
                $h.Dispose()
            }
        }

        It "Strips the runspace affinity from the <SbkType>" -TestCases @(
            @{ SbkType = 'Function' }
            @{ SbkType = 'ScriptBlock' }
        ) {
            param ($SbkType)

            $h = $null
            $rs = [RunspaceFactory]::CreateRunspace()
            try {
                $rs.Open()

                $ps = [PowerShell]::Create()
                $ps.Runspace = $rs
                $toRun = $ps.AddScript({
                    $TestVar = 10

                    if ($args[0] -eq 'Function') {
                        function func { $TestVar }
                        ${function:func}
                    }
                    else {
                        { $TestVar }
                    }

                }).AddArgument($SbkType).Invoke()[0]
                $ps.Dispose()

                $h = New-PSNetDetourHook { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook $toRun

                $TestVar = 20
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 20
            }
            finally {
                $rs.Dispose()
                if ($h) { $h.Dispose() }
            }
        }

        It "Fails to hook method run in another thread on default runspace" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                throw "Should not be called"
            }
            try {
                {
                    [PSNetDetour.Tests.TestClass]::RunInAnotherThread()
                } | Should -Throw "*Hook for 'TestClass static Int32 StaticIntArgs(Int32 a)' is being invoked in a thread with no active Runspace. Create hook with -UseRunspace New or -UseRunspace Pool to invoke the hook in a separate Runspace or RunspacePool.*"
            }
            finally {
                $h.Dispose()
            }
        }

        It "Hooks method run in another thread with new runspace managed by hook" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                [Runspace]::DefaultRunspace.Id
            } -UseRunspace New
            try {
                $rid = [PSNetDetour.Tests.TestClass]::RunInAnotherThread()
                $rid | Should -Not -Be ([Runspace]::DefaultRunspace.Id)
                Get-Runspace -Id $rid | Should -Not -BeNullOrEmpty
            }
            finally {
                $h.Dispose()
            }

            Get-Runspace -Id $rid | Should -BeNullOrEmpty
        }

        It "Hooks method run in another thread with new runspace managed by caller" {
            $rs = [RunspaceFactory]::CreateRunspace()
            try {
                $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                    [Runspace]::DefaultRunspace.Id
                } -UseRunspace $rs
                try {
                    $rs.Open()
                    $rid = [PSNetDetour.Tests.TestClass]::RunInAnotherThread()
                    $rid | Should -Be $rs.Id
                    Get-Runspace -Id $rid | Should -Not -BeNullOrEmpty
                }
                finally {
                    $h.Dispose()
                }

                Get-Runspace -Id $rid | Should -Not -BeNullOrEmpty

            }
            finally {
                $rs.Dispose()
            }

            Get-Runspace -Id $rid | Should -BeNullOrEmpty
        }

        It "Hooks method run in another thread with new runspace pool managed by hook" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                [Runspace]::DefaultRunspace.Id
            } -UseRunspace Pool
            try {
                $rid = [PSNetDetour.Tests.TestClass]::RunInAnotherThread()
                $rid | Should -Not -Be ([Runspace]::DefaultRunspace.Id)
                Get-Runspace -Id $rid | Should -Not -BeNullOrEmpty
            }
            finally {
                $h.Dispose()
            }

            Get-Runspace -Id $rid | Should -BeNullOrEmpty
        }

        It "Hooks method run in another thread with new runspace pool managed by caller" {
            $rs = [RunspaceFactory]::CreateRunspacePool()
            try {
                $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                    [Runspace]::DefaultRunspace.Id
                } -UseRunspace $rs
                try {
                    $rs.Open()
                    $rid = [PSNetDetour.Tests.TestClass]::RunInAnotherThread()
                    $rid | Should -Not -Be ([Runspace]::DefaultRunspace.Id)
                    Get-Runspace -Id $rid | Should -Not -BeNullOrEmpty
                }
                finally {
                    $h.Dispose()
                }

                Get-Runspace -Id $rid | Should -Not -BeNullOrEmpty

            }
            finally {
                $rs.Dispose()
            }

            Get-Runspace -Id $rid | Should -BeNullOrEmpty
        }

        It "Fails with invalid UseRunspace value" {
            {
                New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                    [Runspace]::DefaultRunspace.Id
                } -UseRunspace Invalid
            } | Should -Throw 'Invalid UseRunspace value ''Invalid''. Valid values are ''Current'', ''New'', ''Pool'', or a Runspace/RunspacePool instance.'
        }

        It "Invokes async Task that has not been awaited yet" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::RunTaskAsync([int]) } -Hook {
                [System.Threading.Tasks.Task]::FromResult(100)
            }
            try {
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled = 0
                $result = [PSNetDetour.Tests.TestClass]::RunTaskAsync(1).GetAwaiter().GetResult()
                $result | Should -Be 100

                # As our hook skipped invoking the original this should never be called
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled | Should -Be 0
            }
            finally {
                $h.Dispose()
            }
        }

        It "Invokes async Task that has been awaited" {
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::TaskAsync() } -Hook {
                # The original method returns the ManagedThreadId + 1 but the
                # ManagedThreadId will be in another thread so we check that it's
                # not ours
                $tid = [System.Threading.Thread]::CurrentThread.ManagedThreadId
                $Detour.Invoke().GetAwaiter().GetResult() | Should -Not -Be ($tid + 1)

                [System.Threading.Tasks.Task]::FromResult(-1)
            } -UseRunspace New
            try {
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled = 0
                $result = [PSNetDetour.Tests.TestClass]::RunTaskAsync(1).GetAwaiter().GetResult()
                $result | Should -Be 2

                # Our hook returned -1 and not the ManagedThreadId that the original did.
                [PSNetDetour.Tests.TestClass]::StaticVoidCalled | Should -Be -1
            }
            finally {
                $h.Dispose()
            }
        }

        It "Uses the -State object to pass data into the hook" {
            $state = @{}
            $h = New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                param ($arg1)

                $state = $Detour.State
                $state | Should -BeOfType 'System.Collections.Hashtable'
                $state.Count | Should -Be 0
                $state['Argument'] = $arg1
                $state['Result'] = $Detour.Invoke($arg1)

                100
            } -State $state -UseRunspace New
            try {
                [PSNetDetour.Tests.TestClass]::StaticIntArgs(5) | Should -Be 100
                $state.Argument | Should -Be 5
                $state.Result | Should -Be 6
            }
            finally {
                $h.Dispose()
            }
        }

        It "Creates hook through alias" {
            $modulePath = Join-Path (Get-Module -Name PSNetDetour).ModuleBase 'PSNetDetour.psm1'

            # We use a string to avoid PSSA from expanding on save in the editor
            $ps = [PowerShell]::Create()
            $null = $ps.AddScript(@'
                Import-Module $args[0]
                # Ensure ALC setup doesn't break aliases on second import
                Import-Module $args[0] -Force

                $h = nethook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook { 200 }
                try {
                    [PSNetDetour.Tests.TestClass]::StaticIntNoArgs()
                }
                finally {
                    $h.Dispose()
                }
'@).AddArgument($modulePath)
            $ps.Invoke()[0] | Should -Be 200
        }

        It "Autocompletes -UseRunspace parameter" {
            $actual = Complete 'New-PSNetDetourHook -UseRunspace '

            $actual.Count | Should -Be 3
            $actual[0].CompletionText | Should -Be 'Current'
            $actual[0].ListItemText | Should -Be 'Current'
            $actual[0].ToolTip | Should -Be 'Use the current Runspace for invoking the hook.'

            $actual[1].CompletionText | Should -Be 'New'
            $actual[1].ListItemText | Should -Be 'New'
            $actual[1].ToolTip | Should -Be 'Create a new Runspace for invoking the hook.'

            $actual[2].CompletionText | Should -Be 'Pool'
            $actual[2].ListItemText | Should -Be 'Pool'
            $actual[2].ToolTip | Should -Be 'Create a new RunspacePool for invoking the hook.'
        }

        It "Matches one autocomplete entry for -UseRunspace parameter" {
            $actual = Complete 'New-PSNetDetourHook -UseRunspace C'

            $actual.Count | Should -Be 1
            $actual[0].CompletionText | Should -Be 'Current'
            $actual[0].ListItemText | Should -Be 'Current'
            $actual[0].ToolTip | Should -Be 'Use the current Runspace for invoking the hook.'
        }

        It "Matches no autocomplete entries for -UseRunspace parameter" {
            $actual = Complete 'New-PSNetDetourHook -UseRunspace X'

            $actual.Count | Should -Be 0
        }
    }

    Context "Invalid ScriptBlock Targets" {
        It "Has params" {
            {
                New-PSNetDetourHook -Source { param() } -Hook {}
            } | Should -Throw '*ScriptBlock must not contain the param block.'
        }

        It "Has dynamic params" {
            {
                New-PSNetDetourHook -Source { dynamicparam {} } -Hook {}
            } | Should -Throw '*ScriptBlock must not contain the dynamicparam block.'
        }

        It "Has begin block" {
            {
                New-PSNetDetourHook -Source { begin {} } -Hook {}
            } | Should -Throw '*ScriptBlock must not contain a begin block.'
        }

        It "Has process block" {
            {
                New-PSNetDetourHook -Source { process {} } -Hook {}
            } | Should -Throw '*ScriptBlock must not contain a process block.'
        }

        It "Has no statements" {
            {
                New-PSNetDetourHook -Source {} -Hook {}
            } | Should -Throw '*ScriptBlock end block must have only one statement.'
        }

        It "Has multiple statements" {
            {
                New-PSNetDetourHook -Source { 1; 2 } -Hook {}
            } | Should -Throw '*ScriptBlock end block must have only one statement.'
        }

        It "Has statement that is not a command expression" {
            {
                New-PSNetDetourHook -Source { $a = 1 } -Hook {}
            } | Should -Throw '*ScriptBlock end block statement must be a method call.'
        }

        It "Has multiple pipeline elements" {
            {
                New-PSNetDetourHook -Source { [IO.Path]::GetTempPath() | Write-Output } -Hook {}
            } | Should -Throw '*ScriptBlock pipeline must have only one element.'
        }

        It "Has pipeline element that is not a command expression" {
            {
                New-PSNetDetourHook -Source { Write-Output 'test' } -Hook {}
            } | Should -Throw '*ScriptBlock pipeline element must be a method call.'
        }

        It "Has redirections in method call" {
            {
                New-PSNetDetourHook -Source { [IO.Path]::GetTempPath() > $null } -Hook {}
            } | Should -Throw '*ScriptBlock command expression must not have any redirections.'
        }

        It "Has command expression that is not a method call" {
            {
                New-PSNetDetourHook -Source { [Type]'test' } -Hook {}
            } | Should -Throw '*ScriptBlock expression must be a method call.'
        }

        It "Has method call on variable and not a type" {
            {
                New-PSNetDetourHook -Source { $a.GetTempPath() } -Hook {}
            } | Should -Throw '*ScriptBlock method call must be invoked on a type.'
        }

        It "Has method call on name that is not a constant plain string" {
            {
                New-PSNetDetourHook -Source { [Type]::$someVar() } -Hook {}
            } | Should -Throw '*ScriptBlock method call method name must be a string constant.'
        }

        It "Has method with args that are not types" {
            {
                New-PSNetDetourHook -Source { [IO.Path]::Combine('a', 'b') } -Hook {}
            } | Should -Throw '*Method call argument entry must be a single type value or `[ref]`[typehere].'
        }

        It "Uses unknown type" {
            {
                New-PSNetDetourHook -Source { [NonExistent.Type]::SomeMethod() } -Hook {}
            } | Should -Throw '*Failed to resolve type NonExistent.Type.'
        }

        It "Uses unknown type in generic type definition" {
            {
                New-PSNetDetourHook -Source { [System.Collections.Generic.Dictionary[int, Fake]]::SomeMethod() } -Hook {}
            } | Should -Throw '*Failed to resolve type Fake.'
        }

        It "Uses generic type definition with no type args" {
            {
                New-PSNetDetourHook -Source { [System.Collections.Generic.Dictionary`2]::SomeMethod() } -Hook {}
            } | Should -Throw '*ScriptBlock method call type must not be a generic type, hooks do not work on generics.'
        }

        It "Uses generic type with type args" {
            {
                New-PSNetDetourHook -Source { [System.Collections.Generic.Dictionary[int, string]]::SomeMethod() } -Hook {}
            } | Should -Throw '*ScriptBlock method call type must not be a generic type, hooks do not work on generics.'
        }

        It "Points to invalid method - static method no args" {
            {
                New-PSNetDetourHook -Source { [IO.Path]::NonExistentMethod() } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: Path static ReturnType NonExistentMethod()'
        }

        It "Points to invalid method - static method with args" {
            {
                New-PSNetDetourHook -Source { [IO.Path]::NonExistentMethod([string], [int]) } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: Path static ReturnType NonExistentMethod(String arg0, Int32 arg1)'
        }

        It "Points to invalid method - instance method no args" {
            {
                New-PSNetDetourHook -Source { [IO.Path].NonExistentMethod() } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: Path ReturnType NonExistentMethod()'
        }

        It "Points to invalid method - instance method with args" {
            {
                New-PSNetDetourHook -Source { [IO.Path].NonExistentMethod([string], [int]) } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: Path ReturnType NonExistentMethod(String arg0, Int32 arg1)'
        }

        It "Points to invalid method - constructor method no args" {
            {
                New-PSNetDetourHook -Source { [IO.Path]::new() } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: new Path()'
        }

        It "Points to invalid method - constructor method with args" {
            {
                New-PSNetDetourHook -Source { [IO.Path]::new([string], [int]) } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: new Path(String arg0, Int32 arg1)'
        }

        It "Fails to find internal/private method" {
            {
                New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::InternalMethod() } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: TestClass static ReturnType InternalMethod()'
        }
    }

    if ($IsCoreCLR) {
        Context "PowerShell 7 specific tests" {
            # Contains syntax only valid in PowerShell 7
            . ([Path]::Combine($PSScriptRoot, 'New-PSNetDetourHook.Pwsh7.ps1'))
        }
    }
}
