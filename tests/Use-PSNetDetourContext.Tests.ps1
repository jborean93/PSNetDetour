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

    It "Sends streams to Use-PSNetDetourContext streams" {
        $modulePath = Join-Path (Get-Module -Name PSNetDetour).ModuleBase 'PSNetDetour.psm1'
        $ps = [PowerShell]::Create()

        $capturingHost = [PSNetDetour.Tests.CapturingHost]::new()

        $null = $ps.AddScript({
            Import-Module $args[0]

            $ErrorActionPreference = 'Continue'
            $VerbosePreference = 'Continue'
            $DebugPreference = 'Continue'
            $WarningPreference = 'Continue'
            $ProgressPreference = 'Continue'
            $InformationPreference = 'Continue'

            Use-PSNetDetourContext {
                New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs() } -Hook {
                    Write-Error "error"
                    Write-Verbose "verbose"
                    Write-Debug "debug"
                    Write-Warning "warning"
                    Write-Progress -Activity "Test Activity" -Status "Test Status" -Completed
                    Write-Information "information"
                    Write-Host "host"
                }

                [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs()

                # Tests records from in the context are passed through as well
                Write-Error outer
                Write-Verbose outer
                Write-Debug outer
                Write-Warning outer
                Write-Progress -Activity "Test Outer" -Status "Test Status" -Completed
                Write-Information "outer"
                Write-Host "outer"
                'output'
            }
        }).AddArgument($modulePath)
        $out = $ps.Invoke($null, [System.Management.Automation.PSInvocationSettings]@{ Host = $capturingHost })

        $out.Count | Should -Be 1
        $out[0] | Should -Be output

        $ps.Streams.Error.Count | Should -Be 2
        $ps.Streams.Error[0].Exception.Message | Should -Be 'error'
        $ps.Streams.Error[0].InvocationInfo.PositionMessage | Should -BeLike 'At line:12 char:17*'  # New-PSNetDetourHook line
        $ps.Streams.Error[1].Exception.Message | Should -Be 'outer'
        $ps.Streams.Error[1].InvocationInfo.PositionMessage | Should -BeLike 'At line:11 char:13*'  # Use-PSNetDetourContext line

        $ps.Streams.Progress.Count | Should -Be 2
        $ps.Streams.Progress[0].Activity | Should -Be 'Test Activity'
        $ps.Streams.Progress[0].StatusDescription | Should -Be 'Test Status'
        $ps.Streams.Progress[0].RecordType | Should -Be 'Completed'
        $ps.Streams.Progress[1].Activity | Should -Be 'Test Outer'
        $ps.Streams.Progress[1].StatusDescription | Should -Be 'Test Status'
        $ps.Streams.Progress[1].RecordType | Should -Be 'Completed'

        $ps.Streams.Verbose.Count | Should -Be 2
        $ps.Streams.Verbose[0].Message | Should -Be 'verbose'
        $ps.Streams.Verbose[1].Message | Should -Be 'outer'

        $ps.Streams.Debug.Count | Should -Be 2
        $ps.Streams.Debug[0].Message | Should -Be 'debug'
        $ps.Streams.Debug[1].Message | Should -Be 'outer'

        $ps.Streams.Warning.Count | Should -Be 2
        $ps.Streams.Warning[0].Message | Should -Be 'warning'
        $ps.Streams.Warning[1].Message | Should -Be 'outer'

        $ps.Streams.Information.Count | Should -Be 4
        $ps.Streams.Information[0].MessageData | Should -Be 'information'
        $ps.Streams.Information[0].Tags.Count | Should -Be 0
        $ps.Streams.Information[1].MessageData | Should -Be 'host'
        $ps.Streams.Information[1].Tags.Count | Should -Be 1
        $ps.Streams.Information[1].Tags[0] | Should -Be 'PSHOST'
        $ps.Streams.Information[2].MessageData | Should -Be 'outer'
        $ps.Streams.Information[2].Tags.Count | Should -Be 0
        $ps.Streams.Information[3].MessageData | Should -Be 'outer'
        $ps.Streams.Information[3].Tags.Count | Should -Be 1
        $ps.Streams.Information[3].Tags[0] | Should -Be 'PSHOST'

        $capturingHost.UI.CallHistory.Length | Should -Be 16
        $capturingHost.UI.CallHistory[0] | Should -Be 'WriteVerboseLine: MSG:verbose'
        $capturingHost.UI.CallHistory[1] | Should -Be 'WriteDebugLine: MSG:debug'
        $capturingHost.UI.CallHistory[2] | Should -Be 'WriteWarningLine: MSG:warning'
        $capturingHost.UI.CallHistory[3] | Should -Be 'WriteProgress: ID:0 REC:parent = -1 id = 0 act = Test Activity stat = Test Status cur =  pct = -1 sec = -1 type = Completed'
        $capturingHost.UI.CallHistory[4] | Should -Be 'WriteInformation: REC:information'
        $capturingHost.UI.CallHistory[5] | Should -Be 'WriteLine: VAL:information'
        $capturingHost.UI.CallHistory[6] | Should -Be 'WriteInformation: REC:host'
        $capturingHost.UI.CallHistory[7] | Should -Be 'WriteLine: VAL:host'

        $capturingHost.UI.CallHistory[8] | Should -Be 'WriteVerboseLine: MSG:outer'
        $capturingHost.UI.CallHistory[9] | Should -Be 'WriteDebugLine: MSG:outer'
        $capturingHost.UI.CallHistory[10] | Should -Be 'WriteWarningLine: MSG:outer'
        $capturingHost.UI.CallHistory[11] | Should -Be 'WriteProgress: ID:0 REC:parent = -1 id = 0 act = Test Outer stat = Test Status cur =  pct = -1 sec = -1 type = Completed'
        $capturingHost.UI.CallHistory[12] | Should -Be 'WriteInformation: REC:outer'
        $capturingHost.UI.CallHistory[13] | Should -Be 'WriteLine: VAL:outer'
        $capturingHost.UI.CallHistory[14] | Should -Be 'WriteInformation: REC:outer'
        $capturingHost.UI.CallHistory[15] | Should -Be 'WriteLine: VAL:outer'

        $ps.Dispose()
    }

    It "Throws exception inside hook in context" {
        {
            Use-PSNetDetourContext {
                New-PSNetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs() } -Hook {
                    throw "hook exception"
                }

                # The exception appears from calling this method, to turn it into an exception that
                # will throw we need to try/catch and re-throw it.
                try {
                    [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs()
                }
                catch {
                    throw
                }
            }
        } | Should -Throw 'Exception occurred while invoking hook for StaticVoidNoArgs: hook exception'
    }

    # TODO: Add tests for nested Use-PSNetDetourContext calls
    # TODO: Add tests for multiple hooks inside ScriptBlock
    # TODO: Add tests for explicitly capturing hooks and disposing them inside ScriptBlock
}
