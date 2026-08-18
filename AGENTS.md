# AGENTS.md

Read this before changing drvctl.

## Production vs research

The supported public CLI is `drvctl export`, `drvctl list`, and `drvctl help`.
That's it. Everything else (`inspect-inf`, `inspect-wim`, `plan-driver`,
`validate-plan`, `simulate-apply`, `analyze-publication`,
`prototype-publication`, `prototype-inject-wim`) is a hidden research
command. It's dispatched by exact name in `Cli/CommandLine.cs` like any
other command, it just has no entry in `Cli/HelpText.cs`, so it never shows
up in `drvctl help`. Hidden from discovery does not mean secret, and it is
not a security boundary. Do not add research commands to help text, and do
not treat their absence from help as anything stronger than a filing decision.

## Behavioral facts to keep straight

- Plain `export` never calls DISM.
- `--verify` is metadata-only: file count, relative path, size. It does not hash.
- `--full-verify` adds SHA-256 on top of `--verify`'s checks.
- `--dism` creates a temporary DISM reference export and does a full
  (count, path, size, SHA-256) comparison against it, then deletes the
  temporary export. It is the only production path that touches DISM.
- `--verify`, `--full-verify`, and `--dism` are mutually exclusive.
- There is no public `--workers` flag. Copy and verification concurrency use
  separate automatic policies (`CopyWorkerPolicy`, `VerificationWorkerPolicy`
  in `Cli/DrvCtlApp.cs`) because copying and hashing behave differently
  under load.
- `DRVCTL_COPY_WORKERS` and `DRVCTL_VERIFY_WORKERS` are internal
  research/benchmark environment overrides, not CLI options. Do not surface
  them in end-user help or quick-start docs.

## Working style

- Comments explain why, not what. If a comment restates the method name, delete it.
- Do not guess undocumented Windows behavior. If we don't know why a value
  exists or whether a rule generalizes, say so, don't bake in a plausible-looking assumption.
- Use `fd` and `rg` instead of broad recursive scans, this is a real
  codebase with a real build output directory, don't grep through `bin/` and `obj/`.
- Native AOT is not optional for validating a change. `dotnet build` proves
  the code compiles. It does not prove Native AOT will publish. Run
  `dotnet publish .\drvctl.csproj -c Release -r win-x64` before calling a
  change to native interop or trimming-sensitive code done.
- If a parent or reviewer model is checking your work, native interop
  changes (`src/drvctl/Native/*.cs`, anything touching `SafeHandle`
  subclasses or struct layout) deserve a closer look than an ordinary diff.
  Struct layout and marshalling mistakes here have caused real bugs before.

## Fixture and workspace safety

Research commands (`simulate-apply`, `analyze-publication`,
`prototype-publication`, `prototype-inject-wim`) operate on copied WIMs and
disposable workspaces. Never mutate a canonical baseline or reference WIM in
place, and never use the live registry as a staging area for offline-image
work. Use Offreg for offline hive access and libwim for direct WIM access,
both already wrapped in `src/drvctl/Registry` and `src/drvctl/Images`.

## Docs that matter here

- `README.md` for the end-user quick start
- `PHILOSOPHY.md` for why drvctl is built this way
- `ARCHITECTURE.md` for the technical map
- `SPEC.md` for the exact production contract
- `ROADMAP.md` for what's shipped versus what's still research
- `docs/` for research methodology, experiment history, and deeper technical notes
