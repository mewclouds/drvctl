# Benchmarks

Benchmarks are useful, but only when the scope is stated clearly.

## Driver export

Earlier testing showed a large difference between DISM export and the drvctl direct export path.

A representative test was roughly:

- DISM: about 40 seconds
- drvctl proof of concept: about 2 seconds

The important point is architectural.

Export is mostly discovery and copying. The full servicing stack is unnecessary for that work.

## Single-package direct WIM publication

ACPIVPC was used for the validated direct-publication benchmark.

Median results were roughly:

- drvctl mutation: 8.7 seconds
- DISM servicing: 121 seconds

The outputs passed the validity gates used in that experiment.

## Full 67-package throughput benchmark

A later benchmark processed the entire exported package directory.

Median results were roughly:

- drvctl: 67.8 seconds
- DISM: 400.1 seconds

That benchmark was explicitly performance-only.

The drvctl output was not validated for correctness.

Do not combine the speed result with a claim that all 67 packages are correct.

## Benchmark language

Good:

"drvctl processed the 67-package set about 5.9x faster in the performance-only test."

Bad:

"drvctl replaces DISM for all 67 packages."

The second claim requires compatibility evidence, not timing.
