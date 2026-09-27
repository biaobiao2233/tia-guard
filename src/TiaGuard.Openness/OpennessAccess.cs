using System;
using System.Security.Principal;

namespace TiaGuard.Openness
{
    public sealed class OpennessAccessException : InvalidOperationException
    {
        public OpennessAccessException(string message) : base(message) { }
    }

    public static class OpennessAccess
    {
        public const string GroupName = "Siemens TIA Openness";

        // Check the effective token, not only the account's group membership in SAM.
        // A newly added membership is ineffective until Windows creates a new logon token.
        public static bool HasEffectiveGroupMembership()
        {
            try
            {
                var group = (SecurityIdentifier)new NTAccount(Environment.MachineName, GroupName)
                    .Translate(typeof(SecurityIdentifier));
                using (var identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(group);
            }
            catch (IdentityNotMappedException)
            {
                return false;
            }
        }

        public static void RequireAccess()
        {
            if (!HasEffectiveGroupMembership())
                throw new OpennessAccessException(
                    "The current Windows logon token does not include the Siemens TIA Openness group. " +
                    "Ask an administrator to grant membership if needed, then sign out and sign in again. " +
                    "TIA-Guard will not attempt to bypass this permission.");
        }
    }
}
