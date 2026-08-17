# Task 8 encoding study

`Run-EncodingStudy.ps1` mines the existing lossless 67-package analysis and
uses `drvctl inspect-inf` plus `SetupVerifyInfFileW` to predict values from
package inputs. It does not service images or write publication state.

The reference registry bytes are comparison outputs only. Predictions are
computed first from INF, package, and signature inputs.

```powershell
pwsh -NoProfile -File .\Research\Task8\Run-EncodingStudy.ps1
pwsh -NoProfile -File .\Research\Task8\Test-EncodingStudy.ps1
```

The test script enforces only encodings classified as solved or explicitly
prototype-supported. Rejected DeviceIds and Version-tail hypotheses remain in
the report as counterevidence and are not exposed as predictors.
