# Changelog for PSNetDetour

## v0.1.1 - TBD

* Use `[NullString]::Value` for string parameters in a hook when `$null` was provided
  * This avoids PowerShell converting the null value to `""`
* Bumped `MonoMod.RuntimeDetour` to `25.3.4` which adds support for arm64 on Windows

## v0.1.0 - 2026-01-09

* Initial version of the `PSNetDetour` module
