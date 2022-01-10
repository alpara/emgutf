﻿//----------------------------------------------------------------------------
//  Copyright (C) 2004-2022 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Emgu.TF.Util;
using System.Runtime.InteropServices;

namespace Emgu.TF
{
    /// <summary>
    /// TString type
    /// </summary>
    public class TString : UnmanagedObject
    {
        private static readonly int _sizeOfTString;

        static TString()
        {
            _sizeOfTString = TfInvoke.tfeTStringTypeSize();
        }

        internal static int TypeSize
        {
            get { return _sizeOfTString; }
        }

        private bool _needDispose;

        /// <summary>
        /// Create a new empty TString
        /// </summary>
        public TString()
        {
            _needDispose = true;
            _ptr = TfInvoke.tfeTStringCreate();
        }

        /// <summary>
        /// Create a new TString from input data
        /// </summary>
        /// <param name="data">input data</param>
        public TString(byte[] data)
            : this()
        {
            //IntPtr strPtr = Marshal.StringToHGlobalAuto(s);
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            TfInvoke.tfeTStringCopy(_ptr, handle.AddrOfPinnedObject(), data.Length);
            handle.Free();
            //Marshal.FreeHGlobal(strPtr);
        }

        internal TString(IntPtr ptr, bool needDispose)
        {
            _ptr = ptr;
            _needDispose = needDispose;
        }

        /// <summary>
        /// Returns a const char pointer to the start of the underlying string. The
        /// underlying character buffer may not be null-terminated.
        /// </summary>
        public IntPtr DataPointer
        {
            get { return TfInvoke.tfeTStringGetDataPointer(_ptr); }
        }

        /// <summary>
        /// Get the type of the TString
        /// </summary>
        public TStringType Type
        {
            get { return TfInvoke.tfeTStringGetType(_ptr); }
        }

        /// <summary>
        /// Returns the size of the string.
        /// </summary>
        public int Size
        {
            get { return TfInvoke.tfeTStringGetSize(_ptr); }
        }

        /// <summary>
        /// Return the capacity of the string
        /// </summary>
        public int Capacity
        {
            get { return TfInvoke.tfeTStringGetCapacity(_ptr); }
        }

        /// <summary>
        /// Get the raw data as an array of byte.
        /// </summary>
        public byte[] Data
        {
            get
            {
                byte[] bytes = new byte[Size];
                Marshal.Copy(DataPointer, bytes, 0, bytes.Length);
                return bytes;
            }
        }

        /// <summary>
        /// Release all the unmanaged memory associated with this Buffer
        /// </summary>
        protected override void DisposeObject()
        {
            if (IntPtr.Zero != _ptr)
            {
                if (_needDispose)
                    TfInvoke.tfeTStringDealloc(_ptr);

                _ptr = IntPtr.Zero;
            }
        }

        /// <summary>
        /// TString type
        /// </summary>
        public enum TStringType
        {
            /// <summary>
            /// Small
            /// </summary>
            Small = 0x00,
            /// <summary>
            /// Large
            /// </summary>
            Large = 0x01,
            /// <summary>
            /// Offset
            /// </summary>
            Offset = 0x02,
            /// <summary>
            /// View
            /// </summary>
            View = 0x03,
            /// <summary>
            /// Mask
            /// </summary>
            Mask = 0x03
        }
    }

    /// <summary>
    /// Entry points to the native Tensorflow library.
    /// </summary>
    public static partial class TfInvoke
    {

        [DllImport(ExternLibrary, CallingConvention = TfInvoke.TfCallingConvention)]
        internal static extern IntPtr tfeTStringCreate();

        [DllImport(ExternLibrary, CallingConvention = TfInvoke.TfCallingConvention)]
        internal static extern void tfeTStringInit(IntPtr ptr);

        [DllImport(ExternLibrary, CallingConvention = TfInvoke.TfCallingConvention)]
        internal static extern void tfeTStringDealloc(IntPtr buffer);

        [DllImport(ExternLibrary, CallingConvention = TfInvoke.TfCallingConvention)]
        internal static extern void tfeTStringCopy(IntPtr dst, IntPtr src, int len);

        [DllImport(ExternLibrary, CallingConvention = TfInvoke.TfCallingConvention)]
        internal static extern IntPtr tfeTStringGetDataPointer(IntPtr str);

        [DllImport(ExternLibrary, CallingConvention = TfInvoke.TfCallingConvention)]
        internal static extern int tfeTStringGetSize(IntPtr str);

        [DllImport(ExternLibrary, CallingConvention = TfInvoke.TfCallingConvention)]
        internal static extern int tfeTStringGetCapacity(IntPtr str);
        [DllImport(ExternLibrary, CallingConvention = TfInvoke.TfCallingConvention)]
        internal static extern TString.TStringType tfeTStringGetType(IntPtr str);

        [DllImport(ExternLibrary, CallingConvention = TfInvoke.TfCallingConvention)]
        internal static extern int tfeTStringTypeSize();
    }
}
