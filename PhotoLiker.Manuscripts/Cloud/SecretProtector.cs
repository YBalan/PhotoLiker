using System.Security.Cryptography;
using System.Text;

namespace PhotoLiker.Manuscripts.Cloud
{
    /// <summary>Keeps a secret (the key of an AI service) in a form that is safe to write to a file, and gives it back.</summary>
    public interface ISecretProtector
    {
        /// <summary>The secret as text that can be written to a file.</summary>
        string Protect(string secret);

        /// <summary>The secret that <see cref="Protect"/> made that text of, or null when it can not be read (made for another Windows account or computer, or damaged).</summary>
        string? Unprotect(string protectedText);
    }

    /// <summary>
    /// Windows Data Protection (DPAPI) for the account of the person who runs the program: the file with the keys is of no use when it is copied to another account or another computer, the way
    /// the passwords of the browser are kept. It stops other people and other accounts, not a program that runs as the same person.
    /// </summary>
    public sealed class WindowsSecretProtector : ISecretProtector
    {
        // Another program of the same person asking DPAPI for a blob needs to know this too; it makes a blob of the keys of this program not the one of any other
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PhotoLiker AI services");

        public string Protect(string secret)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("The keys of the AI services can only be kept on Windows.");

            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        public string? Unprotect(string protectedText)
        {
            if (!OperatingSystem.IsWindows())
                return null;

            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return null;
            }
        }
    }
}
