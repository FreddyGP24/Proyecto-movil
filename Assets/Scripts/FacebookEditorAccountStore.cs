#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Facebook.Unity.Settings;
using UnityEngine;

/// <summary>Windows-user encrypted credentials for editor testing; excluded from player builds.</summary>
internal static class FacebookEditorAccountStore
{
    [Serializable]
    internal sealed class Account { public string id; public string name; public string token; }

    [Serializable]
    private sealed class AccountFile
    {
        public int version = 1;
        public string appId;
        public List<Account> accounts = new List<Account>();
    }

    private static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../UserSettings/FacebookEditorAccounts.bin"));

    internal static List<Account> Read() => Load().accounts;

    internal static void Save(string id, string name, string token)
    {
        var file = Load();
        file.accounts.RemoveAll(account => account.id == id);
        file.accounts.Add(new Account { id = id, name = name, token = token });
        Write(file);
    }

    internal static void Forget(string id)
    {
        var file = Load();
        file.accounts.RemoveAll(account => account.id == id);
        Write(file);
    }

    private static AccountFile Load()
    {
        if (!File.Exists(FilePath)) return new AccountFile { appId = FacebookSettings.AppId };
        byte[] plain = Transform(File.ReadAllBytes(FilePath), false);
        try
        {
            var file = JsonUtility.FromJson<AccountFile>(Encoding.UTF8.GetString(plain));
            if (file == null || file.version != 1 || file.appId != FacebookSettings.AppId || file.accounts == null)
                throw new InvalidDataException("El archivo de cuentas no corresponde a esta aplicación.");
            file.accounts.RemoveAll(account => account == null || string.IsNullOrWhiteSpace(account.id) ||
                string.IsNullOrWhiteSpace(account.name) || string.IsNullOrWhiteSpace(account.token));
            return file;
        }
        finally { Array.Clear(plain, 0, plain.Length); }
    }

    private static void Write(AccountFile file)
    {
        byte[] plain = Encoding.UTF8.GetBytes(JsonUtility.ToJson(file));
        try
        {
            byte[] encrypted = Transform(plain, true);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
        finally { Array.Clear(plain, 0, plain.Length); }
    }

    private static byte[] Transform(byte[] data, bool encrypt)
    {
#if UNITY_EDITOR_WIN
        var input = Allocate(data);
        var entropy = Allocate(Encoding.UTF8.GetBytes("UnityFacebook.EditorAccounts.v1:" + FacebookSettings.AppId));
        DataBlob output = default;
        try
        {
            // DPAPI binds this credential to the current Windows user. No plaintext fallback.
            bool success = encrypt
                ? CryptProtectData(ref input, "Unity Facebook Editor", ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.size];
            Marshal.Copy(output.data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Clear(input);
            Clear(entropy);
            if (output.data != IntPtr.Zero)
            {
                for (int i = 0; i < output.size; i++) Marshal.WriteByte(output.data, i, 0);
                LocalFree(output.data);
            }
        }
#else
        throw new PlatformNotSupportedException("Las cuentas guardadas de esta prueba requieren el editor en Windows.");
#endif
    }

#if UNITY_EDITOR_WIN
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int size; public IntPtr data; }

    private static DataBlob Allocate(byte[] bytes)
    {
        var blob = new DataBlob { size = bytes.Length, data = Marshal.AllocHGlobal(bytes.Length) };
        Marshal.Copy(bytes, 0, blob.data, bytes.Length);
        return blob;
    }

    private static void Clear(DataBlob blob)
    {
        for (int i = 0; i < blob.size; i++) Marshal.WriteByte(blob.data, i, 0);
        Marshal.FreeHGlobal(blob.data);
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
#endif
}
#endif
