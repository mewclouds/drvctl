# Task 1

## Goal

Build the native plumbing needed for later research.

## Result

Added:

- direct libwim inspection
- Offreg safe-handle wrappers
- SetupAPI INF inspection

No mutation was performed.

## Important lesson

Native AOT interop needed careful handle ownership and ABI handling. This became the base for everything later.
