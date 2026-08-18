/*
 * Task9HiveEditor is a Research/Task9 tool that exercises DrvCtl's internal
 * offline-registry types directly, so it needs InternalsVisibleTo rather
 * than a public API surface just for one research utility.
 */

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Task9HiveEditor")]
