# PSNetDetour

[![Test workflow](https://github.com/jborean93/PSNetDetour/workflows/Test%20PSNetDetour/badge.svg)](https://github.com/jborean93/PSNetDetour/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/jborean93/PSNetDetour/branch/main/graph/badge.svg?token=b51IOhpLfQ)](https://codecov.io/gh/jborean93/PSNetDetour)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/dt/PSNetDetour.svg)](https://www.powershellgallery.com/packages/PSNetDetour)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/jborean93/PSNetDetour/blob/main/LICENSE)

Hook .NET functions in PowerShell.

## Overview

PSNetDetour is an experimental PowerShell module that enables runtime hooking (detouring) of .NET methods. It allows you to intercept calls to .NET methods, inspect or modify their arguments, and control their return values. When a hooked method is called, your custom PowerShell scriptblock executes instead, giving you full control over the method's behavior.

This module is useful for:

- **Testing and Mocking** - Replace method implementations during tests without modifying production code
- **Debugging and Diagnostics** - Trace method calls, log arguments, and inspect behavior at runtime
- **Behavioral Modification** - Alter how existing .NET methods work without access to source code
- **Research and Experimentation** - Understand how .NET libraries work by intercepting their internal calls

### Experimental Warning

This module is **highly experimental** and should be used with caution:

- **Stability Issues** - Hooking certain methods can cause unexpected crashes, hangs, or process instability
- **Thread Safety** - Methods used in background threads (like `System.Text.StringBuilder`) can be particularly problematic
- **Not for Production** - This module is intended for development, testing, and research environments only
- **Generic Limitations** - Cannot hook methods on generic types (e.g., `List<T>`) or generic methods due to underlying library constraints
- **Breaking Changes** - As an experimental module, the API may change between versions

Use this module at your own risk and thoroughly test in isolated environments before any broader use.

## Documentation

Documentation for this module and details on the cmdlets included can be found [here](docs/en-US/PSNetDetour.md).

### Quick Examples

Hook a static method to return a custom value:

```powershell
Use-NetDetourContext {
    New-NetDetourHook -Source { [System.IO.Path]::GetTempPath() } -Hook {
        "C:\CustomTemp\"
    }
    [System.IO.Path]::GetTempPath()
}
# C:\CustomTemp\
```

Hook an instance method and modify its behavior:

```powershell
Use-NetDetourContext {
    New-NetDetourHook -Source { [System.DateTime].AddDays([double]) } -Hook {
        param($days)
        Write-Host "Adding $days days to $($Detour.Instance.ToString('o'))"
        $Detour.Invoke($days * 2)
    }

    $date = [DateTime]::new(2024, 1, 1)
    $date.AddDays(5).ToString("o")
}
# Adding 5 days to 2024-01-01T00:00:00.0000000
# 2024-01-11T00:00:00.0000000
```

Hook async methods and inspect results:

```powershell
Use-NetDetourContext {
    New-NetDetourHook -Source { [System.Net.Http.HttpClient].GetAsync([string]) } -Hook {
        param($uri)
        Write-Host "HTTP GET: $uri"

        $task = $Detour.Invoke($uri)
        while (-not $task.AsyncWaitHandle.WaitOne(300)) {}
        $response = $task.GetAwaiter().GetResult()

        Write-Host "Response: $($response.StatusCode)"
        $task
    } -UseRunspace New

    $client = [System.Net.Http.HttpClient]::new()
    $task = $client.GetAsync("https://httpbin.org/get")
    while (-not $task.AsyncWaitHandle.WaitOne(300)) {}
    $response = $task.GetAwaiter().GetResult()
}
# HTTP GET: https://httpbin.org/get
# Response: OK
```

Hook methods with ref/out parameters:

```powershell
Use-NetDetourContext {
    New-NetDetourHook -Source { [int]::TryParse([string], [ref][int]) } -Hook {
        param($s, $result)

        Write-Host "Parsing: $s"
        $originalResult = $Detour.Invoke($s, $result)

        if ($originalResult) {
            Write-Host "Parsed value: $($result.Value)"
            $result.Value += 10
        }

        $originalResult
    }

    $num = 0
    [int]::TryParse("123", [ref]$num)
    "Result: $num"
}
# Parsing: 123
# Parsed value: 123
# Result: 133
```

## Requirements

These cmdlets have the following requirements

* PowerShell 5.1, or v7.4+

Support for future PowerShell versions will be dependent on support for those .NET versions in [MonoMod](https://github.com/MonoMod/MonoMod).
Currently Windows arm64 is not supported until `MonoMod` implements support for it.

## Installing

The easiest way to install this module is through [PowerShellGet](https://docs.microsoft.com/en-us/powershell/gallery/overview) or [PSResourceGet](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.psresourceget/?view=powershellget-3.x).

You can install this module by running either of the following `Install-PSResource` or `Install-Module` command.

```powershell
# Install for only the current user
Install-PSResource -Name PSNetDetour -Scope CurrentUser
Install-Module -Name PSNetDetour -Scope CurrentUser

# Install for all users
Install-PSResource -Name PSNetDetour -Scope AllUsers
Install-Module -Name PSNetDetour -Scope AllUsers
```

The `Install-PSResource` cmdlet is part of the new `PSResourceGet` module from Microsoft available in newer versions while `Install-Module` is present on older systems.

## Contributing

Contributing is quite easy, fork this repo and submit a pull request with the changes.
To build this module run `.\build.ps1 -Task Build` in PowerShell.
To test a build run `.\build.ps1 -Task Test` in PowerShell.
This script will ensure all dependencies are installed before running the test suite.
