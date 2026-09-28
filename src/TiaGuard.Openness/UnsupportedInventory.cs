using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaGuard.Openness
{
    internal static class UnsupportedInventory
    {
        // A null/unreadable collection is not evidence that the collection is empty.
        internal static void Check<T>(Func<IEnumerable<T>> read, SnapshotV1 snapshot,
            string category, string objectRef)
        {
            string code;
            try
            {
                var values = read();
                if (values == null) throw new InvalidOperationException();
                if (!values.Any()) return;
                code = category + "_UNSUPPORTED";
            }
            catch (Exception)
            {
                code = category + "_SCAN_FAILED";
            }
            snapshot.Diagnostics.Add(new SnapshotDiagnostic
            {
                Code = code, Severity = "warning", ObjectId = objectRef,
                Message = "The bounded round-trip profile requires an observed empty " + category + " collection."
            });
        }
    }
}
