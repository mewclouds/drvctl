# Known limitations

## Task 12 classification logic

The current sweep collects good raw evidence, but the summary logic needs an audit.

Known issues include:

- exact-match count is currently hardcoded
- analyzer exit status is not strong enough in the classification path
- known omission matching is broad
- unsupported package failures are not separated cleanly
- failure grouping uses contradiction counts instead of root mechanism
- recognition identity checks are recorded but not fully enforced in status

Treat current overall statuses as provisional until reclassification.

## DeviceIds

General encoding is not solved.

This blocks a broad PnP correctness claim.

## Version tails

The common Version core is understood.

Tail variants are not universally derived.

## Vendor-specific INF behavior

Complex SoftwareComponent packages can create large registry differences.

Task 12 should tell us which behaviors repeat.

## Shared services

Shared ownership and update behavior remain unresolved.

## PnP

Offline servicing recognition is not hardware matching.

Boot/runtime validation remains future work.

## Multi-package correctness

The 67-package direct path was benchmarked for throughput.

That benchmark intentionally skipped correctness validation.
