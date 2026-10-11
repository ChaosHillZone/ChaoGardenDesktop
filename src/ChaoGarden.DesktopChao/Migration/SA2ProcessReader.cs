using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ChaoGarden.Core.SaveFormat;

namespace ChaoGarden.Tools.Migration;

/// <summary>
/// Reads Chao data blocks from a running SA2B (sonic2app.exe) process.
/// Windows-only: uses ReadProcessMemory via P/Invoke.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SA2ProcessReader : IDisposable
{
    private readonly Process _process;
    private readonly IntPtr _processHandle;
    private bool _disposed;

    private const uint PROCESS_VM_READ = 0x0010;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        nuint dwSize,
        out nuint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// Attaches to a running sonic2app.exe process. Throws if the process is not found
    /// or if the handle cannot be opened.
    /// </summary>
    public SA2ProcessReader()
    {
        Process[] processes = Process.GetProcessesByName("sonic2app");
        if (processes.Length == 0)
        {
            throw new InvalidOperationException(
                "sonic2app.exe is not running. Launch Sonic Adventure 2 and enter a Chao Garden before migrating.");
        }

        _process = processes[0];

        // Dispose extra process handles
        for (int i = 1; i < processes.Length; i++)
        {
            processes[i].Dispose();
        }

        _processHandle = OpenProcess(PROCESS_VM_READ, false, _process.Id);
        if (_processHandle == IntPtr.Zero)
        {
            int processId = _process.Id;
            _process.Dispose();
            throw new InvalidOperationException(
                $"Failed to open sonic2app.exe (PID {processId}) for reading. " +
                "Try running as Administrator.");
        }
    }

    /// <summary>
    /// Reads all 24 Chao data blocks from the running process. Returns only occupied slots.
    /// </summary>
    public List<SA2ChaoBlock> ReadAllChaoBlocks()
    {
        List<SA2ChaoBlock> blocks = new List<SA2ChaoBlock>();

        for (int slot = 0; slot < SA2MemoryConstants.MaxChaoSlots; slot++)
        {
            SA2ChaoBlock block = ReadChaoBlock(slot);
            if (block.IsOccupied())
            {
                blocks.Add(block);
            }
        }

        return blocks;
    }

    /// <summary>
    /// Reads a single Chao data block at the given slot index (0-23).
    /// </summary>
    public SA2ChaoBlock ReadChaoBlock(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SA2MemoryConstants.MaxChaoSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex),
                $"Slot index must be between 0 and {SA2MemoryConstants.MaxChaoSlots - 1}.");
        }

        uint address = SA2MemoryConstants.ChaoDataBaseAddress +
                        (uint)(slotIndex * SA2MemoryConstants.ChaoBlockSize);

        byte[] buffer = new byte[SA2MemoryConstants.ChaoBlockSize];

        bool success = ReadProcessMemory(
            _processHandle,
            (IntPtr)address,
            buffer,
            SA2MemoryConstants.ChaoBlockSize,
            out nuint bytesRead);

        if (!success || bytesRead != SA2MemoryConstants.ChaoBlockSize)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to read Chao block at slot {slotIndex} (address 0x{address:X8}). " +
                $"Read {bytesRead}/{SA2MemoryConstants.ChaoBlockSize} bytes. " +
                $"Win32 error: {error} ({new Win32Exception(error).Message})");
        }

        return new SA2ChaoBlock(buffer, slotIndex);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_processHandle != IntPtr.Zero)
            {
                CloseHandle(_processHandle);
            }
            _process.Dispose();
        }
    }
}
