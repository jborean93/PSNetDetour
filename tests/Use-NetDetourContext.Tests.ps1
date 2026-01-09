using namespace System.IO

. ([Path]::Combine($PSScriptRoot, 'common.ps1'))

Describe "Use-NetDetourContext" {
    It "Runs in a child scope" {
        $a = 1
        Use-NetDetourContext {
            $a = 2
        }
        $a | Should -Be 1
    }

    It "Runs in the same scope" {
        $a = 1
        Use-NetDetourContext -NoNewScope {
            $a = 2
        }
        $a | Should -Be 2
    }

    It "Stops hook outside ScriptBlock" {
        Use-NetDetourContext {
            New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
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

    It "Sends streams to Use-NetDetourContext streams" {
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

            Use-NetDetourContext {
                New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs() } -Hook {
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
        $ps.Streams.Error[0].InvocationInfo.PositionMessage | Should -BeLike 'At line:12 char:17*'  # New-NetDetourHook line
        $ps.Streams.Error[1].Exception.Message | Should -Be 'outer'
        $ps.Streams.Error[1].InvocationInfo.PositionMessage | Should -BeLike 'At line:11 char:13*'  # Use-NetDetourContext line

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
        $capturingHost.UI.CallHistory[3] | Should -BeLike 'WriteProgress: ID:? REC:parent = -1 id = 0 act = Test Activity stat = Test Status cur =  pct = -1 sec = -1 type = Completed'
        $capturingHost.UI.CallHistory[4] | Should -Be 'WriteInformation: REC:information'
        $capturingHost.UI.CallHistory[5] | Should -Be 'WriteLine: VAL:information'
        $capturingHost.UI.CallHistory[6] | Should -Be 'WriteInformation: REC:host'
        $capturingHost.UI.CallHistory[7] | Should -Be 'WriteLine: VAL:host'

        $capturingHost.UI.CallHistory[8] | Should -Be 'WriteVerboseLine: MSG:outer'
        $capturingHost.UI.CallHistory[9] | Should -Be 'WriteDebugLine: MSG:outer'
        $capturingHost.UI.CallHistory[10] | Should -Be 'WriteWarningLine: MSG:outer'
        $capturingHost.UI.CallHistory[11] | Should -BeLike 'WriteProgress: ID:? REC:parent = -1 id = 0 act = Test Outer stat = Test Status cur =  pct = -1 sec = -1 type = Completed'
        $capturingHost.UI.CallHistory[12] | Should -Be 'WriteInformation: REC:outer'
        $capturingHost.UI.CallHistory[13] | Should -Be 'WriteLine: VAL:outer'
        $capturingHost.UI.CallHistory[14] | Should -Be 'WriteInformation: REC:outer'
        $capturingHost.UI.CallHistory[15] | Should -Be 'WriteLine: VAL:outer'

        $ps.Dispose()
    }

    It "Sends streams to Use-NetDetourContext streams with hook in Runspace" {
        <#
        This is like the above test but due to the hook running in another
        runspace we can't forward the streams in realtime. Only the error
        stream if forwarded after the hook runs and before the context whereas
        the other streams are only emitted after the context ends.
        #>
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

            Use-NetDetourContext {
                New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs() } -Hook {
                    $ErrorActionPreference = 'Continue'
                    $VerbosePreference = 'Continue'
                    $DebugPreference = 'Continue'
                    $WarningPreference = 'Continue'
                    $ProgressPreference = 'Continue'
                    $InformationPreference = 'Continue'

                    Write-Error "error"
                    Write-Verbose "verbose"
                    Write-Debug "debug"
                    Write-Warning "warning"
                    Write-Progress -Activity "Test Activity" -Status "Test Status" -Completed
                    Write-Information "information"
                    Write-Host "host"
                } -UseRunspace New

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
        $ps.Streams.Error[0].InvocationInfo.PositionMessage | Should -BeLike 'At line:12 char:17*'  # New-NetDetourHook line
        $ps.Streams.Error[1].Exception.Message | Should -Be 'outer'
        $ps.Streams.Error[1].InvocationInfo.PositionMessage | Should -BeLike 'At line:11 char:13*'  # Use-NetDetourContext line

        # The remaining streams are all forwarded after the context ends so they appear
        # after the outer ones.

        $ps.Streams.Progress.Count | Should -Be 2
        $ps.Streams.Progress[0].Activity | Should -Be 'Test Outer'
        $ps.Streams.Progress[0].StatusDescription | Should -Be 'Test Status'
        $ps.Streams.Progress[0].RecordType | Should -Be 'Completed'
        $ps.Streams.Progress[1].Activity | Should -Be 'Test Activity'
        $ps.Streams.Progress[1].StatusDescription | Should -Be 'Test Status'
        $ps.Streams.Progress[1].RecordType | Should -Be 'Completed'

        $ps.Streams.Verbose.Count | Should -Be 2
        $ps.Streams.Verbose[0].Message | Should -Be 'outer'
        $ps.Streams.Verbose[1].Message | Should -Be 'verbose'

        $ps.Streams.Debug.Count | Should -Be 2
        $ps.Streams.Debug[0].Message | Should -Be 'outer'
        $ps.Streams.Debug[1].Message | Should -Be 'debug'

        $ps.Streams.Warning.Count | Should -Be 2
        $ps.Streams.Warning[0].Message | Should -Be 'outer'
        $ps.Streams.Warning[1].Message | Should -Be 'warning'

        $ps.Streams.Information.Count | Should -Be 4
        $ps.Streams.Information[0].MessageData | Should -Be 'outer'
        $ps.Streams.Information[0].Tags.Count | Should -Be 0
        $ps.Streams.Information[1].MessageData | Should -Be 'outer'
        $ps.Streams.Information[1].Tags.Count | Should -Be 1
        $ps.Streams.Information[1].Tags[0] | Should -Be 'PSHOST'
        $ps.Streams.Information[2].MessageData | Should -Be 'information'
        $ps.Streams.Information[2].Tags.Count | Should -Be 0
        $ps.Streams.Information[3].MessageData | Should -Be 'host'
        $ps.Streams.Information[3].Tags.Count | Should -Be 1
        $ps.Streams.Information[3].Tags[0] | Should -Be 'PSHOST'

        $capturingHost.UI.CallHistory.Length | Should -Be 16
        $capturingHost.UI.CallHistory[0] | Should -Be 'WriteVerboseLine: MSG:outer'
        $capturingHost.UI.CallHistory[1] | Should -Be 'WriteDebugLine: MSG:outer'
        $capturingHost.UI.CallHistory[2] | Should -Be 'WriteWarningLine: MSG:outer'
        $capturingHost.UI.CallHistory[3] | Should -BeLike 'WriteProgress: ID:? REC:parent = -1 id = 0 act = Test Outer stat = Test Status cur =  pct = -1 sec = -1 type = Completed'
        $capturingHost.UI.CallHistory[4] | Should -Be 'WriteInformation: REC:outer'
        $capturingHost.UI.CallHistory[5] | Should -Be 'WriteLine: VAL:outer'
        $capturingHost.UI.CallHistory[6] | Should -Be 'WriteInformation: REC:outer'
        $capturingHost.UI.CallHistory[7] | Should -Be 'WriteLine: VAL:outer'

        $capturingHost.UI.CallHistory[8] | Should -Be 'WriteVerboseLine: MSG:verbose'
        $capturingHost.UI.CallHistory[9] | Should -Be 'WriteDebugLine: MSG:debug'
        $capturingHost.UI.CallHistory[10] | Should -Be 'WriteWarningLine: MSG:warning'
        $capturingHost.UI.CallHistory[11] | Should -BeLike 'WriteProgress: ID:? REC:parent = -1 id = 0 act = Test Activity stat = Test Status cur =  pct = -1 sec = -1 type = Completed'
        $capturingHost.UI.CallHistory[12] | Should -Be 'WriteInformation: REC:information'
        $capturingHost.UI.CallHistory[13] | Should -Be 'WriteLine: VAL:information'
        $capturingHost.UI.CallHistory[14] | Should -Be 'WriteInformation: REC:host'
        $capturingHost.UI.CallHistory[15] | Should -Be 'WriteLine: VAL:host'

        $ps.Dispose()
    }

    It "Sends streams to Use-NetDetourContext streams with hook in Runspace and in another thread" {
        <#
        Just like the above the streams cannot be forwarded in realtime but as
        the hook is running in another thread we cannot forward the error
        stream anymore. They will now come after the context ends like the other
        streams in this scenario.
        #>
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

            Use-NetDetourContext {
                New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                    $ErrorActionPreference = 'Continue'
                    $VerbosePreference = 'Continue'
                    $DebugPreference = 'Continue'
                    $WarningPreference = 'Continue'
                    $ProgressPreference = 'Continue'
                    $InformationPreference = 'Continue'

                    Write-Error "error"
                    Write-Verbose "verbose"
                    Write-Debug "debug"
                    Write-Warning "warning"
                    Write-Progress -Activity "Test Activity" -Status "Test Status" -Completed
                    Write-Information "information"
                    Write-Host "host"

                    $Detour.Invoke($arg1)
                } -UseRunspace New

                $null = [PSNetDetour.Tests.TestClass]::RunInAnotherThread()

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
        $ps.Streams.Error[0].Exception.Message | Should -Be 'outer'
        $ps.Streams.Error[0].InvocationInfo.PositionMessage | Should -BeLike 'At line:11 char:13*'  # Use-NetDetourContext line
        $ps.Streams.Error[1].Exception.Message | Should -Be 'error'
        $ps.Streams.Error[1].InvocationInfo.PositionMessage | Should -BeLike 'At line:11 char:13*'  # Use-NetDetourContext line

        # The remaining streams are all forwarded after the context ends so they appear
        # after the outer ones.

        $ps.Streams.Progress.Count | Should -Be 2
        $ps.Streams.Progress[0].Activity | Should -Be 'Test Outer'
        $ps.Streams.Progress[0].StatusDescription | Should -Be 'Test Status'
        $ps.Streams.Progress[0].RecordType | Should -Be 'Completed'
        $ps.Streams.Progress[1].Activity | Should -Be 'Test Activity'
        $ps.Streams.Progress[1].StatusDescription | Should -Be 'Test Status'
        $ps.Streams.Progress[1].RecordType | Should -Be 'Completed'

        $ps.Streams.Verbose.Count | Should -Be 2
        $ps.Streams.Verbose[0].Message | Should -Be 'outer'
        $ps.Streams.Verbose[1].Message | Should -Be 'verbose'

        $ps.Streams.Debug.Count | Should -Be 2
        $ps.Streams.Debug[0].Message | Should -Be 'outer'
        $ps.Streams.Debug[1].Message | Should -Be 'debug'

        $ps.Streams.Warning.Count | Should -Be 2
        $ps.Streams.Warning[0].Message | Should -Be 'outer'
        $ps.Streams.Warning[1].Message | Should -Be 'warning'

        $ps.Streams.Information.Count | Should -Be 4
        $ps.Streams.Information[0].MessageData | Should -Be 'outer'
        $ps.Streams.Information[0].Tags.Count | Should -Be 0
        $ps.Streams.Information[1].MessageData | Should -Be 'outer'
        $ps.Streams.Information[1].Tags.Count | Should -Be 1
        $ps.Streams.Information[1].Tags[0] | Should -Be 'PSHOST'
        $ps.Streams.Information[2].MessageData | Should -Be 'information'
        $ps.Streams.Information[2].Tags.Count | Should -Be 0
        $ps.Streams.Information[3].MessageData | Should -Be 'host'
        $ps.Streams.Information[3].Tags.Count | Should -Be 1
        $ps.Streams.Information[3].Tags[0] | Should -Be 'PSHOST'

        $capturingHost.UI.CallHistory.Length | Should -Be 16
        $capturingHost.UI.CallHistory[0] | Should -Be 'WriteVerboseLine: MSG:outer'
        $capturingHost.UI.CallHistory[1] | Should -Be 'WriteDebugLine: MSG:outer'
        $capturingHost.UI.CallHistory[2] | Should -Be 'WriteWarningLine: MSG:outer'
        $capturingHost.UI.CallHistory[3] | Should -BeLike 'WriteProgress: ID:? REC:parent = -1 id = 0 act = Test Outer stat = Test Status cur =  pct = -1 sec = -1 type = Completed'
        $capturingHost.UI.CallHistory[4] | Should -Be 'WriteInformation: REC:outer'
        $capturingHost.UI.CallHistory[5] | Should -Be 'WriteLine: VAL:outer'
        $capturingHost.UI.CallHistory[6] | Should -Be 'WriteInformation: REC:outer'
        $capturingHost.UI.CallHistory[7] | Should -Be 'WriteLine: VAL:outer'

        $capturingHost.UI.CallHistory[8] | Should -Be 'WriteVerboseLine: MSG:verbose'
        $capturingHost.UI.CallHistory[9] | Should -Be 'WriteDebugLine: MSG:debug'
        $capturingHost.UI.CallHistory[10] | Should -Be 'WriteWarningLine: MSG:warning'
        $capturingHost.UI.CallHistory[11] | Should -BeLike 'WriteProgress: ID:? REC:parent = -1 id = 0 act = Test Activity stat = Test Status cur =  pct = -1 sec = -1 type = Completed'
        $capturingHost.UI.CallHistory[12] | Should -Be 'WriteInformation: REC:information'
        $capturingHost.UI.CallHistory[13] | Should -Be 'WriteLine: VAL:information'
        $capturingHost.UI.CallHistory[14] | Should -Be 'WriteInformation: REC:host'
        $capturingHost.UI.CallHistory[15] | Should -Be 'WriteLine: VAL:host'

        $ps.Dispose()
    }

    It "Throws exception inside hook in context" {
        {
            Use-NetDetourContext {
                New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticVoidNoArgs() } -Hook {
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

    It "Nests context calls" {
        [PSNetDetour.Tests.TestClass]::StaticIntArgs(10) | Should -Be 11

        Use-NetDetourContext {
            New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                param ($arg1)

                $Detour.Invoke($arg1) + 1
            }

            [PSNetDetour.Tests.TestClass]::StaticIntArgs(10) | Should -Be 12

            Use-NetDetourContext {
                New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                    param ($arg1)

                    $Detour.Invoke($arg1) + 1
                }

                [PSNetDetour.Tests.TestClass]::StaticIntArgs(10) | Should -Be 13
            }

            [PSNetDetour.Tests.TestClass]::StaticIntArgs(10) | Should -Be 12
        }

        [PSNetDetour.Tests.TestClass]::StaticIntArgs(10) | Should -Be 11
    }

    It "Captures hooks interleaved with normal output" {
        $out = Use-NetDetourContext {
            New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook { 10 }

            1

            New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook { 11 }

            2

            New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass].InstanceIntNoArgs() } -Hook { 12 }

            [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 10
            [PSNetDetour.Tests.TestClass]::StaticIntArgs(5) | Should -Be 11
            [PSNetDetour.Tests.TestClass]::new().InstanceIntNoArgs() | Should -Be 12
        }

        $out.Count | Should -Be 2
        $out[0] | Should -Be 1
        $out[1] | Should -Be 2

        [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 1
        [PSNetDetour.Tests.TestClass]::StaticIntArgs(5) | Should -Be 6
        [PSNetDetour.Tests.TestClass]::new().InstanceIntNoArgs() | Should -Be 1
    }

    It "Ignores explicitly captured hooks" {
        $hook = $null
        try {
            Use-NetDetourContext -NoNewScope {
                $hook = New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook { 10 }

                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 10
            }

            # Still be active because it was captured in a var and not in the cmdlet.
            [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 10
            $hook | Should -Not -BeNullOrEmpty

            $hook.Dispose()
            $hook = $null

            [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 1
        }
        finally {
            if ($hook) { $hook.Dispose() }
        }
    }

    It "Uses function as context ScriptBlock" {
        Function run {
            New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntArgs([int]) } -Hook {
                param ($arg1)

                $Detour.Invoke($arg1) + 1
            }

            [PSNetDetour.Tests.TestClass]::StaticIntArgs(10) | Should -Be 12
        }

        Use-NetDetourContext ${function:run}
    }

    It "Writes warning for hook with too much output" {
        $warn = @()
        Use-NetDetourContext {
            New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
                10
                "abc"
                return @{ key = "value" }
            }

            [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 10
        } -WarningVariable warn -WarningAction Ignore

        $warn.Count | Should -Be 1
        $warn[0] | Should -Be "Hook for 'TestClass static Int32 StaticIntNoArgs()' produced multiple output values; only the first will be used."
    }

    It "Strips the runspace affinity from the <SbkType>" -TestCases @(
        @{ SbkType = 'Function' }
        @{ SbkType = 'ScriptBlock' }
    ) {
        param ($SbkType)

        $TestVar = 'from main'

        $rs = [RunspaceFactory]::CreateRunspace()
        try {
            $rs.Open()

            $ps = [PowerShell]::Create()
            $ps.Runspace = $rs
            $toRun = $ps.AddScript({
                $TestVar = 'from runspace'

                if ($args[0] -eq 'Function') {
                    function func { $TestVar }
                    ${function:func}
                }
                else {
                    { $TestVar }
                }

            }).AddArgument($SbkType).Invoke()[0]
            $ps.Dispose()

            Use-NetDetourContext $toRun | Should -Be 'from main'
        }
        finally {
            $rs.Dispose()
        }
    }

    It "Does not run hook when setting up hook context" {
        $sourceType = (Get-Command -Name Use-NetDetourContext).ImplementingType.Assembly.GetType('PSNetDetour.ScriptBlockInvokeContext')
        $sourceMethod = $sourceType.GetMethod('CreatePowerShell', [System.Reflection.BindingFlags]'NonPublic, Instance')

        Use-NetDetourContext {
            # Running a hook calls CreatePowerShell internally so could lead to
            # infinite recursion due to hooking CreatePowerShell. When running
            # the CreatePowerShell hook it'll call CreatePowerShell running the
            # hook entrypoint again but due to our checks it'll skip past our
            # hook and run the original method.
            New-NetDetourHook -Method $sourceMethod -Hook {
                param($arg1)

                $Detour.Invoke($arg1)

                # Another recursion, as StaticIntNoArgs will call our hook
                # which calls CreatePowerShell, the hook is skipped and the
                # original result is returned.
                [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 1
            }

            New-NetDetourHook -Source { [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() } -Hook {
                100
            }

            # Triggers the first CreatePowerShell call as part of invoking the hook.
            [PSNetDetour.Tests.TestClass]::StaticIntNoArgs() | Should -Be 100
        }
    }
}
