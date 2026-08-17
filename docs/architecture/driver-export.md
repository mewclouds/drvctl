# Driver export

Driver export is mostly a discovery and copy problem.

The key steps are:

1. enumerate published third-party INFs
2. resolve each package to its Driver Store location
3. deduplicate package directories
4. copy the package tree

The important observation is that Windows already exposes native APIs for the discovery part.

There is no need to invoke the full DISM servicing stack just to find the package files.

## Verification

The tested verification path compared drvctl and DISM export results by:

- path
- size
- SHA-256

The tested package trees matched.

## Why export matters

Export is a good example of where drvctl can provide immediate value without solving offline servicing.

The direct path is simpler, easier to reason about, and much faster in the measured tests.
