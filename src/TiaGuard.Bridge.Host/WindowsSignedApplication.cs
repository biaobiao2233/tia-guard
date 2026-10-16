using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace TiaGuard.Bridge.Host;
internal static class WindowsSignedApplication
{
    // Electron's CompanyName is GitHub; trust the authenticated Siemens signer.
    // Verification is offline and never prompts or changes trust settings.
    public static bool IsSiemensSigned(string path)
    {
#if NETFRAMEWORK
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
#else
        if (!OperatingSystem.IsWindows()) return false;
#endif
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            Marshal.StructureToPtr(file, memory, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2,
                UnionChoice = 1, File = memory, ProviderFlags = 0x1000 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
            using var certificate = X509Certificate.CreateFromSignedFile(path);
            return certificate.Subject.Split(',').Any(p => p.Trim() == "O=Siemens AG");
        }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
        finally { Marshal.DestroyStructure<TrustFile>(memory); Marshal.FreeHGlobal(memory); }
    }
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr File, KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr Policy, Sip;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData, Url;
        public uint ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }
}
