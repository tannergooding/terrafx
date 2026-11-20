// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Assets;

internal readonly struct AssetBoundedFailureFact
{
    internal AssetBoundedFailureFact(
        AssetBoundedFailureStage stage,
        AssetBoundedFailureKind kind,
        AssetBoundedNativeApi nativeApi,
        int nativeError,
        Guid schemaId,
        uint fieldTag,
        AssetBoundedFileDisposition fileDisposition)
    {
        Stage = stage;
        Kind = kind;
        NativeApi = nativeApi;
        NativeError = nativeError;
        SchemaId = schemaId;
        FieldTag = fieldTag;
        FileDisposition = fileDisposition;
    }

    internal uint FieldTag { get; }

    internal AssetBoundedFileDisposition FileDisposition { get; }

    internal AssetBoundedFailureKind Kind { get; }

    internal AssetBoundedNativeApi NativeApi { get; }

    internal int NativeError { get; }

    internal Guid SchemaId { get; }

    internal AssetBoundedFailureStage Stage { get; }
}
