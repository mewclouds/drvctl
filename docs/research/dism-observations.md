# DISM observations

DISM is slow, but driver injection has earned some sympathy.

The experiments show that Add-Driver can touch much more than package files.

Depending on the package, servicing may involve:

- Driver Store publication
- OEM INF publication
- catalogs
- DriverDatabase
- services
- reflected binaries
- PnpLockdownFiles
- vendor registry state
- package-specific configuration

That complexity helps explain why Add-Driver is hard to replace correctly.

Export is different.

Read-only discovery and copying should not need most of this machinery, which is why the direct export path is such a strong target for replacement.
