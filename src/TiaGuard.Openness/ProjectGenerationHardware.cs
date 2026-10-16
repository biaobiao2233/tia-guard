using System;
using System.Collections.Generic;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

namespace TiaGuard.Openness
{
    internal static class ProjectGenerationHardware
    {
        internal static PlcSoftware RequireExactPlc(Device device, string expected)
        {
            var candidates = new List<Tuple<PlcSoftware, string>>();
            foreach (var item in device.DeviceItems) Walk(item, candidates);
            if (candidates.Count != 1 || candidates[0].Item2 != expected)
                throw new InvalidOperationException("Fresh device CPU/software identity does not match the exact 1212C/V4.7 request.");
            return candidates[0].Item1;
        }
        private static void Walk(DeviceItem item, List<Tuple<PlcSoftware, string>> found)
        {
            var plc = item.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            if (plc != null) found.Add(Tuple.Create(plc, item.GetAttribute("TypeIdentifier") as string));
            foreach (var child in item.DeviceItems) Walk(child, found);
        }
    }
}
