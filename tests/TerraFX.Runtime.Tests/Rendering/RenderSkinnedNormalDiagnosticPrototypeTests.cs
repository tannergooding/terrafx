// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RenderSkinnedNormalDiagnosticPrototypeTests
{
    private static GraphBufferVersion CreateBuffer(
        ulong resourceId,
        long byteLength,
        int structuredStride)
    {
        var description = new BufferDescription(
            byteLength,
            BufferUsage.Storage,
            GpuMemoryKind.DeviceLocal,
            structuredStride,
            "diagnostic recording prototype");
        return new GraphBufferVersion(
            1,
            resourceId,
            0,
            description,
            new GraphBufferRange(0, byteLength));
    }

    private static long MeasureBindingSetAllocation(ImmutableArray<BufferBinding> bindings)
    {
        _ = new BindingSet(bindings, [], []);
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var bindingSet = new BindingSet(bindings, [], []);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        GC.KeepAlive(bindingSet);
        return allocatedBytes;
    }

    private static long MeasureBufferBindingAllocation(GraphBufferVersion version)
    {
        _ = new BufferBinding(
            ShaderBindingGroup.Frame,
            2,
            ShaderResourceKind.StorageBuffer,
            version,
            new GraphBufferRange(0, version.Description.ByteLength));
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var binding = new BufferBinding(
            ShaderBindingGroup.Frame,
            2,
            ShaderResourceKind.StorageBuffer,
            version,
            new GraphBufferRange(0, version.Description.ByteLength));
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        GC.KeepAlive(binding);
        return allocatedBytes;
    }

    private static long MeasureFourBindingArrayAllocation()
    {
        _ = new BufferBinding[4];
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var bindings = new BufferBinding[4];
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        GC.KeepAlive(bindings);
        return allocatedBytes;
    }

    private static long MeasurePassConstruction(int drawCount, int meshBufferCount)
    {
        var vertexBuffers = new GraphBufferVersion[meshBufferCount];

        for (var index = 0; index < vertexBuffers.Length; index++)
        {
            vertexBuffers[index] = CreateBuffer(checked((ulong)(index + 1)), 96, 96);
        }

        var draws = new RenderMeshC0NormalDiagnosticDraw[drawCount];
        var membershipBuffer = CreateBuffer(checked((ulong)(meshBufferCount + 1)), checked(drawCount * 32L), 16);
        var paletteBuffer = CreateBuffer(checked((ulong)(meshBufferCount + 2)), 112, 112);
        var resultBuffer = CreateBuffer(checked((ulong)(meshBufferCount + 3)), checked(drawCount * 16L), 16);
        var suffix = new byte[8];

        for (var index = 0; index < draws.Length; index++)
        {
            draws[index] = new RenderMeshC0NormalDiagnosticDraw(
                new Guid(index + 1, 0, 0, suffix),
                index,
                index % meshBufferCount,
                PaletteBase: 0,
                ResultRow: index,
                TestedVertexCount: 3,
                WorkOffset: checked(index * 32L),
                WorkByteLength: 32);
        }

        _ = new RenderMeshC0NormalDiagnosticGraphPass(
            Guid.NewGuid(),
            Guid.NewGuid(),
            tickIndex: 1,
            draws,
            vertexBuffers,
            paletteBuffer,
            membershipBuffer,
            resultBuffer,
            expectedVertexBufferCount: meshBufferCount,
            storageBufferOffsetAlignment: 16);

        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        var pass = new RenderMeshC0NormalDiagnosticGraphPass(
            Guid.NewGuid(),
            Guid.NewGuid(),
            tickIndex: 1,
            draws,
            vertexBuffers,
            paletteBuffer,
            membershipBuffer,
            resultBuffer,
            expectedVertexBufferCount: meshBufferCount,
            storageBufferOffsetAlignment: 16);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        Assert.That(pass.ReadbackFootprint.ByteLength, Is.EqualTo(checked((nint)drawCount * 16)));
        Assert.That((long)pass.ReadbackFootprint.NativeAllocationBytes, Is.EqualTo(pass.ResultBuffer.Description.ByteLength));
        Assert.That(pass.ReadbackFootprint.TotalByteAllowance, Is.GreaterThan(pass.ResultBuffer.Description.ByteLength));
        GC.KeepAlive(pass);
        return allocatedBytes;
    }

    [Test]
    public void AsyncReturnCensusRecordsFourReturnFamilies()
    {
        var rendererSubmit = typeof(Renderer).GetMethod(
            nameof(Renderer.SubmitAsync),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(RenderSnapshot), typeof(RenderGraphPlan), typeof(System.Threading.CancellationToken)],
            modifiers: null)
            ?? throw new InvalidOperationException("The selected Renderer.SubmitAsync overload is unavailable.");
        var rendererSubmitCore = typeof(Renderer).GetMethod("SubmitAsyncCore", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Renderer.SubmitAsyncCore is unavailable.");
        (MethodInfo Method, bool HasAsyncStateMachine)[] methods = [
            (
                typeof(GraphicsDevice).GetMethod("RecordPassAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("GraphicsDevice.RecordPassAsync is unavailable."),
                true),
            (
                typeof(GraphicsDevice).GetMethod("SubmitGraphAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("GraphicsDevice.SubmitGraphAsync is unavailable."),
                true),
            (
                typeof(RenderGraphPlan).GetMethod("RecordPassCallbacksAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("RenderGraphPlan.RecordPassCallbacksAsync is unavailable."),
                true),
            (rendererSubmit, false),
            (rendererSubmitCore, true),
        ];

        TestContext.WriteLine($"Runtime={RuntimeInformation.FrameworkDescription}; Architecture={RuntimeInformation.ProcessArchitecture}");

        foreach (var (method, hasAsyncStateMachine) in methods)
        {
            Assert.That(method.ReturnType.IsGenericType, Is.True);
            Assert.That(method.ReturnType.GetGenericTypeDefinition(), Is.EqualTo(typeof(Task<>)));
            var asyncStateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>();
            MethodInfo bodyMethod;
            Type? stateMachineType = null;

            if (hasAsyncStateMachine)
            {
                stateMachineType = asyncStateMachine?.StateMachineType
                    ?? throw new InvalidOperationException($"Method {method.Name} has no async state-machine metadata.");
                bodyMethod = stateMachineType.GetMethod(
                    "MoveNext",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException($"State machine {stateMachineType} has no MoveNext method.");
            }
            else
            {
                Assert.That(asyncStateMachine, Is.Null);
                bodyMethod = method;
            }

            var body = bodyMethod.GetMethodBody()?.GetILAsByteArray()
                ?? throw new InvalidOperationException($"Method {bodyMethod.Name} has no managed IL body.");
            var assemblyPath = method.Module.Assembly.Location;
            var assemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)));
            var bodyHash = Convert.ToHexString(SHA256.HashData(body));

            TestContext.WriteLine(
                $"Method={method.DeclaringType?.FullName}.{method.Name}; "
                + $"Assembly={assemblyPath}; "
                + $"AssemblySha256={assemblyHash}; "
                + $"ReturnType={method.ReturnType.FullName}; "
                + $"StateMachine={stateMachineType?.FullName ?? "(none)"}; "
                + $"MethodBody={bodyMethod.Name}; "
                + $"MethodBodyLength={body.Length}; "
                + $"MethodBodySha256={bodyHash}");
        }
    }

    [Test]
    public void MeasuresDiagnosticBindingPlanAllocation()
    {
        Assert.That(GraphicsRecordingUseAdmission.IsSupportedProfile, Is.True);
        _ = MeasurePassConstruction(drawCount: 2, meshBufferCount: 1);
        var vertexBuffer = CreateBuffer(1001, 96, 96);
        var paletteBuffer = CreateBuffer(1002, 112, 112);
        var workBuffer = CreateBuffer(1003, 32, 16);
        var resultBuffer = CreateBuffer(1004, 16, 16);
        var inputBindings = ImmutableArray.Create(
            new BufferBinding(
                ShaderBindingGroup.Frame,
                0,
                ShaderResourceKind.StorageBuffer,
                vertexBuffer,
                new GraphBufferRange(0, vertexBuffer.Description.ByteLength)),
            new BufferBinding(
                ShaderBindingGroup.Frame,
                1,
                ShaderResourceKind.StorageBuffer,
                paletteBuffer,
                new GraphBufferRange(0, paletteBuffer.Description.ByteLength)),
            new BufferBinding(
                ShaderBindingGroup.Frame,
                2,
                ShaderResourceKind.StorageBuffer,
                workBuffer,
                new GraphBufferRange(0, workBuffer.Description.ByteLength)),
            new BufferBinding(
                ShaderBindingGroup.Frame,
                3,
                ShaderResourceKind.StorageBuffer,
                resultBuffer,
                new GraphBufferRange(0, resultBuffer.Description.ByteLength)));
        TestContext.WriteLine($"RecordedGraphicsCommand size: {Unsafe.SizeOf<RecordedGraphicsCommand>()}");
        TestContext.WriteLine($"StorageResourceUse size: {Unsafe.SizeOf<StorageResourceUse>()}");
        TestContext.WriteLine($"BufferBinding reference size: {Unsafe.SizeOf<BufferBinding>()}");
        TestContext.WriteLine($"BufferBinding allocation: {MeasureBufferBindingAllocation(vertexBuffer)} bytes");
        TestContext.WriteLine($"BindingSet + copied arrays allocation: {MeasureBindingSetAllocation(inputBindings)} bytes");
        TestContext.WriteLine($"Four-element BufferBinding[] allocation: {MeasureFourBindingArrayAllocation()} bytes");
        TestContext.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}/{RuntimeInformation.ProcessArchitecture}");

        var smallPassBytes = MeasurePassConstruction(drawCount: 2, meshBufferCount: 1);
        var smallPassCharge = RenderMeshC0NormalDiagnosticWork.CalculateKnownBindingPlanConstructionBytes(2, 1);
        Assert.That(smallPassCharge, Is.GreaterThanOrEqualTo(smallPassBytes));
        TestContext.WriteLine($"D=2,M=1: {smallPassBytes} bytes (charge {smallPassCharge})");

        var repeatedGeometryPassBytes = MeasurePassConstruction(drawCount: 1000, meshBufferCount: 1);
        var repeatedGeometryPassCharge = RenderMeshC0NormalDiagnosticWork.CalculateKnownBindingPlanConstructionBytes(1000, 1);
        Assert.That(repeatedGeometryPassCharge, Is.GreaterThanOrEqualTo(repeatedGeometryPassBytes));
        TestContext.WriteLine($"D=1000,M=1: {repeatedGeometryPassBytes} bytes (charge {repeatedGeometryPassCharge})");

        var distinctGeometryPassBytes = MeasurePassConstruction(drawCount: 1000, meshBufferCount: 1000);
        var distinctGeometryPassCharge = RenderMeshC0NormalDiagnosticWork.CalculateKnownBindingPlanConstructionBytes(1000, 1000);
        Assert.That(distinctGeometryPassCharge, Is.GreaterThanOrEqualTo(distinctGeometryPassBytes));
        TestContext.WriteLine($"D=1000,M=1000: {distinctGeometryPassBytes} bytes (charge {distinctGeometryPassCharge})");
    }
}
