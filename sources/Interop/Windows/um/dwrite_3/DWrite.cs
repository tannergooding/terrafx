// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

// Ported from um\dwrite_3.h in the Windows SDK for Windows 10.0.15063.0
// Original source is Copyright © Microsoft. All rights reserved.

using System;

namespace TerraFX.Interop
{
    public static partial class DWrite
    {
        #region DWRITE_E_* Constants
        /// <summary>A font resource could not be accessed because it was remote. This can happen when calling CreateFontFace on a non-local font or trying to measure/draw glyphs that are not downloaded yet.</summary>
        public const int DWRITE_E_REMOTEFONT = unchecked((int)0x8898500D);

        /// <summary>The download was canceled, which happens if the application calls IDWriteFontDownloadQueue::CancelDownload before they finish.</summary>
        public const int DWRITE_E_DOWNLOADCANCELLED = unchecked((int)0x8898500E);

        /// <summary>The download failed to complete because the remote resource is missing or the network is down.</summary>
        public const int DWRITE_E_DOWNLOADFAILED = unchecked((int)0x8898500F);

        /// <summary>A download request was not added or a download failed because there are too many active downloads.</summary>
        public const int DWRITE_E_TOOMANYDOWNLOADS = unchecked((int)0x88985010);
        #endregion

        #region IID_* Constants
        public static readonly Guid IID_IDWriteRenderingParams3 = new Guid(0xB7924BAA, 0x391B, 0x412A, 0x8C, 0x5C, 0xE4, 0x4C, 0xC2, 0xD8, 0x67, 0xDC);

        public static readonly Guid IID_IDWriteFactory3 = new Guid(0x9A1B41C3, 0xD3BB, 0x466A, 0x87, 0xFC, 0xFE, 0x67, 0x55, 0x6A, 0x3B, 0x65);

        public static readonly Guid IID_IDWriteFontSet = new Guid(0x53585141, 0xD9F8, 0x4095, 0x83, 0x21, 0xD7, 0x3C, 0xF6, 0xBD, 0x11, 0x6B);

        public static readonly Guid IID_IDWriteFontSetBuilder = new Guid(0x2F642AFE, 0x9C68, 0x4F40, 0xB8, 0xBE, 0x45, 0x74, 0x01, 0xAF, 0xCB, 0x3D);

        public static readonly Guid IID_IDWriteFontCollection1 = new Guid(0x53585141, 0xD9F8, 0x4095, 0x83, 0x21, 0xD7, 0x3C, 0xF6, 0xBD, 0x11, 0x6C);

        public static readonly Guid IID_IDWriteFontFamily1 = new Guid(0xDA20D8EF, 0x812A, 0x4C43, 0x98, 0x02, 0x62, 0xEC, 0x4A, 0xBD, 0x7A, 0xDF);

        public static readonly Guid IID_IDWriteFontList1 = new Guid(0xDA20D8EF, 0x812A, 0x4C43, 0x98, 0x02, 0x62, 0xEC, 0x4A, 0xBD, 0x7A, 0xDE);

        public static readonly Guid IID_IDWriteFontFaceReference = new Guid(0x5E7FA7CA, 0xDDE3, 0x424C, 0x89, 0xF0, 0x9F, 0xCD, 0x6F, 0xED, 0x58, 0xCD);

        public static readonly Guid IID_IDWriteFont3 = new Guid(0x29748ED6, 0x8C9C, 0x4A6A, 0xBE, 0x0B, 0xD9, 0x12, 0xE8, 0x53, 0x89, 0x44);

        public static readonly Guid IID_IDWriteFontFace3 = new Guid(0xD37D7598, 0x09BE, 0x4222, 0xA2, 0x36, 0x20, 0x81, 0x34, 0x1C, 0xC1, 0xF2);

        public static readonly Guid IID_IDWriteStringList = new Guid(0xCFEE3140, 0x1157, 0x47CA, 0x8B, 0x85, 0x31, 0xBF, 0xCF, 0x3F, 0x2D, 0x0E);

        public static readonly Guid IID_IDWriteFontDownloadListener = new Guid(0xB06FE5B9, 0x43EC, 0x4393, 0x88, 0x1B, 0xDB, 0xE4, 0xDC, 0x72, 0xFD, 0xA7);

        public static readonly Guid IID_IDWriteFontDownloadQueue = new Guid(0xB71E6052, 0x5AEA, 0x4FA3, 0x83, 0x2E, 0xF6, 0x0D, 0x43, 0x1F, 0x7E, 0x91);

        public static readonly Guid IID_IDWriteGdiInterop1 = new Guid(0x4556BE70, 0x3ABD, 0x4F70, 0x90, 0xBE, 0x42, 0x17, 0x80, 0xA6, 0xF5, 0x15);

        public static readonly Guid IID_IDWriteTextFormat2 = new Guid(0xF67E0EDD, 0x9E3D, 0x4ECC, 0x8C, 0x32, 0x41, 0x83, 0x25, 0x3D, 0xFE, 0x70);

        public static readonly Guid IID_IDWriteTextLayout3 = new Guid(0x07DDCD52, 0x020E, 0x4DE8, 0xAC, 0x33, 0x6C, 0x95, 0x3D, 0x83, 0xF9, 0x2D);

        public static readonly Guid IID_IDWriteColorGlyphRunEnumerator1 = new Guid(0x7C5F86DA, 0xC7A1, 0x4F05, 0xB8, 0xE1, 0x55, 0xA1, 0x79, 0xFE, 0x5A, 0x35);

        public static readonly Guid IID_IDWriteFontFace4 = new Guid(0x27F2A904, 0x4EB8, 0x441D, 0x96, 0x78, 0x05, 0x63, 0xF5, 0x3E, 0x3E, 0x2F);

        public static readonly Guid IID_IDWriteFactory4 = new Guid(0x4B0B5BD3, 0x0797, 0x4549, 0x8A, 0xC5, 0xFE, 0x91, 0x5C, 0xC5, 0x38, 0x56);

        public static readonly Guid IID_IDWriteFontSetBuilder1 = new Guid(0x3FF7715F, 0x3CDC, 0x4DC6, 0x9B, 0x72, 0xEC, 0x56, 0x21, 0xDC, 0xCA, 0xFD);

        public static readonly Guid IID_IDWriteAsyncResult = new Guid(0xCE25F8FD, 0x863B, 0x4D13, 0x96, 0x51, 0xC1, 0xF8, 0x8D, 0xC7, 0x3F, 0xE2);

        public static readonly Guid IID_IDWriteRemoteFontFileStream = new Guid(0x4DB3757A, 0x2C72, 0x4ED9, 0xB2, 0xB6, 0x1A, 0xBA, 0xBE, 0x1A, 0xFF, 0x9C);

        public static readonly Guid IID_IDWriteRemoteFontFileLoader = new Guid(0x68648C83, 0x6EDE, 0x46C0, 0xAB, 0x46, 0x20, 0x08, 0x3A, 0x88, 0x7F, 0xDE);

        public static readonly Guid IID_IDWriteInMemoryFontFileLoader = new Guid(0xDC102F47, 0xA12D, 0x4B1C, 0x82, 0x2D, 0x9E, 0x11, 0x7E, 0x33, 0x04, 0x3F);

        public static readonly Guid IID_IDWriteFactory5 = new Guid(0x958DB99A, 0xBE2A, 0x4F09, 0xAF, 0x7D, 0x65, 0x18, 0x98, 0x03, 0xD1, 0xD3);
        #endregion
    }
}
