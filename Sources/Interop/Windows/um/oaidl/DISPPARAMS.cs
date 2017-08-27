// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// Ported from um\oaidl.h in the Windows SDK for Windows 10.0.15063.0
// Original source is Copyright © Microsoft. All rights reserved.

using System.Runtime.InteropServices;

namespace TerraFX.Interop
{
    public /* blittable */ unsafe struct DISPPARAMS
    {
        #region Fields
        [ComAliasName("VARIANTARG[]")]
        public VARIANT* rgvarg;

        [ComAliasName("DISPID[]")]
        public int* rgdispidNamedArgs;

        [ComAliasName("UINT")]
        public uint cArgs;

        [ComAliasName("UINT")]
        public uint cNamedArgs;
        #endregion
    }
}
