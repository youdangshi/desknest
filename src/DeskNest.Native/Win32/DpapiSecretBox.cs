using System.Runtime.InteropServices;
using System.Text;
using DeskNest.Core.Services;

namespace DeskNest.Native.Win32;

/// <summary>API Key 等敏感串加密存储：Windows DPAPI（绑定当前用户），不落明文。</summary>
public sealed class DpapiSecretBox : ISecretBox
{
    public string Protect(string plain)
    {
        try
        {
            var data = Encoding.UTF8.GetBytes(plain);
            var inBlob = new NativeMethods.DATA_BLOB
            {
                cbData = data.Length,
                pbData = Marshal.AllocHGlobal(data.Length)
            };
            Marshal.Copy(data, 0, inBlob.pbData, data.Length);
            try
            {
                if (!NativeMethods.CryptProtectData(ref inBlob, "DeskNest", IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, 0, out var outBlob))
                    return Convert.ToBase64String(data);
                try
                {
                    var result = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                    return "dpapi:" + Convert.ToBase64String(result);
                }
                finally { NativeMethods.LocalFree(outBlob.pbData); }
            }
            finally { Marshal.FreeHGlobal(inBlob.pbData); }
        }
        catch
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        }
    }

    public string? Unprotect(string cipher)
    {
        try
        {
            if (!cipher.StartsWith("dpapi:", StringComparison.Ordinal))
                return Encoding.UTF8.GetString(Convert.FromBase64String(cipher));

            var data = Convert.FromBase64String(cipher["dpapi:".Length..]);
            var inBlob = new NativeMethods.DATA_BLOB
            {
                cbData = data.Length,
                pbData = Marshal.AllocHGlobal(data.Length)
            };
            Marshal.Copy(data, 0, inBlob.pbData, data.Length);
            try
            {
                if (!NativeMethods.CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, 0, out var outBlob))
                    return null;
                try
                {
                    var result = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                    return Encoding.UTF8.GetString(result);
                }
                finally { NativeMethods.LocalFree(outBlob.pbData); }
            }
            finally { Marshal.FreeHGlobal(inBlob.pbData); }
        }
        catch { return null; }
    }
}
