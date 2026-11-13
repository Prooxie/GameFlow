using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace GameFlow.Infrastructure.Requirements.HidHide;

/// <summary>
/// Checks a downloaded executable's Authenticode signature with
/// <c>WinVerifyTrust</c> (full chain, revocation of the whole chain) and
/// returns the signer certificate's simple name. HidHide's release asset has
/// no published SHA-256, so the publisher's signature is what proves the file
/// came from Nefarius and was not altered.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AuthenticodeVerifier
{
    /// <summary>
    /// Returns the signer's simple name when the signature is valid and
    /// trusted, or null with <paramref name="error"/> set otherwise.
    /// </summary>
    public static string? VerifyAndGetSigner(string path, out string? error)
    {
        var trust = WinVerifyTrustFile(path);
        if (trust != 0)
        {
            error = $"the file's digital signature is missing or not trusted (WinVerifyTrust 0x{trust:X8})";
            return null;
        }

        try
        {
#pragma warning disable SYSLIB0057 // No loader replacement exists for reading a signer from a PE file.
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            error = null;
            return signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or IOException)
        {
            error = "the signer certificate could not be read: " + exception.Message;
            return null;
        }
    }

    /// <summary>True when <paramref name="signer"/> is the expected publisher.</summary>
    public static bool IsExpectedPublisher(string? signer, string expectedPublisher) =>
        !string.IsNullOrWhiteSpace(signer) &&
        signer.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase);

    private static uint WinVerifyTrustFile(string path)
    {
        var fileInfo = new WinTrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            pcwszFilePath = path,
        };
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);
            var data = new WinTrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeWholeChain,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdRevocationCheckChain,
            };
            var action = WintrustActionGenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            data.dwStateAction = WtdStateActionClose;
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPtr);
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckChain = 0x40;
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);
}
