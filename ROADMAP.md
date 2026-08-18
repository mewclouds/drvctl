# ROADMAP.md

## Production

Shipped and supported today:

- `list`, with `--verbose`, `--provider`, `--class`
- `export`, with a friendly-by-default, `--verbose`-for-detail split
- Quick confidence verification (`--verify`)
- Expensive confidence verification (`--full-verify`)
- Challenging DISM directly (`--dism`)
- Automatic, independent copy and verification concurrency
- CalVer release versioning through CI, with local dev builds carrying a
  clear placeholder version
- Native AOT, self-contained single-file publish

This is the mature, tested part of the project. Product work here can and
does happen independently of the research track below.

Possible next steps for the production surface, none of them committed:

- driver-store diagnostics beyond `list`
- richer `--verbose` detail where users have asked for it
- packaging and distribution polish

## Research

Everything below is exploratory. It answers questions about how Windows
driver servicing actually works. None of it is a production capability of
the public CLI, and none of it should be described as one.

### Solid enough to build on

- Reading INF packages and Driver Store locations through SetupAPI, the same
  machinery production `list`/`export` already depend on
- Direct WIM manipulation through libwim
- Direct offline registry hive manipulation through Offreg

### Real, but narrow

A single test driver (ACPIVPC) has been published directly into a copied
WIM using the libwim and Offreg path, without invoking DISM, and Windows
offline servicing recognized the resulting package identity, provider,
class, version, hardware ID, and service before any DISM repair ran. That's
a real result: it proves the direct-publication architecture can work end
to end for at least one package. It does not prove general driver
injection. A duplicate DISM add against the same image filled in state that
the direct path had intentionally omitted, which is the current honest
measure of the gap.

### Open and unresolved

- Whether the direct-publication result generalizes past ACPIVPC to other
  driver classes and shapes
- Full DriverDatabase field encoding
- PnpLockdownFiles ownership encoding
- Catalog database publication semantics
- Full DeviceIds encoding
- Whether "Windows recognizes it" implies PnP would actually match and
  install the device, that's a separate, later claim this project has not tested

### Compatibility sweep

A per-package compatibility harness exists to test the direct-publication
path against many packages independently (fresh drvctl WIM, fresh DISM
reference WIM, recognition check, semantic comparison, per-package result).
Treat any current summary counts or pass percentages from that harness as
provisional until the classification logic behind them has been reviewed.
The raw per-package data is trustworthy, the rollup numbers on top of it may not be yet.

### PnP phase

Comes after offline servicing recognition is well understood enough for a
PnP result to mean something. Open questions for that phase: what DeviceIds
actually encode, whether hardware matching works against a directly
published package, whether services and dependencies activate correctly,
and whether a booted image built this way behaves like a DISM-serviced one.

## Long-term direction

Keep using native Windows APIs and libwim for the parts of driver servicing
this project actually understands. Keep DISM as the reference for the parts
it doesn't yet. Move functionality out of the DISM-dependent research column
and into the production column only once it's understood well enough to
stand on its own, not because a prototype happened to work once.
