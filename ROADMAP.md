# ROADMAP.md

## Now

Finish Task 12.

Do not change publication behavior until the one-at-a-time compatibility sweep is done.

The important output is:

`package-compatibility.json`

The current sweep should tell us:

- what drvctl can already publish
- what Windows recognizes
- where semantic comparison diverges
- which failure patterns repeat

## Immediately after Task 12

### Audit the harness before trusting percentages

The raw package data is useful, but the current classification layer needs review.

Before saying "X out of 67 pass":

- check analyzer exit handling
- replace hardcoded exact-match counts
- tighten known-omission rules
- separate unsupported packages from crashes
- group failures by actual mechanism
- verify recognition identity, provider, class, and version comparisons

Then regenerate the compatibility summary from the saved package artifacts.

### Freeze the current research state

Once the audit is done:

- update these docs
- archive the final compatibility JSON/CSV
- document recurring failure families
- document the current supported package shapes

## Next research phase

Pick the highest-value failure family from the audited compatibility matrix.

Good priorities are:

1. failures that prevent Windows recognition
2. one missing rule affecting many packages
3. shared behavior across a driver class
4. behavior needed for eventual PnP correctness

Do not pick the next task because one package looks interesting if another failure affects ten packages.

## Likely future families

These are possibilities, not commitments:

- richer SoftwareComponent registry behavior
- AddReg and vendor configuration
- shared service ownership
- Version-tail variants
- DeviceIds
- unsupported destination directory behavior
- localized metadata
- extension/component-specific state

Task 12 decides which ones actually matter.

## PnP phase

PnP testing comes after offline servicing behavior is stable enough to make the result meaningful.

This phase needs to answer:

- what DeviceIds really encode
- whether hardware matching works
- whether services and dependencies activate correctly
- whether a booted image behaves like the DISM-serviced reference

## Product work can happen in parallel

The read-only side does not need to wait for injection research.

Good product-facing targets:

- list
- export
- verify
- inspect INF
- inspect WIM
- compare package state
- benchmark
- driver-store diagnostics

## Long-term direction

Use native Windows APIs and libwim for the parts we understand.

Keep DISM as the reference for the parts we still need to study.

Replace it where the servicing stack adds cost without adding value.
