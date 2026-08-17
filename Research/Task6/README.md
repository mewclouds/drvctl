# Task 6 publication rule study

This research-only harness uses DISM against copies of a baseline WIM and then
uses `drvctl analyze-publication` for lossless comparison. It is deliberately
outside the production CLI and does not add a runtime servicing dependency.

Run from an elevated PowerShell session:

```powershell
pwsh -NoProfile -File .\Research\Task6\Run-PublicationRuleStudy.ps1
```

Use `-Resume` after an interruption. Existing fixtures and reports are reused;
new operations continue in their own experiment directories. The harness
records and rechecks the original baseline SHA-256 throughout the study.

Every servicing subprocess is launched through `System.Diagnostics.Process`
with `ProcessStartInfo.ArgumentList`. The harness checks the process object's
exit code and stops on failures. DISM is used only by this research script.
