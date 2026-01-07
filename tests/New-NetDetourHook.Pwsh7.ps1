It "Has clean block" {
    {
        New-NetDetourHook -Source { clean {} } -Hook {}
    } | Should -Throw '*ScriptBlock must not contain a clean block.'
}

It "Uses null conditional with method call" {
    {
        New-NetDetourHook -Source { [Type]?.SomeMethod() } -Hook {}
    } | Should -Throw '*ScriptBlock method call must not be null-conditional.'
}

It "Uses generic type args" {
    {
        New-NetDetourHook -Source { [Type]::SomeMethod[int, Fake]() } -Hook {}
    } | Should -Throw '*ScriptBlock method call must not have generic type arguments.'
}
