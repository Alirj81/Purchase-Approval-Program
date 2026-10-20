// Checks the configured Data Protection key folder before the site starts serving requests.
// A missing or read-only folder would otherwise surface as an HTTP 500 on the first sign-in,
// with the real cause buried in the log; failing at start-up names the folder and the identity.
using System.Security.Principal;

namespace PurchaseApproval.Security;

public static class DataProtectionKeyDirectory
{
    /// <summary>
    /// Returns null when the folder exists and the current identity can create and delete a file
    /// in it; otherwise an actionable message naming the configuration key, the path and the
    /// Windows account that needs permission.
    /// </summary>
    public static string? Probe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "DataProtection:KeyDirectory is empty.";
        }

        if (!Directory.Exists(path))
        {
            return $"DataProtection:KeyDirectory '{path}' does not exist. Create the folder, grant the " +
                   $"account running the site ('{CurrentIdentity()}') Modify permission and no other users " +
                   "access, then start the site again. The folder is never created automatically so that " +
                   "it does not inherit permissive permissions by accident.";
        }

        var probeFile = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probeFile))
            {
            }

            File.Delete(probeFile);
            return null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return $"DataProtection:KeyDirectory '{path}' is not writable by '{CurrentIdentity()}' " +
                   $"({exception.Message}). Grant that account Modify permission on the folder.";
        }
    }

    /// <summary>The Windows account (or POSIX user) the process runs as; shown in messages and logs only.</summary>
    public static string CurrentIdentity()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }

        return Environment.UserName;
    }
}
