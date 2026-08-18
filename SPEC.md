# SPEC.md

This is the stable production contract for drvctl. It describes public,
supported behavior only. Hidden research commands are documented in
`docs/` and are not part of this contract, they can change shape or
disappear without notice.

## Public commands

```text
drvctl export <path> [--verify | --full-verify | --dism] [--verbose] [--benchmark]
drvctl list [--verbose] [--provider <text>] [--class <text>]
drvctl help
```

`drvctl` with no arguments, `drvctl --help`, `drvctl -h`, and `drvctl -help`
all behave the same as `drvctl help`. `drvctl export --help` and
`drvctl list --help` print that command's own help instead of running it.

Any other first argument is an unknown command and produces a usage error.

## export

`drvctl export <path>` exports every published third-party driver package to
`<path>`. `<path>` must be new or an already-empty directory. It must not be
a filesystem root, and it must not be inside the Windows directory.

A plain export:

- does not call DISM
- does not compute a SHA-256 hash of any file
- does not run a verification pass
- copies files using the Windows `CopyFile2` API
- uses an automatic, conservative copy worker count

If any file fails to copy, the entire export is removed and the command
exits with a runtime failure. Export is all-or-nothing: nothing partial is
ever left at the destination path.

### Validation modes

At most one of `--verify`, `--full-verify`, `--dism` may be given. Passing
more than one, or passing incompatible combinations, is a usage error.

**`--verify`**: after export, compares the export against the Driver Store
source packages by regular-file count, relative path, and file size. Does
not compute or compare a hash.

**`--full-verify`**: everything `--verify` does, plus a SHA-256 comparison
of every file.

**`--dism`**: creates a temporary reference export using
`dism.exe /Online /Export-Driver`, compares it against the completed drvctl
export by count, relative path, size, and SHA-256, then deletes the
temporary reference export. This is the only validation mode, and the only
export path in general, that invokes DISM. Requires an elevated terminal,
since DISM itself requires administrative privileges. Running `--dism`
without elevation fails before any export work begins.

`--verify` and `--full-verify` never call DISM. Plain export never calls
DISM.

### --verbose

Does not change what export does. Adds technical detail to the output:
logical CPU count, resolved worker counts and whether each came from the
automatic policy or an environment override, the copy engine name, full
Driver Store paths, and, on a validation failure, the specific list of
differences found.

### --benchmark

Reports timing and throughput for the export phases. With `--dism
--benchmark`, also reports DISM's own timing next to drvctl's, explicitly
labeled as a warm-cache comparison since drvctl's own export always runs
first and can leave the filesystem cache warm for the DISM reference export that follows.

## list

`drvctl list` shows every published third-party driver package: published
INF name, provider, class, version, and date. It performs no file copy, no
DISM call, and no verification of any kind.

`--verbose` additionally shows Driver Store package and directory paths,
the class GUID, and the catalog file name.

`--provider <text>` and `--class <text>` each filter to packages whose
respective field contains `<text>` as a case-insensitive substring. Both may
be combined. A package must match both filters to be shown. A package with
no value for the filtered field never matches.

## Concurrency

There is no public `--workers` option. Copy concurrency and verification
concurrency (used by `--full-verify` and `--dism`) are each chosen
automatically and independently, since copying and hashing scale
differently with core count. The exact policy values are an implementation
detail documented in `ARCHITECTURE.md`, not a contract: they may be tuned
between releases without that being a breaking change, as long as the
public flag surface above doesn't change.

## Version display

`drvctl help`, every command's own help text, and general console banners
show a version string sourced from the built executable's own product
version metadata. Local development builds show a placeholder
(`0.0.0-dev`), tagged CI releases show the real release version. There is no
other version display mechanism.

## Exit codes

```text
0  Success. For a validation mode, the comparison also matched exactly.
1  Runtime failure (I/O error, native API failure, DISM not elevated, etc)
2  Usage error, the command line could not be parsed
3  A validation mode ran successfully but found a mismatch
4  DISM itself failed or returned a non-zero exit code
```

## What is not part of this contract

- `drvctl verify` as a separate top-level command does not exist. Validation
  is a flag on `export`.
- Hidden research commands (`inspect-inf`, `inspect-wim`, `plan-driver`,
  `validate-plan`, `simulate-apply`, `analyze-publication`,
  `prototype-publication`, `prototype-inject-wim`) are not public API. They
  are dispatchable by exact name for research and benchmarking purposes
  only, and their behavior, arguments, and existence can change without
  notice.
- `DRVCTL_COPY_WORKERS` and `DRVCTL_VERIFY_WORKERS` are internal research and
  benchmark environment overrides, not supported configuration.
- Offline WIM injection, offline publication, and any related prototype
  behavior described in `docs/` is research, not a production capability of
  the public CLI.
