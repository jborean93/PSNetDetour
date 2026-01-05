using namespace System.IO

. ([Path]::Combine($PSScriptRoot, 'common.ps1'))

Describe "New-PSNetDetourHook" {
    Context "Target Hooks" {
        It "Hooks static void method with no args" {
            $script:marker = $false
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs() } -Hook {
                $script:marker = $true

                [PSNetDetour.Tests.TestClass]::StaticVoidCalled | Should -Be 0
                $Detour.Instance | Should -BeNullOrEmpty
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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::StaticVoidWithArgs([string], [int]) } -Hook {
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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass].InstanceVoidNoArgs() } -Hook {
                param ()

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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass].InstanceVoidWithArgs([string], [int]) } -Hook {
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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass].InstanceIntNoArgs() } -Hook {
                param ()

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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass].InstanceIntWithArgs([int]) } -Hook {
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

        It "Hooks constructor with no args" {
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::new() } -Hook {
                param ()

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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::new([int]) } -Hook {
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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::InternalMethod() } -Hook {
                param ()

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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::new([int]) } -Hook {
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
            $h1 = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::CaseCheck() } -Hook {
                $Detour.Invoke() + 1
            }
            $h2 = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::casecheck() } -Hook {
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
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::staticintnoargs() } -Hook {
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

        It "Hooks property getter" {
            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass].get_SomeProperty() } -Hook {
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

        It "Hooks property setter" {
            $i = [PSNetDetour.Tests.TestClass]::new()

            $h = New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass].set_SomeProperty([int]) } -Hook {
                param ($arg1)

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

        # TODO: Add tests for generics, constructors with base/this calls
        # TODO: Add tests for ref/out params
        # TODO: Add tests for no output, cannot cast output, more output
        # TODO: Add tests for host output
        # TODO: Add tests for throw in hook
        # TODO: Add tests for errors/verbose/debug/warning/progress in hook
        # TODO: Add tests for MethodBase parameter
        # TODO: Add tests for async
        # TODO: Add tests for method invoked in another thread
    }

    Context "Invalid ScriptBlock Targets" {
        It "Has params" {
            {
                New-PSNetDetourHook -Target { param() } -Hook {}
            } | Should -Throw '*ScriptBlock must not contain the param block.'
        }

        It "Has dynamic params" {
            {
                New-PSNetDetourHook -Target { dynamicparam {} } -Hook {}
            } | Should -Throw '*ScriptBlock must not contain the dynamicparam block.'
        }

        It "Has begin block" {
            {
                New-PSNetDetourHook -Target { begin {} } -Hook {}
            } | Should -Throw '*ScriptBlock must not contain a begin block.'
        }

        It "Has process block" {
            {
                New-PSNetDetourHook -Target { process {} } -Hook {}
            } | Should -Throw '*ScriptBlock must not contain a process block.'
        }

        It "Has clean block" -Skip:(-not $IsCoreCLR) {
            {
                New-PSNetDetourHook -Target { clean {} } -Hook {}
            } | Should -Throw '*ScriptBlock must not contain a clean block.'
        }

        It "Has no statements" {
            {
                New-PSNetDetourHook -Target {} -Hook {}
            } | Should -Throw '*ScriptBlock end block must have only one statement.'
        }

        It "Has multiple statements" {
            {
                New-PSNetDetourHook -Target { 1; 2 } -Hook {}
            } | Should -Throw '*ScriptBlock end block must have only one statement.'
        }

        It "Has statement that is not a command expression" {
            {
                New-PSNetDetourHook -Target { $a = 1 } -Hook {}
            } | Should -Throw '*ScriptBlock end block statement must be a method call.'
        }

        It "Has multiple pipeline elements" {
            {
                New-PSNetDetourHook -Target { [IO.Path]::GetTempPath() | Write-Output } -Hook {}
            } | Should -Throw '*ScriptBlock pipeline must have only one element.'
        }

        It "Has pipeline element that is not a command expression" {
            {
                New-PSNetDetourHook -Target { Write-Output 'test' } -Hook {}
            } | Should -Throw '*ScriptBlock pipeline element must be a method call.'
        }

        It "Has redirections in method call" {
            {
                New-PSNetDetourHook -Target { [IO.Path]::GetTempPath() > $null } -Hook {}
            } | Should -Throw '*ScriptBlock command expression must not have any redirections.'
        }

        It "Has command expression that is not a method call" {
            {
                New-PSNetDetourHook -Target { [Type]'test' } -Hook {}
            } | Should -Throw '*ScriptBlock expression must be a method call.'
        }

        It "Has method call on variable and not a type" {
            {
                New-PSNetDetourHook -Target { $a.GetTempPath() } -Hook {}
            } | Should -Throw '*ScriptBlock method call must be invoked on a type.'
        }

        It "Has method call on name that is not a constant plain string" {
            {
                New-PSNetDetourHook -Target { [Type]::$someVar() } -Hook {}
            } | Should -Throw '*ScriptBlock method call method name must be a string constant.'
        }

        It "Hash method with args that are not types" {
            {
                New-PSNetDetourHook -Target { [IO.Path]::Combine('a', 'b') } -Hook {}
            } | Should -Throw '*ScriptBlock method call arguments must be type expressions.'
        }

        It "Uses null conditional with method call" -Skip:(-not $IsCoreCLR) {
            {
                New-PSNetDetourHook -Target { [Type]?.SomeMethod() } -Hook {}
            } | Should -Throw '*ScriptBlock method call must not be null-conditional.'
        }

        It "Uses unknown type" {
            {
                New-PSNetDetourHook -Target { [NonExistent.Type]::SomeMethod() } -Hook {}
            } | Should -Throw '*Failed to resolve type NonExistent.Type.'
        }

        It "Uses unknown type in generic type definition" {
            {
                New-PSNetDetourHook -Target { [System.Collections.Generic.Dictionary[int, Fake]]::SomeMethod() } -Hook {}
            } | Should -Throw '*Failed to resolve type Fake.'
        }

        It "Uses generic type definition with no type args" {
            {
                New-PSNetDetourHook -Target { [System.Collections.Generic.Dictionary`2]::SomeMethod() } -Hook {}
            } | Should -Throw '*Type Dictionary``2 is a generic type but hooks can only be set with generic type arguments.'
        }

        It "Uses unknown type in generic type args" -Skip:(-not $IsCoreCLR) {
            {
                New-PSNetDetourHook -Target { [Type]::SomeMethod[int, Fake]() } -Hook {}
            } | Should -Throw '*Failed to resolve type Fake.'

        }

        It "Points to invalid method - static method no args" {
            {
                New-PSNetDetourHook -Target { [IO.Path]::NonExistentMethod() } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: Path static ReturnType NonExistentMethod()'
        }

        It "Points to invalid method - static method with args" {
            {
                New-PSNetDetourHook -Target { [IO.Path]::NonExistentMethod([string], [int]) } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: Path static ReturnType NonExistentMethod(String arg0, Int32 arg1)'
        }

        It "Points to invalid method - instance method no args" {
            {
                New-PSNetDetourHook -Target { [IO.Path].NonExistentMethod() } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: Path ReturnType NonExistentMethod()'
        }

        It "Points to invalid method - instance method with args" {
            {
                New-PSNetDetourHook -Target { [IO.Path].NonExistentMethod([string], [int]) } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: Path ReturnType NonExistentMethod(String arg0, Int32 arg1)'
        }

        It "Points to invalid method - constructor method no args" {
            {
                New-PSNetDetourHook -Target { [IO.Path]::new() } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: new Path()'
        }

        It "Points to invalid method - constructor method with args" {
            {
                New-PSNetDetourHook -Target { [IO.Path]::new([string], [int]) } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: new Path(String arg0, Int32 arg1)'
        }

        It "Fails to find internal/private method" {
            {
                New-PSNetDetourHook -Target { [PSNetDetour.Tests.TestClass]::InternalMethod() } -Hook {}
            } | Should -Throw '*Failed to find the method described by the ScriptBlock: TestClass static ReturnType InternalMethod()'
        }
    }
}
