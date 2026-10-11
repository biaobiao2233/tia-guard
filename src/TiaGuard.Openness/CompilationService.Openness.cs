// Service acquisition adapted from Czarnak/tia-portal-mcp CompileChecker.cs
// at 7301fb809bba338719dd1d3724da055f27945653.
// Copyright (c) 2026 Łukasz Czarnacki. MIT: third-party/compilation-diagnostics/LICENSE.
using Siemens.Engineering;
using Siemens.Engineering.Compiler;

namespace TiaGuard.Openness
{
    public static partial class CompilationService
    {
        // ACTIVE operation. Caller must first enforce session ownership, offline-copy
        // guards and any operator approval. This service never opens/attaches a portal.
        public static CompilationReport Compile(IEngineeringServiceProvider target,
            CompilationCollectionOptions options = null)
        {
            if (target == null)
                return CompilationMessageCollector.Unavailable("TARGET_UNAVAILABLE", "target");
            return Invoke(() =>
            {
                // PlcSoftware and blocks expose ICompilable as an engineering SERVICE.
                // Do not cast target to ICompilable or reflect over target interfaces.
                var compiler = target.GetService<ICompilable>();
                if (compiler == null)
                    throw new System.InvalidOperationException("Compile service is unavailable.");
                return compiler.Compile();
            }, options);
        }
    }
}
