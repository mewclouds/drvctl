# Task 11

## Goal

Perform the first direct copied-WIM publication and ask Windows whether it recognizes the result.

## Result

Success.

drvctl used libwim and Offreg to create the ACPIVPC publication without DISM creating the state.

DISM later recognized the package before repair.

A duplicate add reused the existing publication identity.

## Benchmark

The validated single-package direct path was much faster than DISM.

A separate 67-package performance-only benchmark also showed a large speed advantage, but did not validate the full output.
