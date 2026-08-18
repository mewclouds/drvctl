# drvctl

`drvctl` is a focused Windows driver package CLI. It lists and exports the
third-party driver packages your system has published, using SetupAPI and
Windows CopyFile2 directly instead of going through DISM.

## Quick start

```powershell
drvctl list
drvctl export C:\Drivers
drvctl export C:\Drivers --verify
drvctl export C:\Drivers --full-verify
drvctl export C:\Drivers --dism
```

`list` shows what you have. `export` copies it out. The three flags on
`export` add increasing levels of confidence that the copy is correct.

## list

```powershell
drvctl list
drvctl list --verbose
drvctl list --provider Intel
drvctl list --class Net
```

Shows the published INF, provider, class, version, and date for every
third-party driver package Windows knows about. `--verbose` adds the Driver
Store paths and package details. `--provider` and `--class` filter to
packages whose value contains the given text. `list` never copies files,
calls DISM, or verifies anything. It just reads.

## export

```powershell
drvctl export C:\Drivers
```

Exports installed third-party driver packages. Nothing extra by default. A
plain export does not call DISM, does not hash anything, and does not run an
expensive verification pass. It resolves published packages through
SetupAPI, copies them with Windows CopyFile2 using an automatic small worker
pool, and tells you what happened.

### Three confidence modes

Export takes at most one of these:

```powershell
drvctl export C:\Drivers --verify
drvctl export C:\Drivers --full-verify
drvctl export C:\Drivers --dism
```

**`--verify`** is quick confidence. It checks that the export has the right
file count, relative paths, and sizes compared to the Driver Store source.
It does not hash anything.

**`--full-verify`** is expensive confidence. It does everything `--verify`
does, then adds SHA-256. Slower, but now every byte has receipts.

**`--dism`** challenges Windows itself. It creates a temporary DISM
reference export, compares it against your drvctl export by count, path,
size, and SHA-256, then deletes the temporary export. This is the only
export mode that touches DISM, and it needs an elevated terminal because
DISM does.

The destination must be new or empty. Plain export never calls DISM, and
neither `--verify` nor `--full-verify` do either. Only `--dism` does.

## Seeing more

```powershell
drvctl export C:\Drivers --verbose
drvctl export C:\Drivers --benchmark
```

`--verbose` doesn't change what drvctl does, it changes what it tells you.
Turn it on to see logical CPU counts, worker counts, the Driver Store paths
involved, and (on a validation run) the exact list of differences if
something didn't match.

`--benchmark` reports timing and throughput. Neither flag is required for
normal use. drvctl's default output already answers what happened, whether
it worked, what you got, and how long it took.

## Concurrency

There is no `--workers` flag. Copy concurrency and verification concurrency
are chosen automatically, and they use separate policies because copying and
hashing behave differently under load. Copying stays deliberately
conservative (a small pool measured to be the sweet spot). Hashing scales up
with your CPU count because SHA-256 parallelizes well. You should not have
to tune thread pools to export your own drivers.

## Requirements to build

- Windows x64
- .NET 10 SDK
- Visual Studio Build Tools with Desktop development with C++
- MSVC x64/x86 build tools
- Windows SDK

## Build the Native AOT executable

```powershell
dotnet publish .\drvctl.csproj -c Release -r win-x64
```

Output:

```text
bin\Release\net10.0-windows\win-x64\publish\drvctl.exe
```

The executable is Native AOT and self-contained.

## Exit codes

```text
0  Success
1  Runtime failure
2  Usage error
3  Verification or comparison found a mismatch
4  DISM itself failed
```

## Further reading

- [`PHILOSOPHY.md`](PHILOSOPHY.md) for why drvctl exists and how it thinks about DISM
- [`ARCHITECTURE.md`](ARCHITECTURE.md) for the technical map
- [`SPEC.md`](SPEC.md) for the exact production contract
- [`AGENTS.md`](AGENTS.md) if you're a coding agent about to touch this repo
