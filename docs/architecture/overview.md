# Architecture overview

drvctl is built around a small set of native primitives instead of one large servicing dependency.

## Live driver-store side

SetupAPI is used to discover and understand installed driver packages.

The export path then copies package contents directly.

## Offline image side

libwim handles the WIM container.

Offreg handles offline registry hives.

The publication planner sits above both.

The planner decides what should exist.

The WIM layer should not reinterpret INF semantics.

## Research layer

Research harnesses create disposable fixtures, run DISM references, compare outputs, and save reports.

Research code can be more experimental than the normal CLI, but it should not quietly change production semantics.

## Why the separation matters

The project is trying to learn what Windows servicing actually requires.

If WIM code, INF parsing, registry logic, and comparison logic all guess independently, the experiments become impossible to trust.

One source of semantics is better than four.
