---
external help file: PSNetDetour.dll-Help.xml
Module Name: PSNetDetour
online version: https://www.github.com/jborean93/PSNetDetour/blob/main/docs/en-US/Use-NetDetourContext.md
schema: 2.0.0
---

# Use-NetDetourContext

## SYNOPSIS
Executes a scriptblock with automatic management of NetDetour hooks.

## SYNTAX

```
Use-NetDetourContext [-ScriptBlock] <ScriptBlock> [-NoNewScope] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
The `Use-NetDetourContext` cmdlet executes a scriptblock and automatically manages the lifecycle of any NetDetourHook objects created within it. When the scriptblock completes (either successfully or with an error), all hooks created by `New-NetDetourHook` inside the context are automatically disposed.

This cmdlet provides a convenient way to use hooks without manually managing their disposal with try/finally blocks. It's similar to C#'s `using` statement.

By default, the scriptblock executes in a new child scope, use the `-NoNewScope` switch to run in the current scope if you need to modify parent scope variables.

The cmdlet properly forwards all PowerShell streams (Error, Warning, Verbose, Debug, Information, and Progress) from hooks back to the caller, even when hooks are executed in separate runspaces. If the hook is executed in the current runspace it will be emitted as soon as the hook created the record but if the hook is run in another runspace the records will only be emitted when `Use-NetDetourContext` ends.

## EXAMPLES

### Example 1: Basic usage with automatic hook disposal
```powershell
Use-NetDetourContext {
    New-NetDetourHook -Source { [System.IO.Path]::GetTempPath() } -Hook {
        "C:\CustomTemp\"
    }
    [System.IO.Path]::GetTempPath()
}

# C:\CustomTemp\

[System.IO.Path]::GetTempPath()

# C:\Users\username\AppData\Local\Temp\
```

The hook is active inside the context and automatically disposed when the scriptblock completes.

### Example 2: Multiple hooks in one context
```powershell
Use-NetDetourContext {
    New-NetDetourHook -Source { [System.IO.Path]::GetTempPath() } -Hook {
        "C:\CustomTemp\"
    }
    New-NetDetourHook -Source { [System.IO.Path]::GetTempFileName() } -Hook {
        "C:\CustomTemp\test.tmp"
    }
    [System.IO.Path]::GetTempPath()
    [System.IO.Path]::GetTempFileName()
}

# C:\CustomTemp\
# C:\CustomTemp\test.tmp
```

All hooks created in the context are automatically disposed when the scriptblock completes.

### Example 3: Using -NoNewScope to modify parent variables
```powershell
$result = $null
Use-NetDetourContext -NoNewScope {
    New-NetDetourHook -Source { [System.Random].Next([int]) } -Hook {
        42
    }
    $result = [System.Random]::new().Next(100)
}

$result
# 42
```

The `-NoNewScope` switch allows the scriptblock to modify variables in the parent scope.

### Example 4: Nested contexts
```powershell
Use-NetDetourContext {
     New-NetDetourHook -Source { [System.Random].Next() } -Hook { 1 }
    [System.Random]::new().Next()  # Returns 1

    Use-NetDetourContext {
        New-NetDetourHook -Source { [System.Random].Next() } -Hook { 2 }
        [System.Random]::new().Next()  # Returns 2
    }

    [System.Random]::new().Next()  # Returns 1 again
}

# 1
# 2
# 1
```

Contexts can be nested, with inner hooks taking precedence and being disposed when the inner context exits.

### Example 5: Error handling with automatic cleanup
```powershell
try {
    Use-NetDetourContext {
        New-NetDetourHook -Source { [System.IO.File]::ReadAllText([string]) } -Hook {
            param($path)
            Write-Host "Reading: $path"
            throw "Simulated error"
        }
        [System.IO.File]::ReadAllText("test.txt")
    }
} catch {
    Write-Host "Caught: $($_.Exception.Message)"
}

[System.IO.File]::ReadAllText("test.txt")

# Reading: test.txt
# Exception occurred while invoking hook for ReadAllText: Simulated error
# Text here
```

Even when errors occur, hooks are properly disposed when exiting the context.

## PARAMETERS

### -NoNewScope
Runs the scriptblock in the current scope instead of a new child scope. This allows the scriptblock to modify variables in the parent scope. By default, scriptblocks run in a new scope which isolates variables.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProgressAction
New common parameter introduced in PowerShell 7.4.

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ScriptBlock
The scriptblock to execute. Any NetDetourHook objects created inside this scriptblock (via `New-NetDetourHook`) that are output to the pipeline (not captured in variables) will be automatically disposed when the scriptblock completes.

The scriptblock can produce output which will be returned from the cmdlet. Hook objects themselves are captured internally and not returned in the output.

```yaml
Type: ScriptBlock
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

## OUTPUTS

### System.Object
Any object emitted by the scriptblock.

## NOTES

## RELATED LINKS
