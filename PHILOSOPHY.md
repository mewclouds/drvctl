# Philosophy

## Windows already knows the answer

Before writing a parser, ask whether Windows can just tell you. drvctl reads
INF files through SetupAPI instead of hand-rolling INF grammar. It resolves
Driver Store locations through SetupAPI instead of guessing folder naming
schemes. It reads its own version from the binary's own resource instead of
inventing a config format. Every one of these is a place where a general
Windows parser or homegrown format would have been more code, and worse
code, than just asking the platform.

Windows already exposes a lot of good machinery. The first question should
be "can Windows tell us?" before writing our own parser.

## DISM is a reference, not a dependency

DISM knows how to export drivers. It also knows how to mount images, service
offline hives, and answer a hundred other questions nobody asked it. That
weight has a cost, and the cost shows up as wall-clock time on every call.

drvctl's plain export never touches DISM. It resolves packages through
SetupAPI and copies them with CopyFile2 directly. DISM shows up in exactly
one place: `--dism`, where it acts as a second opinion. drvctl runs its own
export, then asks DISM to export the same drivers into a temporary
directory, then compares the two byte for byte.

DISM is the reference when we need a reference. It does not need to be the
hot path. Challenging DISM because Windows tools should do better is not
arrogance, it's just a bet that a narrower, purpose-built tool can be both
faster and just as correct, and that you should be able to check that bet
yourself instead of trusting it on faith.

## Correctness beats cleverness

A fast copy that silently drops a file is worse than a slow copy that
doesn't. That's the whole reason the three validation modes exist as
separate, honestly-named things instead of one flag that tries to be clever
about what to check. Quick confidence tells you the shape matched.
Expensive confidence tells you every byte matched. Challenging DISM tells
you Windows agrees. None of them pretend to be a different one.

The plain export path is fast because it does one job (copy files) and
trusts the Windows API it's built on, not because it skips work it should be
doing.

## Evidence beats assumptions

Undocumented Windows behavior is undocumented. If drvctl doesn't know why a
value exists or whether a rule generalizes, the honest answer is "unknown,"
not a plausible-looking guess baked into the code. This matters more in the
hidden research commands than the public CLI, but the instinct is the same
everywhere: say what you observed, not what you assume.

## Research and production have a hard boundary

The public CLI is `export`, `list`, and `help`. Everything else, WIM
mutation, offline registry work, publication prototypes, exists to answer
research questions about how Windows driver servicing actually works. Those
commands are still there and still callable by name. They are not gated by
some clever security mechanism, because hiding a command from `--help`
was never meant to be a lock. It's a filing decision, not a fence.

Keeping that boundary sharp means a user running `drvctl export` never has
to wonder if they've stumbled into an experiment, and a researcher poking at
WIM internals never has to wonder if they're about to ship something half-baked.

## Simple defaults are a feature

`drvctl export C:\Drivers` should not require you to think about worker
pools, cache states, or what DISM does under the hood. There is no
`--workers` flag. Concurrency is chosen automatically, and it's chosen
differently for copying than for hashing because they behave differently
under load, not because more knobs looked impressive on a help screen. If
you want to see the machinery, `--verbose` is right there. If you don't,
you shouldn't have to.
