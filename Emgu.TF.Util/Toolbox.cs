//----------------------------------------------------------------------------
//  Copyright (C) 2004-2021 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------
using System;
using System.Text;

using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Generic;

namespace Emgu.TF.Util
{
    /// <summary>
    /// utilities functions for Emgu
    /// </summary>
    public static partial class Toolbox
    {

        private static IntPtr LoadLibraryExWindows(String dllname, int flags)
        {
            IntPtr handler = LoadLibraryEx(dllname, IntPtr.Zero, flags);

            if (handler == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();

                System.ComponentModel.Win32Exception ex = new System.ComponentModel.Win32Exception(error);
                System.Diagnostics.Trace.WriteLine(String.Format(
                    "LoadLibraryEx(\"{0}\", 0, {3}) failed with error code {1}: {2}",
                    dllname,
                    (uint)error,
                    ex.Message,
                    flags));
                if (error == 5)
                {
                    System.Diagnostics.Trace.WriteLine(String.Format(
                        "Please check if the current user has execute permission for file: {0} ", dllname));
                }
            }
            else
            {
                System.Diagnostics.Trace.WriteLine(String.Format("LoadLibraryEx(\"{0}\", 0, {1}) successfully loaded library.", dllname, flags));
            }

            return handler;
        }

        private static IntPtr LoadLibraryWindows(String dllname)
        {
            const int loadLibrarySearchDllLoadDir = 0x00000100;
            const int loadLibrarySearchDefaultDirs = 0x00001000;
            int flags;
            if (System.IO.Path.IsPathRooted(dllname))
            {
                flags = loadLibrarySearchDllLoadDir | loadLibrarySearchDefaultDirs;
            }
            else
            {
                flags = loadLibrarySearchDefaultDirs;
            }

            IntPtr handler = LoadLibraryExWindows(dllname, flags);

            if (handler == IntPtr.Zero)
            {
                //Try again with the '0' flags. 
                //The first attempt above may fail, if the native dll is within a folder in the PATH environment variable.
                //The call below will also search for folders in PATH environment variable.
                handler = LoadLibraryExWindows(dllname, 0);
            }

            return handler;
        }

        /// <summary>
        /// Maps the specified executable module into the address space of the calling process.
        /// </summary>
        /// <param name="dllname">The name of the dll</param>
        /// <returns>The handle to the library</returns>
        public static IntPtr LoadLibrary(String dllname)
        {
#if UNITY_EDITOR_WIN
            return LoadLibraryWindows(dllname);
#else
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return LoadLibraryWindows(dllname);
            }
            else
            {
                IntPtr handler = Dlopen(dllname, 0x00102); // 0x00002 == RTLD_NOW, 0x00100 = RTL_GLOBAL
                if (handler == IntPtr.Zero)
                {
                    System.Diagnostics.Trace.WriteLine(String.Format("Failed to use dlopen to load {0}", dllname));
                }

                return handler;
            }
#endif
        }

        [DllImport("Kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(
            [MarshalAs(UnmanagedType.LPStr)]
            String fileName,
            IntPtr hFile,
            int dwFlags);

        [DllImport("dl", EntryPoint = "dlopen")]
        private static extern IntPtr Dlopen(
            [MarshalAs(UnmanagedType.LPStr)]
            String dllname, int mode);

        /*
        /// <summary>
        /// Decrements the reference count of the loaded dynamic-link library (DLL). When the reference count reaches zero, the module is unmapped from the address space of the calling process and the handle is no longer valid
        /// </summary>
        /// <param name="handle">The handle to the library</param>
        /// <returns>If the function succeeds, the return value is true. If the function fails, the return value is false.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeLibrary(IntPtr handle);
        */

        /// <summary>
        /// Adds a directory to the search path used to locate DLLs for the application
        /// </summary>
        /// <param name="path">The directory to be searched for DLLs</param>
        /// <returns>True if success</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectory(String path);


        /*
        /// <summary>
        /// Adds a directory to the search path used to locate DLLs for the application
        /// </summary>
        /// <param name="path">The directory to be searched for DLLs</param>
        /// <returns>True if success</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddDllDirectory(String path);
        */

        /// <summary>
        /// Find the loaded assembly with the specific assembly name
        /// </summary>
        /// <param name="assembleName"></param>
        /// <returns></returns>
        public static System.Reflection.Assembly FindAssembly(String assembleName)
        {
            try
            {
                System.Reflection.Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                foreach (System.Reflection.Assembly asm in asms)
                {
                    if (asm.ManifestModule.Name.Equals(assembleName))
                        return asm;
                }
            }
            catch (Exception exception)
            {
                Trace.WriteLine(String.Format("FindAssembly({0}) failed: {1}", assembleName, exception.Message));
            }
            return null;
        }
    }
}
