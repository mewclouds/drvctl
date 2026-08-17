# Open questions

These are the questions that still matter.

## Compatibility

- How many of the 67 packages are recognized by Windows?
- How many have real semantic contradictions after the Task 12 audit?
- Which contradiction families repeat?

## DeviceIds

- What do the observed four-byte values encode?
- Which parts relate to rank, match type, or package class?
- What is needed for real PnP matching?

## Version tail

- What selects the nonzero tail bits?
- Is the rule exposed through SetupAPI or another supported structure?

## SoftwareComponent packages

- Which registry differences come from standard INF behavior?
- Which are vendor-specific?
- Which are required for runtime behavior?

## Shared services

- How are Owners merged and ordered?
- What happens when several packages update the same service?

## PnP

- Which offline fields are required before first boot?
- Which state can Windows reconstruct?
- What must exist before hardware enumeration?

## Product direction

- Which read-only DISM workflows can drvctl replace cleanly now?
- Which injection package families are safe enough to support explicitly?
