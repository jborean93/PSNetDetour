using namespace System.IO

. ([Path]::Combine($PSScriptRoot, 'common.ps1'))

Describe "Use-PSNetDetourContext" {
    It "Stops hook outside ScriptBlock" {
        Use-PSNetDetourContext {
            New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                param ($arg1)

                $arg1 | Should -Be 5
                $Detour.Instance | Should -BeNullOrEmpty
                $Detour.Invoke($arg1) + 2
            }

            [PSNetDetour.Tests.TestClass]::StaticIntArgs(5) | Should -Be 8
        }

        # No longer hooked so Should assertions won't fire in the hook
        # and the return value won't be +2.
        [PSNetDetour.Tests.TestClass]::StaticIntArgs(4) | Should -Be 5
    }

    # TODO: Add tests for nested Use-PSNetDetourContext calls
    # TODO: Add tests for exceptions inside ScriptBlock
    # TODO: Add tests for multiple hooks inside ScriptBlock
    # TODO: Add tests for explicitly capturing hooks and disposing them inside ScriptBlock
    # TODO: Add tests for writing to error/verbose/debug/warning/info/progress streams inside hook and ScriptBlock
    # TODO: Add testss for flow control (continue/break) inside ScriptBlock
}
