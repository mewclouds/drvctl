# Research methodology

This project is controlled trial and error.

The goal is not to make DISM diffs disappear.

The goal is to understand which behavior is required and why.

## Basic loop

1. observe DISM
2. isolate one question
3. change one variable
4. compare
5. keep the raw evidence
6. reject bad ideas quickly
7. generalize only after repeated support

## Good experiment

A good experiment can answer one sentence.

Example:

"Does deleting DriverPackages Version break duplicate servicing?"

Task 9 answered yes for ACPIVPC.

## Bad experiment

A bad experiment changes five registry trees at once and then guesses which one mattered.

## Reference fixtures

Every package-specific compatibility test should start from the same baseline.

Do not let package ordering contaminate a single-package experiment.

## Failed tests are useful

If five packages fail with the same fingerprint, that is often more valuable than fifty unrelated passes.

The compatibility matrix exists to find those families.
