// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FurinaChronicle.Windows;

// Per-data-directory named mutex. A second instance of the same host is stopped before any SQLite
// environment is created; it receives a clear native message rather than opening a second writer.
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex mutex;
    private bool owns;

    private SingleInstanceGuard(Mutex mutex, bool owns)
    {
        this.mutex = mutex;
        this.owns = owns;
    }

    public static SingleInstanceGuard? TryAcquire(string dataRoot)
    {
        string fullRoot = Path.GetFullPath(dataRoot);
        string key = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(fullRoot.ToLowerInvariant())));
        string name = $@"Local\FurinaChronicle.WinUI.{key}";
        var mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex, owns: true);
        }

        mutex.Dispose();
        _ = MessageBoxW(
            IntPtr.Zero,
            "FurinaChronicle 已有一个实例在使用当前数据目录。\n" +
            "请先关闭已打开的窗口，再重新启动；本实例不会写入任何数据。",
            "FurinaChronicle",
            0x00000040);
        return null;
    }

    public void Dispose()
    {
        if (owns)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            owns = false;
        }

        mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(
        IntPtr hWnd,
        string text,
        string caption,
        uint type);
}
