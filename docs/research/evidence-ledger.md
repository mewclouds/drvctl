# Evidence ledger

This file records the findings that future work should not have to rediscover.

## Driver export discovery

**Status:** Solved for the tested export path

Windows APIs can resolve the relevant Driver Store package locations directly.

## Direct WIM mutation

**Status:** Proven

libwim successfully created a modified copied WIM used in later Windows servicing tests.

## Offreg offline mutation

**Status:** Proven

Offline SYSTEM and SOFTWARE state has been modified and written back without loading it into live HKLM.

## ACPIVPC recognition

**Status:** Proven

Windows offline servicing recognized the drvctl-created ACPIVPC publication before repair.

Do not infer PnP correctness.

## Duplicate ACPIVPC add

**Status:** Proven

DISM reused the existing publication identity instead of creating a duplicate package.

## DriverPackages Version

**Status:** Required for tested ACPIVPC offline servicing

Deleting the complete Version value caused error 1168 during package inspection and duplicate servicing.

## Version core

**Status:** Solved across the observed 67-package corpus

The first 40 bytes were predicted across all observed packages.

## Version tail

**Status:** Partially understood

Several tail forms exist.

ACPIVPC uses an 8-byte zero tail.

No general tail rule is claimed.

## Signer metadata

**Status:** Solved in the observed corpus

SignerName and SignerScore can be obtained through `SetupVerifyInfFileW`.

## ConfigScope

**Status:** PrototypeSupported

The observed configuration set used `0x00000F7F`.

The bit meanings are not solved.

## ConfigFlags

**Status:** Unsupported as a general rule

Task 9 showed Windows can reconstruct ACPIVPC ConfigFlags.

That does not tell us how to choose the value for arbitrary packages.

## StatusFlags

**Status:** Unsupported

Many values occur across the corpus.

No general encoder has been established.

## DeviceIds

**Status:** Unsupported as a general encoder

Candidate rules had many counterexamples.

ACPIVPC recognition survived deletion, but PnP matching remains unproven.

## Custom DriverDatabase property 0xFFFF0012

**Status:** Unsupported

ACPIVPC recognition survived without it.

The property's meaning remains unknown.

## Descriptors and Strings

**Status:** Supported for the tested ACPIVPC shape

They can be derived from INF semantics in the supported path.

## Service ownership

**Status:** Mixed

Dedicated single-owner services are understood much better than shared services.

## PnpLockdownFiles

**Status:** Strongly supported for the observed simple cases

Source and single-owner Owners behavior are understood.

Class handling remains narrow and should not be treated as universal.

## OEM INF allocation

**Status:** Strongly observed

Clean-baseline experiments allocate from the lowest available OEM INF slot.

Gap reuse was observed.

## OemInfMap

**Status:** Strongly observed for tested indices

Observed values behave like an occupancy bitmap.

## SYSTEM vs DRIVERS DriverDatabase placement

**Status:** Observed by package family

System and Extension examples used SYSTEM.

SoftwareComponent examples used DRIVERS in the studied fixtures.

Do not call this universal until broader compatibility evidence supports it.

## Recognition despite semantic mismatch

**Status:** Observed in Task 12

At least one A-Volute Nahimic SoftwareComponent package was recognized by Windows while the current comparison layer reported many semantic differences.

The current Task 12 classifier is heuristic, so exact mismatch counts require audit.
