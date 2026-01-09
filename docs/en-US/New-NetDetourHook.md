---
external help file: PSNetDetour.dll-Help.xml
Module Name: PSNetDetour
online version: https://www.github.com/jborean93/PSNetDetour/blob/main/docs/en-US/New-NetDetourHook.md
schema: 2.0.0
---

# New-NetDetourHook

## SYNOPSIS
Creates a hook on a .NET method to intercept calls and modify behavior.

## SYNTAX

### Source (Default)
```
New-NetDetourHook [-Source] <ScriptBlock> [-Hook] <ScriptBlock> [-State <Object>] [-FindNonPublic]
 [-IgnoreConstructorNew] [-UseRunspace <UseRunspaceValue>] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

### Method
```
New-NetDetourHook [-Method] <MethodBase> [-Hook] <ScriptBlock> [-State <Object>]
 [-UseRunspace <UseRunspaceValue>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
The `New-NetDetourHook` cmdlet creates a runtime hook (detour) on a .NET method. When the hooked method is called, your hook scriptblock executes instead, allowing you to inspect arguments, modify behavior, and control the return value. You can optionally call the original method inside the hook using `$Detour.Invoke(...)`.

The method to hook can be specified in two ways:
- Using the `-Source` parameter with a scriptblock containing a method call expression
- Using the `-Method` parameter with a MethodBase object

The hook remains active until the returned NetDetourHook object is disposed. For automatic disposal, use the `Use-NetDetourContext` cmdlet instead.

Inside the hook scriptblock, you have access to:
- Method parameters as scriptblock parameters
- `$Detour.Instance` - The instance object (null for static methods)
- `$Detour.Invoke(...)` - Invokes the original method - arguments are based on the original method
- `$Detour.State` - The state object passed via `-State` parameter (null if not set)

Each hook is added as chain on the original method, if you create subsequent hooks for the same method it'll apply on top and `$Detour.Invoke(...)` will invoke the next hook in the chain.

Due to limitations in the underlying library managing the hooks, it is not possible to hook a method on a generic type like `[System.Collections.Generic.List[int]].Add([int])` or a generic method like `[Array]::Empty([int])`.

Hooking some methods might lead to unexpected crashes or hangs in the process. Common types used in background threads like `System.Text.StringBuilder` can be problematic if you hook methods on them.

## EXAMPLES

### Example 1: Hook a static method
```powershell
$hook = New-NetDetourHook -Source { [System.IO.Path]::GetTempPath() } -Hook {
    "C:\CustomTemp\"
}
try {
    [System.IO.Path]::GetTempPath()
} finally {
    $hook.Dispose()
}

# C:\CustomTemp\
```

This example hooks `GetTempPath()` to return a custom path. As the hook is created outside of `Use-NetDetourContext` it needs to be manually disposed to ensure the hook is no longer in active.

### Example 2: Hook with arguments
```powershell
Use-NetDetourContext {
    New-NetDetourHook -Source { [System.IO.Path]::Combine([string], [string]) } -Hook {
        param($path1, $path2)
        Write-Host "Combining: $path1 and $path2"
        $Detour.Invoke($path1, $path2)
    }
    [System.IO.Path]::Combine("C:\", "temp")
}

# Combining: C:\ and temp
# C:\temp
```

This example intercepts `Path.Combine()` to log the arguments before calling the original method. The hook is run inside `Use-NetDetourContext` which will automatically dispose of the hook once the ScriptBlock is finished.

### Example 3: Hook an instance method
```powershell
Use-NetDetourContext {
    New-NetDetourHook -Source { [System.DateTime].AddDays([double]) } -Hook {
        param($days)
        Write-Host "Instance: $($Detour.Instance.ToString('o'))"
        Write-Host "Adding $days days"
        $Detour.Invoke($days * 2)
    }

    $date = [DateTime]::new(2024, 1, 1)
    $date.AddDays(5).ToString("o")
}

# Instance: 2024-01-01T00:00:00.0000000
# Adding 5 days
# 2024-01-11T00:00:00.0000000
```

This example hooks the `AddDays()` method to double the number of days being added.

### Example 4: Use state to track calls
```powershell
$state = @{ Calls = 0 }
Use-NetDetourContext {
    New-NetDetourHook -Source { [System.Random].Next() } -Hook {
        $Detour.State.Calls++
        $Detour.Invoke()
    } -State $state

    $rnd = [System.Random]::new()
    $rnd.Next()
    $rnd.Next()
}
$state.Calls

# 2
```

This example uses the state parameter to count method invocations. The state value provided is accessible through the `$Detour.State` property.

### Example 5: Hook with $using: variables
```powershell
$prefix = "LOG: "
Use-NetDetourContext {
    New-NetDetourHook -Source { [Console]::WriteLine([string]) } -Hook {
        param($message)

        $Detour.Invoke($using:prefix + $message)
    }
    [Console]::WriteLine("Hello World")
}

# LOG: Hello World
```

This example uses `$using:` to capture variables from the calling scope. The variables are captured when the hook is created and is used as another way of providing data to the hook like the `-State` parameter.

### Example 6: Hook async methods
```powershell
Use-NetDetourContext {
    New-NetDetourHook -Source { [System.Net.Http.HttpClient].GetAsync([string]) } -Hook {
        param($uri)
        Write-Host "HTTP GET: $uri"

        $Detour.Invoke($uri)
    } -UseRunspace New

    $client = [System.Net.Http.HttpClient]::new()
    $task = $client.GetAsync("https://httpbin.org/get")
    while (-not $task.AsyncWaitHandle.WaitOne(300)) {}
    $response = $task.GetAwaiter().GetResult()
}

$response.StatusCode

# HTTP GET: https://httpbin.org/get
# OK
```

This example hooks the async `GetAsync` method on HttpClient using `-UseRunspace New` to handle the async callback context that will be run in another thread. In this case the original Task from `GetAsync()` will be returned back to PowerShell to then await.

### Example 7: Hook async methods and await the result
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

$response.StatusCode

# HTTP GET: https://httpbin.org/get
# Response: OK
# OK
```

This example hooks `GetAsync` and awaits the task result inside the hook using `GetAwaiter().GetResult()`, prints the status code, then returns the original task response back to the caller. It is possible to return a custom task result with a new Http response.

### Example 8: Hook using -Method parameter
```powershell
Use-NetDetourContext {
    $method = [System.IO.Path].GetMethod('GetTempPath', [Type[]]@())
    New-NetDetourHook -Method $method -Hook {
        "C:\CustomTemp\"
    }
    [System.IO.Path]::GetTempPath()
}

# C:\CustomTemp\
```

This example demonstrates using the `-Method` parameter with a MethodInfo object obtained through reflection, providing an alternative to the `-Source` parameter.

### Example 9: Hook methods with ref/out parameters
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

This example shows how to hook methods with `ref` or `out` parameters. In the `-Source` parameter, use `[ref][TypeName]` for ref/out parameters. In the hook, the parameter will be a reference that can be accessed via `.Value`. In this example the `ref` parameter is being updated after the original method is called.

## PARAMETERS

### -FindNonPublic
Allows the cmdlet to find and hook private, internal, or protected methods. By default, only public methods are searched. This is useful when you need to hook internal framework methods that aren't part of the public API. Using this switch will not be able to find non-public types, just methods on the types.

If the method to be hooked is not on a public type, the `-Method` parameter must be used instead of `-Source`.

```yaml
Type: SwitchParameter
Parameter Sets: Source
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Hook
The scriptblock that executes when the hooked method is called. This scriptblock should have parameters matching the method's signature (excluding the instance for instance methods) or no `param()` block at all to access the args through `$args`. Inside the hook, you can access:

- `$Detour.Instance` - The instance object (for instance methods)
- `$Detour.Invoke(...)` - Call the original method with modified or original arguments
- `$Detour.State` - The state object if provided via `-State`

The first output value in the hook will become the method's return value. If the hook produces no output for a method with a return type, the default value is returned (0 for int, null for reference types, etc.). Any remaining output will be discarded.

If the hook is run in the current runspace it will have access to the session state where the method was invoked. If using the hook inside `Use-NetDetourContext` the hook will also be able to write to the various streams. If the hook is run in a new runspace or runspace pool it will not have access to any variables or functions defined outside of the hook scriptblock. The `-State` parameter or `$using:var` syntax can be used to inject variables from when the hook was defined for these scenarios.

```yaml
Type: ScriptBlock
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IgnoreConstructorNew
When the `-Source` value is `{ [TypeName]::new() }` by default it will find the constructor for `TypeName`. This switch will change that logic to instead search for the static method on `TypeName` called `new`.

```yaml
Type: SwitchParameter
Parameter Sets: Source
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Method
A MethodBase object (MethodInfo or ConstructorInfo) representing the method to hook. This provides an alternative to the -Source parameter when you already have a reflection object. You can obtain MethodBase objects using reflection methods like `GetMethod()` or `GetConstructor()`.

```yaml
Type: MethodBase
Parameter Sets: Method
Aliases:

Required: True
Position: 0
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

### -Source
A scriptblock containing a method call expression that identifies the method to hook. The scriptblock must contain a single method call statement in one of these forms:

- Static method: `{ [InstanceType]::MethodName() }` or `{ [InstanceType]::MethodName([Type1], [Type2], ...) }`
- Instance method: `{ [InstanceType].MethodName() }` or `{ [InstanceType].MethodName([Type1], [Type2], ...) }`
- Constructor: `{ [InstanceType]::new() }` or `{ [InstanceType]::new([Type1], [Type2], ...) }`
- Property getter: `{ [InstanceType].get_PropertyName() }`
- Property setter: `{ [TyInstanceTypepe].set_PropertyName([PropertyType]) }`

The type arguments in parentheses are type constraints, not values and represent the types for each parameter. For ref/out parameters, use `[ref][TypeName]`. For pointer parameters like `int*` change `*` to `+`, `[int+]`.

Methods are first searched in a case sensitive way before falling back to searching for a case insensitive match.

```yaml
Type: ScriptBlock
Parameter Sets: Source
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -State
An object that will be available in the hook scriptblock via `$Detour.State`. This is useful for passing context or maintaining state between hook invocations. The state object is shared across all invocations of the hook, making it ideal for counters, caches, or configuration.

```yaml
Type: Object
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UseRunspace
Specifies which runspace to use when executing the hook scriptblock. This is critical for hooks that may be invoked from threads without an active runspace. Valid values are:

- `'Current'` (default): Use the runspace of the thread invoking the method. Fails if no runspace is active.
- `'New'`: Create a new dedicated Runspace for the hook (managed automatically).
- `'Pool'`: Create a new RunspacePool for the hook (managed automatically).
- A Runspace object: Use a specific runspace you manage.
- A RunspacePool object: Use a specific runspace pool you manage.

When using 'New' or 'Pool', the runspace is automatically disposed when the hook is disposed.

```yaml
Type: UseRunspaceValue
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

## OUTPUTS

### PSNetDetour.NetDetourHook
If not run inside `Use-NetDetourContext`, this cmdlet will output the `NetDetourHook` object representing the hook. The hook will stay alive until it is disposed.

## NOTES

## RELATED LINKS
