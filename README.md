# PSNetDetour

[![Test workflow](https://github.com/jborean93/PSNetDetour/workflows/Test%20PSNetDetour/badge.svg)](https://github.com/jborean93/PSNetDetour/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/jborean93/PSNetDetour/branch/main/graph/badge.svg?token=b51IOhpLfQ)](https://codecov.io/gh/jborean93/PSNetDetour)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/dt/PSNetDetour.svg)](https://www.powershellgallery.com/packages/PSNetDetour)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/jborean93/PSNetDetour/blob/main/LICENSE)

Hook .NET functions in PowerShell.
See [about_PSNetDetour](docs/en-US/about_PSNetDetour.md) for more details.

## Documentation

Documentation for this module and details on the cmdlets included can be found [here](docs/en-US/PSNetDetour.md).

## Requirements

These cmdlets have the following requirements

* PowerShell 5.1, or v7.4+

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
