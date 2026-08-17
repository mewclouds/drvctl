# Task 9: Offline Servicing Ablation & Self-Repair Empirical Study

## Overview

Task 9 is an empirical study of Windows Driver Store offline servicing invariants, ablation boundaries, and self-repair behavior. By systematically ablating individual registry values, subtrees, and reflected files from a known-good serviced image and testing offline servicing inspection (`dism /Get-Drivers`, `dism /Get-DriverInfo`) and duplicate package re-addition (`dism /Add-Driver`), this study determines which metadata fields are:
1. **Required persistent state**: Must be accurately synthesized or offline servicing fails.
2. **Reconstructible / repairable state**: Windows will self-heal or reconstruct upon duplicate add/servicing.
3. **Appears optional for offline servicing**: Package remains recognizable and servicing stays idempotent even when absent.
4. **Unknown**: Inconclusive due to test harness or analyzer limitations.

> **Important Interpretation Boundary:**
> This study targets the **Windows offline servicing contract** (`dism.exe` inspection and offline driver injection). It does not boot the WIM or test live kernel PnP runtime hardware binding. Conclusions apply strictly to offline image consistency and servicing compatibility.

---

## Methodology & Safety Protocol

1. **Isolation**: All tests operate on disposable copied WIMs in `C:\DrvCtlAblationStudy`. No live system registry or Driver Store modifications are performed.
2. **Immutability of Reference Images**: Protected reference WIMs (`install-original.wim`, `install-acpivpc.wim`, `install-dism.wim`) are verified against authoritative SHA-256 start hashes before and after all runs. They are never mounted read/write.
3. **Lossless Evidence**: Each experiment records:
   - `mutation.json`: Before/after state, binary hex/hashes of ablated targets.
   - `inspection.json`: DISM `/Get-Drivers` and `/Get-DriverInfo` outputs and exit codes on ablated image.
   - `repair.json`: DISM `/Add-Driver` output and exit codes on ablated image.
   - Structured logs for every native process invocation (command, arguments, timestamps, stdout, stderr, exit code).
   - Lossless publication diffs (`publication-analysis.json`) for:
     - `good-to-ablated`
     - `ablated-to-repaired`
     - `good-to-repaired`

---

## Experiment Index

| ID | Name | Target Hive / Path | Mutation Operation |
|---|---|---|---|
| `00-control` | Control Duplicate Add | Baseline `install-acpivpc.wim` | No ablation (control baseline) |
| `01-statusflags-delete` | StatusFlags | `SYSTEM` / `DriverDatabase\DriverPackages\<Pkg>\StatusFlags` | `delete-value` |
| `02-configflags-delete` | ConfigFlags | `SYSTEM` / `...\Configurations\<Config>\ConfigFlags` | `delete-value` |
| `03-configscope-delete` | ConfigScope | `SYSTEM` / `...\Configurations\<Config>\ConfigScope` | `delete-value` |
| `04-custom-property-delete` | Custom Property 0xFFFF0012 | `SYSTEM` / `...\Properties\{4da162c1-5eb1-4140-a444-5064c9814e76}\0009` | `delete-value` |
| `05-version-tail-zero` | Version Tail (Non-ACPIVPC) | `DRIVERS` / `DriverDatabase\DriverPackages\<Pkg>\Version` (tail 8 bytes) | `zero-tail-8` |
| `06-version-delete` | Complete Version | `SYSTEM` / `DriverDatabase\DriverPackages\<Pkg>\Version` | `delete-value` |
| `07-deviceid-delete` | DeviceIds Mapping Removal | `SYSTEM` / `DriverDatabase\DeviceIds\ACPI\VEN_VPC&DEV_2004\oem0.inf` | `delete-value` |
| `08-deviceid-zero` | DeviceIds Binary Zeroing | `SYSTEM` / `DriverDatabase\DeviceIds\ACPI\VEN_VPC&DEV_2004\oem0.inf` | `set-binary-zero` |
| `09-descriptors-delete` | Descriptors Subtree | `SYSTEM` / `...\Descriptors\ACPI\VEN_VPC&DEV_2004` | `delete-tree` |
| `10-strings-delete` | Strings Subtree | `SYSTEM` / `...\Strings` | `delete-tree` |
| `11-service-owners-delete` | Service Owners | `SYSTEM` / `ControlSet001\Services\ACPIVPC\Owners` | `delete-value` |
| `12-service-displayname-delete` | Service DisplayName | `SYSTEM` / `ControlSet001\Services\ACPIVPC\DisplayName` | `delete-value` |
| `13-pnplockdown-source-delete` | PnpLockdown Source | `SOFTWARE` / `...\PnpLockdownFiles\...\Source` | `delete-value` |
| `14-pnplockdown-owners-delete` | PnpLockdown Owners | `SOFTWARE` / `...\PnpLockdownFiles\...\Owners` | `delete-value` |
| `15-pnplockdown-class-delete` | PnpLockdown Class | `SOFTWARE` / `...\PnpLockdownFiles\...\Class` | `delete-value` |
| `16-pnplockdown-record-delete` | Complete PnpLockdown Record | `SOFTWARE` / `...\PnpLockdownFiles\%SystemRoot%/System32/drivers/AcpiVpc.sys` | `delete-tree` |
| `17-reflected-sys-delete` | Reflected Sys File | Filesystem: `Windows\System32\drivers\AcpiVpc.sys` | `delete-file` |

---

## Final Necessity Matrix

| Field | Experiment | Inspection recognizes package? | Duplicate Add-Driver exit code | OEM identity before | OEM identity after | Target state restored? | Restored bytes exact? | Unexpected semantic changes? | Necessity classification | Recommended strategy | Evidence path |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Control duplicate add | `00-control` | Yes | 0 | `oem0.inf` | `oem0.inf` | N/A (no ablation) | N/A (no ablation) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\00-control` |
| StatusFlags | `01-statusflags-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | No (remains absent) | N/A | No | Appears optional for offline servicing | Omit as unsupported (no general predictor; optional for offline servicing) | `C:\DrvCtlAblationStudy\01-statusflags-delete` |
| ConfigFlags | `02-configflags-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Yes (0x00000000) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\02-configflags-delete` |
| ConfigScope | `03-configscope-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Yes (0x00000F7F) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\03-configscope-delete` |
| custom DriverDatabase property 0xFFFF0012 | `04-custom-property-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | No (remains absent) | N/A | No | Appears optional for offline servicing | Omit from prototype only | `C:\DrvCtlAblationStudy\04-custom-property-delete` |
| Version tail | `05-version-tail-zero` | Yes | 0 | `oem4.inf` | `oem4.inf` | Unknown | Unknown | Unknown | Unknown | More research required | `C:\DrvCtlAblationStudy\05-version-tail-zero` |
| complete Version | `06-version-delete` | No (exit 1168) | 1168 | `oem0.inf` | N/A (failed) | No | No | No (failed cleanly) | Required persistent state | Implement encoder | `C:\DrvCtlAblationStudy\06-version-delete` |
| DeviceIds mapping removal | `07-deviceid-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | No (remains absent) | N/A | No | Appears optional for offline servicing | Omit as unsupported (no general encoder; unproven for PnP) | `C:\DrvCtlAblationStudy\07-deviceid-delete` |
| DeviceIds zero/corruption | `08-deviceid-zero` | Yes | 0 | `oem0.inf` | `oem0.inf` | No (remains zeroed) | N/A | No | Appears optional for offline servicing | Omit as unsupported (no general encoder; unproven for PnP) | `C:\DrvCtlAblationStudy\08-deviceid-zero` |
| Descriptors | `09-descriptors-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Yes (all 3 values) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\09-descriptors-delete` |
| Strings | `10-strings-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Yes (all string values) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\10-strings-delete` |
| service Owners | `11-service-owners-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Semantic match (MULTI_SZ) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\11-service-owners-delete` |
| service DisplayName | `12-service-displayname-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Yes (exact string) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\12-service-displayname-delete` |
| PnpLockdown Source | `13-pnplockdown-source-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Yes (exact path) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\13-pnplockdown-source-delete` |
| PnpLockdown Owners | `14-pnplockdown-owners-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Semantic match (MULTI_SZ) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\14-pnplockdown-owners-delete` |
| PnpLockdown Class | `15-pnplockdown-class-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Yes (0x00000005) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\15-pnplockdown-class-delete` |
| complete PnpLockdown record | `16-pnplockdown-record-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Semantic match (key + 3 values) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\16-pnplockdown-record-delete` |
| reflected AcpiVpc.sys | `17-reflected-sys-delete` | Yes | 0 | `oem0.inf` | `oem0.inf` | Yes | Yes (exact SHA256 match) | No | Reconstructible / repairable state | Use existing solved predictor | `C:\DrvCtlAblationStudy\17-reflected-sys-delete` |

---

## Detailed Findings on Key Experiments

### Experiment 05: Version Tail Zeroing (`05-version-tail-zero`)
- Target: 8-byte tail of the `Version` binary value in `DRIVERS` hive for `a-volutenhapo4swc.inf_amd64_b6d9d50049bf9522`.
- Servicing result: DISM `/Get-Drivers` listed `oem4.inf`, `/Get-DriverInfo` recognized `oem4.inf` (exit code 0), and `/Add-Driver` completed successfully (exit code 0).
- Analysis result: `drvctl analyze-publication` threw `NotSupportedException: Destination directory ID 11 is not yet supported` because `a-volutenhapo4swc.inf` is a software component (SWC) package referencing `DIRID 11` (`%SystemRoot%\System32`).
- Classification: **Unknown / Inconclusive due to analyzer limitation**. This is an analyzer diagnostic limitation, not a Windows servicing failure.

### Experiment 06: Complete Version Deletion (`06-version-delete`)
- Target: Complete `Version` value under `SYSTEM\DriverDatabase\DriverPackages\acpivpc.inf_amd64_fd0a5766a43dadc1`.
- Inspection result: `/Get-Drivers` listed `oem0.inf`, but `/Get-DriverInfo /Driver:oem0.inf` failed with exit code 1168 (`Element not found`).
- Repair result: `/Add-Driver` failed with exit code 1168 (`Element not found`).
- Classification: **Required persistent state**. The `Version` binary value is non-negotiable for Windows offline driver recognition and duplicate package handling.

### Experiment 17: Reflected File Deletion (`17-reflected-sys-delete`)
- Target: `Windows\System32\drivers\AcpiVpc.sys` was removed while the Driver Store FileRepository copy remained intact.
- Inspection result: Package `oem0.inf` remained fully recognizable by DISM.
- Repair result: Duplicate `/Add-Driver` ran cleanly (exit code 0), recognized existing `oem0.inf`, and **recopied `AcpiVpc.sys`** to `Windows\System32\drivers` with an identical SHA-256 hash (`6BC61AF7806A6482754AF7757AE7DD013D345BCC92E18901F6474FEFC3E07C8D`). Zero duplicate packages or FileRepository folders were created.
- Classification: **Reconstructible / repairable state**.

---

## Answers to Core Research Questions

1. **Does deleting `StatusFlags` break servicing recognition?** No. `/Get-DriverInfo` exits with code 0 and recognizes `oem0.inf`.
2. **Does Windows reconstruct `StatusFlags`?** No. `StatusFlags` remains absent after duplicate Add-Driver.
3. **Does deleting `ConfigFlags` break recognition?** No.
4. **Does Windows reconstruct `ConfigFlags`?** Yes, exact DWORD `0x00000000` is restored.
5. **Does deleting `ConfigScope` break recognition?** No.
6. **Does Windows reconstruct `ConfigScope`?** Yes, exact DWORD `0x00000F7F` is restored.
7. **Does deleting the ACPIVPC custom `0xFFFF0012` property break recognition?** No.
8. **Does Windows reconstruct that property?** No, the custom property remains deleted.
9. **What is the result of Version-tail ablation?** Servicing inspection and repair succeeded (exit code 0), but full diff analysis was inconclusive due to analyzer DIRID 11 limitation.
10. **Does deleting the entire `Version` value break duplicate re-add?** Yes. DISM exits with code 1168 (`Element not found`).
11. **Does deleting DeviceIds mapping break recognition?** No, offline package inspection via published name still succeeds.
12. **Does Windows reconstruct DeviceIds?** No, duplicate Add-Driver does not recreate the DeviceIds mapping for an already staged package.
13. **What happens when DeviceIds data is zeroed?** Inspection succeeds, but the corrupted mapping remains zeroed.
14. **Are Descriptors regenerated?** Yes, the entire `Descriptors` subtree (`Configuration`, `Description`, `Manufacturer`) is regenerated.
15. **Are Strings regenerated?** Yes, the entire `Strings` subtree is regenerated.
16. **Is service `Owners` regenerated?** Yes, reconstructed as a `MULTI_SZ` containing `oem0.inf`.
17. **Is service `DisplayName` regenerated?** Yes, exact string restored.
18. **Is PnpLockdown `Source` regenerated?** Yes, exact path restored.
19. **Is PnpLockdown `Owners` regenerated?** Yes, reconstructed as a `MULTI_SZ`.
20. **Is PnpLockdown `Class` regenerated?** Yes, exact DWORD `5` restored.
21. **Is the complete PnpLockdown record regenerated?** Yes, key and all 3 values (`Source`, `Owners`, `Class`) are recreated.
22. **Is `AcpiVpc.sys` recopied after reflected-file deletion?** Yes, exact file restored to `Windows\System32\drivers`.
23. **Did any ablation cause a new OEM INF allocation?** No. All successful repairs reused existing `oem0.inf` (or `oem4.inf`).
24. **Did any ablation cause a second FileRepository directory?** No.
25. **Did any ablation cause a second DriverPackages record?** No.
26. **Which fields appear required for offline servicing?** `complete Version` in `DriverPackages`.
27. **Which fields are repairable?** `ConfigFlags`, `ConfigScope`, `Descriptors`, `Strings`, service `Owners`, service `DisplayName`, `PnpLockdownFiles` (`Source`, `Owners`, `Class`), and reflected driver binaries (`AcpiVpc.sys`).
28. **Which fields appear optional for offline servicing?** `StatusFlags`, custom DriverDatabase property `0xFFFF0012`, and `DeviceIds` mapping (for offline package inspection).
29. **Which results remain unknown?** `Version tail` (Experiment 05) due to analyzer DIRID 11 handling.
30. **Does Task 9 reduce the set of fields drvctl must independently synthesize?** Yes. It demonstrates that many auxiliary database subtrees and reflected files are repairable/reconstructible by Windows, while identifying `Version` as the critical persistent anchor.

---

## Running the Study Scripts

To aggregate existing results and output `ablation-study.json`:
```powershell
pwsh -ExecutionPolicy Bypass -File .\Research\Task9\Summarize-AblationStudy.ps1
```

To run a specific experiment (e.g. Experiment 17) in resume mode:
```powershell
pwsh -ExecutionPolicy Bypass -File .\Research\Task9\Run-AblationStudy.ps1 -ExperimentIds "17-reflected-sys-delete" -Resume
```
