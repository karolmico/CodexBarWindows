using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CodexBar.Core.Credentials;

namespace CodexBar.Providers.Windows;

/// <summary>
/// DPAPI in the current-user scope. A token file written by CodexBar can only be read back by the
/// same Windows account on this machine, so copying the file elsewhere yields nothing.
/// </summary>
public sealed class DpapiDataProtector : IDataProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        return Transform(plaintext, protect: true);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        return Transform(ciphertext, protect: false);
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        if (input.Length == 0)
        {
            return [];
        }

        var inputPointer = Marshal.AllocHGlobal(input.Length);
        var output = default(DataBlob);
        var description = IntPtr.Zero;
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            var inputBlob = new DataBlob { DataLength = input.Length, DataPointer = inputPointer };

            var succeeded = protect
                ? CryptProtectData(
                    ref inputBlob,
                    "CodexBar provider token",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output)
                : CryptUnprotectData(
                    ref inputBlob,
                    out description,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);

            if (!succeeded)
            {
                throw new CryptographicException(
                    $"DPAPI could not process the saved provider token (Win32 error {Marshal.GetLastPInvokeError()}).");
            }

            var result = new byte[output.DataLength];
            Marshal.Copy(output.DataPointer, result, 0, output.DataLength);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(inputPointer);
            if (output.DataPointer != IntPtr.Zero)
            {
                LocalFree(output.DataPointer);
            }

            if (description != IntPtr.Zero)
            {
                LocalFree(description);
            }
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataLength;

        public IntPtr DataPointer;
    }
}
