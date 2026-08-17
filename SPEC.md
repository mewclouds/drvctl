# SPEC.md

## Purpose

drvctl is a Windows driver utility built around the idea that not every driver-store operation should require the full DISM servicing stack.

The project has two main goals.

### Goal 1: fast read-only driver tooling

This includes:

- listing third-party driver packages
- locating package contents in Driver Store
- exporting packages
- inspecting INF/package metadata
- comparing packages and WIM state
- verifying export output

This is the most mature part of the project.

### Goal 2: direct offline driver publication

This is research.

The current prototype can directly mutate a copied WIM with libwim and mutate offline hives with Offreg.

ACPIVPC has been published this way and Windows offline servicing recognized it before DISM repair.

That proves the architecture can work.

It does not prove universal driver injection.

## Core components

### SetupAPI

Use Windows-native INF/package interpretation wherever possible.

Do not replace SetupAPI with a general homegrown INF parser unless there is a specific gap that cannot be handled through the API.

### Offreg

Used for offline registry reads and writes.

The live registry should not be used as a staging area for offline-image work.

### libwim

Used for WIM inspection and direct targeted mutation.

The direct path avoids DISM mounting.

### DISM

DISM is the reference implementation.

Use it to:

- create fixtures
- validate recognition
- compare behavior
- benchmark

Do not make it a hidden runtime dependency of direct publication.

## Export behavior

The export path resolves installed third-party packages and copies their package trees from Driver Store.

Earlier tests compared drvctl output with DISM by:

- relative path
- file size
- SHA-256

The tested outputs matched.

## Publication behavior

A complete driver publication can involve more than copying files.

Known areas include:

- FileRepository package files
- `Windows\INF\oemN.inf`
- catalog publication
- DriverDatabase
- services
- reflected files
- PnpLockdownFiles
- INF-authored registry/configuration behavior
- package-specific vendor state

The current planner should only generate state we can justify.

## Evidence status in plans

Publication operations should carry a status like:

- Solved
- PrototypeSupported
- Unsupported
- OmittedByPolicy

Unsupported state must not silently become a write.

## Current supported claims

We can say:

- drvctl can export third-party packages without using DISM for discovery
- direct libwim mutation works
- offline registry mutation through Offreg works
- ACPIVPC can be directly published into a copied WIM
- Windows offline servicing recognizes that ACPIVPC publication before repair
- the current direct multi-package path is much faster than DISM in the performance-only benchmark

We cannot say:

- drvctl is a general `/Add-Driver` replacement
- every exported driver is semantically correct
- PnP correctness is proven
- DeviceIds encoding is solved
- every DriverDatabase field is understood
- all vendor-specific INF effects are implemented
