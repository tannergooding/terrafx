// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

internal enum BoundedStaticMeshFailureStage : byte
{
    None = 0,
    Bootstrap = 1,
    Registration = 2,
    WorkerStart = 3,
    Open = 4,
    NativeFileCheck = 5,
    Hash = 6,
    Header = 7,
    Profile = 8,
    Directory = 9,
    PayloadRead = 10,
    Decode = 11,
    Reference = 12,
    Publication = 13,
    CandidateFileRelease = 14,
    DisplacedOrFinalFileRelease = 15,
    HashProviderRelease = 16,
    IoEventRelease = 17,
    RequestEventRelease = 18,
    ReplyEventRelease = 19,
    WorkerJoin = 20,
}
