# drvctl

`drvctl` is a focused Windows driver package CLI.

This build intentionally keeps two paths:

```text
drvctl export
    SetupAPI -> CopyFile2 -> destination

drvctl verify
    optional cache flush
    -> DISM reference export first
    -> optional cache flush
    -> the same drvctl exporter
    -> parallel relative path + size + SHA-256 comparison
    -> delete temporary DISM reference
```

There is no DISM fallback in `export`.

There is no signature, catalog, or standalone hash verification mode in this
version. `verify` means compare drvctl's regular-file output against DISM.

## Requirements to build

- Windows x64
- .NET 10 SDK
- Visual Studio Build Tools with Desktop development with C++
- MSVC x64/x86 build tools
- Windows SDK

## Build the Native AOT executable

```powershell
dotnet publish .\drvctl.csproj `
    -c Release `
    -r win-x64
```

Output:

```text
bin\Release\net10.0-windows\win-x64\publish\drvctl.exe
```

The executable is Native AOT and self-contained.

## Production export

```powershell
.\bin\Release\net10.0-windows\win-x64\publish\drvctl.exe `
    export "C:\Drivers"
```

Benchmark drvctl only:

```powershell
.\bin\Release\net10.0-windows\win-x64\publish\drvctl.exe `
    export "C:\Drivers" `
    --workers 4 `
    --benchmark
```

## Verify against DISM

Run from an elevated terminal:

```powershell
.\bin\Release\net10.0-windows\win-x64\publish\drvctl.exe `
    verify "C:\Drivers" `
    --benchmark
```

Cache-flushed comparison:

```powershell
.\bin\Release\net10.0-windows\win-x64\publish\drvctl.exe `
    verify "C:\Drivers" `
    --workers 4 `
    --benchmark `
    --flush-cache
```

`--flush-cache` requests a system file cache flush before DISM and again before
drvctl. It should be described as cache-flushed rather than guaranteed cold.
A fresh reboot remains the stronger clean-start benchmark.

## Exit codes

```text
0  Success
1  Runtime or verification harness failure
2  Invalid usage
3  drvctl and DISM outputs differ
4  DISM export failed
5  drvctl export failed during verification
```
