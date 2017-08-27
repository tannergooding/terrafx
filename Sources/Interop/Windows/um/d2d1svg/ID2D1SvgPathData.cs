// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// Ported from um\d2d1svg.h in the Windows SDK for Windows 10.0.15063.0
// Original source is Copyright © Microsoft. All rights reserved.

using System;
using System.Runtime.InteropServices;
using System.Security;

namespace TerraFX.Interop
{
    /// <summary>Interface describing SVG path data. Path data can be set as the 'd' attribute on a 'path' element. The path data set is factored into two arrays. The segment data array stores all numbers and the commands array stores the set of commands. Unlike the string data set in the d attribute, each command in this representation uses a fixed number of elements in the segment data array. Therefore, the path 'M 0,0 100,0 0,100 Z' is represented as: 'M0,0 L100,0 L0,100 Z'. This is split into two arrays, with the segment data containing '0,0 100,0 0,100', and the commands containing 'M L L Z'.</summary>
    [Guid("C095E4F4-BB98-43D6-9745-4D1B84EC9888")]
    public /* blittable */ unsafe struct ID2D1SvgPathData
    {
        #region Fields
        public readonly Vtbl* lpVtbl;
        #endregion

        #region Delegates
        /// <summary>Removes data from the end of the segment data array.</summary>
        /// <param name="dataCount">Specifies how much data to remove.</param>
        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = false, ThrowOnUnmappableChar = false)]
        [return: ComAliasName("HRESULT")]
        public /* static */ delegate int RemoveSegmentDataAtEnd(
            [In] ID2D1SvgPathData* This,
            [In, ComAliasName("UINT32")] uint dataCount
        );

        /// <summary>Updates the segment data array. Existing segment data not updated by this method are preserved. The array is resized larger if necessary to accomodate the new segment data.</summary>
        /// <param name="data">The data array.</param>
        /// <param name="dataCount">The number of data to update.</param>
        /// <param name="startIndex">The index at which to begin updating segment data. Must be less than or equal to the size of the segment data array.</param>
        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = false, ThrowOnUnmappableChar = false)]
        [return: ComAliasName("HRESULT")]
        public /* static */ delegate int UpdateSegmentData(
            [In] ID2D1SvgPathData* This,
            [In, ComAliasName("FLOAT[]")] float* data,
            [In, ComAliasName("UINT32")] uint dataCount,
            [In, ComAliasName("UINT32")] uint startIndex = 0
        );

        /// <summary>Gets data from the segment data array.</summary>
        /// <param name="data">Buffer to contain the segment data array.</param>
        /// <param name="dataCount">The element count of the buffer.</param>
        /// <param name="startIndex">The index of the first segment data to retrieve.</param>
        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = false, ThrowOnUnmappableChar = false)]
        [return: ComAliasName("HRESULT")]
        public /* static */ delegate int GetSegmentData(
            [In] ID2D1SvgPathData* This,
            [Out, ComAliasName("FLOAT[]")] float* data,
            [In, ComAliasName("UINT32")] uint dataCount,
            [In, ComAliasName("UINT32")] uint startIndex = 0
        );

        /// <summary>Gets the size of the segment data array.</summary>
        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = false, ThrowOnUnmappableChar = false)]
        [return: ComAliasName("UINT32")]
        public /* static */ delegate uint GetSegmentDataCount(
            [In] ID2D1SvgPathData* This
        );

        /// <summary>Removes commands from the end of the commands array.</summary>
        /// <param name="commandsCount">Specifies how many commands to remove.</param>
        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = false, ThrowOnUnmappableChar = false)]
        [return: ComAliasName("HRESULT")]
        public /* static */ delegate int RemoveCommandsAtEnd(
            [In] ID2D1SvgPathData* This,
            [In, ComAliasName("UINT32")] uint commandsCount
        );

        /// <summary>Updates the commands array. Existing commands not updated by this method are preserved. The array is resized larger if necessary to accomodate the new commands.</summary>
        /// <param name="commands">The commands array.</param>
        /// <param name="commandsCount">The number of commands to update.</param>
        /// <param name="startIndex">The index at which to begin updating commands. Must be less than or equal to the size of the commands array.</param>
        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = false, ThrowOnUnmappableChar = false)]
        [return: ComAliasName("HRESULT")]
        public /* static */ delegate int UpdateCommands(
            [In] ID2D1SvgPathData* This,
            [In, ComAliasName("D2D1_SVG_PATH_COMMAND")] D2D1_SVG_PATH_COMMAND* commands,
            [In, ComAliasName("UINT32")] uint commandsCount,
            [In, ComAliasName("UINT32")] uint startIndex = 0
        );

        /// <summary>Gets commands from the commands array.</summary>
        /// <param name="commands">Buffer to contain the commands</param>
        /// <param name="commandsCount">The element count of the buffer.</param>
        /// <param name="startIndex">The index of the first commands to retrieve.</param>
        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = false, ThrowOnUnmappableChar = false)]
        [return: ComAliasName("HRESULT")]
        public /* static */ delegate int GetCommands(
            [In] ID2D1SvgPathData* This,
            [Out, ComAliasName("D2D1_SVG_PATH_COMMAND[]")] D2D1_SVG_PATH_COMMAND* commands,
            [In, ComAliasName("UINT32")] uint commandsCount,
            [In, ComAliasName("UINT32")] uint startIndex = 0
        );

        /// <summary>Gets the size of the commands array.</summary>
        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = false, ThrowOnUnmappableChar = false)]
        [return: ComAliasName("UINT32")]
        public /* static */ delegate uint GetCommandsCount(
            [In] ID2D1SvgPathData* This
        );

        /// <summary>Creates a path geometry object representing the path data.</summary>
        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, BestFitMapping = false, CharSet = CharSet.Unicode, SetLastError = false, ThrowOnUnmappableChar = false)]
        [return: ComAliasName("HRESULT")]
        public /* static */ delegate int CreatePathGeometry(
            [In] ID2D1SvgPathData* This,
            [In] D2D1_FILL_MODE fillMode,
            [Out] ID2D1PathGeometry1** pathGeometry
        );
        #endregion

        #region Structs
        public /* blittable */ struct Vtbl
        {
            #region Fields
            public ID2D1SvgAttribute.Vtbl BaseVtbl;

            public IntPtr RemoveSegmentDataAtEnd;

            public IntPtr UpdateSegmentData;

            public IntPtr GetSegmentData;

            public IntPtr GetSegmentDataCount;

            public IntPtr RemoveCommandsAtEnd;

            public IntPtr UpdateCommands;

            public IntPtr GetCommands;

            public IntPtr GetCommandsCount;

            public IntPtr CreatePathGeometry;
            #endregion
        }
        #endregion
    }
}
