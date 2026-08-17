# AGENTS.md

Read this before changing drvctl.

This project is partly a normal Windows utility and partly reverse-engineering research. The read-only side is much more mature than driver injection.

## The rule that matters most

Do not guess Windows servicing behavior.

If we do not know why a value exists, how it is derived, or whether a rule generalizes, keep it marked unknown or unsupported.

Do not copy bytes from a DISM-generated fixture into implementation code just because it makes a diff disappear.

## Read these first

Before changing publication or WIM code:

1. `SPEC.md`
2. `ROADMAP.md`
3. `docs/current-state.md`
4. `docs/evidence-ledger.md`
5. `docs/known-limitations.md`
6. `docs/architecture/driver-publication.md`
7. `docs/research/compatibility-method.md`
8. the latest experiment notes
9. `package-compatibility.json` once Task 12 finishes

Then inspect the actual repository. These Phase A docs are based on the experiment history, not a fresh code audit.

## Evidence labels

Use these words consistently.

- **Solved**: deterministic rule with strong validation
- **PrototypeSupported**: works for a narrow tested case
- **Observed**: seen in one or more experiments, rule still unknown
- **Hypothesis**: plausible explanation that needs testing
- **Unsupported**: behavior exists but drvctl does not know how to reproduce it
- **Disproven**: tested idea did not hold

Do not call something solved because it worked once.

## Keep the milestones separate

A driver can pass one layer and fail the next.

1. WIM can be opened
2. package files and registry state exist
3. Windows offline servicing recognizes the package
4. drvctl matches DISM semantics
5. PnP can match/install it
6. the driver works after boot

Recognition is not PnP correctness.

A package with semantic mismatches can still be recognized by Windows.

## Fixture safety

Never mutate the canonical baseline or reference WIMs in place.

Do not modify:

- live Driver Store
- live CatRoot
- live registry for offline experiments
- source driver export under `C:\Drivers`

Use copied WIMs and disposable workspaces.

Use Offreg for offline hive work.

Use libwim for direct WIM access.

DISM is allowed in research as a reference, validator, and benchmark target.

## Compatibility sweeps

Do not fix publication logic while a compatibility sweep is running.

Record the failure and continue.

A harness bug may be fixed if it does not change publication semantics. Record the fix and rerun only affected packages.

Task 12 exists to map the current implementation, not improve it mid-run.

## Review before trusting reports

Do not trust a summary count without checking how it was produced.

The current Task 12 harness has a few known issues that require a post-sweep audit:

- semantic mismatch classification is heuristic
- some omission categories are too broad
- exact-match count is hardcoded in the current harness
- analyzer exit status needs stronger handling
- failure fingerprints group by contradiction count rather than mechanism
- unsupported package handling needs a proper distinction from execution failure

The raw outputs are still useful. Reclassify them after the sweep.

## When research changes our understanding

Update:

- `docs/evidence-ledger.md`
- `docs/known-limitations.md`
- the experiment note
- `ROADMAP.md` if the next target changes

Do not erase old wrong ideas. Mark them disproven.

## Phase B documentation pass

The next code-aware pass should verify:

- current repository layout
- current command names and help text
- class and file names
- build and publish commands
- current report schemas
- current test commands
- which prototype rules are actually enabled

Fix stale names and paths without weakening the evidence boundaries in these docs.
