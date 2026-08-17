# Task 11 — Direct Copied-WIM Publication & Servicing Benchmark

## Overview

Task 11 implements and evaluates the first direct publication of a driver package (`ACPIVPC`) into a copied Windows WIM without using DISM to create the publication state.

The publication path is:
1. `install-original.wim` copied to output working WIM.
2. Extracted registry hives (`SYSTEM`, `SOFTWARE`, `DRIVERS`) mutated via Offreg using the Task 10 verified publication plan.
3. Package files (`FileRepository`, `Windows\INF\oem0.inf`, `CatRoot\oem0.cat`, `drivers\AcpiVpc.sys`) and mutated registry hives injected via direct `libwim-15.dll` API (`wimlib_add_tree` and `wimlib_overwrite`).
4. Output WIM verified internally (`SelfVerification`) and through DISM recognition oracle (`/Get-Drivers`, `/Get-DriverInfo`).

## Benchmark Methodology

- **Comparators**: `drvctl prototype-inject-wim` (Native AOT) vs `DISM /Mount-Image` + `/Add-Driver` + `/Unmount-Image /Commit`.
- **Baseline**: `install-original.wim` (Index 1).
- **Package**: `C:\Drivers\acpivpc.inf_amd64_fd0a5766a43dadc1` (ACPIVPC).
- **Run Protocol**: 1 warm-up pair followed by 5 measured pairs executed in alternating order (Pair 1: drvctl then DISM, Pair 2: DISM then drvctl, etc.).
- **Timing Breakdowns**:
  - `BaselineCopyMs`: Fresh copy of baseline WIM.
  - `MutationOrServicingMs`: Direct WIM update operations vs DISM mount + add + commit unmount.
  - `EndToEndMs`: Sum of baseline copy and mutation/servicing time.
- **Validity Gate**: All measured runs must satisfy self-verification / DISM recognition before speedup calculations are published.
