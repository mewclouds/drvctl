# Task 12

## Status

In progress when this documentation pack was generated.

## Goal

Test every package independently from a fresh baseline.

For each package:

1. drvctl publishes it
2. DISM creates a separate reference
3. Windows recognition is checked
4. semantic comparison is run
5. the package result is saved

## Important warning

The current harness raw data is useful, but its classification layer needs a post-sweep audit.

Do not treat current mismatch counts or compatibility percentages as final until that review is done.
