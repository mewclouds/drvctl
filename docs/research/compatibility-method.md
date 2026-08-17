# Compatibility method

Task 12 is meant to answer one question:

How much of the exported package set can the current frozen implementation handle?

## Per package

Each package gets two fresh baseline copies.

One is serviced by drvctl.

One is serviced by DISM.

Then the harness checks:

- drvctl execution
- DISM reference execution
- Windows recognition
- semantic differences

## Why one package at a time

This removes package-order effects.

If package 20 fails, we want to know it failed because of package 20, not because package 19 changed OEM allocation or shared service state.

## What the final matrix should contain

For every package:

- identity
- class/provider/version
- drvctl result
- DISM result
- recognition result
- analyzer result
- raw contradiction details
- omission details
- failure stage
- failure fingerprint
- timing

## Post-sweep audit

Before using compatibility percentages:

1. verify analyzer exit codes
2. verify reports were freshly generated
3. remove hardcoded exact-match counts
4. tighten omission rules
5. enforce identity/provider/class/version recognition checks
6. regroup failures by actual paths or mechanisms

The saved raw artifacts mean this can be done without rerunning the full sweep in most cases.
