// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Entities;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    private const string DiagnosticCode = "DegenerateSkinnedNormal";
    private const string DiagnosticMessage = "Skinned mesh vertices required the dominant-bone normal fallback.";
    private const string DiagnosticOperation = "Renderer.SubmitAsync";

    internal async Task<GpuPipeline> CreateSkinnedNormalDiagnosticComputePipelineAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not create Mesh diagnostic pipelines.");
        var binaryFormat = device.Capabilities.Backend switch {
            GraphicsBackend.D3D12 => ShaderBinaryFormat.Dxil,
            GraphicsBackend.Vulkan => ShaderBinaryFormat.SpirV,
            _ => throw new NotSupportedException("The selected GraphicsDevice has no Mesh diagnostic shader artifact."),
        };
        using var shaderStorage = LoadShaderBytecode(
            "TerraFX.Rendering.Shaders.SkinnedNormalDiagnostic.compute.",
            binaryFormat);
        using var shaderBytecode = shaderStorage.AcquireReadLease(0, shaderStorage.Length);
        return await device.CreatePipelineAsync(
            new ComputePipelineDescription(
                new ShaderBytecodeInput(
                    ShaderStage.Compute,
                    binaryFormat,
                    shaderBytecode,
                    RenderMeshC0ShaderReflection.SkinnedNormalDiagnosticCompute),
                bindingLayoutVersion: 1),
            cancellationToken).ConfigureAwait(false);
    }

    private void PublishSkinnedNormalDiagnostics(
        RenderSnapshot snapshot,
        ImmutableArray<RenderSkinnedNormalDiagnostic> diagnostics,
        long submissionSequence,
        long completedFence,
        uint deviceId,
        uint deviceEpoch,
        uint surfaceEpoch,
        uint swapchainEpoch,
        WorldDiagnosticFundingControl fundingControl)
    {
        var serviceOwner = _diagnosticsServiceOwner
            ?? throw new InvalidOperationException("The native Renderer has no borrowed host diagnostic service owner.");
        ArgumentNullException.ThrowIfNull(fundingControl);
        var attributionAccount = fundingControl.Account;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(submissionSequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(completedFence);
        ArgumentOutOfRangeException.ThrowIfZero(deviceId);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        var surfaceEpochIsZero = surfaceEpoch == 0;
        var swapchainEpochIsZero = swapchainEpoch == 0;

        if (surfaceEpochIsZero != swapchainEpochIsZero
            || (attributionAccount.WorldGuid != snapshot.WorldGuid)
            || (attributionAccount.WorldOwnerId == 0))
        {
            throw new InvalidOperationException("The diagnostic source or presentation generation is invalid.");
        }

        Span<int> contextStringLengths = [
            "SnapshotId".Length, 32,
            "DrawId".Length, 32,
            "SubmissionSequence".Length, 19,
            "CompletedFence".Length, 19,
            "TestedVertexCount".Length, 6,
            "DegenerateVertexCount".Length, 6,
        ];

        for (var index = 0; index < diagnostics.Length; index++)
        {
            var result = diagnostics[index];

            if (result.DegenerateVertexCount == 0)
            {
                continue;
            }

            if ((result.SnapshotId != snapshot.SnapshotId)
                || (result.WorldGuid != snapshot.WorldGuid)
                || (result.TickIndex != snapshot.TickIndex)
                || (result.DrawId == Guid.Empty)
                || (result.TestedVertexCount <= 0)
                || (result.DegenerateVertexCount < 0)
                || (result.DegenerateVertexCount > result.TestedVertexCount))
            {
                throw new InvalidOperationException("A validated skinned-normal diagnostic row no longer matches its frozen source.");
            }

            var admissionBytes = DiagnosticsService.GetNoncoalescingWarningAdmissionBytes(
                DiagnosticCode.Length,
                DiagnosticOperation.Length,
                DiagnosticMessage.Length,
                commandIdentityLength: 110,
                contextStringLengths: contextStringLengths);

            if (!serviceOwner.TryReserveAttributionPayload(fundingControl, admissionBytes, out var reservation))
            {
                continue;
            }

            try
            {
                _ = WriteReservedSkinnedNormalDiagnostic(
                    snapshot,
                    result,
                    submissionSequence,
                    completedFence,
                    deviceId,
                    deviceEpoch,
                    surfaceEpoch,
                    swapchainEpoch,
                    serviceOwner,
                    fundingControl,
                    ref reservation);
            }
            finally
            {
                reservation.Dispose();
                serviceOwner.CompleteAttributionReservation(fundingControl);
            }
        }
    }

    private bool WriteReservedSkinnedNormalDiagnostic(
        RenderSnapshot snapshot,
        RenderSkinnedNormalDiagnostic result,
        long submissionSequence,
        long completedFence,
        uint deviceId,
        uint deviceEpoch,
        uint surfaceEpoch,
        uint swapchainEpoch,
        WorldDiagnosticsServiceOwner serviceOwner,
        WorldDiagnosticFundingControl fundingControl,
        ref DiagnosticAttributionReservation reservation)
    {
        var snapshotId = snapshot.SnapshotId.ToString("N");
        var drawId = result.DrawId.ToString("N");
        var submissionSequenceText = submissionSequence.ToString(CultureInfo.InvariantCulture);
        var completedFenceText = completedFence.ToString(CultureInfo.InvariantCulture);
        var testedVertexCount = result.TestedVertexCount.ToString(CultureInfo.InvariantCulture);
        var degenerateVertexCount = result.DegenerateVertexCount.ToString(CultureInfo.InvariantCulture);
        var commandIdentity = string.Concat(
            "submission/",
            submissionSequenceText,
            "/snapshot/",
            snapshotId,
            "/draw/",
            drawId);
        ImmutableArray<DiagnosticContextEntry> context = [
            new DiagnosticContextEntry("SnapshotId", snapshotId),
            new DiagnosticContextEntry("DrawId", drawId),
            new DiagnosticContextEntry("SubmissionSequence", submissionSequenceText),
            new DiagnosticContextEntry("CompletedFence", completedFenceText),
            new DiagnosticContextEntry("TestedVertexCount", testedVertexCount),
            new DiagnosticContextEntry("DegenerateVertexCount", degenerateVertexCount),
        ];
        var diagnostic = new Diagnostic(
            DiagnosticCode,
            DiagnosticSeverity.Warning,
            serviceOwner.Diagnostics.GetTimestamp(),
            _hostId,
            snapshot.WorldGuid,
            deviceId,
            surfaceId: null,
            deviceEpoch,
            (surfaceEpoch == 0) ? null : surfaceEpoch,
            (swapchainEpoch == 0) ? null : swapchainEpoch,
            snapshot.TickIndex,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity,
            DiagnosticOperation,
            nativeResult: null,
            DiagnosticMessage,
            context);
        return serviceOwner.WriteNoncoalescingWarning(fundingControl, diagnostic, ref reservation);
    }
}
