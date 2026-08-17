# Driver publication

Driver publication is much harder than export.

Copying package files is only one part of the job.

## State seen in experiments

Depending on the package, DISM may create or modify:

- FileRepository package files
- published OEM INF
- catalog state
- DriverDatabase package records
- DriverDatabase device mappings
- configuration records
- services
- reflected files outside FileRepository
- PnpLockdownFiles
- vendor-specific registry state
- other INF-driven configuration

Different driver classes do not all follow the same path.

## Current model

The publication planner produces the state drvctl understands.

The executor applies only supported operations.

Unknown fields stay explicit.

## Why recognition can still work with mismatches

Windows offline servicing does not appear to require every DISM-generated field just to recognize a published package.

Task 9 proved this for ACPIVPC through ablation.

Task 12 has already shown at least one complex package that Windows recognizes even though semantic comparison reports many differences.

That is useful, but it does not mean those differences are safe to ignore.

They may matter later during install, service activation, vendor software setup, or PnP.
