using System.Runtime.InteropServices;
using System.Text;

namespace Leaf.Services;

/// <summary>
/// Credential service using Windows Credential Manager.
/// Stores PAT tokens securely in the Windows credential store.
/// </summary>
public class CredentialService : ICredentialService
{
    private const string CredentialPrefix = "Leaf:";

    public void StorePat(string organization, string pat)
    {
        var targetName = GetTargetName(organization);
        WriteCredential(targetName, "git", pat);
    }

    public string? GetPat(string organization)
    {
        var targetName = GetTargetName(organization);
        return ReadCredential(targetName);
    }

    public void RemovePat(string organization)
    {
        var targetName = GetTargetName(organization);
        DeleteCredentialInternal(targetName);
    }

    public IEnumerable<string> GetStoredOrganizations()
    {
        var credentials = EnumerateCredentials();
        return credentials
            .Where(c => c.StartsWith(CredentialPrefix))
            .Select(c => c.Substring(CredentialPrefix.Length));
    }

    /// <summary>
    /// Save a generic credential (wrapper for UI).
    /// </summary>
    public void SaveCredential(string name, string username, string password)
    {
        var targetName = GetTargetName(name);
        WriteCredential(targetName, username, password);
    }

    /// <summary>
    /// Get a generic credential (wrapper for UI).
    /// </summary>
    public string? GetCredential(string name)
    {
        var targetName = GetTargetName(name);
        return ReadCredential(targetName);
    }

    /// <summary>
    /// Delete a generic credential (wrapper for UI).
    /// </summary>
    public void DeleteCredential(string name)
    {
        var targetName = GetTargetName(name);
        CredDeleteW(targetName, CRED_TYPE_GENERIC, 0);
    }

    private static string GetTargetName(string organization)
    {
        return $"{CredentialPrefix}{organization}";
    }

    #region Windows Credential Manager P/Invoke

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredEnumerateW(string? filter, uint flags, out uint count, out IntPtr credentials);

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    private static void WriteCredential(string targetName, string userName, string secret)
    {
        var byteArray = Encoding.Unicode.GetBytes(secret);

        var credential = new CREDENTIAL
        {
            Type = CRED_TYPE_GENERIC,
            TargetName = targetName,
            CredentialBlobSize = (uint)byteArray.Length,
            CredentialBlob = Marshal.AllocHGlobal(byteArray.Length),
            Persist = CRED_PERSIST_LOCAL_MACHINE,
            UserName = userName
        };

        try
        {
            Marshal.Copy(byteArray, 0, credential.CredentialBlob, byteArray.Length);

            if (!CredWriteW(ref credential, 0))
            {
                throw new InvalidOperationException($"Failed to write credential: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

    private static string? ReadCredential(string targetName)
    {
        if (!CredReadW(targetName, CRED_TYPE_GENERIC, 0, out IntPtr credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            var passwordBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, passwordBytes, 0, (int)credential.CredentialBlobSize);
            return Encoding.Unicode.GetString(passwordBytes);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    private static void DeleteCredentialInternal(string targetName)
    {
        CredDeleteW(targetName, CRED_TYPE_GENERIC, 0);
    }

    private static List<string> EnumerateCredentials()
    {
        var result = new List<string>();

        if (!CredEnumerateW($"{CredentialPrefix}*", 0, out uint count, out IntPtr credentialsPtr))
        {
            return result;
        }

        try
        {
            for (int i = 0; i < count; i++)
            {
                var credPtr = Marshal.ReadIntPtr(credentialsPtr, i * IntPtr.Size);
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                result.Add(cred.TargetName);
            }
        }
        finally
        {
            CredFree(credentialsPtr);
        }

        return result;
    }

    #endregion
}
