# Current state

## What is already solid

The export side is the strongest part of drvctl.

Native Windows APIs can resolve installed third-party driver packages quickly, and the package trees can be copied directly from Driver Store.

Earlier verification showed matching package trees against DISM reference output.

## What changed with Tasks 9 to 11

The project crossed an important line.

It is no longer only simulating publication state.

drvctl now has a direct WIM path using libwim and Offreg.

For ACPIVPC, drvctl created the package state inside a copied WIM and Windows offline servicing recognized it before DISM repaired anything.

That proves the basic publication architecture is real.

## ACPIVPC result

Windows reported the expected package identity, provider, class, version, hardware ID, and service.

A duplicate DISM add reused the existing OEM INF and FileRepository identity.

The duplicate add filled in some state that drvctl intentionally omitted.

## Performance

### Single package

The validated ACPIVPC experiment showed a large speed advantage over DISM's mount, service, and commit workflow.

### Full exported set

A later performance-only benchmark processed all 67 exported packages.

Median times on the test machine were roughly:

- drvctl: 68 seconds
- DISM: 400 seconds

The benchmark was intentionally not a correctness test.

## Task 12

Task 12 tests each package independently.

Every package gets:

- a fresh drvctl WIM
- a fresh DISM reference WIM
- Windows recognition checks
- semantic comparison
- a structured package result

This is the first broad compatibility map.

Early results already show an important pattern:

A package can be recognized by Windows even when drvctl does not reproduce all DISM semantics.

That means "recognized" and "correctly configured" must stay separate.

## Important warning about current Task 12 labels

The current harness classification should be treated as provisional.

The raw reports are valuable, but the summary logic needs a review after the sweep.

Do not publish compatibility percentages until that audit is complete.
