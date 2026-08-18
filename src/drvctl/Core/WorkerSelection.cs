/*
 * Shared shape for automatic concurrency decisions. See CopyWorkerPolicy and
 * VerificationWorkerPolicy in Cli/DrvCtlApp.cs for the two policies that
 * produce these.
 */

namespace DrvCtl.Core;

/// The result of an automatic worker-count decision, plus whether a hidden
/// research/benchmark environment override supplied it instead of the normal
/// heuristic - so verbose output can label it honestly.
internal readonly record struct WorkerSelection(int Workers, bool FromOverride)
{
    /// "automatic" or "environment override", exactly as shown in --verbose output.
    internal string Label => FromOverride ? "environment override" : "automatic";
}
