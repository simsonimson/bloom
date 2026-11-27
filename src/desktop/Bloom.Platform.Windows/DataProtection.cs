using System.Runtime.InteropServices;

namespace Bloom.Platform.Windows;

/// <summary>
/// Windows DPAPI (CryptProtectData, current user). Encrypted data can only be read by the same
/// Windows user on the same machine. Used for the Roblox refresh token (never stored in plaintext).
/// </summary>
public static class DataProtection
{
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    public static byte[] Protect(byte[] data, byte[] entropy) => Transform(data, entropy, protect: true);

    public static byte[] Unprotect(byte[] data, byte[] entropy) => Transform(data, entropy, protect: false);

    private static byte[] Transform(byte[] data, byte[] entropy, bool protect)
    {
        var input = Pin(data);
        var extra = Pin(entropy);
        var output = default(DATA_BLOB);
        try
        {
            var ok = protect
                ? CryptProtectData(ref input, null, ref extra, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, output.cbData);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
            Marshal.FreeHGlobal(extra.pbData);
            if (output.pbData != IntPtr.Zero)
            {
                // Wipe before freeing: the buffer may hold the plaintext token.
                for (var i = 0; i < output.cbData; i++) Marshal.WriteByte(output.pbData, i, 0);
                LocalFree(output.pbData);
            }
        }
    }

    private static DATA_BLOB Pin(byte[] bytes)
    {
        var blob = new DATA_BLOB { cbData = bytes.Length, pbData = Marshal.AllocHGlobal(Math.Max(1, bytes.Length)) };
        Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
        return blob;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB dataIn, string? description, ref DATA_BLOB entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DATA_BLOB dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB dataIn, IntPtr description, ref DATA_BLOB entropy,
        IntPtr reserved, IntPtr prompt, int flags, ref DATA_BLOB dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);
}
