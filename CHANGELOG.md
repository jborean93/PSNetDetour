# Changelog for PSNetDetour

## v0.1.1 - TBD

* Use `[NullString]::Value` for string parameters in a hook when `$null` was provided
  * This avoids PowerShell converting the null value to `""`

## v0.1.0 - 2026-01-09

* Initial version of the `PSNetDetour` module
