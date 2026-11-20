// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Rendering;

internal readonly struct BoundedStaticMeshFailureFact
{
    internal BoundedStaticMeshFailureFact(
        BoundedStaticMeshFailureStage stage,
        BoundedStaticMeshFailureKind kind,
        BoundedStaticMeshNativeApi nativeApi,
        int nativeError,
        Guid schemaId,
        uint fieldTag,
        BoundedStaticMeshFileDisposition fileDisposition)
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

    internal BoundedStaticMeshFileDisposition FileDisposition { get; }

    internal BoundedStaticMeshFailureKind Kind { get; }

    internal BoundedStaticMeshNativeApi NativeApi { get; }

    internal int NativeError { get; }

    internal Guid SchemaId { get; }

    internal BoundedStaticMeshFailureStage Stage { get; }
}
