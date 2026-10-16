using System;

namespace TiaGuard.Openness
{
    public static partial class CompilationService
    {
        // The action is invoked once. Reading diagnostics must never retry compilation.
        internal static CompilationReport Invoke(Func<object> compile,
            CompilationCollectionOptions options = null)
        {
            // Invalid limits must fail before the active operation.
            var snapshot = (options ?? new CompilationCollectionOptions()).Snapshot();
            object result;
            try { result = compile(); }
            catch (Exception error) when (CompilationMessageCollector.CanRecord(error))
            {
                return CompilationMessageCollector.Unavailable("COMPILE_INVOCATION_FAILED", "Compile", error);
            }
            return CompilationMessageCollector.Collect(result, snapshot);
        }
    }
}
