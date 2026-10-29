# Public API baseline

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

This chapter owns public types, constructors, and member signatures. Subsystem chapters own their behavioral contracts; the informative [31](31-public-api-consumer-guide.md) is a consumer guide, not another declaration source. A disagreement is a specification defect, not a precedence rule permitting incompatible implementations.

Declarations use .NET 11/C# 15 with nullable enabled. They specify member shape rather than implementation bodies; compiled consumer evidence is recorded separately in [37](37-completeness-and-audit.md). [02](02-dotnet-api-memory-and-ownership.md) owns global library conventions and preview-feature policy.

## 1. Declaration rules and defaults

Apply [02](02-dotnet-api-memory-and-ownership.md#1-public-library-conventions)'s constructor/type/failure rules, [02 section 3](02-dotnet-api-memory-and-ownership.md#3-leases-and-asynchronous-operations)'s borrowing/completion rules, and [05](05-owned-buffers-and-internal-collections.md)'s storage contracts. Default initializers below must match [04](04-capacity-and-workload-profiles.md) and the relevant subsystem owner. Operation-specific paragraphs define payload applicability, parameter validation, and completion boundaries not expressed by a signature.

Apply [02's public type boundary](02-dotnet-api-memory-and-ownership.md#public-type-boundary) to every reachable signature/payload type independently of its private implementation. The current declarations do not expose `Stopwatch` or `TimeProvider`; this revision adds no timing wrapper or public source-injection requirement. `ImmutableArray<T>` is accepted foundational array interchange and retains its existing signatures. Dictionary lookup contracts use `IReadOnlyDictionary<TKey, TValue>`, not a required `ImmutableDictionary` representation; constrained dictionary generics remain a scenario-specific hot-path option, not a new generic receipt framework. Each owning implementation unit verifies the boundary semantics, including raw timing members such as `Diagnostic.MonotonicTimestamp` with explicit clock-domain/unit/frequency/conversion meaning rather than provider inference.

The host/world commit boundary is chapter [13](13-jobs-and-frame-pipeline.md)'s irrevocable stage-9 publication marker. Physics local commit, external subscriber delivery, graphics queue completion, presentation completion, audio command application, voice end, and durable save replacement remain separate observations; no API below collapses them.

[02's integer policy](02-dotnet-api-memory-and-ownership.md#integer-types-and-boundaries) applies throughout these declarations and their supporting value contracts: `int` bounded counts/indices/dimensions, `nint` contiguous CPU ranges, and `long` logical budgets/extents/revisions/timelines. Negative domain inputs reject before mutation/admission; domain-specific zero rules and checked overflow/narrowing remain authoritative. Schema/importer/layout version numbers use validated `int` even when their wire representation is unsigned. Ordinary counters do not wrap; exhaustion uses the owning failure protocol before publishing work.

Remaining unsigned members deliberately retain frozen identity/epoch/slot domains, contact clip bits, context/collision masks or exact unsigned tolerance ratios. Internal `resourceId` values are opaque full-width identifiers, not arithmetic offsets. No integer-signature revision changes a geometry component layout, native/shader packer or permanent binary tag/wire kind. Source/native adapters validate signed representability rather than reinterpreting incoming high bits.

## 2. Namespace placement and issuers

[29](29-project-setup-build-and-deployment.md#1-repository-and-assembly-dag) owns assembly/namespace placement and allowed dependencies. Declaration namespaces below must match that table; no type adds a forbidden edge.

`TerraFX.Entities`, `TerraFX.Scenes`, `TerraFX.Animation`, `TerraFX.Rendering`, `TerraFX.UI`, `TerraFX.Integration` and `TerraFX.Hosting` are namespaces in the one `TerraFX.Runtime` assembly/package, not seven physical products. Public declarations retain their names/signatures; enforce logical dependency and issuer rules as well as the physical DAG. Rebuild consumers/generated metadata for changed assembly identity rather than claiming precompiled compatibility.

`SpatialSettings`, `ChunkIndex2/3`, `SpatialPosition2/3`, `SpatialFrame2/3`, `SpatialBounds2/3`, `RelativePose2/3`, `WindowHandle` and the three shared callable interfaces live in the root `TerraFX` namespace. Geometry/Angle lives in `TerraFX.Numerics`, buffers in `TerraFX.Buffers`, bounded object pools in `TerraFX.Pooling`, and diagnostics in `TerraFX.Diagnostics`; all are owned by the `TerraFX.Core` assembly/package, not a matching `.Core` namespace or additional projects. The `TerraFX.UI` namespace can name a window without a logical dependency on `TerraFX.Platform`; Runtime's physical Platform reference is for hosting. `EngineHost` creates the `World`; scene activation takes `World`, never `EngineContext`.

Engine-issued `WindowHandle`/`MonitorHandle` values have internal constructors; issuance is authorized only to the platform owner, but internal accessibility itself is not a per-caller restriction. `Entity` likewise has an internal constructor, with semantic construction authority only on `World.IssueEntity`. `AssetStore` has a private constructor and the public `CreateAsync` factory; the host calls that same factory for its independently owned instance, while standalone callers own stores they create. `DiagnosticsService` is internally constructed and owned either by `EngineHost` or a standalone `World`; hosted Worlds borrow the host-owned service through the admitted construction plan. These issuer paths add no forbidden physical or logical dependency and do not expose a public handle/owner fabrication API.

## 3. Core geometry, spatial values, and buffers

### Geometry

The six `TerraFX.Numerics` types and their full arithmetic/equality/formatting contracts are owned by [06](06-owned-geometry-and-numerics-interchange.md), with the method and kernel revision in [08](08-binary32-kernels-and-predicates.md). Their storage/interchange profile is `EngineGeometryBinary32Rows2`, separate from coordinate profile `LeftHandedYUpForwardZ1`, spatial profile `FloatChunk32V2`, and runtime/ISA profile. `IsFinite()` returns component masks; the argumentless versus scalar-value `All`/`Any`/`None` overloads follow [06](06-owned-geometry-and-numerics-interchange.md#2-representation-and-defaults). The changed predicate signatures require an API-major/consumer rebuild, not a changed component layout.

[06's representation contract](06-owned-geometry-and-numerics-interchange.md#2-representation-and-defaults) fixes private storage: BCL `Vector2`/`Vector3`, direct `Vector128<float>` for engine `Vector4`/`Quaternion`, and three engine `Vector2`/four engine `Vector4` matrix rows. C# default sequential layout suffices; declaration notation below omits private fields and is not an instruction to generate extra auto-property backing. These private backing requirements add no intrinsic-vector interchange or required `Plane` API; the public interchange and inversion names are specified separately below.

Forward conversion names are `AsSystem` followed by the corresponding type name. Reverse conversion names are `AsTerraFX` followed by that name, in the `SystemNumericsExtensions` class below. The extensions live in `TerraFX.Numerics`, not the BCL namespace, and belong to `TerraFX.Core`. Receivers are by value; either extension syntax or fully qualified static invocation is valid. No reverse factory aliases, conversion operators or public intrinsic-vector conversions are added. Both families remain safe-callable with `AllowUnsafeBlocks = false` at the consumer; implementation bitcasts are confined to reviewed unsafe body blocks under [02](02-dotnet-api-memory-and-ownership.md). `As` does not introduce a borrowed alias.

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace TerraFX.Numerics;

public enum AngleUnit
{
    Degrees = 0,
    Turns = 1,
    Radians = 2
}

public readonly struct Angle
    : IComparable<Angle>,
      IEquatable<Angle>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IAdditionOperators<Angle, Angle, Angle>,
      ISubtractionOperators<Angle, Angle, Angle>,
      IUnaryNegationOperators<Angle, Angle>,
      IMultiplyOperators<Angle, float, Angle>,
      IDivisionOperators<Angle, float, Angle>,
      IEqualityOperators<Angle, Angle, bool>,
      IComparisonOperators<Angle, Angle, bool>
{
    public Angle(float value, AngleUnit unit);
    public float Value { get; }
    public AngleUnit Unit { get; }
    public static IComparer<Angle> CreateComparer(AngleUnit unit);
    public static Angle FromDegrees(float value);
    public static Angle FromTurns(float value);
    public static Angle FromRadians(float value);
    public float ToDegrees();
    public float ToTurns();
    public float ToRadians();
    public (float Sin, float Cos) SinCos();
    public static Angle operator +(Angle left, Angle right);
    public static Angle operator -(Angle left, Angle right);
    public static Angle operator -(Angle value);
    public static Angle operator *(Angle value, float scalar);
    public static Angle operator *(float scalar, Angle value);
    public static Angle operator /(Angle value, float scalar);
    public static bool operator <(Angle left, Angle right);
    public static bool operator <=(Angle left, Angle right);
    public static bool operator ==(Angle left, Angle right);
    public static bool operator !=(Angle left, Angle right);
    public static bool operator >(Angle left, Angle right);
    public static bool operator >=(Angle left, Angle right);
    public int CompareTo(Angle other);
    public bool Equals(Angle other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider);
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
}

public readonly struct Vector2
    : IComparable<Vector2>,
      IEquatable<Vector2>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IAdditionOperators<Vector2, Vector2, Vector2>,
      ISubtractionOperators<Vector2, Vector2, Vector2>,
      IUnaryNegationOperators<Vector2, Vector2>,
      IMultiplyOperators<Vector2, float, Vector2>,
      IDivisionOperators<Vector2, float, Vector2>,
      IEqualityOperators<Vector2, Vector2, bool>,
      IComparisonOperators<Vector2, Vector2, bool>,
      IMultiplyOperators<Vector2, Vector2, Vector2>,
      IDivisionOperators<Vector2, Vector2, Vector2>
{
    public Vector2(float x, float y);
    public Vector2(float value);
    public float X { get; }
    public float Y { get; }
    public Vector2 IsFinite();
    public bool All();
    public bool All(float value);
    public bool Any();
    public bool Any(float value);
    public bool None();
    public bool None(float value);
    public static Vector2 Zero { get; }
    public static Vector2 One { get; }
    public static Vector2 UnitX { get; }
    public static Vector2 UnitY { get; }
    public System.Numerics.Vector2 AsSystemVector2();
    public static Vector2 operator +(Vector2 left, Vector2 right);
    public static Vector2 operator -(Vector2 left, Vector2 right);
    public static Vector2 operator -(Vector2 value);
    public static Vector2 operator *(Vector2 left, Vector2 right);
    public static Vector2 operator /(Vector2 left, Vector2 right);
    public static Vector2 operator *(Vector2 value, float scalar);
    public static Vector2 operator *(float scalar, Vector2 value);
    public static Vector2 operator /(Vector2 value, float scalar);
    public static bool operator ==(Vector2 left, Vector2 right);
    public static bool operator !=(Vector2 left, Vector2 right);
    public static float Dot(Vector2 left, Vector2 right);
    public static float LengthSquared(Vector2 value);
    public static float Length(Vector2 value);
    public static float DistanceSquared(Vector2 left, Vector2 right);
    public static float Distance(Vector2 left, Vector2 right);
    public static Vector2 Lerp(Vector2 first, Vector2 second, float amount);
    public static Vector2 Normalize(Vector2 value);
    public static bool TryNormalize(Vector2 value, out Vector2 normalized);
    public static Vector2 TransformPoint(Vector2 point, Matrix3x2 matrix);
    public static Vector2 TransformDirection(Vector2 direction, Matrix3x2 matrix);
    public static bool operator <(Vector2 left, Vector2 right);
    public static bool operator <=(Vector2 left, Vector2 right);
    public static bool operator >(Vector2 left, Vector2 right);
    public static bool operator >=(Vector2 left, Vector2 right);
    public int CompareTo(Vector2 other);
    public bool Equals(Vector2 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider);
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
}

public readonly struct Vector3
    : IComparable<Vector3>,
      IEquatable<Vector3>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IAdditionOperators<Vector3, Vector3, Vector3>,
      ISubtractionOperators<Vector3, Vector3, Vector3>,
      IUnaryNegationOperators<Vector3, Vector3>,
      IMultiplyOperators<Vector3, float, Vector3>,
      IDivisionOperators<Vector3, float, Vector3>,
      IEqualityOperators<Vector3, Vector3, bool>,
      IComparisonOperators<Vector3, Vector3, bool>,
      IMultiplyOperators<Vector3, Vector3, Vector3>,
      IDivisionOperators<Vector3, Vector3, Vector3>
{
    public Vector3(float x, float y, float z);
    public Vector3(float value);
    public Vector3(Vector2 xy, float z);
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public Vector3 IsFinite();
    public bool All();
    public bool All(float value);
    public bool Any();
    public bool Any(float value);
    public bool None();
    public bool None(float value);
    public static Vector3 Zero { get; }
    public static Vector3 One { get; }
    public static Vector3 UnitX { get; }
    public static Vector3 UnitY { get; }
    public static Vector3 UnitZ { get; }
    public System.Numerics.Vector3 AsSystemVector3();
    public static Vector3 operator +(Vector3 left, Vector3 right);
    public static Vector3 operator -(Vector3 left, Vector3 right);
    public static Vector3 operator -(Vector3 value);
    public static Vector3 operator *(Vector3 left, Vector3 right);
    public static Vector3 operator /(Vector3 left, Vector3 right);
    public static Vector3 operator *(Vector3 value, float scalar);
    public static Vector3 operator *(float scalar, Vector3 value);
    public static Vector3 operator /(Vector3 value, float scalar);
    public static bool operator ==(Vector3 left, Vector3 right);
    public static bool operator !=(Vector3 left, Vector3 right);
    public static float Dot(Vector3 left, Vector3 right);
    public static float LengthSquared(Vector3 value);
    public static float Length(Vector3 value);
    public static float DistanceSquared(Vector3 left, Vector3 right);
    public static float Distance(Vector3 left, Vector3 right);
    public static Vector3 Lerp(Vector3 first, Vector3 second, float amount);
    public static Vector3 Normalize(Vector3 value);
    public static bool TryNormalize(Vector3 value, out Vector3 normalized);
    public static Vector3 Cross(Vector3 left, Vector3 right);
    public static Vector3 TransformPoint(Vector3 point, Matrix4x4 matrix);
    public static Vector3 TransformDirection(Vector3 direction, Matrix4x4 matrix);
    public static Vector3 Rotate(Vector3 value, Quaternion rotation);
    public static bool operator <(Vector3 left, Vector3 right);
    public static bool operator <=(Vector3 left, Vector3 right);
    public static bool operator >(Vector3 left, Vector3 right);
    public static bool operator >=(Vector3 left, Vector3 right);
    public int CompareTo(Vector3 other);
    public bool Equals(Vector3 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider);
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
}

public readonly struct Vector4
    : IComparable<Vector4>,
      IEquatable<Vector4>,
      IFormattable,
      ISpanFormattable,
      IUtf8SpanFormattable,
      IAdditionOperators<Vector4, Vector4, Vector4>,
      ISubtractionOperators<Vector4, Vector4, Vector4>,
      IUnaryNegationOperators<Vector4, Vector4>,
      IMultiplyOperators<Vector4, float, Vector4>,
      IDivisionOperators<Vector4, float, Vector4>,
      IEqualityOperators<Vector4, Vector4, bool>,
      IComparisonOperators<Vector4, Vector4, bool>,
      IMultiplyOperators<Vector4, Vector4, Vector4>,
      IDivisionOperators<Vector4, Vector4, Vector4>
{
    public Vector4(float x, float y, float z, float w);
    public Vector4(float value);
    public Vector4(Vector3 xyz, float w);
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float W { get; }
    public Vector4 IsFinite();
    public bool All();
    public bool All(float value);
    public bool Any();
    public bool Any(float value);
    public bool None();
    public bool None(float value);
    public static Vector4 Zero { get; }
    public static Vector4 One { get; }
    public static Vector4 UnitX { get; }
    public static Vector4 UnitY { get; }
    public static Vector4 UnitZ { get; }
    public static Vector4 UnitW { get; }
    public System.Numerics.Vector4 AsSystemVector4();
    public static Vector4 operator +(Vector4 left, Vector4 right);
    public static Vector4 operator -(Vector4 left, Vector4 right);
    public static Vector4 operator -(Vector4 value);
    public static Vector4 operator *(Vector4 left, Vector4 right);
    public static Vector4 operator /(Vector4 left, Vector4 right);
    public static Vector4 operator *(Vector4 value, float scalar);
    public static Vector4 operator *(float scalar, Vector4 value);
    public static Vector4 operator /(Vector4 value, float scalar);
    public static bool operator ==(Vector4 left, Vector4 right);
    public static bool operator !=(Vector4 left, Vector4 right);
    public static float Dot(Vector4 left, Vector4 right);
    public static float LengthSquared(Vector4 value);
    public static float Length(Vector4 value);
    public static float DistanceSquared(Vector4 left, Vector4 right);
    public static float Distance(Vector4 left, Vector4 right);
    public static Vector4 Lerp(Vector4 first, Vector4 second, float amount);
    public static Vector4 Normalize(Vector4 value);
    public static bool TryNormalize(Vector4 value, out Vector4 normalized);
    public static Vector4 Transform(Vector4 value, Matrix4x4 matrix);
    public static bool operator <(Vector4 left, Vector4 right);
    public static bool operator <=(Vector4 left, Vector4 right);
    public static bool operator >(Vector4 left, Vector4 right);
    public static bool operator >=(Vector4 left, Vector4 right);
    public int CompareTo(Vector4 other);
    public bool Equals(Vector4 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider);
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
}

public readonly struct Quaternion : IEquatable<Quaternion>, IFormattable, ISpanFormattable, IUtf8SpanFormattable, IAdditionOperators<Quaternion, Quaternion, Quaternion>, ISubtractionOperators<Quaternion, Quaternion, Quaternion>, IUnaryNegationOperators<Quaternion, Quaternion>, IMultiplyOperators<Quaternion, float, Quaternion>, IEqualityOperators<Quaternion, Quaternion, bool>, IMultiplyOperators<Quaternion, Quaternion, Quaternion>
{
    public Quaternion(float x, float y, float z, float w);
    public Quaternion(Vector3 vector, float scalar);
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float W { get; }
    public Vector4 IsFinite();
    public static Quaternion Identity { get; }
    public System.Numerics.Quaternion AsSystemQuaternion();
    public static Quaternion operator +(Quaternion left, Quaternion right);
    public static Quaternion operator -(Quaternion left, Quaternion right);
    public static Quaternion operator -(Quaternion value);
    public static Quaternion operator *(Quaternion value, float scalar);
    public static Quaternion operator *(float scalar, Quaternion value);
    public static Quaternion operator *(Quaternion left, Quaternion right);
    public static bool operator ==(Quaternion left, Quaternion right);
    public static bool operator !=(Quaternion left, Quaternion right);
    public static float Dot(Quaternion left, Quaternion right);
    public static Quaternion Multiply(Quaternion left, Quaternion right);
    public static Quaternion Compose(Quaternion first, Quaternion second);
    public static Quaternion Conjugate(Quaternion value);
    public static Quaternion Normalize(Quaternion value);
    public static bool TryNormalize(Quaternion value, out Quaternion normalized);
    public static Quaternion Invert(Quaternion value);
    public static bool TryInvert(Quaternion value, out Quaternion inverse);
    public static Quaternion CreateFromAxisAngle(Vector3 unitAxis, float radians);
    public static Quaternion CreateFromAxisAngle(Vector3 unitAxis, Angle angle);
    public static Quaternion Lerp(Quaternion first, Quaternion second, float amount);
    public static Quaternion Slerp(Quaternion first, Quaternion second, float amount);
    public bool Equals(Quaternion other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider);
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
}

public readonly struct Matrix3x2 : IEquatable<Matrix3x2>, IFormattable, ISpanFormattable, IUtf8SpanFormattable, IMultiplyOperators<Matrix3x2, Matrix3x2, Matrix3x2>, IEqualityOperators<Matrix3x2, Matrix3x2, bool>
{
    public Matrix3x2(Vector2 row1, Vector2 row2, Vector2 row3);
    public Vector2 Row1 { get; }
    public Vector2 Row2 { get; }
    public Vector2 Row3 { get; }
    public float this[int row, int column] { get; }
    public Matrix3x2 IsFinite();
    public bool All();
    public bool All(float value);
    public bool Any();
    public bool Any(float value);
    public bool None();
    public bool None(float value);
    public static Matrix3x2 Identity { get; }
    public System.Numerics.Matrix3x2 AsSystemMatrix3x2();
    public static Matrix3x2 CreateTranslation(Vector2 translation);
    public static Matrix3x2 CreateScale(Vector2 scale);
    public static Matrix3x2 CreateScale(float scale);
    public static Matrix3x2 CreateRotation(float radians);
    public static Matrix3x2 CreateRotation(Angle angle);
    public static Matrix3x2 Multiply(Matrix3x2 left, Matrix3x2 right);
    public static Matrix3x2 operator *(Matrix3x2 left, Matrix3x2 right);
    public static Matrix3x2 Invert(Matrix3x2 value);
    public static bool TryInvert(Matrix3x2 value, out Matrix3x2 inverse);
    public static bool operator ==(Matrix3x2 left, Matrix3x2 right);
    public static bool operator !=(Matrix3x2 left, Matrix3x2 right);
    public bool Equals(Matrix3x2 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider);
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
}

public readonly struct Matrix4x4 : IEquatable<Matrix4x4>, IFormattable, ISpanFormattable, IUtf8SpanFormattable, IMultiplyOperators<Matrix4x4, Matrix4x4, Matrix4x4>, IEqualityOperators<Matrix4x4, Matrix4x4, bool>
{
    public Matrix4x4(Vector4 row1, Vector4 row2, Vector4 row3, Vector4 row4);
    public Vector4 Row1 { get; }
    public Vector4 Row2 { get; }
    public Vector4 Row3 { get; }
    public Vector4 Row4 { get; }
    public float this[int row, int column] { get; }
    public Matrix4x4 IsFinite();
    public bool All();
    public bool All(float value);
    public bool Any();
    public bool Any(float value);
    public bool None();
    public bool None(float value);
    public static Matrix4x4 Identity { get; }
    public System.Numerics.Matrix4x4 AsSystemMatrix4x4();
    public static Matrix4x4 CreateTranslation(Vector3 translation);
    public static Matrix4x4 CreateScale(Vector3 scale);
    public static Matrix4x4 CreateScale(float scale);
    public static Matrix4x4 CreateRotationX(float radians);
    public static Matrix4x4 CreateRotationY(float radians);
    public static Matrix4x4 CreateRotationZ(float radians);
    public static Matrix4x4 CreateRotationX(Angle angle);
    public static Matrix4x4 CreateRotationY(Angle angle);
    public static Matrix4x4 CreateRotationZ(Angle angle);
    public static Matrix4x4 CreateFromQuaternion(Quaternion rotation);
    public static Matrix4x4 CreateFromAxisAngle(Vector3 unitAxis, float radians);
    public static Matrix4x4 CreateFromAxisAngle(Vector3 unitAxis, Angle angle);
    public static Matrix4x4 CreatePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlane, float farPlane);
    public static Matrix4x4 CreateInfinitePerspectiveFieldOfView(float fieldOfView, float aspectRatio, float nearPlane);
    public static Matrix4x4 CreateOrthographic(float width, float height, float nearPlane, float farPlane);
    public static Matrix4x4 CreateLookTo(Vector3 position, Vector3 direction, Vector3 up);
    public static Matrix4x4 CreateLookAt(Vector3 position, Vector3 target, Vector3 up);
    public static bool TryCreateLookTo(Vector3 position, Vector3 direction, Vector3 up, out Matrix4x4 view);
    public static bool TryCreateLookAt(Vector3 position, Vector3 target, Vector3 up, out Matrix4x4 view);
    public static Matrix4x4 Multiply(Matrix4x4 left, Matrix4x4 right);
    public static Matrix4x4 operator *(Matrix4x4 left, Matrix4x4 right);
    public static Matrix4x4 Transpose(Matrix4x4 value);
    public static Matrix4x4 Invert(Matrix4x4 value);
    public static bool TryInvert(Matrix4x4 value, out Matrix4x4 inverse);
    public static bool TryDecompose(Matrix4x4 value, out Vector3 scale, out Quaternion rotation, out Vector3 translation);
    public static bool operator ==(Matrix4x4 left, Matrix4x4 right);
    public static bool operator !=(Matrix4x4 left, Matrix4x4 right);
    public bool Equals(Matrix4x4 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
    public string ToString([StringSyntax("NumericFormat")] string? format, IFormatProvider? formatProvider);
    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax("NumericFormat")] ReadOnlySpan<char> format, IFormatProvider? provider);
}

public static class SystemNumericsExtensions
{
    public static Vector2 AsTerraFXVector2(this System.Numerics.Vector2 value);
    public static Vector3 AsTerraFXVector3(this System.Numerics.Vector3 value);
    public static Vector4 AsTerraFXVector4(this System.Numerics.Vector4 value);
    public static Quaternion AsTerraFXQuaternion(this System.Numerics.Quaternion value);
    public static Matrix3x2 AsTerraFXMatrix3x2(this System.Numerics.Matrix3x2 value);
    public static Matrix4x4 AsTerraFXMatrix4x4(this System.Numerics.Matrix4x4 value);
}
```

`Compose(first, second)` applies first, then second, and equals `Multiply(second, first)`; `Quaternion` multiplication itself remains conventional Hamilton multiplication. Matrix and vector operations retain ordinary row-vector algebra. `TransformPoint`, `TransformDirection`, `Rotate`, and homogeneous `Vector4.Transform` are distinct contracts; surface normals use inverse transpose and normalization, not a direction alias. The six `AsSystem`-prefixed forward members and six `AsTerraFX` reverse extensions copy every component bit and do not change basis, validate, normalize, or alias storage. Raw constructors preserve IEEE values; raw default quaternion/matrix values are zero and are not valid identity poses.

### Spatial values and frames

Spatial configuration and frame identity are explicit. There is no global double origin or world-wide double position; a point is an `int` chunk plus binary32 offset, and a `SpatialFrame` bounds local arithmetic.

```csharp
using System;
using System.Numerics;

namespace TerraFX;

public interface IAction<TArgument> where TArgument : allows ref struct
{
    void Invoke(scoped TArgument argument);
}

public interface IFunc<TArgument, TResult> where TArgument : allows ref struct
{
    TResult Invoke(scoped TArgument argument);
}

public interface IReadOnlyRefFunc<TArgument, TResult> where TArgument : allows ref struct
{
    TResult Invoke(scoped in TArgument argument);
}

public readonly struct ChunkIndex2
    : IComparable<ChunkIndex2>,
      IEquatable<ChunkIndex2>,
      IComparisonOperators<ChunkIndex2, ChunkIndex2, bool>
{
    public ChunkIndex2(int x, int y);
    public int X { get; }
    public int Y { get; }
    public int CompareTo(ChunkIndex2 other);
    public bool Equals(ChunkIndex2 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(ChunkIndex2 left, ChunkIndex2 right);
    public static bool operator !=(ChunkIndex2 left, ChunkIndex2 right);
    public static bool operator <(ChunkIndex2 left, ChunkIndex2 right);
    public static bool operator <=(ChunkIndex2 left, ChunkIndex2 right);
    public static bool operator >(ChunkIndex2 left, ChunkIndex2 right);
    public static bool operator >=(ChunkIndex2 left, ChunkIndex2 right);
}

public readonly struct ChunkIndex3
    : IComparable<ChunkIndex3>,
      IEquatable<ChunkIndex3>,
      IComparisonOperators<ChunkIndex3, ChunkIndex3, bool>
{
    public ChunkIndex3(int x, int y, int z);
    public int X { get; }
    public int Y { get; }
    public int Z { get; }
    public int CompareTo(ChunkIndex3 other);
    public bool Equals(ChunkIndex3 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(ChunkIndex3 left, ChunkIndex3 right);
    public static bool operator !=(ChunkIndex3 left, ChunkIndex3 right);
    public static bool operator <(ChunkIndex3 left, ChunkIndex3 right);
    public static bool operator <=(ChunkIndex3 left, ChunkIndex3 right);
    public static bool operator >(ChunkIndex3 left, ChunkIndex3 right);
    public static bool operator >=(ChunkIndex3 left, ChunkIndex3 right);
}

public readonly struct SpatialSettings : IEquatable<SpatialSettings>
{
    public SpatialSettings(int metersPerUnitExponent, int chunkSizeExponent);
    public int MetersPerUnitExponent { get; }
    public int ChunkSizeExponent { get; }
    public float ChunkWidth { get; }
    public static SpatialSettings Default { get; }
    public bool Equals(SpatialSettings other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(SpatialSettings left, SpatialSettings right);
    public static bool operator !=(SpatialSettings left, SpatialSettings right);
}

public readonly struct SpatialProfileId : IEquatable<SpatialProfileId>, IFormattable
{
    public SpatialProfileId(Guid value);
    public static SpatialProfileId Default { get; }
    public Guid Value { get; }
    public bool IsValid { get; }
    public bool Equals(SpatialProfileId other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
    public string ToString(string? format, IFormatProvider? formatProvider);
    public static bool operator ==(SpatialProfileId left, SpatialProfileId right);
    public static bool operator !=(SpatialProfileId left, SpatialProfileId right);
}

public static class SpatialProfiles
{
    public static void Register(SpatialProfileId id, SpatialSettings settings);
    public static bool TryGetSettings(SpatialProfileId id, out SpatialSettings settings);
}

public readonly struct SpatialPosition2 : IEquatable<SpatialPosition2>
{
    public SpatialPosition2(ChunkIndex2 chunk, TerraFX.Numerics.Vector2 offset);
    public ChunkIndex2 Chunk { get; }
    public TerraFX.Numerics.Vector2 Offset { get; }
    public bool Equals(SpatialPosition2 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(SpatialPosition2 left, SpatialPosition2 right);
    public static bool operator !=(SpatialPosition2 left, SpatialPosition2 right);
}

public readonly struct SpatialPosition3 : IEquatable<SpatialPosition3>
{
    public SpatialPosition3(ChunkIndex3 chunk, TerraFX.Numerics.Vector3 offset);
    public ChunkIndex3 Chunk { get; }
    public TerraFX.Numerics.Vector3 Offset { get; }
    public bool Equals(SpatialPosition3 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(SpatialPosition3 left, SpatialPosition3 right);
    public static bool operator !=(SpatialPosition3 left, SpatialPosition3 right);
}

public readonly struct SpatialFrame2
{
    public SpatialFrame2(uint ownerId, SpatialProfileId profileId, SpatialSettings settings, SpatialPosition2 origin, long revision, TerraFX.Numerics.Vector2 minimum, TerraFX.Numerics.Vector2 maximum);
    public uint OwnerId { get; }
    public SpatialProfileId ProfileId { get; }
    public SpatialSettings Settings { get; }
    public SpatialPosition2 Origin { get; }
    public long Revision { get; }
    public TerraFX.Numerics.Vector2 Minimum { get; }
    public TerraFX.Numerics.Vector2 Maximum { get; }
}

public readonly struct SpatialFrame3
{
    public SpatialFrame3(uint ownerId, SpatialProfileId profileId, SpatialSettings settings, SpatialPosition3 origin, long revision, TerraFX.Numerics.Vector3 minimum, TerraFX.Numerics.Vector3 maximum);
    public uint OwnerId { get; }
    public SpatialProfileId ProfileId { get; }
    public SpatialSettings Settings { get; }
    public SpatialPosition3 Origin { get; }
    public long Revision { get; }
    public TerraFX.Numerics.Vector3 Minimum { get; }
    public TerraFX.Numerics.Vector3 Maximum { get; }
}

public readonly struct SpatialBounds2
{
    public SpatialBounds2(SpatialPosition2 minimum, SpatialPosition2 maximum, SpatialSettings settings);
    public SpatialPosition2 Minimum { get; }
    public SpatialPosition2 Maximum { get; }
    public SpatialSettings Settings { get; }
}

public readonly struct SpatialBounds3
{
    public SpatialBounds3(SpatialPosition3 minimum, SpatialPosition3 maximum, SpatialSettings settings);
    public SpatialPosition3 Minimum { get; }
    public SpatialPosition3 Maximum { get; }
    public SpatialSettings Settings { get; }
}

public readonly struct RelativePose2
{
    public RelativePose2(TerraFX.Numerics.Vector2 localPosition, float angle, SpatialFrame2 frame);
    public TerraFX.Numerics.Vector2 LocalPosition { get; }
    public float Angle { get; }
    public SpatialFrame2 Frame { get; }
}

public readonly struct RelativePose3
{
    public RelativePose3(TerraFX.Numerics.Vector3 localPosition, TerraFX.Numerics.Quaternion rotation, SpatialFrame3 frame);
    public TerraFX.Numerics.Vector3 LocalPosition { get; }
    public TerraFX.Numerics.Quaternion Rotation { get; }
    public SpatialFrame3 Frame { get; }
}

public readonly struct SpatialRay3 : IEquatable<SpatialRay3>
{
    public SpatialRay3(SpatialPosition3 origin, TerraFX.Numerics.Vector3 unitDirection, float maximumDistance);
    public SpatialPosition3 Origin { get; }
    public TerraFX.Numerics.Vector3 Direction { get; }
    public float MaxDistance { get; }
    public bool Equals(SpatialRay3 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(SpatialRay3 left, SpatialRay3 right);
    public static bool operator !=(SpatialRay3 left, SpatialRay3 right);
}

public readonly struct SpatialRay2 : IEquatable<SpatialRay2>
{
    public SpatialRay2(SpatialPosition2 origin, TerraFX.Numerics.Vector2 unitDirection, float maximumDistance);
    public SpatialPosition2 Origin { get; }
    public TerraFX.Numerics.Vector2 Direction { get; }
    public float MaxDistance { get; }
    public bool Equals(SpatialRay2 other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(SpatialRay2 left, SpatialRay2 right);
    public static bool operator !=(SpatialRay2 left, SpatialRay2 right);
}

public static class SpatialMath
{
    public static float ConvertQuantity(float value, int sourceExponent, int targetExponent, int lengthDimension);
    public static float ConvertQuantity(long numerator, long denominator, int sourceExponent, int targetExponent, int lengthDimension);
    public static SpatialPosition2 Normalize(SpatialPosition2 position, SpatialSettings settings);
    public static SpatialPosition3 Normalize(SpatialPosition3 position, SpatialSettings settings);
    public static SpatialPosition2 ConvertPosition(SpatialPosition2 position, SpatialProfileId sourceProfileId, SpatialSettings sourceSettings, SpatialProfileId targetProfileId, SpatialSettings targetSettings);
    public static SpatialPosition3 ConvertPosition(SpatialPosition3 position, SpatialProfileId sourceProfileId, SpatialSettings sourceSettings, SpatialProfileId targetProfileId, SpatialSettings targetSettings);
    public static bool TryGetDisplacement(SpatialPosition2 from, SpatialPosition2 to, SpatialSettings settings, float maximumDistance, out TerraFX.Numerics.Vector2 displacement);
    public static bool TryGetDisplacement(SpatialPosition3 from, SpatialPosition3 to, SpatialSettings settings, float maximumDistance, out TerraFX.Numerics.Vector3 displacement);
    public static bool TryToFrameRelative(SpatialPosition2 position, SpatialFrame2 frame, out TerraFX.Numerics.Vector2 relativePosition);
    public static bool TryToFrameRelative(SpatialPosition3 position, SpatialFrame3 frame, out TerraFX.Numerics.Vector3 relativePosition);
}
```

`SpatialMath.ConvertQuantity` follows [07's bounded exact scalar conversion](07-spatial-coordinates-and-indexing.md): the float overload consumes the exact finite binary32 dyadic, while the `long` numerator/positive denominator overload consumes the exact rational literal. Both apply the complete dimensional decimal ratio before one nearest-even binary32 rounding, admit source/target exponents `[-30, 30]` and length dimension `[-3, 3]`, and throw `ArgumentOutOfRangeException` for invalid parameters or `OverflowException` for a result rounded to infinity. Nonfinite float input throws `ArgumentException`. Successful subnormals and correctly signed underflow zero are allowed by the converter; the consuming field retains its own domain admission. Float signed zero is preserved; exact zero numerator returns positive zero. Core owns the bounded private arithmetic; consumers need no internal/test friendship or public arbitrary-precision type.

`SpatialProfileId(Guid value)` rejects `Guid.Empty`; zero initialization is invalid. [07](07-spatial-coordinates-and-indexing.md) owns the fixed Default GUID, registration/freeze rules, capacity and settings. `Register` is idempotent for identical declarations; conflicts throw `ArgumentException` and exhaustion throws `InvalidOperationException` before mutation. `TryGetSettings` returns false only for an unregistered valid ID, with an unusable default out value. IDs supplied with raw positions are caller declarations, not provenance.

`SpatialPosition2` contains only `ChunkIndex2` (8 bytes) and `Vector2 Offset` (8 bytes), for exactly 16 bytes. `SpatialPosition3` contains only `ChunkIndex3` (12 bytes) and `Vector3 Offset` (12 bytes), for exactly 24 bytes. Neither type stores settings, owner, frame, or profile metadata. Constructors copy the raw cell and float offsets; they reject nonfinite offsets but do not canonicalize or claim an interpretation. Offset range, aliases, and unit meaning are evaluated only by operations given explicit `SpatialSettings`.

`ChunkIndex2/3`, `SpatialSettings`, `SpatialPosition2/3`, and `SpatialRay2/3` equality compares their stored fields; position and ray equality is representation equality, not coordinate equivalence across aliases or settings. Equality never normalizes a position or infers its profile. Operations that compare or convert spatial coordinates require explicit settings/profile inputs.

`SpatialSettings` validates its unit fields once in the explicit constructor. Its defaults, ranges, closed geometric endpoint and unique persistent encoding are defined in [07](07-spatial-coordinates-and-indexing.md). `Normalize` produces that persistent form; bounds/query witnesses retain the owning closed-border semantics.

`TryGetDisplacement` interprets both raw positions under its explicit settings; false means only no nearby displacement admitted by `maximumDistance`. `TryToFrameRelative` uses the supplied frame settings; false means only outside that frame's extent. Invalid inputs or integer overflow throw. `ConvertPosition` resolves both registered profile/settings declarations and applies [07](07-spatial-coordinates-and-indexing.md)'s exact conversion and checked rounded carries. Invalid/unregistered or mismatched declarations throw `ArgumentException`; cell overflow throws `OverflowException`. None of these signatures can infer a metadata-free position's origin.

`SpatialFrame2/3` constructors reject zero owner IDs, invalid profile IDs, zero revisions, noncanonical origins, nonfinite or unordered extents, and settings that disagree with the registered profile. Bounds constructors compare exact spatial coordinates under their explicit settings, preserve closed shared-border endpoints, and reject unordered bounds; they do not flatten chunk cells. Relative poses pair local geometry/rotation with the exact dimensional frame and reject nonfinite local values or invalid rotations. Frames, snapshots, and cross-world conversion records carry the applicable owner/profile identity, settings, and frame revision; raw spatial positions do not. A frame change publishes a new revision and replaces derived caches only; it does not translate or rewrite persistent positions, root/local poses, velocity, force, relative anchors, or proxy ownership.

### Unmanaged buffers

The buffer implementation contract is [05](05-owned-buffers-and-internal-collections.md). It deliberately provides no public pointer, escaping span, `MemoryManager<T>`, implicit conversion, or disposable value-container.

```csharp
using System;
using System.Collections.Immutable;

namespace TerraFX.Buffers;

public sealed class BufferAllocationOptions
{
    public BufferAllocationOptions(nint maximumAllocationBytes, nint alignment);
    public nint MaximumAllocationBytes { get; }
    public nint Alignment { get; }
}

public sealed class UnmanagedBuffer<T> : IDisposable where T : unmanaged
{
    private UnmanagedBuffer();
    public static UnmanagedBuffer<T> Allocate(nint length, BufferAllocationOptions options);
    public nint Length { get; }
    public nint ByteLength { get; }
    public BufferReadLease<T> AcquireRead(nint offset, nint length);
    public BufferWriteLease<T> AcquireWrite(nint offset, nint length);
    public void CopyTo(nint sourceOffset, Span<T> destination);
    public void CopyFrom(ReadOnlySpan<T> source, nint destinationOffset);
    public void Dispose();
}

public sealed class OwnedBuffer<T> : IDisposable where T : unmanaged
{
    internal OwnedBuffer(UnmanagedBuffer<T> storage);
    public nint Length { get; }
    public nint ByteLength { get; }
    public BufferReadLease<T> AcquireReadLease(nint offset, nint length);
    public void Dispose();
}

public sealed class BufferReadLease<T> : IDisposable where T : unmanaged
{
    internal BufferReadLease(UnmanagedBuffer<T> owner, nint offset, nint length);
    public nint Length { get; }
    public BufferReadLease<T> Retain();
    public void UseWindow<TAction>(nint relativeOffset, int length, scoped ref TAction action)
        where TAction : struct, IAction<ReadOnlySpan<T>>, allows ref struct;
    public void Dispose();
}

public sealed class BufferWriteLease<T> : IDisposable where T : unmanaged
{
    internal BufferWriteLease(UnmanagedBuffer<T> owner, nint offset, nint length);
    public nint Length { get; }
    public void UseWindow<TAction>(nint relativeOffset, int length, scoped ref TAction action)
        where TAction : struct, IAction<Span<T>>, allows ref struct;
    public void Dispose();
}

public sealed class BufferChain<T> : IDisposable where T : unmanaged
{
    public BufferChain(ImmutableArray<BufferReadLease<T>> segments);
    public long Length { get; }
    public int SegmentCount { get; }
    public BufferChain<T> Retain();
    public void CopyTo(long sourceOffset, Span<T> destination);
    public bool VisitWindows<TVisitor>(scoped ref TVisitor visitor)
        where TVisitor : struct, IFunc<ReadOnlySpan<T>, bool>, allows ref struct;
    public void Dispose();
}
```

The bounded reusable-object pool is a Core-owned semantic child namespace, not another assembly/package. [05](05-owned-buffers-and-internal-collections.md#7-first-class-bounded-object-pools) owns its state machine, failure and affinity rules. Tokens are not disposable owners or payload getters.

```csharp
using System;
using TerraFX;

namespace TerraFX.Pooling;

public interface IPoolPolicy<T> where T : class
{
    T Create();
    void Reset(T value);
    void Destroy(T value);
}

public readonly struct PoolRental<T> where T : class
{
    internal PoolRental(ObjectPool<T> owner, int slot, uint generation);
}

public readonly struct PoolSnapshot
{
    internal PoolSnapshot(int maximumCapacity, int createdCount, int availableCount,
        int rentedCount, int returningCount, int quarantinedCount, int retiredCount,
        int rentalHighWater, long misses, bool isClosed, bool isDrained);
    public int MaximumCapacity { get; }
    public int CreatedCount { get; }
    public int AvailableCount { get; }
    public int RentedCount { get; }
    public int ReturningCount { get; }
    public int QuarantinedCount { get; }
    public int RetiredCount { get; }
    public int RentalHighWater { get; }
    public long Misses { get; }
    public bool IsClosed { get; }
    public bool IsDrained { get; }
}

public sealed class ObjectPool<T> : IDisposable where T : class
{
    public ObjectPool(int maximumCapacity, IPoolPolicy<T> policy);
    public PoolSnapshot Snapshot { get; }
    public bool IsClosed { get; }
    public bool IsDrained { get; }
    public void Prewarm(int availableCount);
    public PoolRental<T> Rent();
    public bool TryRent(out PoolRental<T> rental);
    public void Use<TAction>(PoolRental<T> rental, scoped ref TAction action)
        where TAction : struct, IAction<T>, allows ref struct;
    public void Return(PoolRental<T> rental);
    public void Trim(int availableCount);
    public void RetryCleanup();
    public void Close();
    public void Dispose();
}
```

All members use [05](05-owned-buffers-and-internal-collections.md)'s checked admission, independent credits, write exclusivity, callback exception and disposal rules. Generic callable state is borrowed by reference and remains concrete through internal dispatch; no copying, boxing, retained reference or delegate adaptation occurs. `VisitWindows` reports complete traversal versus explicit visitor stop using the owner's exact window splitting, count and disposal semantics. `Allocate` accepts zero length without native allocation. `BufferChain` retains its ordered read segments independently of the supplied leases. `OwnedBuffer<T>` publishes finalized private storage through the chapter's one-time read-only transfer; it is not a mutable alias wrapper. Actual consumer completion, not cancellation of an observation, determines async credit release. Public lease/window/disposal calls are forbidden on real-time callbacks.

### M03 Core schema and diagnostic construction

| Owner / public surface | Exact amendment contract |
|---|---|
| Core: `ContentSchemaReference(Guid schemaId, int major, int minor, ContentSchemaScope scope)` | Readonly immutable value exposing Guid SchemaId, int Major, int Minor and ContentSchemaScope Scope; constructor requires nonempty ID, positive major/nonnegative minor, scope Content0/Foundation1. Default is intrinsically invalid; equality compares the full tuple. Metadata descriptors (`DataSchema`, `ContentFieldDescriptor`, tag/assignment/default/profile descriptions) expose exactly the [M03 3](10-binary-data-and-authoring-formats.md#m03-3-complete-descriptor-notation-and-expansion) fields/enums/ordered immutable collections, not CLR member discovery. |
| Core: compiled schema registrations | `CompiledContentSchemaRegistry` is an immutable application-owned set of compiled schema descriptors and rooted codecs. It is emitted from source declarations and cannot be created from package bytes, imported catalogues, schema digests, approval receipts, or runtime discovery. Schema references resolve only by their exact ID/major/minor/scope tuple. |
| Core/owner generated semantic values | Each selected row is an immutable typed model exposing exactly that table's fields/optional presence/ordered immutable collections; constructor signatures take those operands, never CLR discovery or serialized padding. Foundation-valued fields use the [M03 2](10-binary-data-and-authoring-formats.md#m03-2-preserved-foundation-and-reader-boundary) owned canonical-record/parsed-value wrapper so inert optional bytes survive. Explicit descriptor/attribute generation emits public symbolic author values/codecs and distinct World-bound component bindings. An Entity-valued author field uses stable Guid/selector input, not a fabricated live Entity. Each selected codec root is present in the application's closed compiled registration graph. |
| Core: `BinarySchemaDiagnostic` | Immutable SchemaId, uint Tag, physical path of uint tags plus optional int array indices, category and optional exact claim/limit. Preserve the schema-version contract and actual field/ancestor path; structural scanner failures retain their structural diagnostic. No validator ID, version, per-field rule-use index or executable rule assignment is serialized. Adds no diagnostic identity to the record bytes. Native/contextual and state codecs can return owned diagnostics without mutable last-error state or an Assets dependency in Entities. |

### M03 compiled schema registrations

`CompiledContentSchemaRegistry` is an immutable application-owned set of compiled schema descriptors and rooted codecs. The application build emits this set from source declarations; runtime accepts no schema manifest, imports, historical schema table, migration recipe, approval registry, or executable catalogue data from an asset package. Descriptor metadata is a build input, not a package record or content identity digest.

The registry resolves a schema only by its exact `ContentSchemaReference` tuple. A later schema version is a separate compiled registration and owns its fixed required fields and behavior. Unknown versions reject; there is no latest/minor fallback. Adding an application schema requires its compiled descriptor and codec in the application and a rebuild. No validator/rule identity or migration selector is serialized.

There is no public dynamic catalogue-admission, historical lookup, schema-approval, or generic migration API. Content schema diagnostics remain fixed-schema diagnostics with the physical schema/tag/path and failure category. EBIN structural validation and schema-specific validation remain distinct.
### EBIN structural reader

The public Core reader exposes schema-agnostic bounded EBIN structure without making M02's private `FoundationCodec`, `EbinRecord`, or persistence bindings public. `TerraFX.Assets` and `TerraFX.Entities` use this shared implementation rather than duplicating parsers or obtaining blanket friend access.

```csharp
namespace TerraFX;

public sealed class EbinReadLimits
{
    public EbinReadLimits(int maximumRecordBytes, int maximumFieldsPerRecord, int maximumDepth,
        int maximumArrayElements, int maximumUtf8Bytes, long maximumAggregateRecords,
        long maximumAggregateFields, long maximumAggregateArrayElements);
    public static EbinReadLimits Foundation { get; }
    public long MaximumAggregateArrayElements { get; }
    public long MaximumAggregateFields { get; }
    public long MaximumAggregateRecords { get; }
    public int MaximumArrayElements { get; }
    public int MaximumDepth { get; }
    public int MaximumFieldsPerRecord { get; }
    public int MaximumRecordBytes { get; }
    public int MaximumUtf8Bytes { get; }
}

public enum EbinReadPolicy
{
    Structural,
    Canonical
}

public enum EbinReadStatus
{
    Complete,
    NeedMoreData,
    InvalidRecord,
    UnsupportedSchema
}

public readonly record struct EbinReadDiagnostic
{
    internal EbinReadDiagnostic(Guid schemaId, uint tag, string category, ulong offset, ulong claim, ulong limit);
    public ulong Claim { get; }
    public string Category { get; }
    public ulong Limit { get; }
    public ulong Offset { get; }
    public Guid SchemaId { get; }
    public uint Tag { get; }
}

public readonly record struct EbinRecordHeader
{
    internal EbinRecordHeader(Guid schemaId, int major, int minor, int fieldCount, int recordByteLength);
    public int FieldCount { get; }
    public int Major { get; }
    public int Minor { get; }
    public int RecordByteLength { get; }
    public Guid SchemaId { get; }
}

public readonly ref struct EbinFieldReadView
{
    internal EbinFieldReadView(uint tag, ushort kind, ushort flags, ulong count, ReadOnlySpan<byte> payload);
    public ulong Count { get; }
    public bool IsRequired { get; }
    public ushort Kind { get; }
    public ReadOnlySpan<byte> Payload { get; }
    public uint Tag { get; }
}

public readonly ref struct EbinRecordReadView
{
    internal EbinRecordReadView(ReadOnlySpan<byte> encodedBytes);
    public ReadOnlySpan<byte> EncodedBytes { get; }
    public int FieldCount { get; }
    public EbinRecordHeader Header { get; }
    public EbinFieldReadView GetField(int index);
    public EbinRecordReadView GetRecord(uint fieldTag);
    public EbinRecordReadView GetRecordArrayElement(uint fieldTag, int elementIndex);
    public bool TryGetField(uint tag, out EbinFieldReadView field);
}

public static class EbinStructuralReader
{
    public static EbinReadStatus Read<TAction>(scoped ReadOnlySpan<byte> source, EbinReadLimits limits,
        EbinReadPolicy policy, scoped ref TAction action, out int bytesConsumed,
        out EbinReadDiagnostic? diagnostic)
        where TAction : struct, IAction<EbinRecordReadView>, allows ref struct;
    public static EbinReadStatus ReadHeader(scoped ReadOnlySpan<byte> source, EbinReadLimits limits,
        out EbinRecordHeader header, out int bytesConsumed, out EbinReadDiagnostic? diagnostic);
    public static EbinReadStatus Validate(scoped ReadOnlySpan<byte> source, EbinReadLimits limits,
        EbinReadPolicy policy, out int bytesConsumed, out EbinReadDiagnostic? diagnostic);
}
```

Public schema versions are validated signed `int` under section 1; EBIN's wire UInt32 major/minor values above `int.MaxValue` reject before projection. `Foundation` preserves the original M02 parser's 65,536-byte, 64-field, depth-64 and 1,048,576-element limits. The separately supplied retained-storage budget remains additional and cannot widen them. M03 callers choose explicit owner limits; every recursive record/payload/array scan shares one checked depth and aggregate-counter state, including prefix validation. A child scan cannot restart counters or use Foundation defaults.

`EbinReadLimits` requires `MaximumRecordBytes >= 64`, `MaximumDepth` in `[1, 64]` (the EBIN nesting ceiling in [27](27-state-serialization-replay-and-recovery.md#5-savereplay-admission-limits)), and positive aggregate-record capacity because the root counts. Field, element, UTF-8, aggregate-field and aggregate-element maxima may be zero; that permits only zero-field records, empty arrays/UTF-8 payloads and scans without counted children as applicable. Invalid/null limits are rejected before reading.

Root depth is one; a nested record (including each record-array element) increments depth once, while an array container does not. Count each physical record, field-table entry and fixed-/record-array element once. Encoded root byte length already includes nested bytes. Validate UInt64 claims/products/alignment before signed projection, slicing or allocation; enforce header/table/range/overlap/padding/tag/flag/scalar/Boolean/UTF-8/array-offset/child-extent rules before any callback. Empty RecordArray payloads are exactly 16 bytes and zero-field records exactly 64 bytes.

`ReadHeader.Complete` consumes the 64-byte header and returns only a bounded claim. `Validate` and `Read.Complete` consume exactly one complete record prefix; every other status consumes zero and exposes no usable header/view. `Read` invokes the synchronous scoped action only after complete recursive validation; exceptions propagate after cleanup. `GetRecord` throws `KeyNotFoundException` for an absent tag and `InvalidOperationException` for a field of another kind; `GetRecordArrayElement` has the same tag/kind behavior and throws `ArgumentOutOfRangeException` for an invalid index. Returned child views remain within the already validated bytes and cannot restart limits. `Canonical` additionally requires contiguous canonical record-array elements and the exact canonical field layout throughout nested records and arrays; `Structural` validates admissible structure but does not imply canonical packing or semantic schema admission. The reader allocates no per-field/per-child object graph, retains no span, installs no schema/codec and executes no arbitrary constructor. File/chain callers retain and account input/decompression/staging credits; this API never flattens an unbounded package.

### EBIN structural writer and payload helpers

The structural writer packs generic borrowed field descriptions into caller scratch, validates the exact canonical candidate, and commits a complete record prefix. Payload helpers construct supported payload representations without exposing a generic marshaller or retaining/accounting caller storage.

```csharp
using TerraFX.Numerics;

namespace TerraFX;

public readonly record struct EbinPayloadSlice
{
    public EbinPayloadSlice(int offset, int byteLength);
    public int ByteLength { get; }
    public int Offset { get; }
}

public readonly record struct EbinFieldSource
{
    public EbinFieldSource(uint tag, ushort kind, ushort flags, ulong count, EbinPayloadSlice payload);
    public ulong Count { get; }
    public ushort Flags { get; }
    public ushort Kind { get; }
    public EbinPayloadSlice Payload { get; }
    public uint Tag { get; }
}

public readonly ref struct EbinRecordWriteView
{
    public EbinRecordWriteView(Guid schemaId, int major, int minor,
        ReadOnlySpan<EbinFieldSource> fields, ReadOnlySpan<byte> payloadStorage);
    public ReadOnlySpan<EbinFieldSource> Fields { get; }
    public int Major { get; }
    public int Minor { get; }
    public ReadOnlySpan<byte> PayloadStorage { get; }
    public Guid SchemaId { get; }
}

public enum EbinWriteStatus
{
    Complete,
    InvalidRecord,
    UnsupportedSchema,
    WorkspaceTooSmall,
    DestinationTooSmall
}

public enum EbinStructuralDiagnosticSource
{
    Construction,
    EncodedInput,
    Candidate
}

public readonly record struct EbinStructuralDiagnostic
{
    public ulong Claim { get; }
    public string Category { get; }
    public ulong Limit { get; }
    public ulong? Offset { get; }
    public Guid SchemaId { get; }
    public EbinStructuralDiagnosticSource Source { get; }
    public uint Tag { get; }
    public static EbinStructuralDiagnostic FromRead(EbinReadDiagnostic diagnostic);
}

public static class EbinStructuralWriter
{
    public static EbinWriteStatus Commit(scoped ReadOnlySpan<byte> canonicalCandidate,
        EbinReadLimits limits, scoped Span<byte> destination, out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Measure(scoped in EbinRecordWriteView source, EbinReadLimits limits,
        out int recordBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Prepare(scoped in EbinRecordWriteView source, EbinReadLimits limits,
        scoped Span<byte> candidateStorage, out int candidateBytes,
        out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(scoped in EbinRecordWriteView source, EbinReadLimits limits,
        scoped Span<byte> candidateStorage, scoped Span<byte> destination, out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic);
}

public static class EbinPayloadWriter
{
    public static EbinWriteStatus Write(byte value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(ushort value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(uint value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(ulong value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(sbyte value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(short value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(int value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(long value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(bool value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(float value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(Guid value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(Vector2 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(Vector3 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(Vector4 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(Quaternion value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(Matrix3x2 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(Matrix4x4 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(SpatialPosition2 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus Write(SpatialPosition3 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);

    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<byte> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<ushort> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<uint> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<ulong> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<sbyte> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<short> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<int> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<long> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<bool> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<float> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Guid> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Vector2> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Vector3> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Vector4> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Quaternion> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Matrix3x2> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Matrix4x4> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<SpatialPosition2> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<SpatialPosition3> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic);

    public static EbinWriteStatus WriteFixedArray(ushort elementKind, ulong count,
        scoped ReadOnlySpan<byte> canonicalElements, EbinReadLimits limits,
        scoped Span<byte> payloadDestination, out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus MeasureRecordArray(scoped ReadOnlySpan<EbinPayloadSlice> children,
        scoped ReadOnlySpan<byte> childStorage, EbinReadLimits limits, out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic);
    public static EbinWriteStatus WriteRecordArray(scoped ReadOnlySpan<EbinPayloadSlice> children,
        scoped ReadOnlySpan<byte> childStorage, EbinReadLimits limits,
        scoped Span<byte> payloadDestination, out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic);
}
```

Scalar helpers use fixed widths at most 64 bytes and therefore need no `EbinReadLimits`. Array helpers validate count, checked wire extent, aggregate element/byte budgets and complete encoded inputs before whole-destination overlap or workspace checks. Record-array children share one fresh scanner state, count at depth one, and must consume their full slices. Successful helpers alter only the exact payload prefix; any returned refusal leaves the entire destination unchanged and reports zero bytes. Inputs remain unchanged and output backing remains live/exclusively borrowed through the call. See [10 M03 structural and payload writing](10-binary-data-and-authoring-formats.md#m03-structural-and-payload-writing) for accounting, encoded-byte diagnostics, failure precedence and the strict distinction between payload construction and complete-root admission.

## 4. Hosting, worlds, and jobs

```csharp
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Audio;
using TerraFX;
using TerraFX.Diagnostics;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Rendering;
using TerraFX.Platform;
using TerraFX.Input;

namespace TerraFX.Hosting;

public sealed class EngineHostOptions
{
    public EngineHostOptions(HostProfile profile, PlatformBackend platform, GraphicsBackend? graphicsBackend, AssetStoreOptions assetStoreOptions);
    public HostProfile Profile { get; }
    public PlatformBackend Platform { get; }
    public GraphicsBackend? GraphicsBackend { get; }
    public AssetStoreOptions AssetStoreOptions { get; }
    public int CpuWorkerCount { get; init; } = Math.Min(64, Math.Max(1, Environment.ProcessorCount - 2));
    public SimulationOverrunPolicy OverrunPolicy { get; init; } = SimulationOverrunPolicy.Pause;
    public GraphicsFailurePolicy GraphicsFailurePolicy { get; init; } = GraphicsFailurePolicy.StopHost;
    public ClipboardServicePolicy ClipboardServicePolicy { get; init; } = ClipboardServicePolicy.NotRequested;
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed class EngineHost : IAsyncDisposable
{
    private EngineHost();
    public static Task<EngineHost> StartAsync(EngineHostOptions options, CancellationToken cancellationToken = default);
    public Task Completion { get; }
    public int CpuWorkerCount { get; }
    public AssetStore Assets { get; }
    public DiagnosticsService Diagnostics { get; }
    public WorldPersistence Persistence { get; }
    public PlatformHost Platform { get; }
    public GraphicsDevice? GraphicsDevice { get; }
    public GpuResidencyService? GpuResidency { get; }
    public Renderer Renderer { get; }
    public Task InvokeAsync(Action<EngineContext> action, CancellationToken cancellationToken = default);
    public bool TryInvoke(Action<EngineContext> action, CancellationToken cancellationToken, [NotNullWhen(true)] out Task? completion);
    public Task<AudioMixer> CreateAudioMixerAsync(AudioMixerOptions options, CancellationToken cancellationToken = default);
    public Task<WindowCreationReceipt> CreateWindowAsync(WindowOptions options, CancellationToken cancellationToken = default);
    public Surface GetSurface(WindowHandle window);
    public Task<FrameChangeReceipt> ChangeFrameAsync(World world, SpatialFrame3 frame, CancellationToken cancellationToken = default);
    public Task<FrameChangeReceipt> ChangeFrameAsync(World world, SpatialFrame2 frame, CancellationToken cancellationToken = default);
    public Task<TimeScaleReceipt> ChangeTimeScaleAsync(World world, float timeScale, CancellationToken cancellationToken = default);
    public Task StopAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}

public enum TimeScaleChangeStatus
{
    Applied,
    Rejected
}

public sealed class TimeScaleReceipt
{
    internal TimeScaleReceipt(TimeScaleChangeStatus status, float? appliedTimeScale, ImmutableArray<Diagnostic> diagnostics);
    public TimeScaleChangeStatus Status { get; }
    public float? AppliedTimeScale { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public sealed class EngineContext
{
    internal EngineContext();
    public World CreateWorld(WorldOptions options);
    public InputRouter CreateInputRouter(InputRecipientKind recipientKind);
    public InputBindingReceipt BindGameplayInput(WindowHandle window, World world);
}

public enum InputBindingStatus
{
    Bound,
    Rejected
}

public sealed class InputBindingReceipt
{
    internal InputBindingReceipt(InputBindingStatus status, InputRouter? router, WindowHandle? window, Guid? worldGuid, Diagnostic? rejection);
    public InputBindingStatus Status { get; }
    public InputRouter? Router { get; }
    public WindowHandle? Window { get; }
    public Guid? WorldGuid { get; }
    public Diagnostic? Rejection { get; }
}
```

`EngineHost` has no public constructor; `StartAsync` owns startup and returns only a running host or a faulted task with startup context. `CpuWorkerCount` admits `[0, 64]`; zero runs stable parallel partitions inline. The host owns one Entities CPU worker pool shared by its Worlds and disposes it only after every World has joined. A host-created `WorldOptions.CpuWorkerCount` must be `null` (inherit the host pool) or equal the host's configured count; a mismatched explicit count rejects before World allocation. A standalone World's null count resolves to the documented default and it owns its pool. `Headless` requires an explicit headless platform and no graphics backend; `OffscreenNative` requires an explicit real D3D12 or Vulkan backend. No backend, display, endpoint, or physical-input fallback is inferred. The host owns its dedicated owner thread and keeps pumping native readiness and accepted owner-control operations while paused, minimized, or stopping. `EngineContext.CreateInputRouter` is owner-bound and creates one router for exactly one `HostLiveUI` or `SimulationTimeline` recipient. `BindGameplayInput` establishes [14](14-custom-input-actions-and-text.md)'s window/world/timeline route and returns `Bound` with a router only on success; `Rejected` returns a diagnostic and no router.

`Assets`, `Diagnostics`, `Persistence`, `Platform`, and `Renderer` are initialized host-owned services available after `StartAsync` succeeds. `EngineHost` creates its `Assets` service through `AssetStore.CreateAsync` using the required immutable `EngineHostOptions.AssetStoreOptions`, then owns and disposes that instance during shutdown; standalone stores use the same factory and are owned/disposed by their callers. Host construction validates the Assets registration independently of each future `WorldRegistration`; `CreateWorld` checks their compatible compiled schema registrations and package schema tuples before allocating a World. `Renderer` is a CPU-capable extraction service in every host profile. `GraphicsDevice` and `GpuResidency` are both available when a graphics backend is selected and both `null` for `Headless`; no placeholder device is constructed. In `Headless`, `Renderer.Extract` validates and copies logical scene data only, while `BuildGraph` and `SubmitAsync` throw `InvalidOperationException` because no real graphics device exists; no GPU output is synthesized. Capture/extraction requires the World owner's current canonical mutation revision to match a committed/restored marker. `OffscreenNative` has a real device and no interactive surface. `CreateAudioMixerAsync` creates an independently owned mixer from explicit `AudioMixerOptions`; the selected physical or offline endpoint is required by those options and is never inferred. A pre-canceled token prevents creation; after admission the task completes with the created mixer or faults with initialization context, and the caller owns/disposes the returned mixer.

`GetSurface` is synchronous and owner-thread-only. It returns the same host-owned logical `Surface` for a live interactive `WindowHandle`; a default/foreign handle throws `ArgumentException`, while off-owner-thread access, a stale/closed handle, or a live window without an interactive surface throws `InvalidOperationException`. Successful interactive window creation establishes this mapping before returning its `Created` receipt. Applying `CloseWindowAsync` invalidates the mapping and the logical surface immediately; native surface/swapchain storage remains fence-retained until GPU use and queued presentation have actually retired. `EngineHost.GetSurface` does not add a `Platform` dependency to `TerraFX.Graphics`.

`EngineHostOptions` requires host profile, platform, nullable graphics backend, and `AssetStoreOptions` in its constructor; that constructor has no optional parameters or fallback cache path. `OverrunPolicy` defaults to the chapter [03](03-shared-conventions-and-clocks.md) pause-on-overrun policy, `GraphicsFailurePolicy` defaults to `StopHost`, and `ShutdownTimeout` defaults to five seconds. A null graphics backend is valid only when the selected host profile does not require one. All option values are validated before host construction. `AssetStoreOptions.AbsoluteCacheDirectory` must be an explicit absolute trusted cache root, not an asset-controlled path; the host passes those frozen options to `AssetStore.CreateAsync` and does not report startup complete until the store is ready.

`InvokeAsync` and `TryInvoke` invoke a bounded synchronous callback on the host owner between ticks. The `EngineContext` is valid only during that callback; retaining it does not retain authority, and later use throws. The callback cannot await, recursively pump, or synchronously wait for its own completion. `TryInvoke` returns false only for a full bounded mailbox, with `completion = null`; true supplies the same asynchronously completed or faulted task as `InvokeAsync`. A pre-canceled `InvokeAsync` returns a canceled task; a pre-canceled `TryInvoke` throws `OperationCanceledException` before admission. `StopAsync` is idempotent; cancellation of its wait cannot undo accepted shutdown. `EngineContext` has no frame-change method: it is a borrowed synchronous callback capability, not the ordinary-thread queue service or standalone World control route. `World.SetFrame` is the owner-idle standalone route; either `EngineHost.ChangeFrameAsync` overload targets the exact hosted World binding and does not require the caller to be the World owner.

After valid target, authority and immutable-frame usage checks, a pre-canceled frame token yields a canceled Task with no admission. A valid non-Try request refused solely because the pending owner queue is full yields a Task faulted with the contextual Entities-owned `WorldOwnerQueueFullException`; queue-full is not a synchronous exception, successful `Rejected` receipt or Try result. It consumes no owner-request/control/canonical-mutation sequence, does not invalidate the marker, and leaves the candidate reusable. The fault/receipt selection is linearized with target/authority revalidation and token observation; later cancellation cannot replace a selected admission refusal. Accepted work permanently consumes its owner-request cursor and invalidates capture eligibility. Its original Task reports actual application (`Applied`/expected `Rejected`) or faults on unexpected precommit preparation/cleanup failure after actual uses end. The token cannot withdraw accepted work; only an independent `operation.WaitAsync(waitToken)` observation is cancelable. Applied is irrevocable even if later `Retirement` faults. Other owner-control operations retain their own cancellation and capacity channels. All queues remain bounded by `WorldOptions.MaximumPendingOwnerRequests`.

```csharp
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Diagnostics;

namespace TerraFX.Entities;

public enum WorldRestoreIntent
{
    None = 0,
    RecordedContinuation = 1,
    Branch = 2,
    IndependentFork = 3,
}

public sealed class WorldOptions
{
    public WorldOptions(WorldRegistration registration);
    public WorldRegistration Registration { get; }
    public Guid? WorldGuid { get; init; }
    public Guid? InitialReplayLineageId { get; init; }
    public WorldAdmissionDomain? AdmissionDomain { get; init; }
    public int? CpuWorkerCount { get; init; }
    public int TickRate { get; init; } = 60;
    public int MaximumLiveEntities { get; init; } = 1_048_576;
    public int MaximumEntitySlots { get; init; } = 2_097_152;
    public int MaximumIdentityAssignments { get; init; } = 33_554_432;
    public int MaximumIdentityBranches { get; init; } = 1024;
    public int MaximumEntityReferencePositions { get; init; } = 4_194_304;
    public int MaximumActiveSceneInstances { get; init; } = 65_536;
    public int MaximumSceneOccurrences { get; init; } = 1_048_576;
    public int MaximumRecordedControls { get; init; } = 65_536;
    public int MaximumArchetypes { get; init; } = 4096;
    public int MaximumDeferredCommands { get; init; } = 65_536;
    public int MaximumComponentTypes { get; init; } = 256;
    public SimulationProfile Profile { get; init; } = SimulationProfile.Standard;
    public float InitialTimeScale { get; init; } = 1;
    public SpatialSettings SpatialSettings { get; init; } = SpatialSettings.Default;
    public SpatialProfileId SpatialProfileId { get; init; } = SpatialProfileId.Default;
    public bool Enable2DFrame { get; init; } = true;
    public (TerraFX.Numerics.Vector2 Minimum, TerraFX.Numerics.Vector2 Maximum)?
        FrameExtent2 { get; init; }
    public (TerraFX.Numerics.Vector3 Minimum, TerraFX.Numerics.Vector3 Maximum)?
        FrameExtent3 { get; init; }
    public int MaximumPendingOwnerRequests { get; init; } = 128;
    public WorldSchedulingLimits SchedulingLimits { get; init; } = WorldSchedulingLimits.Default;
}

public sealed class WorldAdmissionDomain : IDisposable
{
    public WorldAdmissionDomain(long maximumBytes);
    public long MaximumBytes { get; }
    public Task Retirement { get; }
    public void Dispose();
}

public sealed class WorldSchedulingLimits
{
    public WorldSchedulingLimits(
        long maximumRegistrationBytes,
        long maximumGraphBytes,
        long maximumWorkPlanBytes,
        long maximumCommandStagingBytes,
        long maximumOwnedStateBytes,
        long maximumDiagnosticBytes,
        int maximumConflictComparisons);
    public static WorldSchedulingLimits Default { get; }
    public long MaximumRegistrationBytes { get; }
    public long MaximumGraphBytes { get; }
    public long MaximumWorkPlanBytes { get; }
    public long MaximumCommandStagingBytes { get; }
    public long MaximumOwnedStateBytes { get; }
    public long MaximumDiagnosticBytes { get; }
    public long MaximumResourceAndSystemStateBytes { get; }
    public int MaximumConflictComparisons { get; }
    }

public enum SystemResourceKind
{
    None = 0,
    Component = 1,
    Resource = 2,
    Tag = 3,
    EntityMembership = 4,
}

public enum SystemAccessMode
{
    None = 0,
    Read = 1,
    Write = 2,
}

public enum SystemThreadMode
{
    None = 0,
    OwnerThread = 1,
    Parallel = 2,
}

public readonly struct SystemPartitionRange : IEquatable<SystemPartitionRange>
{
    public SystemPartitionRange(int start, int length);
    public int Start { get; }
    public int Length { get; }
    public int EndExclusive { get; }
    public bool IsValid { get; }
    public bool Equals(SystemPartitionRange other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(SystemPartitionRange left, SystemPartitionRange right);
    public static bool operator !=(SystemPartitionRange left, SystemPartitionRange right);
}

public readonly struct SystemResourceAccess : IEquatable<SystemResourceAccess>
{
    public SystemResourceAccess(SystemResourceKind kind, Guid resourceId, SystemAccessMode mode);
    public SystemResourceAccess(SystemResourceKind kind, Guid resourceId, SystemAccessMode mode, SystemPartitionRange range);
    public SystemResourceKind Kind { get; }
    public Guid ResourceId { get; }
    public SystemAccessMode Mode { get; }
    public SystemPartitionRange? Range { get; }
    public bool IsValid { get; }
    public bool Equals(SystemResourceAccess other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(SystemResourceAccess left, SystemResourceAccess right);
    public static bool operator !=(SystemResourceAccess left, SystemResourceAccess right);
}

public sealed class SystemResourceDescription
{
    public SystemResourceDescription(Guid resourceId, int partitionCount, bool allowParallelAccess);
    public Guid ResourceId { get; }
    public int PartitionCount { get; }
    public bool AllowParallelAccess { get; }
}

public interface IWorldSystemFactory
{
    WorldSystemFactoryBinding Binding { get; }
}

public interface IWorldSystemCreate<TSystem>
    where TSystem : class, IWorldSystem
{
    TSystem Invoke();
}

public interface IWorldSystemDefaults<TSystem>
    where TSystem : class, IWorldSystem
{
    void Invoke(TSystem system);
}

public interface IWorldSystemRelease<TSystem>
    where TSystem : class, IWorldSystem
{
    void Invoke(TSystem system);
}

public interface IWorldSystemValueReader<TSource, TValue>
    where TSource : notnull
    where TValue : unmanaged
{
    TValue Invoke(in TSource source);
}

public interface IWorldSystemOptionalReader<TSource, TValue>
    where TSource : notnull
    where TValue : unmanaged
{
    bool HasValue(in TSource source);
    TValue GetPresent(in TSource source);
}

public sealed class WorldSystemFactoryBinding
{
    private WorldSystemFactoryBinding();
    public WorldSystemFactoryDescriptor Descriptor { get; }
    public static WorldSystemFactoryBinding Stateless<TSystem, TCreate, TRelease>(
        Guid factoryId, int factoryVersion, WorldSystemSourceStateDeclaration sourceState,
        TCreate create, TRelease release)
        where TSystem : class, IWorldSystem
        where TCreate : struct, IWorldSystemCreate<TSystem>
        where TRelease : struct, IWorldSystemRelease<TSystem>;
    public static WorldSystemFactoryBinding Stateful<TSystem, TAuthor, TCreate, TDefaults, TRelease>(
        Guid factoryId, int factoryVersion, WorldSystemSchema<TSystem> schema,
        WorldSystemSourceStateDeclaration sourceState,
        ImmutableArray<WorldSystemFieldBinding<TSystem, TAuthor>> fields,
        TCreate create, TDefaults defaults,
        IWorldSystemStateCodec<TSystem, TAuthor> codec, TRelease release)
        where TSystem : class, IWorldSystem
        where TAuthor : notnull
        where TCreate : struct, IWorldSystemCreate<TSystem>
        where TDefaults : struct, IWorldSystemDefaults<TSystem>
        where TRelease : struct, IWorldSystemRelease<TSystem>;
}

public sealed class WorldSystemFactoryDescriptor
{
    internal WorldSystemFactoryDescriptor();
    public Guid FactoryId { get; }
    public int FactoryVersion { get; }
    public Type SystemType { get; }
    public Type? AuthorType { get; }
    public ContentSchemaReference? StateSchema { get; }
    public WorldSystemSourceStateDeclaration SourceState { get; }
    public ImmutableArray<WorldSystemFieldDescriptor> Fields { get; }
}

public sealed class WorldSystemSchema<TSystem>
    where TSystem : class, IWorldSystem
{
    private WorldSystemSchema();
    public ContentSchemaReference Schema { get; }
    public ImmutableArray<WorldSystemFieldDescriptor> Fields { get; }
    public static WorldSystemSchema<TSystem> FromRegistry(
        CompiledContentSchemaRegistry schemas, ContentSchemaReference schema);
    public WorldSystemFieldDescriptor GetField(ComponentFieldPath path);
}

public sealed class WorldSystemFieldDescriptor
{
    internal WorldSystemFieldDescriptor();
    public ContentSchemaReference StateSchema { get; }
    public ComponentFieldPath Path { get; }
    public ContentFieldDescriptor Field { get; }
}

public enum WorldSystemSourcePurpose
{
    Authoritative = 1,
    DerivedCache = 2,
    ImmutableConfiguration = 3
}

public sealed class WorldSystemSourcePath
{
    public WorldSystemSourcePath(ImmutableArray<string> members);
    public ImmutableArray<string> Members { get; }
}

public sealed class WorldSystemSourceLeaf
{
    public WorldSystemSourceLeaf(
        WorldSystemSourcePath path, Type valueType, WorldSystemSourcePurpose purpose);
    public WorldSystemSourcePath Path { get; }
    public Type ValueType { get; }
    public WorldSystemSourcePurpose Purpose { get; }
}

public sealed class WorldSystemSourceStateDeclaration
{
    public WorldSystemSourceStateDeclaration(
        Type systemType, ImmutableArray<WorldSystemSourceLeaf> leaves);
    public Type SystemType { get; }
    public ImmutableArray<WorldSystemSourceLeaf> Leaves { get; }
}

public sealed class WorldSystemFieldBinding<TSystem, TAuthor>
    where TSystem : class, IWorldSystem
    where TAuthor : notnull
{
    private WorldSystemFieldBinding();
    public WorldSystemFieldDescriptor Descriptor { get; }
    public ImmutableArray<WorldSystemSourcePath> RuntimeSourcePaths { get; }
    public ImmutableArray<WorldSystemSourcePath> AuthorSourcePaths { get; }
    public static WorldSystemFieldBinding<TSystem, TAuthor> Scalar<TValue, TRuntime, TAuthorReader>(
        WorldSystemFieldDescriptor field,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        TRuntime runtime, TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IWorldSystemValueReader<TSystem, TValue>
        where TAuthorReader : struct, IWorldSystemValueReader<TAuthor, TValue>;
    public static WorldSystemFieldBinding<TSystem, TAuthor> Optional<TValue, TRuntime, TAuthorReader>(
        WorldSystemFieldDescriptor field,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        TRuntime runtime, TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IWorldSystemOptionalReader<TSystem, TValue>
        where TAuthorReader : struct, IWorldSystemOptionalReader<TAuthor, TValue>;
    public static WorldSystemFieldBinding<TSystem, TAuthor> EntityReference<TRuntime, TAuthorReader>(
        WorldSystemFieldDescriptor field,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        TRuntime runtime, TAuthorReader author)
        where TRuntime : struct, IWorldSystemValueReader<TSystem, Entity>
        where TAuthorReader : struct, IWorldSystemValueReader<TAuthor, Guid>;
    public static WorldSystemFieldBinding<TSystem, TAuthor> OptionalEntityReference<TRuntime, TAuthorReader>(
        WorldSystemFieldDescriptor field,
        ImmutableArray<WorldSystemSourcePath> runtimeSourcePaths,
        ImmutableArray<WorldSystemSourcePath> authorSourcePaths,
        TRuntime runtime, TAuthorReader author)
        where TRuntime : struct, IWorldSystemOptionalReader<TSystem, Entity>
        where TAuthorReader : struct, IWorldSystemOptionalReader<TAuthor, Guid>;
}

public enum WorldSystemReadStatus
{
    Read = 1,
    Rejected = 2
}

public enum WorldSystemReadFailureKind
{
    MalformedRecord = 1,
    SchemaMismatch = 2,
    InvalidField = 3,
    InvalidValue = 4,
    ConsumptionMismatch = 5
}

public sealed class WorldSystemReadBinding<TAuthor>
    where TAuthor : notnull
{
    internal WorldSystemReadBinding();
    public Guid SystemId { get; }
    public ContentSchemaReference Schema { get; }
    public nint SourceByteLength { get; }
    public WorldSystemReadResult<TAuthor> Complete(
        [DisallowNull] in TAuthor author, nint consumedBytes);
    public WorldSystemReadResult<TAuthor> Reject(
        WorldSystemReadFailureKind kind, nint sourceOffset,
        ComponentFieldPath? path, string message);
    public WorldSystemReadResult<TAuthor> Reject(
        EbinReadStatus status, EbinReadDiagnostic? diagnostic);
}

public sealed class WorldSystemReadResult<TAuthor>
    where TAuthor : notnull
{
    internal WorldSystemReadResult();
    public WorldSystemReadStatus Status { get; }
    public nint ConsumedBytes { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool TryGetAuthorValue(
        [MaybeNullWhen(false), NotNullWhen(true)] out TAuthor value);
}

public sealed class WorldSystemValidationBinding
{
    internal WorldSystemValidationBinding();
    public Guid SystemId { get; }
    public ContentSchemaReference Schema { get; }
    public ImmutableArray<Diagnostic> ValidateFields<TSystem>(TSystem system)
        where TSystem : class, IWorldSystem;
}

public sealed class WorldSystemCaptureBinding
{
    internal WorldSystemCaptureBinding();
    public Guid SystemId { get; }
    public ContentSchemaReference Schema { get; }
    public Guid GetEntityId(WorldSystemFieldDescriptor referenceField, Entity entity);
}

public interface IWorldSystemStateCodec<TSystem, TAuthor>
    where TSystem : class, IWorldSystem
    where TAuthor : notnull
{
    WorldSystemReadResult<TAuthor> Read(
        scoped ReadOnlySpan<byte> source,
        WorldSystemReadBinding<TAuthor> binding,
        CancellationToken cancellationToken);
    ImmutableArray<Diagnostic> Validate(
        TSystem system, WorldSystemValidationBinding binding,
        CancellationToken cancellationToken);
    nint Count(TSystem system, WorldSystemCaptureBinding binding,
        CancellationToken cancellationToken);
    nint Write(
        TSystem system, scoped Span<byte> destination,
        WorldSystemCaptureBinding binding, CancellationToken cancellationToken);
    void Restore(
        TSystem system, in TAuthor author, WorldStateRestoreBinding binding,
        CancellationToken cancellationToken);
}

public enum WorldSystemCleanupOperation
{
    ConstructionUnwind = 1,
    Disposal = 2
}

public sealed class WorldSystemCleanupFailure
{
    internal WorldSystemCleanupFailure(WorldSystemCleanupFailureControl preparedControl);
    public Guid SystemId { get; }
    public Guid FactoryId { get; }
    public int FactoryVersion { get; }
    public int ReleaseAttemptIndex { get; }
    public Exception OriginalException { get; }
}

public sealed class WorldSystemCleanupReport
{
    internal WorldSystemCleanupReport(WorldSystemCleanupReportControl preparedControl);
    public Guid WorldGuid { get; }
    public WorldSystemCleanupOperation Operation { get; }
    public Exception? ConstructionPrimaryException { get; }
    public int ReleaseAttemptCount { get; }
    public int RetainedFailureCapacity { get; }
    public int RetainedFailureCount { get; }
    public long FailureCount { get; }
    public long OmittedFailureCount { get; }
    public WorldSystemCleanupFailure GetFailure(int index);
}

public sealed class WorldSystemCleanupException : AggregateException
{
    internal WorldSystemCleanupException(WorldSystemCleanupReport report);
    public WorldSystemCleanupReport SystemCleanupReport { get; }
    public Exception? ConstructionPrimaryException { get; }
}

public sealed class WorldSystemCleanupReportFinalizationException : InvalidOperationException
{
    internal WorldSystemCleanupReportFinalizationException(
        WorldSystemCleanupReportFaultControl preparedControl);
    public WorldSystemCleanupReport SystemCleanupReport { get; }
    public Exception? ConstructionPrimaryException { get; }
    public Exception OriginalCleanupException { get; }
    public Exception ReportFinalizationException { get; }
}

internal sealed class WorldSystemCleanupFailureControl
{
    internal WorldSystemCleanupFailureControl();
}

internal sealed class WorldSystemCleanupReportControl
{
    internal WorldSystemCleanupReportControl();
}

internal sealed class WorldSystemCleanupReportFaultControl
{
    internal WorldSystemCleanupReportFaultControl();
}

public sealed class WorldRegistrationBuilder
{
    public WorldRegistrationBuilder(CompiledContentSchemaRegistry schemas);
    public WorldRegistrationBuilder RegisterComponent<T>(ComponentSchema<T> schema, IComponentFactory<T> factory) where T : unmanaged;
    public WorldRegistrationBuilder RegisterTag(EntityTagSchema schema);
    public WorldRegistrationBuilder RegisterSystem(SystemDescription description, IWorldSystemFactory factory);
    public WorldRegistrationBuilder RegisterResource<T>(SystemResourceDescription description,
        IWorldResourceFactory<T> factory, IWorldResourceStateCodec<T> codec) where T : unmanaged;
    public WorldRegistrationBuilder RegisterAttachment<T, TAuthor>(AttachmentScope scope,
        WorldAttachmentFactoryBinding<T, TAuthor> binding) where T : class;
    public WorldRegistrationBuilder RegisterSceneSupport(SceneSupportRegistration support);
    public WorldRegistrationBuilder RegisterCpuExtractionSupport(CpuExtractionSupportRegistration support);
    public WorldRegistration Build();
}

public sealed class WorldRegistration
{
    internal WorldRegistration(CompiledContentSchemaRegistry schemas, ImmutableArray<ComponentRegistration> components,
        ImmutableArray<EntityTagSchema> tags, ImmutableArray<SystemRegistration> systems);
    public CompiledContentSchemaRegistry Schemas { get; }
    public ComponentLayout GetComponentLayout(ContentSchemaReference schema);
    public ComponentFactoryDescriptor GetComponentBinding(ContentSchemaReference schema);
    public WorldResourceRegistrationDescriptor GetResourceBinding(Guid resourceId);
    public WorldAttachmentFactoryDescriptor GetAttachmentBinding(
        AttachmentScope scope, ContentSchemaReference schema);
}

public readonly struct ComponentLayout
{
    public ComponentLayout(Guid layoutId, int logicalBytes);
    public Guid LayoutId { get; }
    public int LogicalBytes { get; }
}

public sealed class ComponentSchema<T> where T : unmanaged
{
    public ComponentSchema(ContentSchemaReference schema, ComponentLayout layout,
        ImmutableArray<ComponentFieldDescriptor> fields, ComponentStorageFlags flags);
    public ContentSchemaReference Schema { get; }
    public ComponentLayout Layout { get; }
    public ImmutableArray<ComponentFieldDescriptor> Fields { get; }
    public ComponentStorageFlags Flags { get; }
    public static ComponentSchema<T> FromRegistry(CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema, ComponentLayout layout);
    public ComponentFieldDescriptor GetField(ComponentFieldPath path);
}

public enum ComponentPropertySegmentKind
{
    FieldTag = 1,
    FixedArrayIndex = 2
}

public readonly struct ComponentPropertySegment
{
    private ComponentPropertySegment(ComponentPropertySegmentKind kind, uint value);
    public static ComponentPropertySegment FieldTag(uint tag);
    public static ComponentPropertySegment FixedArrayIndex(int index);
    public ComponentPropertySegmentKind Kind { get; }
    public uint Value { get; }
}

public sealed class ComponentFieldPath
{
    public ComponentFieldPath(ImmutableArray<ComponentPropertySegment> segments);
    public ImmutableArray<ComponentPropertySegment> Segments { get; }
}

public sealed class ComponentFieldDescriptor
{
    internal ComponentFieldDescriptor();
    public ContentSchemaReference ComponentSchema { get; }
    public ComponentFieldPath Path { get; }
    public ContentFieldDescriptor Field { get; }
}

public enum ComponentStorageSegmentKind
{
    Member = 1,
    InlineIndex = 2
}

public readonly struct ComponentStorageSegment
{
    private ComponentStorageSegment(ComponentStorageSegmentKind kind, string? memberName, int? inlineIndex);
    public static ComponentStorageSegment Member(string memberName);
    public static ComponentStorageSegment InlineIndex(int index);
    public ComponentStorageSegmentKind Kind { get; }
    public string? MemberName { get; }
    public int? Index { get; }
}

public sealed class ComponentStoragePath
{
    public ComponentStoragePath(ImmutableArray<ComponentStorageSegment> segments);
    public ImmutableArray<ComponentStorageSegment> Segments { get; }
}

public enum ComponentStorageKind
{
    Value = 1,
    OptionalValue = 2,
    EntityHandle = 3,
    OptionalEntityHandle = 4,
    InlineArray = 5,
    Composite = 6,
    TransientNativeValue = 7
}

public enum ComponentStoragePurpose
{
    SchemaValue = 1,
    DeclaredRuntimeOnly = 2
}

public sealed class ComponentStorageLeaf
{
    public ComponentStorageLeaf(ComponentStoragePath path, ComponentStorageKind kind,
        int byteOffset, int byteExtent, int inlineCount, int inlineStride,
        ComponentStoragePurpose purpose);
    public ComponentStoragePath Path { get; }
    public ComponentStorageKind Kind { get; }
    public int ByteOffset { get; }
    public int ByteExtent { get; }
    public int InlineCount { get; }
    public int InlineStride { get; }
    public ComponentStoragePurpose Purpose { get; }
}

public readonly struct ComponentPaddingInterval
{
    public ComponentPaddingInterval(int byteOffset, int byteExtent);
    public int ByteOffset { get; }
    public int ByteExtent { get; }
}

public sealed class ComponentStorageDeclaration
{
    public ComponentStorageDeclaration(int logicalBytes,
        ImmutableArray<ComponentStorageLeaf> leaves,
        ImmutableArray<ComponentPaddingInterval> padding);
    public int LogicalBytes { get; }
    public ImmutableArray<ComponentStorageLeaf> Leaves { get; }
    public ImmutableArray<ComponentPaddingInterval> Padding { get; }
}

public sealed class ComponentFactoryDescriptor
{
    internal ComponentFactoryDescriptor();
    public ContentSchemaReference Schema { get; }
    public ComponentLayout Layout { get; }
    public ComponentStorageDeclaration Storage { get; }
    public ComponentTransientRestore Restore { get; }
    public ComponentStorageFlags Flags { get; }
    public Guid FactoryId { get; }
    public int FactoryVersion { get; }
    public ImmutableArray<ComponentFieldDescriptor> Fields { get; }
    public ImmutableArray<ComponentDerivedDependency> DerivedDependencies { get; }
}

public sealed class ComponentDerivedDependency
{
    private ComponentDerivedDependency();
    public ContentSchemaReference Schema { get; }
    public Type ComponentType { get; }
    public static ComponentDerivedDependency FromSchema<TDependency>(
        ComponentSchema<TDependency> schema)
        where TDependency : unmanaged;
}

public interface IComponentValueReader<TValueOwner, TValue>
{
    TValue Get(in TValueOwner value);
}

public interface IComponentValueAccessor<T, TValue> :
    IComponentValueReader<T, TValue> where T : unmanaged
{
    void Set(ref T value, in TValue fieldValue);
}

public interface IComponentOptionalReader<TValueOwner, TValue>
{
    bool IsPresent(in TValueOwner value);
    TValue GetPresent(in TValueOwner value);
}

public interface IComponentOptionalAccessor<T, TValue> :
    IComponentOptionalReader<T, TValue> where T : unmanaged
{
    void SetPresent(ref T value, in TValue fieldValue);
    void Clear(ref T value);
}

public interface IComponentArrayReader<TValueOwner, TElement>
{
    int GetCount(in TValueOwner value);
    TElement GetElement(in TValueOwner value, int index);
}

public interface IComponentArrayAccessor<T, TElement> :
    IComponentArrayReader<T, TElement> where T : unmanaged
{
    void SetElement(ref T value, int index, in TElement element);
}

public sealed class ComponentFieldBinding<T, TAuthor> where T : unmanaged
{
    private ComponentFieldBinding();
    public ComponentFieldDescriptor Descriptor { get; }
    public ImmutableArray<ComponentStoragePath> StoragePaths { get; }

    public static ComponentFieldBinding<T, TAuthor> Scalar<TValue, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field, ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime, TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IComponentValueAccessor<T, TValue>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, TValue>;

    public static ComponentFieldBinding<T, TAuthor> Optional<TValue, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field, ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime, TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IComponentOptionalAccessor<T, TValue>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, TValue>;

    public static ComponentFieldBinding<T, TAuthor> EntityReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field, ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime, TAuthorReader author)
        where TRuntime : struct, IComponentValueAccessor<T, Entity>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, Guid>;

    public static ComponentFieldBinding<T, TAuthor> OptionalEntityReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field, ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime, TAuthorReader author)
        where TRuntime : struct, IComponentOptionalAccessor<T, Entity>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, Guid>;

    public static ComponentFieldBinding<T, TAuthor> StableGuidReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field, ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime, TAuthorReader author)
        where TRuntime : struct, IComponentValueAccessor<T, Guid>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, Guid>;

    public static ComponentFieldBinding<T, TAuthor> OptionalStableGuidReference<TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field, ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime, TAuthorReader author)
        where TRuntime : struct, IComponentOptionalAccessor<T, Guid>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, Guid>;

    public static ComponentFieldBinding<T, TAuthor> FixedArray<TElement, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field, ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime, TAuthorReader author)
        where TElement : unmanaged
        where TRuntime : struct, IComponentArrayAccessor<T, TElement>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, TElement>;

    public static ComponentFieldBinding<T, TAuthor> Aggregate<TStored, TSymbolic, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field, ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime, TAuthorReader author,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> children)
        where TStored : unmanaged
        where TRuntime : struct, IComponentValueAccessor<T, TStored>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, TSymbolic>;

    public static ComponentFieldBinding<T, TAuthor> OptionalAggregate<TStored, TSymbolic, TRuntime, TAuthorReader>(
        ComponentFieldDescriptor field, ImmutableArray<ComponentStoragePath> storagePaths,
        TRuntime runtime, TAuthorReader author,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> children)
        where TStored : unmanaged
        where TRuntime : struct, IComponentOptionalAccessor<T, TStored>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, TSymbolic>;
}

public interface IComponentFactory<T> where T : unmanaged
{
    ComponentFactoryBinding<T> Binding { get; }
}

public enum ComponentTransientRestore
{
    None = 0,
    Reset = 1,
    Derived = 2
}

public sealed class ComponentFactoryBinding<T> where T : unmanaged
{
    private ComponentFactoryBinding();
    public static ComponentFactoryBinding<T> Create<TAuthor>(
        Guid factoryId, int factoryVersion, ComponentSchema<T> schema,
        ComponentStorageDeclaration storage, ComponentTransientRestore restore,
        ImmutableArray<ComponentDerivedDependency> derivedDependencies,
        ImmutableArray<ComponentFieldBinding<T, TAuthor>> fields,
        IComponentDefaultProvider<T> defaults,
        IComponentRestoreProvider<T, TAuthor> restorer,
        IComponentStateCodec<T, TAuthor> codec);
    public ComponentFactoryDescriptor Descriptor { get; }
}

public sealed class ComponentDefaultBinding
{
    internal ComponentDefaultBinding();
}

public interface IComponentDefaultProvider<T> where T : unmanaged
{
    T Create(ComponentDefaultBinding binding);
}

public interface IComponentRestoreProvider<T, TAuthor> where T : unmanaged
{
    T Restore(in TAuthor author, WorldStateRestoreBinding binding,
        CancellationToken cancellationToken);
    T RebuildDerived(WorldStateRestoreBinding binding,
        CancellationToken cancellationToken);
}

public enum ComponentReadStatus
{
    Read = 1,
    Rejected = 2
}

public enum ComponentReadFailureKind
{
    MalformedRecord = 1,
    SchemaMismatch = 2,
    InvalidField = 3,
    InvalidValue = 4,
    ConsumptionMismatch = 5
}

public sealed class ComponentReadBinding<TAuthor>
{
    internal ComponentReadBinding();
    public ContentSchemaReference Schema { get; }
    public nint SourceByteLength { get; }
    public ComponentReadResult<TAuthor> Complete([DisallowNull] in TAuthor author, nint consumedBytes);
    public ComponentReadResult<TAuthor> Reject(ComponentReadFailureKind kind,
        nint sourceOffset, ComponentFieldPath? path, string message);
    public ComponentReadResult<TAuthor> Reject(EbinReadStatus status,
        EbinReadDiagnostic? diagnostic);
}

public sealed class ComponentReadResult<TAuthor>
{
    internal ComponentReadResult();
    public ComponentReadStatus Status { get; }
    public nint ConsumedBytes { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool TryGetAuthorValue([MaybeNullWhen(false), NotNullWhen(true)] out TAuthor value);
}

public interface IComponentStateCodec<T, TAuthor> where T : unmanaged
{
    ComponentReadResult<TAuthor> Read(scoped ReadOnlySpan<byte> source,
        ComponentReadBinding<TAuthor> binding, CancellationToken cancellationToken);
    ImmutableArray<Diagnostic> Validate(in T value,
        ComponentValidationBinding binding, CancellationToken cancellationToken);
    nint Count(in T value, ComponentCaptureBinding binding,
        CancellationToken cancellationToken);
    nint Write(in T value, scoped Span<byte> destination,
        ComponentCaptureBinding binding, CancellationToken cancellationToken);
}

public sealed class WorldStateRestoreBinding
{
    internal WorldStateRestoreBinding();
    public Entity ResolveEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId);
    public Entity ResolveEntity(WorldSystemFieldDescriptor referenceField, Guid targetEntityId);
    public TDependency ReadSameEntity<TDependency>() where TDependency : unmanaged;
}

public sealed class ComponentCaptureBinding
{
    internal ComponentCaptureBinding();
    public Guid GetEntityId(ComponentFieldDescriptor referenceField, Entity entity);
}

public sealed class ComponentValidationBinding
{
    internal ComponentValidationBinding();
    public ImmutableArray<Diagnostic> ValidateFields<T>(in T value) where T : unmanaged;
}

public interface IWorldResourceFactory<T> where T : unmanaged
{
    WorldResourceFactoryBinding<T> Binding { get; }
}

public interface IWorldResourceStateCodec<T> where T : unmanaged
{
    WorldResourceCodecBinding<T> Binding { get; }
}

public sealed class WorldResourceSchema<T> where T : unmanaged
{
    private WorldResourceSchema();
    public static WorldResourceSchema<T> FromRegistry(CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema);
    public ContentSchemaReference Schema { get; }
    public ImmutableArray<WorldResourceFieldDescriptor> Fields { get; }
    public WorldResourceFieldDescriptor GetField(ComponentFieldPath path);
}

public sealed class WorldResourceFieldDescriptor
{
    internal WorldResourceFieldDescriptor();
    public ContentSchemaReference Schema { get; }
    public ComponentFieldPath Path { get; }
    public ContentFieldDescriptor Metadata { get; }
}

public sealed class WorldResourceFieldBinding<T, TAuthor> where T : unmanaged
{
    private WorldResourceFieldBinding();
    public WorldResourceFieldDescriptor Descriptor { get; }
    public ImmutableArray<ComponentStoragePath> PartitionStoragePaths { get; }

    public static WorldResourceFieldBinding<T, TAuthor>
        PartitionArray<TElement, TRuntime, TAuthorReader>(
            WorldResourceFieldDescriptor field,
            ImmutableArray<ComponentStoragePath> partitionStoragePaths,
            TRuntime runtime,
            TAuthorReader author)
        where TElement : unmanaged
        where TRuntime : struct, IComponentValueReader<T, TElement>
        where TAuthorReader : struct, IComponentArrayReader<TAuthor, TElement>;
}

public sealed class WorldResourceFactoryDescriptor
{
    internal WorldResourceFactoryDescriptor();
    public ContentSchemaReference Schema { get; }
    public Guid FactoryId { get; }
    public int FactoryVersion { get; }
    public ComponentStorageDeclaration PartitionStorage { get; }
    public ImmutableArray<WorldResourceFieldDescriptor> Fields { get; }
}

public sealed class WorldResourceRegistrationDescriptor
{
    internal WorldResourceRegistrationDescriptor(SystemResourceDescription description,
        WorldResourceFactoryDescriptor factory);
    public Guid ResourceId { get; }
    public int PartitionCount { get; }
    public bool AllowParallelAccess { get; }
    public WorldResourceFactoryDescriptor Factory { get; }
}

public sealed class WorldResourceFactoryBinding<T> where T : unmanaged
{
    internal WorldResourceFactoryBinding(ResourceFactoryFamily<T> family);
    public WorldResourceFactoryDescriptor Descriptor { get; }
}

public sealed class WorldResourceFactoryBinding<T, TAuthor> where T : unmanaged
{
    private WorldResourceFactoryBinding(ResourceFactoryFamily<T, TAuthor> family);
    public static WorldResourceFactoryBinding<T, TAuthor> Create(
        Guid factoryId,
        int factoryVersion,
        WorldResourceSchema<T> schema,
        ComponentStorageDeclaration partitionStorage,
        ImmutableArray<WorldResourceFieldBinding<T, TAuthor>> fields,
        IWorldResourceDefaultProvider<T> defaults);
    public WorldResourceFactoryBinding<T> RegistrationBinding { get; }
    public WorldResourceFactoryDescriptor Descriptor { get; }
}

public sealed class WorldResourceCodecBinding<T> where T : unmanaged
{
    private WorldResourceCodecBinding();
    public static WorldResourceCodecBinding<T> Create<TAuthor>(
        WorldResourceFactoryBinding<T, TAuthor> factoryBinding,
        IWorldResourceStateCodec<T, TAuthor> codec);
    public WorldResourceFactoryDescriptor Descriptor { get; }
}

public interface IWorldResourceDefaultProvider<T> where T : unmanaged
{
    T Create(int partitionIndex, WorldResourceDefaultBinding binding);
}

public interface IWorldResourceStateCodec<T, TAuthor> where T : unmanaged
{
    WorldResourceReadResult<TAuthor> Read(scoped ReadOnlySpan<byte> source,
        WorldResourceReadBinding<TAuthor> binding, CancellationToken cancellationToken);
    void Restore(in TAuthor author, WorldResourceRestoreBinding<T> binding,
        CancellationToken cancellationToken);
    ImmutableArray<Diagnostic> Validate(scoped WorldResourceStateView<T> value,
        WorldResourceValidationBinding binding, CancellationToken cancellationToken);
    nint Count(scoped WorldResourceStateView<T> value,
        WorldResourceCaptureBinding binding, CancellationToken cancellationToken);
    nint Write(scoped WorldResourceStateView<T> value, scoped Span<byte> destination,
        WorldResourceCaptureBinding binding, CancellationToken cancellationToken);
}

public sealed class WorldResourceDefaultBinding
{
    internal WorldResourceDefaultBinding();
    public Guid ResourceId { get; }
    public ContentSchemaReference Schema { get; }
    public int PartitionCount { get; }
    public int PartitionIndex { get; }
}

public readonly ref struct WorldResourceStateView<T> where T : unmanaged
{
    internal WorldResourceStateView(WorldResourceOperationControl operation,
        ReadOnlySpan<T> partitions);
    public Guid ResourceId { get; }
    public ContentSchemaReference Schema { get; }
    public int PartitionCount { get; }
    public T ReadPartition(int partitionIndex);
}

public sealed class WorldResourceValidationBinding
{
    internal WorldResourceValidationBinding();
    public ImmutableArray<Diagnostic> ValidateFields<T>(
        scoped WorldResourceStateView<T> value) where T : unmanaged;
}

public sealed class WorldResourceCaptureBinding
{
    internal WorldResourceCaptureBinding();
    public Guid ResourceId { get; }
    public ContentSchemaReference Schema { get; }
    public int PartitionCount { get; }
    public int GetCount(WorldResourceFieldDescriptor field);
    public TElement GetElement<TElement>(WorldResourceFieldDescriptor field,
        int partitionIndex) where TElement : unmanaged;
}

public sealed class WorldResourceRestoreBinding<T> where T : unmanaged
{
    internal WorldResourceRestoreBinding();
    public Guid ResourceId { get; }
    public ContentSchemaReference Schema { get; }
    public int PartitionCount { get; }
    public void SetPartition(int partitionIndex, in T value);
}

public enum WorldResourceReadStatus
{
    Read = 1,
    Rejected = 2
}

public enum WorldResourceReadFailureKind
{
    MalformedRecord = 1,
    SchemaMismatch = 2,
    InvalidField = 3,
    InvalidValue = 4,
    ConsumptionMismatch = 5
}

public sealed class WorldResourceReadBinding<TAuthor>
{
    internal WorldResourceReadBinding();
    public Guid ResourceId { get; }
    public ContentSchemaReference Schema { get; }
    public int PartitionCount { get; }
    public nint SourceByteLength { get; }
    public WorldResourceReadResult<TAuthor> Complete(
        [DisallowNull] in TAuthor author, nint consumedBytes);
    public WorldResourceReadResult<TAuthor> Reject(WorldResourceReadFailureKind kind,
        nint sourceOffset, ComponentFieldPath? path, string message);
    public WorldResourceReadResult<TAuthor> Reject(EbinReadStatus status,
        EbinReadDiagnostic? diagnostic);
}

public sealed class WorldResourceReadResult<TAuthor>
{
    internal WorldResourceReadResult();
    public WorldResourceReadStatus Status { get; }
    public nint ConsumedBytes { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool TryGetAuthorValue([MaybeNullWhen(false), NotNullWhen(true)] out TAuthor value);
}

```

`WorldResourceSchema<T>.FromRegistry` resolves an exact active Content `ResourceState` tuple. Factory IDs are nonempty, versions positive, and partition storage has the exact unmanaged `T` stride/coverage. Arrays are initialized, copied and contain no null entries. `PartitionArray` binds the whole required FA domain: one runtime partition reader produces one element and the engine supplies count P; author count/elements come from the captured author-array reader. Creation validates complete field/storage closure without invoking readers.

For the selected resource-counter, metadata remains `Values FA(U64)[1..64]`. Actual P is 1, 2 or 64; wire element stride is 8, independent of runtime `T` stride. Registration rejects P outside descriptor bounds. Runtime partitions prohibit Entity/private-handle/native-owner leaves. Each World receives fresh engine-owned `T[P]`; no caller array or shared counter substitutes for it. Captured configuration is immutable; accessor/layout truth remains independently qualified.

`ResourceFactoryFamily<T>` is an assembly-private carrier; its concrete `ResourceFactoryFamily<T,TAuthor>` is created only by the typed factory producer. It owns copied schema/storage/field facts and captured readers/defaults. The typed binding and erased `RegistrationBinding` share that exact control. Codec `Create` captures the typed callback family and same factory control before erasure; there is no erased factory `Create`, erased-to-typed conversion, author-Type token, descriptor reconstruction or covariance route. Different `TAuthor` families cannot pair through the typed producer. Descriptor-identical independent families remain distinct and registration compares captured factory-control identity, not descriptor equality or CLR assignability.

`RegisterResource` retains the description's `ResourceId`, actual `PartitionCount` and `AllowParallelAccess`, copied factory descriptor, and closed typed factory/codec dispatch sharing the verified control. `Build` freezes it; getters are never consulted later. Bindings are sealed and issuer-restricted; no partially initialized binding escapes.

`WorldRegistration.GetResourceBinding` performs exact full-Guid-equality lookup by `ResourceId` among resource entries frozen by a successful `WorldRegistrationBuilder.Build`. `ResourceId` is unique within that registration's resource-entry set; this lookup adds no cross-kind, schema, factory or CLR-type uniqueness rule. `Guid.Empty` throws `new ArgumentException("The resource ID must be nonempty.", nameof(resourceId))`. An unknown nonempty ID throws `new KeyNotFoundException("No resource with the supplied ID is registered.")`. No schema-registration, CLR-type, factory or engine-defined membership fallback occurs. Success returns an initialized immutable descriptor containing copied `ResourceId`, `PartitionCount`, `AllowParallelAccess` and factory descriptor facts. It synthesizes no null/default result and invokes no factory/codec `Binding` getter, provider or field reader. Only the registration builder/freeze path issues it after the accepted capture, family-pairing and validation sequence. It exposes no family control, executable dispatch, mutable backing or operation authority. Its facts remain readable independently of World lifetime; repeated-result reference identity is unspecified.

Perform existing builder state and ordinary argument checks first. Required-null checks follow parameter order `description`, `factory`, `codec`. Capture `factory.Binding` exactly once and immediately reject null; only then capture `codec.Binding` exactly once and immediately reject null; only then compare captured family controls. Perform remaining registration validation before copying or mutating entries. Getter exceptions propagate as their original objects/stacks and stop processing; do not retry or call a later getter. No defaults/accessor/codec callback, runtime backing allocation or OwnerId issuance precedes pairing.

The exact null/pairing exceptions are:

```csharp
new ArgumentException("The factory returned no resource binding.", nameof(factory));
new ArgumentException("The codec returned no resource binding.", nameof(codec));
new ArgumentException(
    "The resource factory and codec belong to different captured families.",
    nameof(codec));
```

The respective `ParamName` values are `factory`, `codec`, and `codec`. Producer wrapper/control/copied-metadata allocation is distinct from runtime backing allocation.

`WorldResourceOperationControl` is assembly-private and issued only by the resource dispatcher. It binds registration/family, operation, callback generation, thread and access mode. Class bindings default null; default views are invalid. Views return copied `T` values; a binding/view pair requires the same live callback. Full-domain capture/validation occurs at the admitted quiescent boundary; concurrent disjoint write scopes validate their own leaves until joining.

Restore must install every partition index `0..P-1` exactly once. Authority precedes range/duplicate/value checks. Missing, duplicate or foreign operations are sticky `InvalidOperationException` protocol faults; an out-of-range partition index throws `ArgumentOutOfRangeException(nameof(partitionIndex))`. No default filling repairs missing input. Revoke the binding in `finally`; incomplete restoration cannot publish.

Only the live Read binding issues one result. Read requires a nonnull complete immutable author value, exact source consumption and initialized-empty diagnostics. Rejected has no usable author value, consumed0 and initialized nonempty diagnostics. Complete checks the captured author domains against P. Invalid/null/foreign/duplicate results, default/null diagnostics, bad consumption or callback exceptions fault the actual operation, not malformed-input admission. Reject preserves actual InvalidRecord/UnsupportedSchema diagnostics; Complete/NeedMoreData passed to Reject is protocol misuse. Results retain immutable data/copied provenance, not live authority. Revoked misuse outside an invocation remains throw-only and cannot fault a committed World.

```csharp
public enum AttachmentScope
{
    World = 1,
    Entity = 2
}

public enum AttachmentOwnership
{
    CallerOwned = 1,
    Transferred = 2
}

public sealed class WorldAttachmentFactoryBinding<T, TAuthor> where T : class
{
    private WorldAttachmentFactoryBinding();
    public static WorldAttachmentFactoryBinding<T, TAuthor> Create(
        Guid factoryId,
        int factoryVersion,
        CompiledContentSchemaRegistry schemas,
        ContentSchemaReference schema,
        ImmutableArray<WorldAttachmentFieldBinding<T, TAuthor>> fields,
        IWorldAttachmentStateProvider<T, TAuthor> provider);
    public WorldAttachmentFactoryDescriptor Descriptor { get; }
}

public sealed class WorldAttachmentFactoryDescriptor
{
    internal WorldAttachmentFactoryDescriptor();
    public ContentSchemaReference Schema { get; }
    public Guid FactoryId { get; }
    public int FactoryVersion { get; }
    public ImmutableArray<WorldAttachmentFieldDescriptor> Fields { get; }
}

public sealed class WorldAttachmentFieldDescriptor
{
    internal WorldAttachmentFieldDescriptor();
    public ContentSchemaReference Schema { get; }
    public ComponentFieldPath Path { get; }
    public ContentFieldDescriptor Field { get; }
}

public sealed class WorldAttachmentFieldBinding<T, TAuthor> where T : class
{
    private WorldAttachmentFieldBinding();
    public WorldAttachmentFieldDescriptor Descriptor { get; }

    public static WorldAttachmentFieldBinding<T, TAuthor>
        Value<TValue, TRuntime, TAuthorReader>(
            CompiledContentSchemaRegistry schemas,
            ContentSchemaReference schema,
            ComponentFieldPath path,
            TRuntime runtime,
            TAuthorReader author)
        where TValue : unmanaged
        where TRuntime : struct, IComponentValueReader<T, TValue>
        where TAuthorReader : struct, IComponentValueReader<TAuthor, TValue>;

    public static WorldAttachmentFieldBinding<T, TAuthor>
        OptionalEntityReference<TRuntime, TAuthorReader>(
            CompiledContentSchemaRegistry schemas,
            ContentSchemaReference schema,
            ComponentFieldPath path,
            TRuntime runtime,
            TAuthorReader author)
        where TRuntime : struct, IComponentOptionalReader<T, Entity>
        where TAuthorReader : struct, IComponentOptionalReader<TAuthor, Guid>;
}

public interface IWorldAttachmentStateProvider<T, TAuthor> where T : class
{
    WorldAttachmentReadResult<TAuthor> Read(scoped ReadOnlySpan<byte> source,
        WorldAttachmentReadBinding<TAuthor> binding, CancellationToken cancellationToken);
    T Restore(in TAuthor author, WorldAttachmentRestoreBinding binding,
        CancellationToken cancellationToken);
    ImmutableArray<Diagnostic> Validate(T value,
        WorldAttachmentValidationBinding binding, CancellationToken cancellationToken);
    nint Count(T value, WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken);
    nint Write(T value, scoped Span<byte> destination,
        WorldAttachmentCaptureBinding binding, CancellationToken cancellationToken);
    void Release(T value, WorldAttachmentReleaseBinding binding);
}

public enum WorldAttachmentReadStatus
{
    Read = 1,
    Rejected = 2
}

public enum WorldAttachmentReadFailureKind
{
    MalformedRecord = 1,
    SchemaMismatch = 2,
    InvalidField = 3,
    InvalidValue = 4,
    ConsumptionMismatch = 5
}

public sealed class WorldAttachmentReadBinding<TAuthor>
{
    internal WorldAttachmentReadBinding();
    public ContentSchemaReference Schema { get; }
    public nint SourceByteLength { get; }
    public WorldAttachmentReadResult<TAuthor> Complete(
        [DisallowNull] in TAuthor author, nint consumedBytes);
    public WorldAttachmentReadResult<TAuthor> Reject(WorldAttachmentReadFailureKind kind,
        nint sourceOffset, ComponentFieldPath? path, string message);
    public WorldAttachmentReadResult<TAuthor> Reject(EbinReadStatus status,
        EbinReadDiagnostic? diagnostic);
}

public sealed class WorldAttachmentReadResult<TAuthor>
{
    internal WorldAttachmentReadResult();
    public WorldAttachmentReadStatus Status { get; }
    public nint ConsumedBytes { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool TryGetAuthorValue([MaybeNullWhen(false), NotNullWhen(true)] out TAuthor value);
}

public sealed class WorldAttachmentRestoreBinding
{
    internal WorldAttachmentRestoreBinding();
    public Entity ResolveEntity(WorldAttachmentFieldDescriptor referenceField,
        Guid targetEntityId);
}

public sealed class WorldAttachmentCaptureBinding
{
    internal WorldAttachmentCaptureBinding();
    public Guid GetEntityId(WorldAttachmentFieldDescriptor referenceField, Entity entity);
}

public sealed class WorldAttachmentValidationBinding
{
    internal WorldAttachmentValidationBinding();
    public ImmutableArray<Diagnostic> ValidateFields<T>(T value) where T : class;
}

public sealed class WorldAttachmentReleaseBinding
{
    internal WorldAttachmentReleaseBinding();
    public Guid WorldGuid { get; }
    public AttachmentScope Scope { get; }
    public ContentSchemaReference Schema { get; }
    public Guid? OwnerEntityId { get; }
    public ulong ReleasedInstallationId { get; }
    public ulong? ReplacementInstallationId { get; }
}

public sealed class WorldAttachmentReleaseFailure
{
    internal WorldAttachmentReleaseFailure(Guid worldGuid, AttachmentScope scope,
        ContentSchemaReference schema, Guid? ownerEntityId, ulong releasedInstallationId,
        ulong? replacementInstallationId, Exception originalException);
    public Guid WorldGuid { get; }
    public AttachmentScope Scope { get; }
    public ContentSchemaReference Schema { get; }
    public Guid? OwnerEntityId { get; }
    public ulong ReleasedInstallationId { get; }
    public ulong? ReplacementInstallationId { get; }
    public Exception OriginalException { get; }
}

public sealed class WorldAttachmentReleaseException : InvalidOperationException
{
    internal WorldAttachmentReleaseException(
        WorldAttachmentReleaseFailure primaryFailure,
        ImmutableArray<WorldAttachmentReleaseFailure> additionalFailures,
        long omittedFailureCount,
        WorldSystemCleanupReport? systemCleanupReport);
    public WorldAttachmentReleaseFailure PrimaryFailure { get; }
    public Guid WorldGuid { get; }
    public AttachmentScope Scope { get; }
    public ContentSchemaReference Schema { get; }
    public Guid? OwnerEntityId { get; }
    public ulong ReleasedInstallationId { get; }
    public ulong? ReplacementInstallationId { get; }
    public bool StorageCommitted { get; }
    public ImmutableArray<WorldAttachmentReleaseFailure> AdditionalFailures { get; }
    public long FailureCount { get; }
    public long OmittedFailureCount { get; }
    public WorldSystemCleanupReport? SystemCleanupReport { get; }
}

internal sealed class WorldAttachmentReleaseReportFaultControl
{
    internal WorldAttachmentReleaseReportFaultControl();
}

public sealed class WorldAttachmentReleaseReportFinalizationException
    : InvalidOperationException
{
    internal WorldAttachmentReleaseReportFinalizationException(
        WorldAttachmentReleaseReportFaultControl preparedControl);
    public Guid WorldGuid { get; }
    public AttachmentScope Scope { get; }
    public ContentSchemaReference Schema { get; }
    public Guid? OwnerEntityId { get; }
    public ulong ReleasedInstallationId { get; }
    public ulong? ReplacementInstallationId { get; }
    public long CommittedMutationRevision { get; }
    public long ReleaseFailureCount { get; }
    public bool StorageCommitted { get; }
    public Exception OriginalReleaseException { get; }
    public Exception ReportFinalizationException { get; }
    public WorldSystemCleanupReport? SystemCleanupReport { get; }
}

```

World scope has no Entity owner. Entity scope records the stable persistent ID of its target Entity before membership revocation; that owner is distinct from any Target reference in the payload. `WorldAttachmentFactoryBinding<T,TAuthor>.Create` resolves an exact active Content tuple with role `AttachmentState`, requires nonempty FactoryId and positive FactoryVersion, and copies descriptor facts while capturing concrete typed field readers and the provider once. It rejects default/null collections, null entries, foreign or duplicate fields, incompatible shapes and incomplete selected-field closure before callbacks.

`RegisterAttachment` registers transferred immutable-value ownership. Its key is `(scope, exact active schema tuple)`; `(scope,T)` must resolve unambiguously to one registration. Conflicting or duplicate entries reject before builder mutation. One binding may be reused by two explicit registrations, one World and one Entity; it does not implicitly register both scopes. Build freezes both the entry and its actual typed dispatch before World construction. There is no runtime discovery, late registration, separate codec binding, author-Type token or descriptor-to-executable reconstruction route. `T` and `TAuthor` are immutable approved value graphs; neither may retain mutable caller storage, World/binding authority, borrowed bytes or unaccounted native ownership. Runtime shape checks do not prove arbitrary CLR immutability or accessor truth; source/artifact qualification does.

The existing unconstrained-source `IComponentValueReader` and `IComponentOptionalReader` are reused; mutation-capable unmanaged accessors are not. `Value` requires a compatible required fixed-value field; Guid remains opaque unless its descriptor declares a reference role. `OptionalEntityReference` requires an optional Entity-reference field and separate presence/value readers; presence is checked before `GetPresent`. Readers are captured by value. These are the selected attachment-counter's Counter U64, optional Target Entity/stable Guid and OpaqueId Guid producers; no record-array, mutable-attachment or native-owner producer is added.

Registration does not create an attachment automatically; absence remains absence. `SetAttachment` installs an explicit value and schema defaults do not repair required source data. `Validate` executes through the real installation/reference-validation operation. Persistence-dependent Read/Restore/Count/Write observations remain M03-B-owned; Release runs only through committed installation cleanup, not an application-created binding.

Only the attachment dispatcher issues Read/Restore/Validate/Capture/Release bindings for the exact frozen family, operation, callback generation and thread. Defaults are null or invalid, bindings revoke in `finally`, and no public constructor or backing escapes exist. Read receives a complete structurally admitted record. Complete requires a nonnull complete immutable author value and exact consumption; Read results have initialized-empty diagnostics, while Rejected results have no usable author value, consumed0 and initialized nonempty diagnostics. Reject preserves actual InvalidRecord/UnsupportedSchema diagnostics; Complete/NeedMoreData passed to Reject is protocol misuse. Null/foreign/duplicate results, default diagnostic arrays, null entries and callback protocol misuse fault the active operation. Expired binding misuse outside an invocation only throws and cannot poison a committed World.

Restore resolves declared reference fields only through the complete candidate map; it grants no component-read authority. Capture/validation use the actual receiving World's declared reference mappings. Only the release coordinator constructs failure values and complete reports. WorldGuid is nonempty; Schema is the exact installed active AttachmentState tuple; Scope is World or Entity; OwnerEntityId is null exactly for World scope and nonempty for Entity scope; ReleasedInstallationId is positive; ReplacementInstallationId is null for removal/destruction and otherwise distinct and positive. PrimaryFailure and every OriginalException are nonnull. Convenience properties copy PrimaryFailure facts, and `ReferenceEquals(InnerException, PrimaryFailure.OriginalException)` is true. AdditionalFailures is initialized, contains no null entries and excludes the primary; OmittedFailureCount is nonnegative; FailureCount is checked `1 + AdditionalFailures.Length + OmittedFailureCount`. Complete means the bounded report was finalized, not that every failure object was retained. `StorageCommitted` is true. Reports have immutable copied facts and original exception references, with no release/mutation authority.

The finalization carrier and its `WorldAttachmentReleaseReportFaultControl` are prepared before storage commit. The coordinator reserves the carrier and slots for first-failure facts, committed revision, exact release-failure count and original exception references before commit, then seals those facts without allocation before escape. It cannot be rewritten or resealed. Both cause properties are nonnull and preserve exact identity; `InnerException` is null because the carrier is preconstructed. It publishes no AdditionalFailures array or fabricated complete-report claim; `StorageCommitted` is true.

With N=0, there are no Release callbacks and no failure-report or current-plan reporting reservation; the standing shutdown allowance remains separately owned. Apply `0 <= K <= N-1` only when N is positive. Mandatory idle-report reservation refusal is a contextual `InvalidOperationException` before write grant and preserves old storage, caller ownership, revision, marker and `LastFault`; the World remains usable. Idle finalization failure uses the prepared finalization exception, faults the World, preserves committed storage/transfer/revision and continues safe cleanup once. In Step, the actual exception becomes `WorldStepException.InnerException`; the tick is not published or advanced, and an already-installed attachment transaction is not claimed to roll back.

```csharp
public sealed class SystemDescription
{
    public SystemDescription(Guid systemId, int order, SimulationStage stage, ImmutableArray<SystemResourceAccess> access, ImmutableArray<Guid> before, ImmutableArray<Guid> after, SystemThreadMode threadMode);
}

public sealed class EntityQueryDescription
{
    public EntityQueryDescription(ImmutableArray<Guid> requiredSchemaIds, ImmutableArray<Guid> excludedSchemaIds, ImmutableArray<SystemResourceAccess> access);
    public EntityQueryDescription(ImmutableArray<Guid> requiredSchemaIds, ImmutableArray<Guid> excludedSchemaIds, ImmutableArray<SystemResourceAccess> access, SystemPartitionRange range);
    public ImmutableArray<Guid> RequiredSchemaIds { get; }
    public ImmutableArray<Guid> ExcludedSchemaIds { get; }
    public ImmutableArray<SystemResourceAccess> Access { get; }
    public SystemPartitionRange? Range { get; }
}

public sealed class EntityTagSchema
{
    public EntityTagSchema(Guid schemaId, int schemaVersion);
    public Guid SchemaId { get; }
    public int SchemaVersion { get; }
}

public interface IWorldSystem
{
    void Execute(WorldSystemContext context);
}

public sealed class WorldStepException : InvalidOperationException
{
    internal WorldStepException(long tick, Diagnostic diagnostic, Exception innerException);
    public long Tick { get; }
    public Diagnostic Diagnostic { get; }
}

public enum HierarchyDestroyMode
{
    DetachChildrenKeepWorld = 0,
    DestroySubtree = 1
}

public readonly struct Entity : IEquatable<Entity>
{
    internal Entity(uint ownerId, uint index, uint generation);
    public uint OwnerId { get; }
    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid { get; }
    public bool Equals(Entity other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(Entity left, Entity right);
    public static bool operator !=(Entity left, Entity right);
}

public ref struct EntityChunkView
{
    internal EntityChunkView(ReadOnlySpan<Entity> entities);
    public ReadOnlySpan<Entity> Entities { get; }
    public ReadOnlySpan<T> Read<T>() where T : unmanaged;
    public Span<T> Write<T>() where T : unmanaged;
}

public sealed class World : IDisposable
{
    private World(WorldConstructionPlan constructionPlan);
    public static World Create(WorldOptions options);
    public Guid WorldGuid { get; }
    public Guid ReplayLineageId { get; }
    public Guid IdentityBranchId { get; }
    public Guid ExecutionSessionId { get; }
    public SpatialFrame3 CurrentFrame3 { get; }
    public SpatialFrame2? CurrentFrame2 { get; }
    public SpatialFrame3 CreateNextFrame3(SpatialPosition3 origin, Vector3 minimum, Vector3 maximum);
    public SpatialFrame2 CreateNextFrame2(SpatialPosition2 origin, Vector2 minimum, Vector2 maximum);
    public FrameChangeReceipt SetFrame(SpatialFrame3 frame);
    public FrameChangeReceipt SetFrame(SpatialFrame2 frame);
    public Diagnostic? LastFault { get; }
    public DiagnosticsService Diagnostics { get; }
    public WorldSystemCleanupReport? LastSystemCleanupReport { get; }
    public int CpuWorkerCount { get; }
    public Guid GetEntityId(Entity entity);
    public WorldEntityIdentity GetEntityIdentity(Entity entity);
    public bool TryGetEntity(Guid stableEntityId, out Entity entity);
    public bool TryGetEntity(WorldEntityIdentity identity, out Entity entity);
    public void BeginIdentityBranch(Guid identityBranchId);
    public Entity CreateEntity(EntityDescription description);
    public void DestroyEntity(Entity entity, HierarchyDestroyMode mode);
    public bool Has<T>(Entity entity) where T : unmanaged;
    public bool HasTag(Entity entity, Guid schemaId);
    public void AddTag(Entity entity, Guid schemaId);
    public void RemoveTag(Entity entity, Guid schemaId);
    public bool TryGet<T>(Entity entity, out T value) where T : unmanaged;
    public void Set<T>(Entity entity, in T value) where T : unmanaged;
    public void Add<T>(Entity entity, in T value) where T : unmanaged;
    public void Add<T>(Entity entity) where T : unmanaged;
    public void Remove<T>(Entity entity) where T : unmanaged;
    public void SetAttachment<T>(T value,
        AttachmentOwnership ownership = AttachmentOwnership.CallerOwned) where T : class;
    public void SetAttachment<T>(Entity entity, T value,
        AttachmentOwnership ownership = AttachmentOwnership.CallerOwned) where T : class;
    public bool TryGetAttachment<T>([NotNullWhen(true)] out T? value) where T : class;
    public bool TryGetAttachment<T>(Entity entity,
        [NotNullWhen(true)] out T? value) where T : class;
    public bool RemoveAttachment<T>() where T : class;
    public bool RemoveAttachment<T>(Entity entity) where T : class;
    public void SetTimeScale(float timeScale);
    public void PumpControlBoundary();
    public void ForEachChunk<TAction>(EntityQueryDescription query, scoped ref TAction action)
        where TAction : struct, IAction<EntityChunkView>, allows ref struct;
    public WorldStepReceipt Step();
    public bool TrySubscribe(Action<CommittedWorldPublication> callback, [NotNullWhen(true)] out WorldSubscription? subscription);
    public void Dispose();
}

public sealed class WorldStepReceipt
{
    internal WorldStepReceipt();
    public CommittedWorldPublication Publication { get; }
    public PublicationDeliveryReport DeliveryReport { get; }
    public Guid WorldGuid { get; }
    public long CompletedTick { get; }
    public long NextTickIndex { get; }
    public SpatialFrame3 Frame3 { get; }
    public SpatialFrame2? Frame2 { get; }
    public ImmutableArray<CommandReceipt> CommandReceipts { get; }
}

public sealed class CommittedWorldPublication
{
    internal CommittedWorldPublication();
    public Guid ExecutionSessionId { get; }
    public uint WorldOwnerId { get; }
    public long PublicationSequence { get; }
    public Guid WorldGuid { get; }
    public long CompletedTick { get; }
    public long NextTickIndex { get; }
    public SpatialFrame3 Frame3 { get; }
    public SpatialFrame2? Frame2 { get; }
    public ImmutableArray<CommandReceipt> CommandReceipts { get; }
    public ImmutableArray<CommittedWorldEvent> Events { get; }
    public CommandReceipt GetCommandReceipt(EntityToken token);
}

public sealed class PublicationDeliveryReport
{
    internal PublicationDeliveryReport();
    public CommittedWorldPublication Publication { get; }
    public Guid ExecutionSessionId { get; }
    public uint WorldOwnerId { get; }
    public long PublicationSequence { get; }
    public ImmutableArray<SubscriberDeliveryResult> Results { get; }
}

public sealed class SubscriberDeliveryResult
{
    internal SubscriberDeliveryResult();
    public CommittedWorldPublication Publication { get; }
    public Guid ExecutionSessionId { get; }
    public uint WorldOwnerId { get; }
    public long PublicationSequence { get; }
    public long SubscriptionId { get; }
    public SubscriberDeliveryStatus Status { get; }
    public SubscriberDeliveryFailure? Failure { get; }
}

public enum SubscriberDeliveryStatus
{
    Delivered = 0,
    Failed = 1
}

public sealed class SubscriberDeliveryFailure
{
    internal SubscriberDeliveryFailure();
    public SubscriberDeliveryFailureKind Kind { get; }
    public Exception OriginalException { get; }
    public Diagnostic Diagnostic { get; }
}

public enum SubscriberDeliveryFailureKind
{
    SubscriberException = 0
}

public sealed class WorldSubscription : IDisposable
{
    internal WorldSubscription();
    public long SubscriptionId { get; }
    public void Dispose();
}

public sealed class CommandReceipt
{
    internal CommandReceipt();
    public ImmutableArray<WorldEntityIdentity> DestroyedEntityIdentities { get; }
    public bool TryResolve(EntityToken token, out Entity entity);
}

public readonly struct EntityToken : IEquatable<EntityToken>
{
    public bool IsValid { get; }
    public bool Equals(EntityToken other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(EntityToken left, EntityToken right);
    public static bool operator !=(EntityToken left, EntityToken right);
}

public enum FrameChangeStatus
{
    Applied,
    Rejected
}

public sealed class FrameChangeReceipt
{
    internal FrameChangeReceipt();
    public FrameChangeStatus Status { get; }
    public Guid WorldGuid { get; }
    public SpatialFrame3? CommittedFrame3 { get; }
    public SpatialFrame2? CommittedFrame2 { get; }
    public long? CommittedFrameRevision { get; }
    public long? ControlSequence { get; }
    public long? AppliedAtNextTick { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public Task Retirement { get; }
}

public sealed class WorldFrameTransactionException : Exception
{
    internal WorldFrameTransactionException(Exception? innerException,
        Diagnostic? primaryDiagnostic, Guid worldGuid, int dimension,
        ImmutableArray<Exception> cleanupExceptions);
    public Diagnostic? PrimaryDiagnostic { get; }
    public Guid WorldGuid { get; }
    public int Dimension { get; }
    public ImmutableArray<Exception> CleanupExceptions { get; }
}

public sealed class WorldFrameRetirementException : Exception
{
    internal WorldFrameRetirementException(Guid worldGuid, int dimension,
        SpatialFrame3? committedFrame3, SpatialFrame2? committedFrame2,
        long committedFrameRevision, long controlSequence, long appliedAtNextTick,
        ImmutableArray<Exception> cleanupExceptions);
    public Guid WorldGuid { get; }
    public int Dimension { get; }
    public SpatialFrame3? CommittedFrame3 { get; }
    public SpatialFrame2? CommittedFrame2 { get; }
    public long CommittedFrameRevision { get; }
    public long ControlSequence { get; }
    public long AppliedAtNextTick { get; }
    public ImmutableArray<Exception> CleanupExceptions { get; }
}

public sealed class WorldOwnerQueueFullException : InvalidOperationException
{
    internal WorldOwnerQueueFullException();
    public Diagnostic Diagnostic { get; }
    public Guid WorldGuid { get; }
    public int Dimension { get; }
    public int PendingCount { get; }
    public int PendingLimit { get; }
    public long NextOwnerRequestSequence { get; }
    public long AcceptedOwnerRequests { get; }
    public long NextControlSequence { get; }
}

public readonly struct WorldEntityIdentity : IEquatable<WorldEntityIdentity>
{
    internal WorldEntityIdentity(Guid worldGuid, Guid creationLineageId, Guid creationBranchId, Guid entityId);
    public Guid WorldGuid { get; }
    public Guid CreationLineageId { get; }
    public Guid CreationBranchId { get; }
    public Guid EntityId { get; }
    public bool IsValid { get; }
    public bool Equals(WorldEntityIdentity other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
}

public sealed class WorldIdentityRemap : IDisposable
{
    internal WorldIdentityRemap();
    public ReadOnlySpan<byte> SourceCheckpointDigest { get; }
    public ReadOnlySpan<byte> SourcePayloadDigest { get; }
    public Guid SourceWorldGuid { get; }
    public Guid SourceReplayLineageId { get; }
    public Guid SourceIdentityBranchId { get; }
    public Guid TargetWorldGuid { get; }
    public Guid TargetReplayLineageId { get; }
    public Guid TargetIdentityBranchId { get; }
    public bool TryMapEntity(Guid sourceEntityId, out Guid targetEntityId);
    public bool TryMapSceneInstance(Guid sourceSceneInstanceId, out Guid targetSceneInstanceId);
    public void Dispose();
}

public sealed class WorldSystemContext
{
    internal WorldSystemContext(SimulationStepContext step, CommandWriter commands);
    public SimulationStepContext Step { get; }
    public CommandWriter Commands { get; }
    public Guid GetEntityId(Entity entity);
    public WorldEntityIdentity GetEntityIdentity(Entity entity);
    public bool TryGetEntity(Guid stableEntityId, out Entity entity);
    public bool TryGetEntity(WorldEntityIdentity identity, out Entity entity);
    public void ForEachChunk<TAction>(EntityQueryDescription query, scoped ref TAction action)
        where TAction : struct, IAction<EntityChunkView>, allows ref struct;
    public void ForEachResourcePartition<T, TAction>(
        Guid resourceId,
        SystemPartitionRange range,
        scoped ref TAction action)
        where T : unmanaged
        where TAction : struct, IAction<WorldResourcePartitionView<T>>,
            allows ref struct;
}

public readonly ref struct WorldResourcePartitionView<T>
    where T : unmanaged
{
    internal WorldResourcePartitionView(
        WorldResourcePartitionControl control,
        int partitionIndex);

    public int PartitionIndex { get; }

    public T Read();

    public void Write(in T value);
}

internal sealed class WorldResourcePartitionControl
{
    internal WorldResourcePartitionControl();
}

public static class WorldSystemResources
{
    public static Guid EntityMembershipId { get; }
}

public sealed class CommandWriter
{
    internal CommandWriter();
    public EntityToken Create(EntityDescription description);
    public void Set<T>(Entity entity, in T value) where T : unmanaged;
    public void Set<T>(EntityToken entity, in T value) where T : unmanaged;
    public void Add<T>(Entity entity, in T value) where T : unmanaged;
    public void Add<T>(EntityToken entity, in T value) where T : unmanaged;
    public void Remove<T>(Entity entity) where T : unmanaged;
    public void Remove<T>(EntityToken entity) where T : unmanaged;
    public void AddTag(Entity entity, Guid schemaId);
    public void AddTag(EntityToken entity, Guid schemaId);
    public void RemoveTag(Entity entity, Guid schemaId);
    public void RemoveTag(EntityToken entity, Guid schemaId);
    public void Destroy(Entity entity, HierarchyDestroyMode mode);
    public void Destroy(EntityToken entity, HierarchyDestroyMode mode);
    public void SetEntityReference<T>(
        Entity owner,
        ComponentFieldPath path,
        Entity? target)
        where T : unmanaged;
    public void SetEntityReference<T>(
        Entity owner,
        ComponentFieldPath path,
        EntityToken? target)
        where T : unmanaged;
    public void SetEntityReference<T>(
        EntityToken owner,
        ComponentFieldPath path,
        Entity? target)
        where T : unmanaged;
    public void SetEntityReference<T>(
        EntityToken owner,
        ComponentFieldPath path,
        EntityToken? target)
        where T : unmanaged;
}
```

The engine-produced successful `CommandReceipt` adds the get-only member `ImmutableArray<WorldEntityIdentity> DestroyedEntityIdentities { get; }`. It contains the copied receipt-local subsequence of qualified identities actually destroyed in effective structural order, including generated descendants attributed to their original command. It has no public setter or caller construction path; failure produces no successful receipt. The vector grants no membership authority and proves neither cleanup-dispatch order nor a global order across receipts. Its preapplication and retained backing are included in the existing `CMD+D` and `O+W+D` charge ledgers in [04](04-capacity-and-workload-profiles.md).

`WorldSystemResources.EntityMembershipId` is the fixed M03 identity-membership resource `5ae0d511-7e5f-81df-bb07-e2b259892443`. It is a World-wide read-only permission with no range; it permits stable identity lookup only, not component/resource access, writes, enumeration or partition bypass. `WorldIdentityRemap` owns an independently budgeted immutable mapping from the pinned source checkpoint's live Entity/scene-instance GUIDs to target stable GUIDs. It retains neither World nor checkpoint bytes, exposes no live handles, and is present only on a successful load receipt.

The existing engine-produced `CommittedWorldPublication` and `PublicationDeliveryReport` expose `ExecutionSessionId`, `WorldOwnerId` and `PublicationSequence`; `WorldSubscription` exposes its positive monotonic `SubscriptionId`. These are copied into delivery observations before publication. None has a public constructor or setter for operational identity metadata.

`CommittedWorldPublication` exposes the complete event batch through the
following get-only member; this is an addition to the existing publication
contract and does not define its other member set:

```csharp
public ImmutableArray<CommittedWorldEvent> Events { get; }
```

The supporting event base is owned by `TerraFX.Entities`:

```csharp
public abstract class CommittedWorldEvent
{
    private protected CommittedWorldEvent(
        ContentSchemaReference schema,
        uint worldOwnerId,
        long completedTick,
        long sequence);

    public ContentSchemaReference Schema { get; }
    public uint WorldOwnerId { get; }
    public long CompletedTick { get; }
    public long Sequence { get; }
}
```

The base has no public constructor or setter. An admitted descendant is a named,
sealed, deeply immutable engine type; no external subclass, plugin event
interface, or caller-supplied emitter is admitted. `Schema` is a valid exact
Content-scope `EventPayload` reference with a nonempty ID, positive Major, and
nonnegative Minor. `WorldOwnerId` is nonzero and equals the containing
publication's owner; `CompletedTick` equals that publication's completed tick;
`Sequence` is the dense zero-based index in `Events`. The concrete CLR type
identifies the event kind; no generic type string or plugin discriminator is
added. The current M03-A descendant set is empty. These clauses add only
`Events` and its element type; the remaining complete publication/receipt
member list is not accepted by implication.

The internal issuer support is not a public API: `TerraFX.Core` exposes its
`TerraFX.OwnerIdAllocator.Allocate(): uint` member to the reviewed Runtime friend
assembly. Semantic authority is bound to exact signatures and caller edges below;
CLR accessibility and assembly friendship alone grant no authority.

| Exact semantic actor signature | Allowed caller / effect |
|---|---|
| `static World Create(WorldOptions options)` | Public standalone entry; validates and seals its admission inputs. In M03-A it is the only caller permitted to construct `World(WorldConstructionPlan)`; it has no direct owner-allocator permission. |
| `World(WorldConstructionPlan constructionPlan)` | Revalidates the exact admitted plan and issues once after final domain/identity validation. This is the only Runtime caller of `TerraFX.OwnerIdAllocator.Allocate(): uint` across full M03. Hosted and restore factories become additional exact construction callers only under their owning milestones. |
| `Entity IssueEntity(uint index, uint generation)` | Exact entity-issuance helper. In M03-A, only `World.CommitStructuralTransactions()` may call it; producers and generated helpers may not. The future replacement-restore route receives a separately reviewed exact binding under its owning milestone. |
| `Entity(uint ownerId, uint index, uint generation)` | Internal constructor; only `World.IssueEntity(uint,uint)` may call it. Replacement reconstruction issues on the candidate World through its separately enrolled restore route, not by direct construction. |
| `WorldStepReceipt Step()` | Standalone public step; may call `World.ExecuteTick()` only after standalone admission and only for one admitted fixed tick. |
| `WorldStepReceipt ExecuteTick()` | Common tick lifecycle helper; in M03-A, its only caller is `World.Step()`. The M04 host owner loop requires its own exact caller binding before enrollment and cannot invoke public `Step()` on a host-managed World. |
| `void PumpControlBoundary()` | Public idle owner-boundary entry; may call `World.CommitStructuralTransactions()` for accepted control work without advancing time. |
| `Entity CreateEntity(EntityDescription description)` | May call `World.CommitStructuralTransactions()` for an immediate owner-boundary create; returns only after that request commits and its Entity is issued. |
| `void DestroyEntity(Entity entity, HierarchyDestroyMode mode)` | May call `World.CommitStructuralTransactions()` for an immediate owner-boundary destroy; the explicit mode is preserved. |
| `void Add<T>(Entity entity, in T value) where T : unmanaged` | May call `World.CommitStructuralTransactions()` for the immediate structural Add form. |
| `void Add<T>(Entity entity) where T : unmanaged` | May call `World.CommitStructuralTransactions()` for the immediate default-initializing Add form. |
| `void Remove<T>(Entity entity) where T : unmanaged` | May call `World.CommitStructuralTransactions()` for an immediate owner-boundary removal. |
| `void AddTag(Entity entity, Guid schemaId)` | May call `World.CommitStructuralTransactions()` for an immediate tag transition. |
| `void RemoveTag(Entity entity, Guid schemaId)` | May call `World.CommitStructuralTransactions()` for an immediate tag transition. |
| `void CommitStructuralTransactions()` | World-owned preflight/commit coordinator; called only by `World.ExecuteTick()`, `World.PumpControlBoundary()`, and the exact immediate structural operations listed above. It may call `World.IssueEntity(uint,uint)`; producers receive no commit or issuance permission. `Set<T>` remains nonstructural and is not a coordinator caller. |

The policy key includes declaring assembly, metadata type, method kind/name,
staticness, generic arity, exact parameter/ref/scoped forms, and return/ref form.
The two constructors use constructor-kind `.ctor` keys with a non-ref `void`
signature; the object created by `new` is not a constructor return value.
The declarations above are the M03-A actor inventory, not a claim that the
implementation or enforcement already exists. Hosted construction and restored
candidate actors are not A exemptions: before M04 or M03-B enrolls them, record
their full signatures and exact caller edges in the owning contract and policy.
Unlisted/generated helpers receive no exemption. No type-wide `World.*` grant,
new friendship, dependency edge, public allocator, owner-ID constructor, or
`World.OwnerId` property is added.

#### WorldGuid registry semantic actors

These engine-private actors close the WorldGuid admission boundary; they are not
public APIs, new architecture exemptions, or permission to call Hosting from
Entities.

| Exact type/member | Issuer/caller and authority |
|---|---|
| Core `WorldAdmissionParticipant.Claim(Type)` | Only `WorldGuidRegistry.GetSelected()`, passing its own fixed `typeof(WorldGuidRegistry)`; claims selected-Core participation, not owner issuance. |
| Entities `WorldGuidRegistry.GetSelected()` | Performs the Type claim and one-shot bootstrap; accepts no options or demand argument and returns only the fully initialized selected instance. |
| Entities `WorldGuidGrant` | Registry-issued fixed-route cell/generation bundle; no caller-supplied counts. |
| Entities `WorldGuidSlabGrant` | Registry-issued exact G ticket/category/ordinal/generation; consumed only by registry backing allocation. |
| Entities `WorldPreparedReplacement` | Issued by exact-target preparation and retained opaquely by Hosting; no GUID-based reconstruction. |
| `bool WorldPreparedReplacement.TryCaptureCommit(out WorldReplacementOperation? operation, out WorldGuidRefusal refusal)` | Called only by public Hosting `WorldPersistence.CommitLoadAsync(WorldLoadPlan, CancellationToken)`; forwards to the sealed Entities control for all-or-none capture. |
| `WorldPreparedReplacement.CloseAcquisition()` | Called only by Hosting `WorldLoadPlan.Dispose()`; closes acquisition but cannot end admitted consumers. |
| `WorldReplacementOperation.TransferSourceToPlayback(WorldPlaybackControl playback)` | Called once by that operation's owner-boundary execution after commit-source quiescence; transfers, rather than duplicates, `S_c`. |
| `WorldGuidUse` / `WorldGuidUseControl` | Fixed independent-use admission against an exact active lifetime control; retains its own U and `W_u/D_u` interests, and cannot quarantine an active binding. |
| `WorldGuidRetirementPermit` | Issued only by `WorldLifetimeControl` after unpublished failure or exact binding revocation; permits retirement/quarantine only of that O generation. |
| `WorldGuidRefusal` | Fixed bounded refusal facts, including unresolved-destination state; diagnostic formatting is outside gates. |

All value carriers default invalid, with no registry/control reference, positive
generation, or authorized role. Reference controls have no caller construction
path; copies do not duplicate ownership. Capture, transfer, ticket consumption
and ending are single-use checked transitions against the owning control.
`IWorldGuidRetentionOwner` is memberless and implemented only by
`WorldConstructionPlanControl`, `WorldLifetimeControl`,
`WorldReplacementOperation` and `WorldGuidUseControl`; implementing it conveys
no mutation, acquisition, close or quarantine authority. Data-only
`WorldRetainedHistoryControl` and `WorldPlaybackControl` do not gain process
binding authority. `WorldGuidUseControl` may quarantine only its own U/domain
interests. Only `WorldLifetimeControl`, presenting its exact retirement permit,
may quarantine an unpublished or previously revoked binding. An independent U
can delay retirement of a revoked O but cannot change an active binding.
No generic `Quarantine(Guid)`, caller-configurable demand structure, or
GUID-based reattachment route is allowed.

`WorldOptions` defaults are `WorldGuid = null`, `TickRate = 60`, `MaximumLiveEntities = 1,048,576`, `MaximumArchetypes = 4096`, `MaximumDeferredCommands = 65,536`, `MaximumComponentTypes = 256`, `MaximumPendingOwnerRequests = 128`, `Profile = Standard`, `InitialTimeScale = 1`, `SpatialSettings = SpatialSettings.Default`, `SpatialProfileId = SpatialProfileId.Default`, and `SchedulingLimits = WorldSchedulingLimits.Default`. `WorldRegistration` is required and immutable before allocation. A null `CpuWorkerCount` resolves to the documented default for standalone `World.Create`; host-created Worlds inherit the host pool when null. Explicit values admit `[0, 64]`; hosted explicit values must match the host. World creation resolves the spatial ID through `SpatialProfiles`, requires matching registered settings, validates compiled schema-registration closure and exact component/tag/system factories before allocating owner/live storage. Custom spatial settings therefore require an explicitly registered matching profile ID; the zero-initialized ID remains invalid. Raw positions cannot prove their source profile. Admitted tick rates are `[1, 1000]`; every override is validated before allocation. The options type is a sealed class, not a zero-defaultable value type.

`WorldOptions.FrameExtent2` and `FrameExtent3` are nullable named value tuples of `(Minimum, Maximum)`, using `Vector2` and `Vector3` respectively. Both default to null. A supplied tuple contains already-selected-distance-unit values; copy its binary32 lane bits without normalization, clamping or unit conversion. Require every endpoint finite and `Minimum <= Maximum` on each axis; equal endpoints and finite signed zero retain their supplied bits. If `FrameExtent3` is null, convert exact physical -4096 and +4096 meters once into the frozen `SpatialSettings` distance unit, using `SpatialMath.ConvertQuantity(-4096L,1L,0,targetExponent,1)` and the corresponding positive literal, and use those endpoints on each axis. If `Enable2DFrame` is true, apply the same null/default or explicit-tuple rule to `FrameExtent2`. If `Enable2DFrame` is false, `FrameExtent2` must be null and `CurrentFrame2` is absent; do not construct or convert a 2D default. Validate extent inputs before owner/live-storage allocation. A nonfinite or axis-inverted supplied tuple throws `ArgumentException` naming the corresponding property; a present `FrameExtent2` while `Enable2DFrame` is false throws `ArgumentException` naming `FrameExtent2`. Conversion overflow rejects creation through `OverflowException`; no clamped, infinite, zero-substitute or unconverted-meter fallback occurs. The resulting extents remain subject to the selected frame's existing finite/ordered and subsystem-admission rules.

`WorldSchedulingLimits.Default` has byte limits of 16 MiB registration, 8 MiB graph, 64 MiB work plans, 32 MiB command staging, 2 GiB shared owned state and 8 MiB diagnostics; resource/system state is additionally bounded by min(128 MiB, configured shared owned state), plus 16,777,216 conflict comparisons. Each byte limit can be lowered exactly to 4096 bytes; the comparison limit can be lowered to zero. The scheduler charges actual owned memory and in-flight reservations, not only logical payload. `SystemPartitionRange` is a nonempty checked half-open range; its default is invalid. `SystemResourceAccess.IsValid` and `SystemPartitionRange.IsValid` validate only intrinsic representation, not registration correspondence, ownership, execution phase or contextual domain bounds. Access equality preserves absent versus present range; `Tag` access is read-only.

`Entity`, `EntityDescription`, `EntityToken`, `CommandReceipt`, `SimulationStepContext`, `ComponentSchema<T>` field descriptors, `SystemResourceAccess`, `EntityQueryDescription`, `WorldStepReceipt`, `CommittedWorldEvent`, `CommittedWorldPublication`, `PublicationDeliveryReport`, and `WorldSubscription` use [12](12-entities-components-and-queries.md) and [13](13-jobs-and-frame-pipeline.md)'s contracts. `Entity` carries `OwnerId`, `Index`, and `Generation`; its default is invalid. Component registration is `T : unmanaged`, with no marker interface. `TryGet<T>` returns false only when the live entity lacks that registered component; stale or foreign entities throw.

The declaration block above is the complete selected M03-A public member set
for these receipt, publication, subscriber-result, subscription and token
types in `TerraFX.Entities` (`TerraFX.Runtime`). The classes are sealed,
engine-produced reference types with no public construction path, setter,
`init`, record synthesis, clone or deconstruction surface. Every exposed
`ImmutableArray` is initialized and has no null elements; default arrays are
invalid. Nullable members represent only their documented absence.
`WorldStepReceipt.Publication` is the exact immutable publication supplied to
that Step's captured subscribers. Receipt aliases are copied from it, never
resampled from later World reads. Its `DeliveryReport` is returned only after
the captured batch is complete and the immutable report is sealed. The report
and each result refer to that same publication instance and bind the complete
`(ExecutionSessionId, WorldOwnerId, PublicationSequence, SubscriptionId)`
identity. `Delivered` has null `Failure`; `Failed` has a complete
`SubscriberException` failure containing the exact caught exception object
and Diagnostic. The exception's mutable stack, data and application graph
are not deeply frozen. Zero captured subscribers yields an initialized-empty
Results array. A pre-marker failure returns no new publication, receipt or
report and preserves the prior completed report. Caller-retained output
remains readable across later Steps and World disposal without retaining
mutable World storage. No public `LastPublication`, `LastDeliveryReport`,
`TickIndex` or OwnerId getter is added to `World`; the publication and receipt
provide the caller-visible committed observations.

For the executed tick `t`, `CompletedTick` is `t` and `NextTickIndex` is
`checked(t + 1)`. The publication copies the actual execution session, nonzero
operational World owner, positive publication sequence, persistent WorldGuid,
ticks and one coherent frame generation from the committed record. `Frame3`
is complete and valid. `Frame2` is absent exactly when 2D is disabled; a
present default frame is invalid. Preserve all seven frame members and exact
binary32 lane bits. Current A's successful `Events` is initialized-empty,
including when command receipts are nonempty.

One `CommandReceipt` represents each nonempty admitted logical
writer-prefix/barrier, ordered by stage, exact signed registered
`SystemOrder`, then `WriterPartition`. Repeated queries/calls sharing that
prefix share one receipt; all-canceled prefixes still produce receipts.
Unused writers, refused appends and generated descendants create none.
`DestroyedEntityIdentities` retains the receipt-local effective destruction
order. Failed enclosing Steps expose no successful receipt. `TryResolve` is
historical: a same-publication surviving Create returns its originally
assigned Entity; a canceled Create returns false and exactly default Entity.
Default/unissued, foreign-binding, wrong-publication, another group's token,
and non-Create tokens throw `ArgumentException(nameof(token))`; the out value
on a throwing call is not an observation. Lookup never checks current
membership and remains stable after destruction, slot reuse, later Steps and
disposal.
`CommittedWorldPublication.GetCommandReceipt` returns the owning group for a
token issued by a Create in that publication, including a canceled Create, and
returns that exact object from the publication's `CommandReceipts` array. A
token from another group/barrier in the same publication resolves to that
other group; default/unissued, foreign-binding, another-publication or
non-Create tokens throw `ArgumentException(nameof(token))`. Lookup is
historical and does not check current membership or create an ambient
invocation fault.

`EntityToken` is opaque and has no declared public constructor or issuer. Its
default value is invalid and equals another default token. Copies of one issued Create
compare equal; tokens from distinct Creates remain distinct even when their
other binding facts match. Cancellation, playback and disposal do not change
intrinsic validity or equality. `GetHashCode` is consistent with equality.
Equality does not grant writer authority or projected liveness.
`WorldSubscription.Dispose` is owner-affine and idempotent:
disposing an active subscription unregisters it for later captures only,
does not cancel an invocation already captured for delivery, and does not
change the immutable report already being assembled. Disposal during
postcommit delivery is valid. After World disposal, disposal on the original
owner is an idempotent no-op; wrong-thread disposal still throws the
contextual owner-affinity exception. `SubscriptionId` remains the issued value
and grants no registration authority.

`EntityChunkView` is a stack-only borrowed view. `Read<T>`/`Write<T>` return only the declared component spans for the current callback's physical-row slice, and access outside the registered query/system read-write set is refused. `Entities` and each returned component span describe the same slice. `ForEachChunk` callback cardinality and range behavior follow [13](13-jobs-and-frame-pipeline.md#1-ownership-and-worker-admission): a present query Range is one callback per selected populated plan partition; an absent Range is one callback per matching nonempty physical chunk. It borrows ordinary/ref-struct callable state by reference and invokes synchronously through constrained generic dispatch in stable archetype/chunk/row order. State is neither copied nor erased to an interface object/delegate; mutations remain visible on return or exception. A callback cannot await, retain a span/ref, mutate structure, recurse into a structural barrier, or call a forbidden native service. The scheduler joins admitted jobs before structural work; worker identity never replaces the stable system/partition/sequence command key.

`ForEachResourcePartition<T,TAction>` traverses ascending resource partition indices synchronously on the executing thread with the same uncopied scoped callable-state contract. It validates the exact registered resource and actual `[0, PartitionCount)` domain against the active invocation's declared access/range before callbacks. `WorldResourcePartitionView<T>.Read()` returns a copy; `Write(in T)` is available only under Write access. Its `PartitionIndex` is lifetime-checked, and no mutable backing/reference/span escapes; every view is revoked in `finally`, including exceptional exits.

`World.Create(options)` called by a standalone owner creates a World on that calling thread and owns its worker pool; a host-created World is owned and stepped only by the Host scheduler. `World.Step()` is owner-thread-only for a standalone World and issues exactly one fixed tick; its immutable context is engine-issued, not caller-constructed. It may explicitly single-step a paused standalone World and does not consume paused gameplay input. Calling `Step` directly on a host-managed World is refused. `PumpControlBoundary()` drains accepted owner-control operations while a standalone World is idle without advancing time; Host invokes the same boundary between catch-up ticks and while no tick is admitted. `SetTimeScale` is owner-only and invalidates the committed marker before changing canonical control state; the Hosting async counterpart queues and completes with an applied/rejected receipt. `World.Step` succeeds only with a committed `WorldStepReceipt` for one admitted fixed tick. A precommit tick fault suppresses that tick's publication, faults the world, updates `LastFault`, and throws `WorldStepException` with the failed tick, diagnostic, and original exception. The committed receipt and current report distinguish canonical publication from each subscriber's `Delivered`/`Failed` result. `TrySubscribe` is owner-thread-only and valid only for a live nonfaulted World outside system/provider and precommit invocation; it is also valid during postcommit subscriber delivery, where changes affect later captured batches only. It returns false only at the chapter [13](13-jobs-and-frame-pipeline.md) per-world bound of 64 subscriptions; false leaves the subscription null, consumes no ID and changes no registration or canonical World state. That otherwise-valid refusal emits the bounded `WorldSubscriptionCapacityExceeded` warning through this World’s diagnostic service. `World.Diagnostics` is the associated Core-owned service while the World is live or faulted and throws `ObjectDisposedException` after disposal; standalone World disposal closes its service, whereas hosted World disposal does not close the shared host service. A subscriber fault cannot undo the stage-9 marker or block later subscribers.

`World.CreateEntity` and `DestroyEntity` are idle owner-boundary operations. During a tick, use the `CommandWriter` from `WorldSystemContext`; structural effects appear only after whole-batch preflight/commit and resolve temporary `EntityToken` values through the resulting `CommandReceipt`. Immediate destruction, both deferred target forms and scene unload always name `HierarchyDestroyMode`; the detach policy is not a mode-less call default, and mode is never inferred from a constructor, target kind or nullable parent. Unknown modes reject before append/reservation/sequence allocation; the writer validates invocation and authority before mode, then target. Successful deferred receipts expose copied destruction identities in receipt-local structural order.

The four `CommandWriter.SetEntityReference<T>` overloads accept live/temporary Entity owner and target forms only through their declared overloads. Resolve `T` and `ComponentFieldPath` to the frozen typed Entity-reference binding; no reflection, arbitrary property setter or token-in-Entity field is permitted. A null target clears only an optional reference; null for a required reference rejects, and a present default/invalid Entity or token is not null. Live Entity arms must match the receiving World and current generation. Token arms must belong to the exact logical writer prefix/tick/barrier and name an earlier admitted Create; validate owner and target arms independently. Validate receiver authority before processing token arguments, then resolve admitted commands against the complete sequential shadow and final receiving map. A default, foreign, wrong-prefix/barrier or already-canceled token supplied to a valid live writer throws `ArgumentException` naming the declared parameter and faults that invocation; a refused append has no command key and consumes no sequence or reservation. A reference admitted before later cancellation is rejected during complete structural preflight and reports its own admitted command key, offending Entity/token and contextual reason. An expired `CommandWriter` receiver throws contextual `InvalidOperationException` before argument inspection and cannot fault a successor invocation. Exception messages are not fixed API text. Chapter 12 owns projected token liveness; Chapter 13 owns fault routing and cause selection.

`EntityDescription` permits a nonempty UUIDv8 `PersistentId` candidate; its public construction does not issue or reserve a World identity. `World.CreateEntity` rejects such a candidate with `ArgumentException(nameof(description))` before mutation or assignment. `CommandWriter.Create` rejects it with the same declared parameter exception and records the actual exception as the live invocation's first sticky fault before throwing; no key, sequence, token, payload preparation or staging reservation is consumed. These are assignment-domain checks, not authored-ID validation. General structural preflight preserves its actual exception cause; only `ExplicitIdentityCollision` has no required `ParamName`, while other reasons retain their declared exception contracts.

`ComponentLayout` rejects an empty LayoutId or logical size outside `[1, 32756]`; `ComponentSchema<T>` additionally requires its selected size to equal `Unsafe.SizeOf<T>()`, initialized required arrays and valid intrinsic flags/tuple. `FromRegistry` resolves only the exact active component tuple and derives selected fields/defaults/rules/storage/restore/flags from Core metadata. `GetField` follows an exact selected `ComponentFieldPath`; unresolved or ambiguous paths throw rather than returning null or a best match. Property paths are initialized, nonempty and at most 64 segments, begin with a positive FieldTag, and traverse only declared records or fixed arrays; they cannot name a RecordArray ordinal or CLR member. Storage member paths are ordinal source spellings, bounded in length/UTF-8 bytes; inline indices are nonnegative. A storage declaration has initialized unique paths and byte-offset-ordered leaves/padding that cover exactly `[0, LogicalBytes)` with no overlap or gap. Inline count/stride multiplication is checked; non-inline leaves cannot carry inline metadata. Runtime construction checks these declarations, but cannot discover undeclared fields or prove true CLR offsets.

`ComponentFieldBinding` factories capture concrete accessor structs by value and reject incompatible selected field roles, required/optional shape, wire type, fixed extent, storage path, duplicate/foreign child or incomplete aggregate closure before provider invocation. Scalar Guid access is opaque; StableGuidReference and EntityReference have explicit separate constructors and reference roles. Optional getters check presence before value access; Clear is the absent transition. Entity getters/setters expose only copied live values, and World-bound mappings use the exact engine-issued complete map. `ComponentFactoryBinding<T>.Create<TAuthor>` validates and captures schema/storage/providers/field roots once, requires positive FactoryId/version and the exact managed stride, then exposes an immutable `ComponentFactoryDescriptor`; the descriptor and `WorldRegistration.GetComponentBinding` are copied facts, not executable authority. Default/restore/capture/validation bindings are engine-issued, operation/thread-scoped and revoked when their callback ends.

For `ComponentReadBinding<TAuthor>`, `Complete` rejects null roots and any consumed extent other than the exact scoped record length; a valid value-type default remains legal if its selected schema admits it. `Reject` requires a defined failure kind, an in-range source offset, a structurally valid optional path and a nonempty bounded message, or a valid non-Complete Core EBIN status/diagnostic whose structural facts are preserved. Each live callback may issue exactly one result through Complete/Reject. A Read result has one complete author root, exact consumption and empty diagnostics; a Rejected result has no usable root, zero consumed bytes and nonempty diagnostics. `TryGetAuthorValue` annotates nullability by status. Null/foreign/duplicate results, a returned null, bad consumption or callback exceptions fault the actual operation; they are not converted to malformed-input receipts. A returned immutable result may outlive its binding but carries no later mutation or lookup authority.

The frame API's `Applied` receipt has no diagnostics and exactly one complete dimensional frame, its nonwrapping revision, the newly appended signed control sequence and application NextTick. `Rejected` has nonempty diagnostics and no committed frame/revision/control/NextTick; a successful precommit abort makes its Retirement task already successful. A preparation/abort failure throws or faults the original operation with `WorldFrameTransactionException`, preserving its primary exception/diagnostic, exact target/dimension and bounded independent cleanup exceptions, with no commit facts. Postcommit cleanup failure faults only `Retirement` with `WorldFrameRetirementException`, which preserves WorldGuid/dimension/complete committed frame/revision/control/NextTick and original cleanup failures. Its fault remains in the World/domain cleanup ledger even if the receipt is dropped. These operational facts add no canonical record or C0 validation rule.

### M03 World construction and participant bindings

| Owner / public surface | Exact amendment contract |
|---|---|
| Entities: `EntityDescription` / initialization | Immutable explicit `Guid? PersistentId` plus typed component initialization/tag sets; null requests automatic issuance, and a present candidate must be nonempty. Construction permits a nonempty UUIDv8 candidate; the actual World assignment rejects the reserved domain. `ComponentInitialization.Value<T>(in T)`/`Defaults<T>()`, T:unmanaged, retain rooted typed value/factory dispatch without boxing columns/reflection. Registration resolves exact schema identity/defaults. Immediate/deferred creation assigns shared ordinal/history only in the successful whole commit; same-writer canceled tokens consume none. |
| Entities: resources and participants | `RegisterResource<T>` captures factory then codec binding exactly once and pairs them by typed family-control identity before remaining validation or runtime backing/OwnerId allocation. The frozen entry retains the description's ResourceId/PartitionCount/AllowParallelAccess, exact Core ResourceState descriptor and closed typed State2 dispatch. Portable unmanaged partitions have fresh engine-owned backing, never caller arrays. `WorldSystemContext.ForEachResourcePartition<T,TAction>` uses concrete scoped action/views, global indices and checked declared access/ranges; no mutable backing escapes. |
| Entities: component Derived dependencies | `ComponentDerivedDependency.FromSchema<TDependency>` names only an exact active, registered Persistent Component tuple/type. `ComponentFactoryBinding<T>.Create` captures an initialized duplicate-free dependency list; non-Derived factories require initialized-empty lists. Admission validates complete projected membership, and candidate restoration establishes the global Persistent-before-Derived barrier. Only the exact component-Derived callback may use `ReadSameEntity<TDependency>`; system and attachment Restore receive no such authority. |
| Entities: stateful systems | `RegisterSystem` freezes a typed `WorldSystemFactoryBinding` separately from `SystemDescription` scheduling facts. The closed factory descriptor binds exact SystemState schema/source leaves, field readers, codec and Create/Defaults/Release roots; stateful execution grants only its own authoritative state. System instances are fresh, nonreentrant and released exactly once under the cleanup contract below. |
| Entities: system cleanup | A preprepared `WorldSystemCleanupReport` retains at most `min(N,64)` failures in RFC-SystemId order, counts all omitted failures, and preserves exact primary/cleanup/finalization exception identities. Construction failures use exception-carried reports because no World exists; disposal publishes the same sealed report through the exception and `World.LastSystemCleanupReport`. Accepted attachment top-level exception identity remains unchanged. |
| Entities: authoritative attachments/state bindings | `AttachmentScope` selects World or Entity owner; `AttachmentOwnership` selects CallerOwned transient values or registered Transferred immutable values. `RegisterAttachment<T,TAuthor>` freezes exact AttachmentState schema/factory/provider/field readers by typed family; same-World callback admission is nonreentrant, read-only TryGet observes only committed/current storage, and each committed transferred installation has an exactly-once release obligation. No mutable authoritative edit API is included. Persistence-dependent Read/Restore/Count/Write execution remains B-owned; Release/report failure semantics and State2 contracts are exact above. |
| Entities: World identity/frame/options | Retain WorldGuid/ReplayLineageId/IdentityBranchId/ExecutionSessionId and WorldOptions.WorldGuid. `InitialReplayLineageId` retains revised5's optional input: supplied nonempty value is exact fresh-history naming only; null selects one independent UUIDv4 after validated inputs, WorldGuid reservation and session acquisition but before owner issuance. It grants no continuation authority and is never resolved during restore. Add nullable exact-target `AdmissionDomain`; null fresh standalone construction creates the default 8-GiB engine domain, while hosted creation uses its host domain. The engine-private hosted factory consumes the exact host worker-pool lease/domain; a conflicting explicit World domain is rejected before construction rather than ignored. The restore-candidate factory consumes only its admitted `WorldConstructionPlan`. `WorldAdmissionDomain(maximumBytes)` is frozen `[4096, 8589934592]`, exposes `MaximumBytes`, `Retirement` and `Dispose`; external ownership stays external. Add CurrentFrame3/nullable CurrentFrame2 and copied publication/receipt frames; exact state/configuration limits and Enable2DFrame default true. `WorldOptions.FrameExtent2/3` declarations and their null/default, dimensional-disable, finite/order, unit, validation and failure behavior are defined by the defaults/admission contract above. `CreateNextFrame3/2` makes owner-idle nonmutating inspected candidates; `SetFrame` admits only the exact standalone World capability and current+1 dimensional revision. Exact Host-targeted dimensional overloads use the same transaction without caller-owner affinity. Receipt retirement and transaction/retirement failures stay separate from Applied; no public OwnerId, host callback route or World-owner allocator is exposed. |
| Entities: qualified membership | Retain the engine-produced WorldEntityIdentity/internal constructor, `GetEntityId`, `GetEntityIdentity` and both existing World/context TryGetEntity forms. Add the missing enum value EntityMembership=4 while retaining WorldSystemResources.EntityMembershipId. Context methods require its fixed Read/no-range grant and every invocation/owner/thread/phase/epoch check; no enumeration/data/range bypass. Qualified value equality is WorldGuid/CreationLineageId/CreationBranchId/EntityId, not authority or public Entity construction. |
| Entities: admission domain / branching | Explicit frozen Entities-owned `WorldAdmissionDomain(maximumBytes)` charges active/candidate/retiring/quarantined state, source/transformed records/scratch/remaps until actual release; the lineage registrar has exact M/K/E/I/R/B ceilings and fixed prospective accounting in chapter 04. Root standalone creation owns a default domain; host/caller may explicitly share its domain; replacement/load plans retain the exact target domain. No process service locator or Entities->Hosting dependency. Owner-boundary `BeginIdentityBranch(Guid branchId)` retains compatible lineage, reserves branch/control state before visibility and never resets assignments. Fresh lineage is confined to fresh admission or the explicitly admitted migration/fork load contract, not an arbitrary branch argument. Default-domain close ownership transfers once, allocation/callback-free, at successful replacement publication; pre-swap failure preserves the old owner, and old retirement never closes or waits for the surviving domain. |
| Entities/Hosting: recorded command adapter | Explicit/generated typed `CommandWriter.SetEntityReference<T>` forms select a descriptor-approved property path and live/token target, resolving only at complete shadow preflight. No arbitrary reflective property setter or token-in-Entity field. Prepared standalone continuation owns exact finite state/continuation bytes and source pins; engine actor admits recorded controls/ticks, compares actual preassignment trace/before-commit hash and stops at exhaustion. It grants no host Input or arbitrary code registration. |

### M03 system-state and Derived execution contract

`WorldSystemFactoryBinding.Stateless` requires a nonempty FactoryId, positive FactoryVersion, concrete sealed nonabstract closed `TSystem`, and a source declaration whose `SystemType` is exactly `TSystem`, with no authoritative leaves or state row. `Stateful` additionally requires a nonnull exact active SystemState schema, initialized typed field bindings, defaults, codec, Create, and Release roots. `TAuthor` is a qualified immutable symbolic graph. Each source leaf is classified as Authoritative, DerivedCache, or ImmutableConfiguration; authoritative source leaves have complete one-to-one canonical field coverage, while cache/configuration leaves are excluded from canonical encoding. Schema, source, field, codec, and family controls are captured once; descriptor equality alone never joins executable roots.

The construction sequence is: validate ordinary registration arguments; read `factory.Binding` once; reject null immediately; validate copied descriptor/schema/source/field closure and captured callback roots; freeze the private typed-family control; and complete `WorldRegistration.Build` before World backing or owner construction. Before `Create`, reserve freshness, ownership, and cleanup/report capacity. Invoke `Create`, reject null/wrong type, then atomically validate Runtime-wide object freshness before Defaults, field readers, or mutation. Do not release a null or alias result that was never claimed. Once claimed, any Defaults/validation/construction failure unwinds exactly once through Release. An issued World owner is burned on later failure; the contract does not promise every factory failure precedes issuance.

For stateful Execute, invalidate the canonical marker and grant only that system's own authoritative-state write authority before invocation. Validate state on both normal and exceptional exit, then revoke in `finally`. Stateless systems receive no such grant. Instances do not execute concurrently or reenter. Every provider/reader invocation has an exact same-World nonreentrant boundary; independent mutation, storage/callback, Step/control/structural/capture/replacement/disposal admissions are denied before admission. Legitimate containing dispatch through an issued context, binding, or command remains permitted. Active denial records the first sticky protocol exception before throwing; catching it cannot make dispatch succeed. Expired use is throw-only. No lock spans application callbacks.

`WorldSystemReadBinding<TAuthor>.Complete` accepts only a nonnull complete immutable author and exact source consumption; its result has initialized-empty diagnostics. `Reject` requires a defined failure kind, bounded source offset, valid optional field path and nonempty bounded message, or preserves an actual non-Complete Core EBIN rejection. One live callback issues exactly one result. Rejected results have consumed0, no usable author, and initialized-nonempty diagnostics. Null/foreign/duplicate/inconsistent results, invalid extents, or callback exceptions are protocol/execution failures, never malformed-input fallbacks. Validation/capture/restore ports verify the exact frozen family, instance, schema, operation, generation, thread and reference role. Present references require nonempty stable Entity IDs or valid handles for the exact receiving World; optional absence never invokes resolution. Count/Write are observational; capture validates before Count and verifies exact Count/Write agreement. Restore maps declared references, restores authoritative fields and rebuilds only declared system-owned caches; it does not Execute or read component state.

`ComponentDerivedDependency` lists are initialized, copied, null-free, and duplicate-free. Every entry is the exact active Content Component tuple and exact registered unmanaged type. Only Persistent components are valid; tags, Auxiliary/Foundation records, Transient components, inactive/historical tuples, foreign descriptors, or unregistered/mismatched types reject. `None` and `Reset` factories require an initialized-empty list; Derived factories may also declare initialized-empty when they read no component state. Complete projected membership must contain every dependency before create/add/remove commit or candidate restoration. `ReadSameEntity<TDependency>` requires the exact live component-Derived callback, receiving entity, declared dependency and type, registered component, completed Persistent restore, valid thread/phase, and callback lifetime. It returns a copy and grants no other-entity access, query, write, reference, or borrowed storage; system and attachment Restore do not inherit this capability. Active wrong-kind use is sticky misuse; expired use throws only. Revoke in `finally`.

### M03 system cleanup and finalization contract

For N claimed system instances requiring cleanup, `RetainedFailureCapacity` is exactly `min(N,64)`. Retain the first failures in RFC-SystemId release order and count every omitted failure. `FailureCount == RetainedFailureCount + OmittedFailureCount`; checked counts cannot exceed attempted releases. Each retained entry stores its zero-based release-attempt index, including attempts whose failures were omitted. `GetFailure` accepts only `[0, RetainedFailureCount)` and otherwise throws `ArgumentOutOfRangeException(nameof(index))`. Complete all safe release attempts before sealing. Reports and fallback causes are prepared before callbacks/claims, sealed without allocation before escape, and never rewritten or resealed. A successful cleanup has zero failure/retained/omitted counts. N=0 invokes no Release and needs no per-plan detail reservation; standing shutdown preparation supplies the empty successful report.

Release each claimed instance exactly once in frozen RFC-SystemId order, not execution Order or reverse construction order. A Release returning with sticky misuse fails once with its first sticky exception; otherwise the escaped exception is its failure. Continue safe releases. Do not invoke implicit `IDisposable` or retry. Successful historical object identities remain weak while reachable instances cannot be forgotten. Failed/unknown release ownership remains strongly quarantined and charged. `World.LastSystemCleanupReport` is null before cleanup, publishes only a fully sealed report, and remains readable after disposal without admitting another World operation. Repeated Dispose after completed disposal is a no-op retaining that report and invoking no Release; nested Dispose during active Release is checked before the disposed fast path and is sticky misuse.

The thrown/report channel is exact. Construction primary E0 with successful system cleanup rethrows the identical E0 with original stack; no World exists and no cleanup-failure carrier is needed. Construction E0 plus any system cleanup failure throws `WorldSystemCleanupException`, whose report exposes E0 and cleanup facts without requiring a World. Disposal with system failures throws that exception and publishes the same report through `World.LastSystemCleanupReport`. If aggregate/report finalization itself throws E3, throw the preconstructed `WorldSystemCleanupReportFinalizationException`; its report and exact E3 cause remain accessible without a World. Normal aggregate `InnerExceptions` contains construction primary first, if present, then retained cleanup originals in report order. For E0/E1/E2/E3, the report's construction primary is E0, retained originals are E1 and E2, fallback `OriginalCleanupException` is E1, and `ReportFinalizationException` is E3; the fallback's `InnerException` is null because it was preconstructed. Preserve the accepted attachment exception object when that channel already determines the outcome, attach the sealed system report before escape, and do not replace it with a system aggregate; the Step wrapper retains that same attachment object as `WorldStepException.InnerException`.

### Initial ReplayLineageId and domain admission

Fresh standalone and hosted construction retains the accepted optional initial-lineage input. A supplied nonempty value is used exactly for naming a fresh history; null selects one independent UUIDv4 after frozen input validation, WorldGuid admission and shared execution-session acquisition. Generation failure or contextual collision refuses before owner issuance, without redraw, WorldGuid fallback or continuation fallback. Restored candidates use only source/transformation identity in their admitted `WorldConstructionPlan`; compatible continuation/branch retains source lineage, and an admitted lineage-changing migration/fork uses its explicit target lineage. Stored and supplied nonempty values may be non-v4 or equal WorldGuid.

The domain registrar must index only the validated replay lineage, branch creation lineages and recursively retained fork-source lineages, with chapter 04's accounting and actual-release bounds. Fresh construction must atomically admit complete target membership and its start-new reservation before visibility/OwnerId issuance, applying chapter 12's separate requested-new-lineage and full-union pending-reservation predicates.

RecordedContinuation and compatible Branch preparation must register retained history without a new reservation. A Branch transformation that starts a lineage, a lineage-changing migration, or an IndependentFork must consume the exact reservation already installed with its target membership by the admitted load plan. Candidate construction must retain or transfer that exact target root and grants, not independently register its own lineage or obtain another reservation. Candidate and source/target/operation interests remain distinct.

Publication, abandonment and valid carrier aliases must follow chapter 12's shared-control transitions. Wrapper closure, target-World disposal and external-domain closure must follow chapter 27's distinct capture/publication rules. The sole private constructor must consume the complete non-fabricable `WorldConstructionPlan` and remain the only Core World-owner issuer. These are required behaviors; their presence here supplies no implementation or execution evidence.

`WorldAdmissionDomain(maximumBytes)` accepts an inclusive `[4096, 8589934592]` budget, freezes it at construction and issues no World or OwnerId. `Dispose` is idempotent for the actual current close owner and closes new external admissions without synchronously waiting for participating Worlds, workers or consumers. Its `Retirement` task completes only after all actual domain interests, allocations and registry storage retire; cleanup/quarantine failure faults rather than reporting successful release. Caller/host-created domains stay externally owned by their creator; an engine-default domain's close authority follows the current World through successful replacement. Disposing a replaced World releases only its own interests and never disposes or waits for the shared domain.

## 5. Scenes, cameras, and animation

```csharp
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX;
using TerraFX.Diagnostics;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Scenes
{
public sealed class SceneLoadOptions
{
    public SceneLoadOptions();
    public AssetLoadOptions AssetLoadOptions { get; init; } = AssetLoadOptions.Default;
    public int MaximumExpandedEntities { get; init; } = 1048576;
    public int MaximumPrefabEntities { get; init; } = 65536;
    public int MaximumPrefabDepth { get; init; } = 32;
    public int MaximumHierarchyDepth { get; init; } = 256;
}

public sealed class SceneLoader
{
    public SceneLoader(World world, AssetStore assets);
    public Task<PreparedScene> PrepareAsync(AssetId sceneId, SceneLoadOptions options, CancellationToken cancellationToken = default);
    public Task<SceneActivationReceipt> ActivateAsync(PreparedScene preparedScene, CancellationToken cancellationToken = default);
}

public enum ReparentMode
{
    KeepLocal,
    KeepWorld
}

public enum SceneActivationStatus
{
    Activated,
    Rejected
}

public sealed class SceneActivationReceipt
{
    internal SceneActivationReceipt(SceneActivationStatus status, SceneInstance? instance, long? committedStructuralRevision, ImmutableArray<Diagnostic> diagnostics);
    public SceneActivationStatus Status { get; }
    public SceneInstance? Instance { get; }
    public long? CommittedStructuralRevision { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public sealed class PreparedScene : IDisposable
{
    internal PreparedScene(AssetId assetId);
    public AssetId AssetId { get; }
    public void Dispose();
}

public sealed class SceneInstance
{
    internal SceneInstance(Guid sceneInstanceId);
    public Guid SceneInstanceId { get; }
    public bool TryGetEntity(Guid stableEntityId, out Entity entity);
    public SceneTransaction BeginTransaction();
    public Task<SceneUnloadReceipt> UnloadAsync(HierarchyDestroyMode mode, CancellationToken cancellationToken = default);
}

public sealed class SceneTransaction : IDisposable
{
    internal SceneTransaction();
    public void Reparent(Entity child, Entity? parent, ReparentMode mode);
    public void SetRootPosition(Entity root, SpatialPosition3 position);
    public SceneValidationResponse Validate();
    public Task<SceneTransactionReceipt> CommitAsync(CancellationToken cancellationToken = default);
    public void Dispose();
}

public enum SceneValidationStatus
{
    Valid,
    Rejected
}

public sealed class SceneValidationResponse
{
    internal SceneValidationResponse(SceneValidationStatus status, ImmutableArray<Diagnostic> diagnostics, long baseStructuralRevision);
    public SceneValidationStatus Status { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public long BaseStructuralRevision { get; }
}

public enum SceneTransactionStatus
{
    Committed,
    Rejected
}

public sealed class SceneTransactionReceipt
{
    internal SceneTransactionReceipt(SceneTransactionStatus status, long? committedStructuralRevision, ImmutableArray<Diagnostic> diagnostics);
    public SceneTransactionStatus Status { get; }
    public long? CommittedStructuralRevision { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public enum SceneUnloadStatus
{
    Unloaded,
    AlreadyUnloaded,
    Rejected
}

public sealed class SceneUnloadReceipt
{
    internal SceneUnloadReceipt(SceneUnloadStatus status, Guid sceneInstanceId, long? structuralRevision, ImmutableArray<Diagnostic> diagnostics);
    public SceneUnloadStatus Status { get; }
    public Guid SceneInstanceId { get; }
    public long? StructuralRevision { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public readonly struct ScreenProjection
{
    internal ScreenProjection(TerraFX.Numerics.Vector2 physicalPixel, float reversedDepth, bool isInFront, bool isInsideViewport);
    public TerraFX.Numerics.Vector2 PhysicalPixel { get; }
    public float ReversedDepth { get; }
    public bool IsInFront { get; }
    public bool IsInsideViewport { get; }
}

public static class CameraMath
{
    public static bool TryWorldToScreen(SpatialPosition3 position, CameraSnapshot camera, out ScreenProjection projection);
    public static SpatialRay3 ScreenToRay(TerraFX.Numerics.Vector2 physicalPixel, CameraSnapshot camera);
}
}
```

`SceneLoader` belongs to `TerraFX.Scenes`, binds to its target `World` and `AssetStore` at construction, and depends on neither `EngineContext` nor `TerraFX.Hosting`; this keeps the dependency graph acyclic. Preparation is asynchronous and owns validated asset/package/spatial reservations. `PreparedScene` is bound to that exact target World and one successful activation attempt. `ActivateAsync` queues one owner-control structural intent and completes only after the atomic boundary accepts or rejects it. `Activated` has a non-null instance, committed structural revision, and empty diagnostics; `Rejected` has no instance or committed revision and a nonempty diagnostic array. It never exposes a partially active scene. Disposal before admission prevents activation; after admission the operation owns independent credits and disposal cannot revoke it. `SceneValidationResponse` uses `Valid` with no diagnostics or `Rejected` with diagnostics and always reports its base structural revision, but is only a preview, not a reservation. `CommitAsync` queues the retained intent, revalidates it against state at the boundary, and completes with `Committed` and a committed revision/no diagnostics or `Rejected` with no committed revision and diagnostics. Intents are applied sequentially against current state; `KeepWorld` recomputes the child's current world pose immediately before that operation, including earlier accepted operations in the transaction. Cycles, stale/foreign entities, invalid parent chains, overflow, depth exhaustion, or any partial-capacity failure reject the complete transaction with no observable mutation. `SceneInstance.TryGetEntity` resolves stable authored IDs only while those entities remain live. `SceneUnloadReceipt` includes a revision only for `Unloaded` or `AlreadyUnloaded`; `Rejected` includes diagnostics. Unload is a queued structural request: its task completes when the world transaction and the instance's own package-backed asset leases retire; render/audio snapshots retain their independent package-backed leases until their actual consumers complete.

`SceneTransaction` stages typed hierarchy/root edits, validates and preflights them as one unit, and commits at the structural boundary. `Reparent` always names `KeepLocal` or `KeepWorld`. `SetRootPosition` accepts a `SpatialPosition3`; each root stores that spatial position separately and its `LocalTransform.Position` is `Vector3.Zero`. Descendant translations remain parent-local `Vector3` values. A frame change replaces derived transform/camera/render/physics/audio caches only; it does not shift root positions or child-local transforms. The default entity destroy mode is `DetachChildrenKeepWorld`, but every public call passes its selected `HierarchyDestroyMode`. Camera records and `CameraSnapshot` are chapter [16](16-scenes-transforms-and-cameras.md) contracts. `TryWorldToScreen` returns false only when a perspective point lies on the camera plane (`clip.W = 0`); its output is unusable on false. Behind/near/far/outside flags otherwise remain meaningful. `ScreenToRay` validates the physical pixel and camera snapshot and returns a normalized `SpatialRay3`.

```csharp
using TerraFX.Assets;
using TerraFX.Diagnostics;
using TerraFX.Entities;
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Animation
{
public sealed class AnimationPlaybackOptions
{
    public AnimationPlaybackOptions(float speed);
    public float Speed { get; }
    public static AnimationPlaybackOptions Default { get; }
}

public enum SeekEventPolicy
{
    PoseOnly,
    EmitCrossedEvents
}

public sealed class AnimationController
{
    public AnimationController(World world);
    public Task<AnimationCommandReceipt> PlayClipAsync(Entity entity, AssetLease<AnimationClipAsset> clip, AnimationPlaybackOptions options, CancellationToken cancellationToken = default);
    public Task<AnimationCommandReceipt> SeekClipAsync(Entity entity, TimeSpan position, SeekEventPolicy eventPolicy, CancellationToken cancellationToken = default);
    public Task<AnimationCommandReceipt> RequestTransitionAsync(Entity entity, AnimationTransitionRequest transition, CancellationToken cancellationToken = default);
    public Task<AnimationCommandReceipt> SetLayerWeightAsync(Entity entity, byte layerIndex, float weight, CancellationToken cancellationToken = default);
    public Task<AnimationCommandReceipt> StopClipAsync(Entity entity, CancellationToken cancellationToken = default);
}

public enum AnimationCommandStatus
{
    Applied,
    Rejected
}

public sealed class AnimationCommandReceipt
{
    internal AnimationCommandReceipt(AnimationCommandStatus status, long? appliedTick, Diagnostic? rejection);
    public AnimationCommandStatus Status { get; }
    public long? AppliedTick { get; }
    public Diagnostic? Rejection { get; }
}
}
```

`AnimationPlaybackOptions.Default.Speed` is exactly `1`; the constructor has no optional speed. Finite admitted speed is `[-4, +4]`. `SeekClipAsync` always names `PoseOnly` or `EmitCrossedEvents`; it does not hide the event policy in an overload default. `Applied` receipts have an applied tick and no rejection diagnostic; `Rejected` receipts have no applied tick and a diagnostic. Each command task completes with an animation-specific applied/rejected receipt at the animation stage, not when a renderer displays a pose. Before returning the task, `PlayClipAsync` takes its own version pin; active playback retains a separate pin until stop/completion. Cancellation after command acceptance does not undo stage application or release playback ownership. `AnimationClipAsset`, `SkeletonAsset`, `RootMotionRequest`, `MotionFeedback`, key/event timing, interpolation and layer limits are owned by [20](20-animation-and-deformation.md) and [09](09-native-assets-and-source-adapters.md). Root motion is copied tick data consumed by an application movement system; there is no implicit character controller.

### M03 scene addressing and registry bindings

| Owner / public surface | Exact amendment contract |
|---|---|
| Scenes: options, addresses and slots | Add optional non-v8 `SceneLoadOptions.SceneInstanceId` (null requests automatic shared issuance), MaximumExpandedOccurrences default1048576 and immutable `SceneExternalBinding(Guid slotId,Entity? target)` input. `SceneEntityAddress` copies a bounded root-relative placement path/authored ID; `TryGetEntity(SceneEntityAddress,out Entity)` preserves root-only existing shorthand. `TryGetProvenance(Entity,out SceneEntityProvenance)` returns immutable source occurrence/path/AssetId/expanded key/qualified identity only for exact live instance membership. No caller occurrence namespace or ambient Guid lookup. |
| Scenes: registry enlistment | `WorldRegistrationBuilder.RegisterSceneSupport(SceneSupportRegistration)` enlists one closed Scenes factory before freeze through the lower Entities-owned participant/retirement interface. `SceneSupport.CreateRegistration()` is the sole producer of the sealed immutable opaque registration, whose constructor is internal. SceneLoader obtains only that exact registry; none can register late or construct a second registry for the same World binding. Candidate restore fills its new registry before publication; old loaders/instances revoke and retained source-asset references/package leases retire under their owners. |

```csharp
namespace TerraFX.Entities;

public sealed class SceneSupportRegistration
{
    internal SceneSupportRegistration();
}
```

```csharp
using TerraFX.Entities;

namespace TerraFX.Scenes;

public static class SceneSupport
{
    public static SceneSupportRegistration CreateRegistration();
}
```

## 6. Assets, schema codecs, and packages

`AssetId` is a positive signed 32-bit value scoped to one selected content set. The EBIN field is UInt32; zero and values above `int.MaxValue` are invalid. It is not a GUID, a package identifier, a content hash, or a runtime handle.

```csharp
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;

namespace TerraFX.Assets;

public readonly struct AssetId : IEquatable<AssetId>, IFormattable
{
    public AssetId(int value);
    public int Value { get; }
    public bool IsValid { get; }
    public static bool operator ==(AssetId left, AssetId right);
    public static bool operator !=(AssetId left, AssetId right);
    public bool Equals(AssetId other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public override string ToString();
    public string ToString(string? format, IFormatProvider? formatProvider);
}

public sealed class AssetStoreRegistrationBuilder
{
    public AssetStoreRegistrationBuilder(CompiledContentSchemaRegistry schemas);
    public AssetStoreRegistrationBuilder RegisterCodec<T>(IBinarySchemaCodec<T> codec);
    public AssetStoreRegistrationBuilder RegisterCodec<T>(IContextualBinarySchemaCodec<T> codec);
    public AssetStoreRegistration Build();
}

public sealed class AssetStoreRegistration
{
    internal AssetStoreRegistration(CompiledContentSchemaRegistry schemas);
    public CompiledContentSchemaRegistry Schemas { get; }
}

public sealed class AssetStoreOptions
{
    public AssetStoreOptions(string absolutePackagePath,
        scoped ReadOnlySpan<byte> expectedPackageSha256, AssetStoreRegistration registration);
    public string AbsolutePackagePath { get; }
    public ImmutableArray<byte> ExpectedPackageSha256 { get; }
    public AssetStoreRegistration Registration { get; }
    public long CpuAssetBudgetBytes { get; init; } = 536870912;
    public int MaximumDecodedAssetBytes { get; init; } = 268435456;
    public int MaximumConcurrentDecodeBytes { get; init; } = 268435456;
    public int MaximumLiveAssetLeases { get; init; } = 4096;
}

public sealed class AssetStore : IAsyncDisposable
{
    private AssetStore();
    public static Task<AssetStore> CreateAsync(AssetStoreOptions options,
        CancellationToken cancellationToken = default);
    public Task<AssetLease<T>> LoadAsync<T>(AssetId assetId,
        CancellationToken cancellationToken = default);
    public Task ReplacePackageAsync(AssetStoreOptions options,
        CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}

public sealed class AssetLease<T> : IDisposable
{
    internal AssetLease(AssetId assetId, T value, IDisposable packageLease);
    public AssetId AssetId { get; }
    public T Value { get; }
    public void Dispose();
}

public enum BinaryRecordStatus
{
    Complete,
    NeedMoreData,
    DestinationTooSmall,
    InvalidRecord,
    UnsupportedSchema
}

public interface IBinarySchemaCodec<T>
{
    Guid SchemaId { get; }
    int SchemaMajor { get; }
    int SchemaMinor { get; }
    BinaryRecordStatus Read(scoped ReadOnlySpan<byte> source,
        [MaybeNull] out T value, out int bytesConsumed);
    BinaryRecordStatus Write(in T value, scoped Span<byte> destination,
        out int bytesWritten);
}

public sealed class AssetCodecContext
{
    internal AssetCodecContext(AssetId rootAssetId, CompiledContentSchemaRegistry schemas,
        SpatialProfileId profileId, SpatialSettings spatialSettings);
    public AssetId RootAssetId { get; }
    public CompiledContentSchemaRegistry Schemas { get; }
    public SpatialProfileId ProfileId { get; }
    public SpatialSettings SpatialSettings { get; }
}

public interface IContextualBinarySchemaCodec<T>
{
    Guid SchemaId { get; }
    int SchemaMajor { get; }
    int SchemaMinor { get; }
    BinaryRecordStatus Read(scoped ReadOnlySpan<byte> source, AssetCodecContext context,
        [MaybeNull] out T value, out int bytesConsumed, out BinarySchemaDiagnostic? diagnostic);
    BinaryRecordStatus Write(in T value, AssetCodecContext context,
        scoped Span<byte> destination, out int bytesWritten,
        out BinarySchemaDiagnostic? diagnostic);
}
```

The expected digest is exactly 32 bytes and applies to the complete package, not an individual asset. The store uses the application's compiled schema registrations; package data cannot add schemas or codecs. `LoadAsync<T>` resolves the AssetId only in the selected content set and requires the registered schema expected by `T` and every declared typed reference. It returns a CPU lease, not GPU/audio readiness. A replacement package is validated before an atomic content-set switch; outstanding leases keep their old package backing alive until disposal. Cancellation cannot release bytes still used by a decoder or consumer. Source adapters and deterministic cooking are offline Content.Tools/MSBuild responsibilities, not runtime importer or cook-receipt APIs.

The asset ID allocator, sidecar persistence, provisional ranges and finalization rules are in [11](11-assets-import-streaming-and-packages.md). Ordinary saves retain AssetIds; only StrictLocal may pin the package-wide SHA-256. No public `AssetVersion`, per-asset digest, import-recipe identity, package catalogue, staging graph, publication receipt, or generic migration API is defined.
## 7. Graphics and rendering

Native ownership is a private implementation decision under [02 section 4](02-dotnet-api-memory-and-ownership.md#4-native-memory-handles-and-disposal), not permission to change these declared base types/interfaces or disposal authority. `GraphicsDevice` stays sealed and host-owned; direct native fields/finalizer cleanup do not add a public `SafeHandle` base, handle escape or independent caller disposal path. Existing disposable GPU owners keep their declared `IDisposable` surface and actual-consumer retirement rules.

```csharp
using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Diagnostics;

namespace TerraFX.Graphics
{
public sealed class BufferDescription
{
    public BufferDescription(long byteLength, BufferUsage usage, GpuMemoryKind memoryKind, int? structuredStride, string debugName);
    public long ByteLength { get; }
    public BufferUsage Usage { get; }
    public GpuMemoryKind MemoryKind { get; }
    public int? StructuredStride { get; }
    public string DebugName { get; }
}

public sealed class TextureDescription
{
    public TextureDescription(TextureDimension dimension, int width, int height, int depth, int arrayLayers, int mipLevels, TextureFormat format, TextureUsage usage, string debugName);
    public TextureDimension Dimension { get; }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public int ArrayLayers { get; }
    public int MipLevels { get; }
    public TextureFormat Format { get; }
    public TextureUsage Usage { get; }
    public string DebugName { get; }
}

[Flags]
public enum TextureUsage
{
    None = 0,
    Sampled = 1,
    Storage = 2,
    ColorAttachment = 4,
    DepthAttachment = 8,
    CopySource = 16,
    CopyDestination = 32
}

public readonly struct TextureSubresource
{
    public TextureSubresource(int mipLevel, int arrayLayer, TextureAspect aspect);
    public int MipLevel { get; }
    public int ArrayLayer { get; }
    public TextureAspect Aspect { get; }
}

public readonly struct TextureDataLayout
{
    public TextureDataLayout(nint rowPitch, nint slicePitch);
    public nint RowPitch { get; }
    public nint SlicePitch { get; }
}

public sealed class TextureReadback : IDisposable
{
    internal TextureReadback(TextureFormat format, TextureSubresource subresource, int width, int height, int depth, nint rowPitch, nint slicePitch, OwnedBuffer<byte> storage);
    public TextureFormat Format { get; }
    public TextureSubresource Subresource { get; }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public nint RowPitch { get; }
    public nint SlicePitch { get; }
    public nint ByteLength { get; }
    public BufferReadLease<byte> AcquireReadLease();
    public void Dispose();
}

public sealed class SamplerDescription
{
    public SamplerDescription(SamplerFilter minification, SamplerFilter magnification, SamplerFilter mip, SamplerAddressMode addressU, SamplerAddressMode addressV, SamplerAddressMode addressW, float maximumAnisotropy);
    public SamplerFilter Minification { get; }
    public SamplerFilter Magnification { get; }
    public SamplerFilter Mip { get; }
    public SamplerAddressMode AddressU { get; }
    public SamplerAddressMode AddressV { get; }
    public SamplerAddressMode AddressW { get; }
    public float MaximumAnisotropy { get; }
}

public enum ShaderStage
{
    Vertex,
    Pixel,
    Compute
}

public enum ShaderBinaryFormat
{
    Dxil,
    SpirV
}

public enum ShaderValueType
{
    Float,
    Float2,
    Float3,
    Float4,
    Float4x4,
    Int,
    Int2,
    Int3,
    Int4,
    UInt,
    UInt2,
    UInt3,
    UInt4
}

public enum ShaderInterpolation
{
    Smooth,
    NoPerspective,
    Flat
}

public enum ShaderBindingGroup
{
    Frame,
    Material,
    Object,
    Pass
}

public enum ShaderResourceKind
{
    UniformBuffer,
    SampledImage,
    StorageImage,
    StorageBuffer,
    Sampler
}

public enum ShaderCapability
{
    Float16,
    Float64,
    WaveOperations,
    DescriptorIndexing,
    ScalarBlockLayout,
    GeometryShader,
    Tessellation
}

public readonly struct ShaderIoSignature
{
    public ShaderIoSignature(string semanticName, int semanticIndex, int? location, ShaderValueType valueType, ShaderInterpolation interpolation);
    public string SemanticName { get; }
    public int SemanticIndex { get; }
    public int? Location { get; }
    public ShaderValueType ValueType { get; }
    public ShaderInterpolation Interpolation { get; }
}

public readonly struct ShaderResourceSignature
{
    public ShaderResourceSignature(ShaderBindingGroup group, int binding, ShaderResourceKind kind, int arrayCount);
    public ShaderBindingGroup Group { get; }
    public int Binding { get; }
    public ShaderResourceKind Kind { get; }
    public int ArrayCount { get; }
}

public readonly struct ShaderFieldLayout
{
    public ShaderFieldLayout(string name, ShaderValueType valueType, int byteOffset, int byteLength, int arrayCount, int arrayStride);
    public string Name { get; }
    public ShaderValueType ValueType { get; }
    public int ByteOffset { get; }
    public int ByteLength { get; }
    public int ArrayCount { get; }
    public int ArrayStride { get; }
}

public sealed class ShaderRecordLayout
{
    public ShaderRecordLayout(string name, int byteLength, ImmutableArray<ShaderFieldLayout> fields);
    public string Name { get; }
    public int ByteLength { get; }
    public ImmutableArray<ShaderFieldLayout> Fields { get; }
}

public sealed class ShaderReflection
{
    public ShaderReflection(ShaderStage stage, ImmutableArray<ShaderIoSignature> inputs, ImmutableArray<ShaderIoSignature> outputs, ImmutableArray<ShaderResourceSignature> resources, ImmutableArray<ShaderRecordLayout> recordLayouts, ImmutableArray<ShaderCapability> requiredCapabilities, int bindingLayoutVersion, int groupSizeX, int groupSizeY, int groupSizeZ);
    public ShaderStage Stage { get; }
    public ImmutableArray<ShaderIoSignature> Inputs { get; }
    public ImmutableArray<ShaderIoSignature> Outputs { get; }
    public ImmutableArray<ShaderResourceSignature> Resources { get; }
    public ImmutableArray<ShaderRecordLayout> RecordLayouts { get; }
    public ImmutableArray<ShaderCapability> RequiredCapabilities { get; }
    public int BindingLayoutVersion { get; }
    public int GroupSizeX { get; }
    public int GroupSizeY { get; }
    public int GroupSizeZ { get; }
}

public sealed class ShaderBytecodeInput
{
    public ShaderBytecodeInput(ShaderStage stage, ShaderBinaryFormat format, BufferReadLease<byte> bytecode, ShaderReflection reflection);
    public ShaderStage Stage { get; }
    public ShaderBinaryFormat Format { get; }
    public BufferReadLease<byte> Bytecode { get; }
    public ShaderReflection Reflection { get; }
}

public enum PrimitiveTopology
{
    TriangleList
}

public enum VertexElementFormat
{
    Float2,
    Float3,
    Float4,
    Half2,
    Half4,
    UNorm8x4
}

public readonly struct VertexElementDescription
{
    public VertexElementDescription(int location, VertexElementFormat format, int byteOffset);
    public int Location { get; }
    public VertexElementFormat Format { get; }
    public int ByteOffset { get; }
}

public enum PipelineBlendMode
{
    Opaque,
    PremultipliedAlpha
}

public enum PipelineDepthMode
{
    Disabled,
    ReversedGreaterEqualReadOnly,
    ReversedGreaterEqualWrite
}

public enum PipelineCullMode
{
    None,
    Front,
    Back
}

public abstract class PipelineDescription
{
    internal PipelineDescription(int bindingLayoutVersion);
    public int BindingLayoutVersion { get; }
}

public sealed class GraphicsPipelineDescription : PipelineDescription
{
    public GraphicsPipelineDescription(ImmutableArray<ShaderBytecodeInput> shaders, ImmutableArray<VertexElementDescription> vertexElements, int vertexStride, ImmutableArray<TextureFormat> colorTargetFormats, TextureFormat? depthTargetFormat, PrimitiveTopology topology, PipelineBlendMode blendMode, PipelineDepthMode depthMode, PipelineCullMode cullMode, bool frontFaceClockwise, int bindingLayoutVersion);
    public ImmutableArray<ShaderBytecodeInput> Shaders { get; }
    public ImmutableArray<VertexElementDescription> VertexElements { get; }
    public int VertexStride { get; }
    public ImmutableArray<TextureFormat> ColorTargetFormats { get; }
    public TextureFormat? DepthTargetFormat { get; }
    public PrimitiveTopology Topology { get; }
    public PipelineBlendMode BlendMode { get; }
    public PipelineDepthMode DepthMode { get; }
    public PipelineCullMode CullMode { get; }
    public bool FrontFaceClockwise { get; }
}

public sealed class ComputePipelineDescription : PipelineDescription
{
    public ComputePipelineDescription(ShaderBytecodeInput shader, int bindingLayoutVersion);
    public ShaderBytecodeInput Shader { get; }
}

public sealed class GraphicsDevice
{
    internal GraphicsDevice();
    public GraphicsCapabilities Capabilities { get; }
    public uint DeviceEpoch { get; }
    public GraphicsDeviceStatus Status { get; }
    public Task<GpuBuffer> CreateBufferAsync(BufferDescription description, CancellationToken cancellationToken = default);
    public Task<GpuTexture> CreateTextureAsync(TextureDescription description, CancellationToken cancellationToken = default);
    public Task<GpuSampler> CreateSamplerAsync(SamplerDescription description, CancellationToken cancellationToken = default);
    public Task<GpuPipeline> CreatePipelineAsync(PipelineDescription description, CancellationToken cancellationToken = default);
    public Task<UploadReceipt> UploadBufferAsync(GpuBuffer destination, long destinationOffset, ReadOnlySpan<byte> source, CancellationToken cancellationToken = default);
    public Task<UploadReceipt> UploadBufferAsync(GpuBuffer destination, long destinationOffset, BufferReadLease<byte> source, nint sourceOffset, nint byteLength, CancellationToken cancellationToken = default);
    public Task<UploadReceipt> UploadTextureAsync(GpuTexture destination, TextureSubresource subresource, BufferReadLease<byte> source, nint sourceOffset, TextureDataLayout sourceLayout, CancellationToken cancellationToken = default);
    public Task<OwnedBuffer<byte>> ReadbackBufferAsync(GpuBuffer source, long sourceOffset, nint byteLength, CancellationToken cancellationToken = default);
    public Task<TextureReadback> ReadbackTextureAsync(GpuTexture source, TextureSubresource subresource, CancellationToken cancellationToken = default);
}

public enum UploadStatus
{
    Ready,
    Rejected
}

public sealed class UploadReceipt
{
    internal UploadReceipt(UploadStatus status, uint deviceEpoch, long? completedFence, Diagnostic? rejection);
    public UploadStatus Status { get; }
    public uint DeviceEpoch { get; }
    public long? CompletedFence { get; }
    public Diagnostic? Rejection { get; }
}

public sealed class GpuBuffer : IDisposable
{
    internal GpuBuffer(GpuBufferHandle handle, long byteLength, uint deviceEpoch);
    public GpuBufferHandle Handle { get; }
    public long ByteLength { get; }
    public uint DeviceEpoch { get; }
    public void Dispose();
}

public sealed class GpuTexture : IDisposable
{
    internal GpuTexture(GpuTextureHandle handle, TextureDescription description, uint deviceEpoch);
    public GpuTextureHandle Handle { get; }
    public TextureDescription Description { get; }
    public uint DeviceEpoch { get; }
    public void Dispose();
}

public sealed class GpuSampler : IDisposable
{
    internal GpuSampler(GpuSamplerHandle handle, uint deviceEpoch);
    public GpuSamplerHandle Handle { get; }
    public uint DeviceEpoch { get; }
    public void Dispose();
}

public sealed class GpuPipeline : IDisposable
{
    internal GpuPipeline(GpuPipelineHandle handle, uint deviceEpoch);
    public GpuPipelineHandle Handle { get; }
    public uint DeviceEpoch { get; }
    public void Dispose();
}

public delegate void RenderPassCallback(scoped CommandRecorder recorder);

public enum GraphResourceState
{
    Undefined,
    CopySource,
    CopyDestination,
    VertexRead,
    IndexRead,
    UniformRead,
    ShaderSampledRead,
    StorageRead,
    StorageWrite,
    ColorAttachment,
    DepthRead,
    DepthWrite,
    Present
}

public enum GraphImportCompletionPolicy
{
    WaitForLastUse,
    RequireIdle
}

public sealed class GraphImportOptions
{
    public GraphImportOptions(GraphResourceState initialState, bool contentsValid, GraphImportCompletionPolicy completionPolicy);
    public GraphResourceState InitialState { get; }
    public bool ContentsValid { get; }
    public GraphImportCompletionPolicy CompletionPolicy { get; }
}

public enum GraphBufferUsage
{
    VertexRead,
    IndexRead,
    UniformRead,
    StorageRead,
    StorageWrite,
    CopySource,
    CopyDestination
}

public enum GraphTextureUsage
{
    SampledRead,
    StorageRead,
    StorageWrite,
    CopySource,
    CopyDestination
}

public enum TextureAspect
{
    Color,
    Depth
}

public readonly struct GraphBufferRange
{
    public GraphBufferRange(long byteOffset, long byteLength);
    public long ByteOffset { get; }
    public long ByteLength { get; }
}

public readonly struct GraphTextureRange
{
    public GraphTextureRange(int firstMip, int mipCount, int firstLayer, int layerCount, TextureAspect aspect);
    public int FirstMip { get; }
    public int MipCount { get; }
    public int FirstLayer { get; }
    public int LayerCount { get; }
    public TextureAspect Aspect { get; }
}

public enum AttachmentLoadAction
{
    Clear,
    Load,
    DontCare
}

public enum AttachmentStoreAction
{
    Store,
    Discard
}

public readonly struct RenderPassId : IEquatable<RenderPassId>
{
    internal RenderPassId(uint graphOwnerId, int value);
    public bool IsValid { get; }
    public int Value { get; }
    public bool Equals(RenderPassId other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(RenderPassId left, RenderPassId right);
    public static bool operator !=(RenderPassId left, RenderPassId right);
}

public sealed class GraphBufferVersion
{
    internal GraphBufferVersion(uint deviceEpoch, ulong resourceId, int version, BufferDescription description, GraphBufferRange range);
    public uint DeviceEpoch { get; }
    public int Version { get; }
    public BufferDescription Description { get; }
    public GraphBufferRange Range { get; }
}

public sealed class GraphTextureVersion
{
    internal GraphTextureVersion(uint deviceEpoch, ulong resourceId, int version, TextureDescription description, GraphTextureRange range);
    public uint DeviceEpoch { get; }
    public int Version { get; }
    public TextureDescription Description { get; }
    public GraphTextureRange Range { get; }
}

public sealed class RenderPassDescription
{
    public RenderPassDescription(string name, int order, ImmutableArray<RenderPassId> predecessors, bool hasExternalSideEffects);
    public string Name { get; }
    public int Order { get; }
    public ImmutableArray<RenderPassId> Predecessors { get; }
    public bool HasExternalSideEffects { get; }
}

public enum RenderFrameDimension
{
    TwoDimensional,
    ThreeDimensional
}

public sealed class RenderGraphOptions
{
    public RenderGraphOptions(Guid snapshotId, Guid worldGuid, long tickIndex, RenderFrameDimension frameDimension, SpatialProfileId profileId, long frameRevision, ImmutableArray<Guid> cameraIds, PixelSize targetExtent, Surface surface);
    public RenderGraphOptions(Guid snapshotId, Guid worldGuid, long tickIndex, RenderFrameDimension frameDimension, SpatialProfileId profileId, long frameRevision, ImmutableArray<Guid> cameraIds, PixelSize targetExtent, GpuTexture offscreenColorTarget);
    public Guid SnapshotId { get; }
    public Guid WorldGuid { get; }
    public long TickIndex { get; }
    public RenderFrameDimension FrameDimension { get; }
    public SpatialProfileId ProfileId { get; }
    public long FrameRevision { get; }
    public ImmutableArray<Guid> CameraIds { get; }
    public PixelSize TargetExtent { get; }
    public Surface? Surface { get; }
    public GpuTexture? OffscreenColorTarget { get; }
}

public sealed class RenderGraph
{
    public RenderGraph(RenderGraphOptions options);
    public GraphBufferVersion ImportBuffer(GpuBuffer buffer, GraphBufferRange range, GraphImportOptions options);
    public GraphTextureVersion ImportTexture(GpuTexture texture, GraphTextureRange range, GraphImportOptions options);
    public GraphBufferVersion CreateTransientBuffer(BufferDescription description);
    public GraphTextureVersion CreateTransientTexture(TextureDescription description);
    public RenderPassId AddRasterPass(RenderPassDescription description, RenderPassCallback callback);
    public RenderPassId AddComputePass(RenderPassDescription description, RenderPassCallback callback);
    public RenderPassId AddCopyPass(RenderPassDescription description, RenderPassCallback callback);
    public void ReadBuffer(RenderPassId pass, GraphBufferVersion version, GraphBufferUsage usage, GraphBufferRange range);
    public GraphBufferVersion WriteBuffer(RenderPassId pass, GraphBufferVersion previousVersion, GraphBufferUsage usage, GraphBufferRange range);
    public void ReadTexture(RenderPassId pass, GraphTextureVersion version, GraphTextureUsage usage, GraphTextureRange range);
    public GraphTextureVersion WriteTexture(RenderPassId pass, GraphTextureVersion previousVersion, GraphTextureUsage usage, GraphTextureRange range);
    public GraphTextureVersion WriteColorAttachment(RenderPassId pass, GraphTextureVersion previousVersion, GraphTextureRange range, AttachmentLoadAction loadAction, AttachmentStoreAction storeAction, TerraFX.Numerics.Vector4 clearColor);
    public GraphTextureVersion WriteDepthAttachment(RenderPassId pass, GraphTextureVersion previousVersion, GraphTextureRange range, AttachmentLoadAction loadAction, AttachmentStoreAction storeAction, float clearDepth);
    public GraphTextureVersion? PresentationSource { get; }
    public void SetPresentationSource(GraphTextureVersion version);
    public RenderGraphCompileResponse Compile();
}

public enum RenderGraphCompileStatus
{
    Compiled,
    Rejected
}

public sealed class RenderGraphCompileResponse
{
    internal RenderGraphCompileResponse(RenderGraphCompileStatus status, RenderGraphPlan? plan, ImmutableArray<Diagnostic> diagnostics);
    public RenderGraphCompileStatus Status { get; }
    public RenderGraphPlan? Plan { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public sealed class RenderGraphPlan
{
    internal RenderGraphPlan(Guid snapshotId, Guid worldGuid, long tickIndex, RenderFrameDimension frameDimension, SpatialProfileId profileId, long frameRevision, ImmutableArray<Guid> cameraIds, PixelSize targetExtent, uint deviceEpoch, Surface? surface, GpuTexture? offscreenColorTarget, uint surfaceEpoch, uint swapchainEpoch);
    public Guid SnapshotId { get; }
    public Guid WorldGuid { get; }
    public long TickIndex { get; }
    public RenderFrameDimension FrameDimension { get; }
    public SpatialProfileId ProfileId { get; }
    public long FrameRevision { get; }
    public ImmutableArray<Guid> CameraIds { get; }
    public PixelSize TargetExtent { get; }
    public uint DeviceEpoch { get; }
    public uint SurfaceEpoch { get; }
    public uint SwapchainEpoch { get; }
}

public sealed class BufferBinding
{
    public BufferBinding(ShaderBindingGroup group, int binding, GraphBufferVersion resource, GraphBufferRange range);
    public ShaderBindingGroup Group { get; }
    public int Binding { get; }
    public GraphBufferVersion Resource { get; }
    public GraphBufferRange Range { get; }
}

public sealed class TextureBinding
{
    public TextureBinding(ShaderBindingGroup group, int binding, GraphTextureVersion resource, GraphTextureRange range);
    public ShaderBindingGroup Group { get; }
    public int Binding { get; }
    public GraphTextureVersion Resource { get; }
    public GraphTextureRange Range { get; }
}

public sealed class SamplerBinding
{
    public SamplerBinding(ShaderBindingGroup group, int binding, GpuSampler sampler);
    public ShaderBindingGroup Group { get; }
    public int Binding { get; }
    public GpuSampler Sampler { get; }
}

public sealed class BindingSet
{
    public BindingSet(ImmutableArray<BufferBinding> buffers, ImmutableArray<TextureBinding> textures, ImmutableArray<SamplerBinding> samplers);
    public ImmutableArray<BufferBinding> Buffers { get; }
    public ImmutableArray<TextureBinding> Textures { get; }
    public ImmutableArray<SamplerBinding> Samplers { get; }
}

public readonly struct VertexBufferBinding
{
    public VertexBufferBinding(int slot, GraphBufferVersion buffer, GraphBufferRange range, int stride);
    public int Slot { get; }
    public GraphBufferVersion Buffer { get; }
    public GraphBufferRange Range { get; }
    public int Stride { get; }
}

public enum IndexFormat
{
    UInt16,
    UInt32
}

public readonly struct IndexBufferBinding
{
    public IndexBufferBinding(GraphBufferVersion buffer, GraphBufferRange range, IndexFormat format);
    public GraphBufferVersion Buffer { get; }
    public GraphBufferRange Range { get; }
    public IndexFormat Format { get; }
}

public readonly struct DrawArguments
{
    public DrawArguments(int vertexCount, int instanceCount, int firstVertex, int firstInstance);
    public int VertexCount { get; }
    public int InstanceCount { get; }
    public int FirstVertex { get; }
    public int FirstInstance { get; }
}

public readonly struct DrawIndexedArguments
{
    public DrawIndexedArguments(int indexCount, int instanceCount, int firstIndex, int baseVertex, int firstInstance);
    public int IndexCount { get; }
    public int InstanceCount { get; }
    public int FirstIndex { get; }
    public int BaseVertex { get; }
    public int FirstInstance { get; }
}

public readonly struct DispatchSize
{
    public DispatchSize(int groupCountX, int groupCountY, int groupCountZ);
    public int GroupCountX { get; }
    public int GroupCountY { get; }
    public int GroupCountZ { get; }
}

public ref struct CommandRecorder
{
    internal CommandRecorder(uint deviceEpoch, RenderPassId passId);
    public void SetPipeline(GpuPipelineHandle pipeline);
    public void SetBindings(BindingSet bindings);
    public void SetVertexBuffer(VertexBufferBinding binding);
    public void SetIndexBuffer(IndexBufferBinding binding);
    public void Draw(DrawArguments arguments);
    public void DrawIndexed(DrawIndexedArguments arguments);
    public void Dispatch(DispatchSize size);
    public void CopyBuffer(GraphBufferVersion source, long sourceOffset, GraphBufferVersion destination, long destinationOffset, long byteLength);
}

public sealed class Surface
{
    internal Surface();
    public uint SurfaceEpoch { get; }
    public uint SwapchainEpoch { get; }
    public PixelSize ClientPixelSize { get; }
    public TextureFormat ColorFormat { get; }
    public Task<SurfaceResizeReceipt> ResizeAsync(PixelSize clientPixelSize, CancellationToken cancellationToken = default);
    public Task<PresentationReceipt> WaitForPresentationAsync(long presentationSequence, CancellationToken cancellationToken = default);
}

public enum SurfaceResizeStatus
{
    Resized,
    Suspended,
    Rejected
}

public sealed class SurfaceResizeReceipt
{
    internal SurfaceResizeReceipt(SurfaceResizeStatus status, PixelSize clientPixelSize, uint surfaceEpoch, uint swapchainEpoch, Diagnostic? rejection);
    public SurfaceResizeStatus Status { get; }
    public PixelSize ClientPixelSize { get; }
    public uint SurfaceEpoch { get; }
    public uint SwapchainEpoch { get; }
    public Diagnostic? Rejection { get; }
}

public enum PresentationStatus
{
    Presented,
    Occluded,
    Suspended,
    DeviceLost,
    SurfaceLost,
    SwapchainLost
}

public sealed class PresentationReceipt
{
    internal PresentationReceipt(PresentationStatus status, long presentationSequence, uint deviceEpoch, uint surfaceEpoch, uint swapchainEpoch, Diagnostic? diagnostic);
    public PresentationStatus Status { get; }
    public long PresentationSequence { get; }
    public uint DeviceEpoch { get; }
    public uint SurfaceEpoch { get; }
    public uint SwapchainEpoch { get; }
    public Diagnostic? Diagnostic { get; }
}
}

namespace TerraFX.Rendering
{
using TerraFX.Assets;
using TerraFX.Animation;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Scenes;

public sealed class GpuResidencyLease : IDisposable
{
    internal GpuResidencyLease(AssetId assetId, uint deviceEpoch, IDisposable packageLease);
    public AssetId AssetId { get; }
    public uint DeviceEpoch { get; }
    public void Dispose();
}

public sealed class GpuResidencyService
{
    internal GpuResidencyService();
    public Task<GpuResidencyLease> EnsureGpuResidencyAsync<T>(AssetLease<T> asset, ResidencyOptions options, CancellationToken cancellationToken = default);
}

public sealed class RenderExtractionOptions2D
{
    public RenderExtractionOptions2D(SpatialFrame2 frame, PixelSize targetExtent, ImmutableArray<Guid> cameraIds);
    public SpatialFrame2 Frame { get; }
    public PixelSize TargetExtent { get; }
    public ImmutableArray<Guid> CameraIds { get; }
}

public sealed class RenderExtractionOptions3D
{
    public RenderExtractionOptions3D(SpatialFrame3 frame, PixelSize targetExtent, ImmutableArray<Guid> cameraIds);
    public SpatialFrame3 Frame { get; }
    public PixelSize TargetExtent { get; }
    public ImmutableArray<Guid> CameraIds { get; }
}

public enum RenderExtractionStatus
{
    Extracted,
    CapacityExceeded
}

public enum RenderExtractionLimitKind
{
    DrawInstances,
    Lights,
    BonePaletteEntries
}

public readonly struct RenderExtractionLimitDiagnostic
{
    public RenderExtractionLimitDiagnostic(RenderExtractionLimitKind limitKind, int requested, int limit);
    public RenderExtractionLimitKind LimitKind { get; }
    public int Requested { get; }
    public int Limit { get; }
}

public sealed class RenderExtractionResponse
{
    internal RenderExtractionResponse(RenderExtractionStatus status, RenderSnapshot? snapshot, RenderExtractionLimitDiagnostic? limitDiagnostic, ImmutableArray<Diagnostic> diagnostics);
    public RenderExtractionStatus Status { get; }
    public RenderSnapshot? Snapshot { get; }
    public RenderExtractionLimitDiagnostic? LimitDiagnostic { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public abstract class RenderSnapshot : IDisposable
{
    internal RenderSnapshot(Guid snapshotId, Guid worldGuid, long tickIndex, ImmutableArray<Guid> cameraIds, PixelSize targetExtent);
    public Guid SnapshotId { get; }
    public Guid WorldGuid { get; }
    public long TickIndex { get; }
    public ImmutableArray<Guid> CameraIds { get; }
    public PixelSize TargetExtent { get; }
    public abstract RenderFrameDimension FrameDimension { get; }
    public abstract SpatialProfileId ProfileId { get; }
    public abstract long FrameRevision { get; }
    public void Dispose();
}

public sealed class RenderSnapshot2D : RenderSnapshot
{
    internal RenderSnapshot2D(Guid snapshotId, Guid worldGuid, long tickIndex, ImmutableArray<Guid> cameraIds, PixelSize targetExtent, SpatialFrame2 frame);
    public SpatialFrame2 Frame { get; }
    public override RenderFrameDimension FrameDimension { get; }
    public override SpatialProfileId ProfileId { get; }
    public override long FrameRevision { get; }
}

public sealed class RenderSnapshot3D : RenderSnapshot
{
    internal RenderSnapshot3D(Guid snapshotId, Guid worldGuid, long tickIndex, ImmutableArray<Guid> cameraIds, PixelSize targetExtent, SpatialFrame3 frame);
    public SpatialFrame3 Frame { get; }
    public override RenderFrameDimension FrameDimension { get; }
    public override SpatialProfileId ProfileId { get; }
    public override long FrameRevision { get; }
}

public sealed class Renderer
{
    public static Renderer CreateHeadless();
    internal Renderer(GraphicsDevice? graphicsDevice);
    public RenderExtractionResponse Extract(World world, RenderExtractionOptions2D options);
    public RenderExtractionResponse Extract(World world, RenderExtractionOptions3D options);
    public RenderGraph BuildGraph(RenderSnapshot snapshot, Surface surface);
    public RenderGraph BuildGraph(RenderSnapshot snapshot, GpuTexture offscreenColorTarget);
    public Task<RenderSubmissionReceipt> SubmitAsync(RenderSnapshot snapshot, RenderGraphPlan graph, CancellationToken cancellationToken = default);
}

public sealed class RenderSubmissionReceipt
{
    internal RenderSubmissionReceipt(long submissionSequence, long completedFence, long? presentationSequence, uint deviceEpoch, uint surfaceEpoch, uint swapchainEpoch);
    public long SubmissionSequence { get; }
    public long CompletedFence { get; }
    public long? PresentationSequence { get; }
    public uint DeviceEpoch { get; }
    public uint SurfaceEpoch { get; }
    public uint SwapchainEpoch { get; }
}
}
``` 

`GraphicsDevice` is host-created and has no public native/backend constructor. The public resource/pipeline methods enqueue bounded render-owner requests; native creation, command recording, submit, and presentation stay on that owner thread. `Renderer.BuildGraph`, graph imports/resource/pass declarations, and `RenderGraph.Compile` are synchronous and confined to the graph-creator thread; they inspect only the immutable snapshot and copied resource descriptions/state. `Renderer.SubmitAsync` transfers the immutable plan to the graphics owner, where each `RenderPassCallback` runs synchronously through its scoped `CommandRecorder`; callbacks cannot escape, await, perform file I/O/ECS access, or retain mutable world/snapshot state. Descriptor constructors have no optional values. Texture sample count is fixed at one in this profile. Resource-creation tasks fault with typed/native context for unsupported usage, format, capability, or allocation failures; they never return invalid handles. `CreatePipelineAsync` accepts exactly one typed graphics or compute description; shader stage, backend bytecode format, reflected stage/layout, device capabilities, target formats, and binding-layout version must agree before creation. Graphics pipelines require one vertex and one pixel stage; compute pipelines require exactly one compute stage. An empty vertex layout is valid only when reflection uses system-value inputs and no vertex buffer. A graphics layout has one `VertexElementDescription` per reflected non-system input; offsets/stride must fit the vertex record, color formats are nondefault and device-supported, and depth mode/format agree. The fixed first-profile topology is `TriangleList`; blend, reversed-Z depth, cull, and front-face choices are explicit. A compute reflection group size is nonzero and within the device limits; graphics group sizes are zero. Invalid descriptions throw before request admission; unsupported device capabilities fault the task with the missing capability context.

`ShaderBytecodeInput.Bytecode` is a nonempty caller-owned read lease over the complete bytecode blob; its stage must equal `ShaderReflection.Stage`. The immutable reflection constructor rejects default arrays, duplicate stage locations/bindings/field names, invalid field ranges/strides, and group-size values inconsistent with its stage. `ShaderIoSignature.Location` is null only for a reflected system-value semantic. `ShaderReflection` describes stage I/O, resource bindings, record layouts, required capabilities, layout version, and compute group size. Reflection is checked against the native DXIL/SPIR-V bytes and the selected backend; caller metadata never substitutes for native validation. `CreatePipelineAsync` takes an independent internal bytecode sublease for every stage before request acceptance and retains those credits until backend pipeline creation actually terminates. A pre-canceled token returns a canceled task before admission; cancellation after admission cannot release bytes or make the task terminal before native creation/cancellation is complete. Success returns a `GpuPipeline` for the current device epoch, while unsupported/mismatched input faults the task; there is no GPU-ready fallback. Graphics owns these bytecode/reflection records directly through `TerraFX.Buffers` and has no `TerraFX.Assets` dependency.

The `ReadOnlySpan<byte>` buffer upload copies its bytes synchronously before returning the task. Buffer and texture lease uploads take independent range subleases before acceptance; those tasks become terminal only after the copy/transition fence has actually completed and the sublease is released. `UploadTextureAsync` transfers one complete mip/layer/aspect, never a partial region. `TextureSubresource` accepts only `Color` or `Depth`; its mip and array-layer indices are checked against the resource, 3D textures require array layer zero, and 2D/3D mip extents are clamped to at least one texel per axis. `TextureDataLayout` rejects zero pitches in its constructor. `RowPitch` and `SlicePitch` are byte pitches for the caller's rows and depth slices; both must cover the checked texel extent without overlap, and the inferred source byte range from `sourceOffset` must fit the lease. The implementation repacks rows into the backend's native staging footprint and honors native pitch/alignment requirements. Unsupported format/aspect/usage combinations fault with capability context. Upload validates destination `TextureUsage.CopyDestination`, device epoch, residency, budget, checked pitch arithmetic and range before acceptance. `ReadbackBufferAsync` requires a resident same-device source with `CopySource` usage and a positive in-range byte extent, and returns only after its actual copy fence and CPU ownership transfer complete. `ReadbackTextureAsync` validates the same-device source, `CopySource` texture usage, aspect/mip/layer, copy-capable format and required residency/budget; it returns only after the copy fence, mapping, and CPU copy complete. Both readback operations acquire independent source and staging/map credits through actual transfer and CPU-copy completion. Unsupported transfer formats/usages fault; neither readback returns a zero-filled success. Cancellation before admission prevents work; after acceptance it cannot release transfer/source credits or make the task terminal before native work and mapping/copy have actually stopped. `OwnedBuffer<T>` is a sealed disposable CPU owner backed by `UnmanagedBuffer<T>`; its read lease remains valid independently until that lease is disposed. `TextureReadback` owns the full selected subresource's copied bytes, exposes immutable format/extent/subresource and row/slice pitches, and its `ByteLength` is exactly `SlicePitch * Depth`; padding in every exposed row/slice, including trailing padding, is zero. `AcquireReadLease` leases the entire readback byte range. Disposing a GPU owner revokes logical use immediately; physical resource/descriptor/allocation release waits for every last-use submission fence. A `GpuPipeline` follows the same rule: disposal blocks later recording, while already recorded pipeline use retains native state through its actual submission fence. A residency lease pins the version/device-epoch mapping through its consumers and retirement fence. A submission fence is not a presentation fence.

`Surface.ClientPixelSize` and `ColorFormat` are copied observations for the active logical surface; a suspended surface has a zero drawable extent and cannot build or submit a graph. `ResizeAsync` returns `Resized` with the new pixel size and epochs, `Suspended` only for a zero drawable extent without creating a swapchain, or `Rejected` with a diagnostic and unchanged active epochs. Native failures fault the task. Closing a window invalidates its host mapping immediately; methods on a previously returned surface report `SurfaceLost` or fault with `ObjectDisposedException`, while native storage remains retired only after both GPU-use and presentation completion. Presentation receipts retain the requested sequence and all three independent epochs; `Presented` confirms presentation, `Occluded`/`Suspended` report non-presentation without claiming a presented frame, and device/surface/swapchain loss statuses carry their diagnostic. Cancellation stops only the caller's wait after the owner has admitted the resize/presentation observation.

`RenderGraphOptions` binds one snapshot identity, world/tick/ordered camera selection, dimensionality, spatial profile/frame revision, physical target extent, and exactly one output target selected by its typed constructor: either a `Surface` or an offscreen `GpuTexture`. Its nullable `Surface` and `OffscreenColorTarget` properties are mutually exclusive. All snapshot identities and the selected target's device/epoch/extent are validated together. Camera IDs are nonempty and unique, and extraction orders them by committed camera order then GUID. The target extent is positive and equals the active surface extent or the offscreen texture's mip-0 width/height; an offscreen target is a real same-device, current-epoch, single-sample 2D texture with depth one, declared `TextureUsage.ColorAttachment`, and a valid mip-0/layer-0 color aspect. The target must declare `TextureUsage.CopySource` if texture readback is requested; `ReadbackTextureAsync` rechecks that capability before accepting the transfer. Offscreen compilation and execution use no surface, swapchain, or WSI. A compiled plan captures the selected output-target identity; offscreen plans have zero `SurfaceEpoch` and `SwapchainEpoch`. A target disposed or rebuilt after compilation is rejected before submission admission, while an accepted submission owns a pin through the actual GPU fence. A graph starts with no user-created resource versions. `ImportBuffer`/`ImportTexture` name the exact byte/subresource range, validate the declared initial state against the same-device owner's tracked state, and capture its last-use; `GraphImportOptions` states whether contents are valid and whether compilation may wait for prior uses or requires the owner idle. The state/content declaration applies uniformly to that imported range; disjoint imports represent ranges with different state, and overlapping imports of one owner are rejected. Each imported version exposes its exact range; every access, binding, and write must be contained within it, and each successive version preserves that range. Transient version 0 has the full resource range and invalid contents. `Undefined` imports cannot claim valid contents. `ReadBuffer`/`ReadTexture` declare typed usage and exact byte/subresource range; `WriteBuffer`/`WriteTexture` and attachment writes return the next version. Ranges have positive checked extents within the version range; a texture range's aspect must match its format. Every pass ID and resource version is graph-owned and device-epoch tagged; cross-graph, stale, out-of-range, incompatible-state, feedback, uninitialized-read, and invalid-version access rejects compilation before command recording. A write depends on the previous writer and all readers of its prior version; attachment `Load` reads that version, while `Clear`/`DontCare` do not. `Clear` uses the explicit finite color/depth value, and the depth value is in `[0, 1]` under the reversed-Z convention; `Discard` invalidates the stored version. Imported last-use fences are acquired from the owning resource, not supplied as raw native fence pointers; `WaitForLastUse` adds the dependency and `RequireIdle` rejects an in-flight owner. Submission revalidates owner state/epoch after compile, reacquires a live output-target reference before admission, and retains target and resource pins through the actual GPU fence.

`Renderer.BuildGraph` binds immutable snapshot passes to the selected surface/offscreen target. [19 section 1](19-render-graph-synchronization-and-presentation.md#1-graph-records-and-compilation) owns pass/version/dependency rules; [19 section 6](19-render-graph-synchronization-and-presentation.md#6-failure-and-reference-examples) owns final linear SDR, tone mapping and one-time terminal encoding. `SetPresentationSource` requires a current valid single-sample 2D color version, exactly one mip/layer, the selected mip's physical extent equal to `TargetExtent`, and a device-supported color format. Missing/invalid final source rejects compilation. `Compiled` carries a non-null immutable plan and no diagnostics; `Rejected` carries diagnostics and no plan.

`CommandRecorder` is scoped to a synchronous render-thread callback. It validates epoch, declared graph ranges, render-scope legality and [17 section 4](17-graphics-device-and-resources.md#4-descriptors-and-pipeline-binding)'s pipeline/reflection/binding compatibility. It cannot escape, await, perform file I/O/ECS access, retain mutable world/snapshot state, or access undeclared ranges.

`Renderer.Extract` applies [21 section 1](21-2d-and-3d-rendering.md#1-draw-extraction-and-portable-semantics)'s owner-bound committed extraction, validation, snapshot identity and capacity rules. `Extracted` carries a non-null snapshot and no capacity diagnostic; `CapacityExceeded` carries no snapshot and one `RenderExtractionLimitDiagnostic` with exact draw/light/bone `Requested`/`Limit`. Successful extraction may also carry copied warnings, including conservatively drawn unknown bounds. Headless target extent is logical viewport metadata, not fabricated output.

`BuildGraph` rejects disposed/stale snapshots, profile/epoch mismatch, closed/suspended surfaces, invalid offscreen targets or extent mismatch before adding work. It and `SubmitAsync` throw `InvalidOperationException` before admission without a real device. Graph/plan identity must match the exact snapshot; accepted submission retains independent snapshot/resource/pipeline pins through actual GPU completion. [21 section 5](21-2d-and-3d-rendering.md#5-particles-and-extension) owns the CPU particle ceiling, observable `RejectSpawn`, and separation from authoritative physics.

`SubmitAsync` checks cancellation before admission and ignores that token after acceptance; it returns a `RenderSubmissionReceipt` only after actual GPU completion and release of submission pins. An interactive receipt has a non-null `PresentationSequence` and the active device/surface/swapchain epochs; the caller passes that sequence and a `CancellationToken` to `Surface.WaitForPresentationAsync(long presentationSequence, CancellationToken cancellationToken)` for an independent observation. Canceling that wait does not cancel presentation, and the task completes only on actual presentation or an explicit terminal status. `Presented` alone confirms presentation. An offscreen receipt has `PresentationSequence = null`, `SurfaceEpoch = 0`, and `SwapchainEpoch = 0`; it makes no presentation claim and needs no surface wait. A synchronous recording callback failure faults the task with context and never submits a partial graph; uncertain native references follow chapter [19](19-render-graph-synchronization-and-presentation.md)'s retirement/quarantine rule. Neither GPU submission nor presentation observation substitutes for the other. Device, surface, swapchain, profile, and frame identities remain independent.

### M03 renderer and integration bindings

| Owner / public surface | Exact amendment contract |
|---|---|
| Rendering/Integration/Hosting | `WorldRegistrationBuilder.RegisterCpuExtractionSupport(CpuExtractionSupportRegistration)` enlists the closed CPU extraction adapters before World construction. `Renderer.CreateCpuExtractionSupportRegistration()` produces the separate sealed immutable opaque registration and declares its Scenes prerequisites; `Renderer.CreateHeadless` and receipt/frame access above use real CPU extraction. Physics-owned native codec bridges are emitted/registered in Integration (or permitted offline tools), not in Physics with an Assets reference. Hosting coordinates save/asset/source participants; Entities/Scenes/Assets codecs never call Hosting. Stage-9 publication and atomic swaps remain callback/allocation-free after preparation. |

```csharp
namespace TerraFX.Entities;

public sealed class CpuExtractionSupportRegistration
{
    internal CpuExtractionSupportRegistration();
}
```

```csharp
using TerraFX.Entities;

namespace TerraFX.Rendering;

public sealed class Renderer
{
    public static CpuExtractionSupportRegistration CreateCpuExtractionSupportRegistration();
}
```

## 8. Standalone physics

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Physics;

public abstract class ShapeDefinition2D
{
    private protected ShapeDefinition2D();
}

public sealed class CircleShapeDefinition2D : ShapeDefinition2D
{
    public CircleShapeDefinition2D(float radius);
    public float Radius { get; }
}

public sealed class BoxShapeDefinition2D : ShapeDefinition2D
{
    public BoxShapeDefinition2D(Vector2 halfExtents);
    public Vector2 HalfExtents { get; }
}

public abstract class ShapeDefinition3D
{
    private protected ShapeDefinition3D();
}

public sealed class SphereShapeDefinition3D : ShapeDefinition3D
{
    public SphereShapeDefinition3D(float radius);
    public float Radius { get; }
}

public sealed class BoxShapeDefinition3D : ShapeDefinition3D
{
    public BoxShapeDefinition3D(Vector3 halfExtents);
    public Vector3 HalfExtents { get; }
}

public sealed class PrimitiveShapeGeometryContext2D
{
    public PrimitiveShapeGeometryContext2D(SpatialSettings spatialSettings);
    public static PrimitiveShapeGeometryContext2D Default { get; }
    public SpatialSettings SpatialSettings { get; }
    public float MinimumPrimitiveExtent { get; }
    public float MaximumLocalExtent { get; }
}

public sealed class PrimitiveShapeGeometryContext3D
{
    public PrimitiveShapeGeometryContext3D(SpatialSettings spatialSettings);
    public static PrimitiveShapeGeometryContext3D Default { get; }
    public SpatialSettings SpatialSettings { get; }
    public float MinimumPrimitiveExtent { get; }
    public float MaximumLocalExtent { get; }
}

public readonly struct ShapeMeasure : IEquatable<ShapeMeasure>
{
    internal ShapeMeasure(float significand, int binaryExponent);
    public float Significand { get; }
    public int BinaryExponent { get; }
    public static bool operator !=(ShapeMeasure left, ShapeMeasure right);
    public static bool operator ==(ShapeMeasure left, ShapeMeasure right);
    public bool Equals(ShapeMeasure other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public bool TryGetSingle(out float value);
}

public enum ShapeValidationStatus
{
    None = 0,
    Valid = 1,
    Rejected = 2,
}

public enum ShapeValidationFaultCode
{
    None = 0,
    PrimitiveExtentBelowMinimum = 1,
    LocalExtentExceeded = 2,
}

public enum ShapeValidationField
{
    None = 0,
    Radius = 1,
    HalfExtentX = 2,
    HalfExtentY = 3,
    HalfExtentZ = 4,
    EnclosingRadius = 5,
}

public sealed class ShapeValidationFault
{
    internal ShapeValidationFault(ShapeValidationFaultCode code, ShapeValidationField field, float observedValue, float limit);
    public ShapeValidationFaultCode Code { get; }
    public ShapeValidationField Field { get; }
    public float ObservedValue { get; }
    public float Limit { get; }
}

public sealed class ShapeStatistics2D
{
    internal ShapeStatistics2D(SpatialSettings spatialSettings,
        int? vertexCount, int? edgeCount, int? faceCount, ShapeMeasure signedArea,
        Vector2 localMinimum, Vector2 localMaximum, float enclosingRadius, int connectedComponentCount,
        int? weldedVertexCount, int? patchCount, float? maximumSnapDisplacement);
    public SpatialSettings SpatialSettings { get; }
    public int? VertexCount { get; }
    public int? EdgeCount { get; }
    public int? FaceCount { get; }
    public ShapeMeasure SignedArea { get; }
    public Vector2 LocalMinimum { get; }
    public Vector2 LocalMaximum { get; }
    public float EnclosingRadius { get; }
    public int ConnectedComponentCount { get; }
    public int? WeldedVertexCount { get; }
    public int? PatchCount { get; }
    public float? MaximumSnapDisplacement { get; }
}

public sealed class ShapeStatistics3D
{
    internal ShapeStatistics3D(SpatialSettings spatialSettings,
        int? vertexCount, int? edgeCount, int? faceCount, ShapeMeasure signedVolume,
        Vector3 localMinimum, Vector3 localMaximum, float enclosingRadius, int connectedComponentCount,
        int? weldedVertexCount, int? patchCount, float? maximumSnapDisplacement);
    public SpatialSettings SpatialSettings { get; }
    public int? VertexCount { get; }
    public int? EdgeCount { get; }
    public int? FaceCount { get; }
    public ShapeMeasure SignedVolume { get; }
    public Vector3 LocalMinimum { get; }
    public Vector3 LocalMaximum { get; }
    public float EnclosingRadius { get; }
    public int ConnectedComponentCount { get; }
    public int? WeldedVertexCount { get; }
    public int? PatchCount { get; }
    public float? MaximumSnapDisplacement { get; }
}

public sealed class ShapeValidationResponse2D
{
    internal ShapeValidationResponse2D(PrimitiveShapeGeometryContext2D geometryContext, ShapeStatistics2D statistics);
    internal ShapeValidationResponse2D(PrimitiveShapeGeometryContext2D geometryContext, ShapeValidationFault fault);
    public PrimitiveShapeGeometryContext2D GeometryContext { get; }
    public ShapeValidationStatus Status { get; }
    public ShapeStatistics2D? Statistics { get; }
    public ShapeValidationFault? Fault { get; }
}

public sealed class ShapeValidationResponse3D
{
    internal ShapeValidationResponse3D(PrimitiveShapeGeometryContext3D geometryContext, ShapeStatistics3D statistics);
    internal ShapeValidationResponse3D(PrimitiveShapeGeometryContext3D geometryContext, ShapeValidationFault fault);
    public PrimitiveShapeGeometryContext3D GeometryContext { get; }
    public ShapeValidationStatus Status { get; }
    public ShapeStatistics3D? Statistics { get; }
    public ShapeValidationFault? Fault { get; }
}

public static class PrimitiveShapeValidator2D
{
    public static ShapeValidationResponse2D Validate(CircleShapeDefinition2D definition, PrimitiveShapeGeometryContext2D geometryContext);
    public static ShapeValidationResponse2D Validate(BoxShapeDefinition2D definition, PrimitiveShapeGeometryContext2D geometryContext);
}

public static class PrimitiveShapeValidator3D
{
    public static ShapeValidationResponse3D Validate(SphereShapeDefinition3D definition, PrimitiveShapeGeometryContext3D geometryContext);
    public static ShapeValidationResponse3D Validate(BoxShapeDefinition3D definition, PrimitiveShapeGeometryContext3D geometryContext);
}

public readonly struct PhysicsOwnerContext
{
    public PhysicsOwnerContext(int ownerThreadId);
    public int OwnerThreadId { get; }
}

public sealed class PhysicsSimulationException : InvalidOperationException
{
    internal PhysicsSimulationException(PhysicsFault fault, Exception? innerException);
    public PhysicsFault Fault { get; }
}

public sealed class PhysicsCapacityLimits
{
    public PhysicsCapacityLimits();
    public int MaximumBodies { get; init; } = 65_536;
    public int MaximumColliders { get; init; } = 131_072;
    public int MaximumJoints { get; init; } = 65_536;
    public int MaximumCollidersPerBody { get; init; } = 16;
    public int MaximumIncidentJointsPerBody { get; init; } = 128;
    public int MaximumBroadPhasePairsPerMicrostep { get; init; } = 1_048_576;
    public int MaximumContactPairs { get; init; } = 262_144;
    public int MaximumManifoldsPerPair { get; init; } = 32;
    public int MaximumContactPointsPerTick { get; init; } = 1_048_576;
    public int MaximumMeshPrimitiveTestsPerTick { get; init; } = 2_097_152;
    public int MaximumCollisionTestsPerTick { get; init; } = 16_777_216;
    public int MaximumEventsPerTick { get; init; } = 1_048_576;
    public long MaximumCopiedCommandBytesPerTick { get; init; } = 16 * 1024 * 1024;
    public long MaximumOwnedAndStagedBytes { get; init; } = 512 * 1024 * 1024;
}

public readonly struct PhysicsRebaseRequest2D
{
    public PhysicsRebaseRequest2D(SpatialPosition2 newFrameOrigin, long expectedFrameRevision);
    public SpatialPosition2 NewFrameOrigin { get; }
    public long ExpectedFrameRevision { get; }
}

public readonly struct PhysicsRebaseRequest3D
{
    public PhysicsRebaseRequest3D(SpatialPosition3 newFrameOrigin, long expectedFrameRevision);
    public SpatialPosition3 NewFrameOrigin { get; }
    public long ExpectedFrameRevision { get; }
}

public enum PhysicsStructuralPreparationStatus
{
    Prepared,
    Rejected
}

public enum PhysicsRestoreStatus
{
    Restored,
    Rejected
}

public readonly struct MassProperties2D
{
    public MassProperties2D(float mass, Vector2 localCenter, float inertia);
    public float Mass { get; }
    public Vector2 LocalCenter { get; }
    public float Inertia { get; }
}

public readonly struct MassProperties3D
{
    public MassProperties3D(float mass, Vector3 localCenter, Matrix4x4 inertia);
    public float Mass { get; }
    public Vector3 LocalCenter { get; }
    public Matrix4x4 Inertia { get; }
}

public readonly struct ContactFeatureKey
{
    public ContactFeatureKey(ContactFeatureKind featureKindA, int indexA, ContactFeatureKind featureKindB, int indexB, uint clipCode);
    public ContactFeatureKind FeatureKindA { get; }
    public int IndexA { get; }
    public ContactFeatureKind FeatureKindB { get; }
    public int IndexB { get; }
    public uint ClipCode { get; }
}

public sealed class ContactPoint2D
{
    internal ContactPoint2D(Vector2 localAnchorA, Vector2 localAnchorB, SpatialPosition2 worldAnchorA, SpatialPosition2 worldAnchorB, float separation, ContactFeatureKey feature, float normalImpulse, float tangentImpulse, bool isSpeculative);
    public Vector2 LocalAnchorA { get; }
    public Vector2 LocalAnchorB { get; }
    public SpatialPosition2 WorldAnchorA { get; }
    public SpatialPosition2 WorldAnchorB { get; }
    public float Separation { get; }
    public ContactFeatureKey Feature { get; }
    public float NormalImpulse { get; }
    public float TangentImpulse { get; }
    public bool IsSpeculative { get; }
}

public sealed class ContactPoint3D
{
    internal ContactPoint3D(Vector3 localAnchorA, Vector3 localAnchorB, SpatialPosition3 worldAnchorA, SpatialPosition3 worldAnchorB, float separation, ContactFeatureKey feature, float normalImpulse, Vector2 tangentImpulse, bool isSpeculative);
    public Vector3 LocalAnchorA { get; }
    public Vector3 LocalAnchorB { get; }
    public SpatialPosition3 WorldAnchorA { get; }
    public SpatialPosition3 WorldAnchorB { get; }
    public float Separation { get; }
    public ContactFeatureKey Feature { get; }
    public float NormalImpulse { get; }
    public Vector2 TangentImpulse { get; }
    public bool IsSpeculative { get; }
}

public sealed class PhysicsStructuralPreparation2D
{
    internal PhysicsStructuralPreparation2D(PhysicsStructuralPreparationStatus status, PhysicsStructuralPlan2D? plan, PhysicsFault? rejection);
    public PhysicsStructuralPreparationStatus Status { get; }
    public PhysicsStructuralPlan2D? Plan { get; }
    public PhysicsFault? Rejection { get; }
}

public sealed class PhysicsStructuralPreparation3D
{
    internal PhysicsStructuralPreparation3D(PhysicsStructuralPreparationStatus status, PhysicsStructuralPlan3D? plan, PhysicsFault? rejection);
    public PhysicsStructuralPreparationStatus Status { get; }
    public PhysicsStructuralPlan3D? Plan { get; }
    public PhysicsFault? Rejection { get; }
}

public sealed class PhysicsStructuralPlan2D : IDisposable
{
    internal PhysicsStructuralPlan2D(uint ownerId, long admissionTick, long baseFrameRevision, long baseStructuralRevision);
    public uint OwnerId { get; }
    public long AdmissionTick { get; }
    public long BaseFrameRevision { get; }
    public long BaseStructuralRevision { get; }
    public void Dispose();
}

public sealed class PhysicsStructuralPlan3D : IDisposable
{
    internal PhysicsStructuralPlan3D(uint ownerId, long admissionTick, long baseFrameRevision, long baseStructuralRevision);
    public uint OwnerId { get; }
    public long AdmissionTick { get; }
    public long BaseFrameRevision { get; }
    public long BaseStructuralRevision { get; }
    public void Dispose();
}

public sealed class PhysicsRebaseReceipt2D
{
    internal PhysicsRebaseReceipt2D(SpatialFrame2 previousFrame, SpatialFrame2 frame, long admissionTick);
    public SpatialFrame2 PreviousFrame { get; }
    public SpatialFrame2 Frame { get; }
    public long AdmissionTick { get; }
}

public sealed class PhysicsRebaseReceipt3D
{
    internal PhysicsRebaseReceipt3D(SpatialFrame3 previousFrame, SpatialFrame3 frame, long admissionTick);
    public SpatialFrame3 PreviousFrame { get; }
    public SpatialFrame3 Frame { get; }
    public long AdmissionTick { get; }
}

public sealed class PhysicsStructuralReceipt2D
{
    internal PhysicsStructuralReceipt2D(ImmutableArray<PhysicsStructuralResult2D> results, long frameRevision, long structuralRevision, PhysicsRebaseReceipt2D? rebase);
    public ImmutableArray<PhysicsStructuralResult2D> Results { get; }
    public long FrameRevision { get; }
    public long StructuralRevision { get; }
    public PhysicsRebaseReceipt2D? Rebase { get; }
}

public sealed class PhysicsStructuralReceipt3D
{
    internal PhysicsStructuralReceipt3D(ImmutableArray<PhysicsStructuralResult3D> results, long frameRevision, long structuralRevision, PhysicsRebaseReceipt3D? rebase);
    public ImmutableArray<PhysicsStructuralResult3D> Results { get; }
    public long FrameRevision { get; }
    public long StructuralRevision { get; }
    public PhysicsRebaseReceipt3D? Rebase { get; }
}

public sealed class PhysicsRestoreResponse2D
{
    internal PhysicsRestoreResponse2D(PhysicsRestoreStatus status, PhysicsWorld2D? freshWorld, PhysicsFault? rejection, IReadOnlyDictionary<Guid, BodyHandle2D>? guidToHandle, IReadOnlyDictionary<Guid, PhysicsRequestId>? guidToRequest);
    public PhysicsRestoreStatus Status { get; }
    public PhysicsWorld2D? FreshWorld { get; }
    public PhysicsFault? Rejection { get; }
    public IReadOnlyDictionary<Guid, BodyHandle2D>? GuidToHandle { get; }
    public IReadOnlyDictionary<Guid, PhysicsRequestId>? GuidToRequest { get; }
}

public sealed class PhysicsRestoreResponse3D
{
    internal PhysicsRestoreResponse3D(PhysicsRestoreStatus status, PhysicsWorld3D? freshWorld, PhysicsFault? rejection, IReadOnlyDictionary<Guid, BodyHandle3D>? guidToHandle, IReadOnlyDictionary<Guid, PhysicsRequestId>? guidToRequest);
    public PhysicsRestoreStatus Status { get; }
    public PhysicsWorld3D? FreshWorld { get; }
    public PhysicsFault? Rejection { get; }
    public IReadOnlyDictionary<Guid, BodyHandle3D>? GuidToHandle { get; }
    public IReadOnlyDictionary<Guid, PhysicsRequestId>? GuidToRequest { get; }
}

public sealed class PhysicsNumericalProfile2D
{
    internal PhysicsNumericalProfile2D(SpatialSettings spatialSettings);
    public SpatialSettings SpatialSettings { get; }
    public float MaximumOperationalDistance { get; }
    public float MinimumPrimitiveExtent { get; }
    public float MinimumPolygonWidth { get; }
    public float MinimumEdgeLength { get; }
    public float MaximumLocalExtent { get; }
    public float MinimumDynamicMass { get; }
    public float MaximumDynamicMass { get; }
    public float MinimumPrincipalInertia { get; }
    public float MaximumPrincipalInertia { get; }
    public float MinimumDensity { get; }
    public float MaximumDensity { get; }
    public float DefaultDensity { get; }
    public float DefaultFriction { get; }
    public float MaximumFriction { get; }
    public float DefaultRestitution { get; }
    public float MaximumRestitution { get; }
    public float MaximumLinearSpeed { get; }
    public float MaximumAngularSpeed { get; }
    public float MaximumGravityMagnitude { get; }
    public float MaximumDampingPerSecond { get; }
    public float MinimumGravityScale { get; }
    public float MaximumGravityScale { get; }
    public float MaximumMicrostepDisplacement { get; }
    public float MaximumMicrostepAngularIncrement { get; }
    public float GeometryTolerance { get; }
    public ulong AxisToleranceNumerator { get; }
    public ulong AxisToleranceDenominator { get; }
    public float ContactMargin { get; }
    public float TouchTolerance { get; }
    public float EndTolerance { get; }
    public float LinearSlop { get; }
    public float AngularSlop { get; }
    public float PositionFraction { get; }
    public float MaximumCorrection { get; }
    public float MaximumAngularCorrection { get; }
    public float RestitutionThreshold { get; }
    public float SleepLinearSpeed { get; }
    public float SleepAngularSpeed { get; }
    public float SleepMaximumPointSpeed { get; }
    public TimeSpan SleepDuration { get; }
    public int MaximumCcdImpactsPerSubstep { get; }
}

public sealed class PhysicsNumericalProfile3D
{
    internal PhysicsNumericalProfile3D(SpatialSettings spatialSettings);
    public SpatialSettings SpatialSettings { get; }
    public float MaximumOperationalDistance { get; }
    public float MinimumPrimitiveExtent { get; }
    public float MinimumPolygonWidth { get; }
    public float MinimumEdgeLength { get; }
    public float MaximumLocalExtent { get; }
    public float MinimumDynamicMass { get; }
    public float MaximumDynamicMass { get; }
    public float MinimumPrincipalInertia { get; }
    public float MaximumPrincipalInertia { get; }
    public float MinimumDensity { get; }
    public float MaximumDensity { get; }
    public float DefaultDensity { get; }
    public float DefaultFriction { get; }
    public float MaximumFriction { get; }
    public float DefaultRestitution { get; }
    public float MaximumRestitution { get; }
    public float MaximumLinearSpeed { get; }
    public float MaximumAngularSpeed { get; }
    public float MaximumGravityMagnitude { get; }
    public float MaximumDampingPerSecond { get; }
    public float MinimumGravityScale { get; }
    public float MaximumGravityScale { get; }
    public float MaximumMicrostepDisplacement { get; }
    public float MaximumMicrostepAngularIncrement { get; }
    public float GeometryTolerance { get; }
    public ulong AxisToleranceNumerator { get; }
    public ulong AxisToleranceDenominator { get; }
    public float ContactMargin { get; }
    public float TouchTolerance { get; }
    public float EndTolerance { get; }
    public float LinearSlop { get; }
    public float AngularSlop { get; }
    public float PositionFraction { get; }
    public float MaximumCorrection { get; }
    public float MaximumAngularCorrection { get; }
    public float RestitutionThreshold { get; }
    public float SleepLinearSpeed { get; }
    public float SleepAngularSpeed { get; }
    public float SleepMaximumPointSpeed { get; }
    public TimeSpan SleepDuration { get; }
    public int MaximumCcdImpactsPerSubstep { get; }
}

public sealed class PhysicsWorldOptions2D
{
    public PhysicsWorldOptions2D();
    public PhysicsWorldOptions2D(SpatialSettings spatialSettings);
    public int TickRate { get; init; } = 60;
    public long FirstTick { get; init; }
    public SpatialSettings SpatialSettings { get; }
    public SpatialProfileId SpatialProfileId { get; init; } = SpatialProfileId.Default;
    public SpatialPosition2 InitialFrameOrigin { get; init; } = new(new ChunkIndex2(0, 0), Vector2.Zero);
    public long InitialFrameRevision { get; init; } = 1;
    public Vector2 Gravity { get; init; }
    public PhysicsNumericalProfile2D NumericalProfile { get; }
    public int Substeps { get; init; } = 4;
    public int VelocityIterations { get; init; } = 12;
    public int PositionIterations { get; init; } = 4;
    public int CcdMaxImpactsPerSubstep { get; init; } = 8;
    public PhysicsCapacityLimits Capacities { get; init; } = new();
}

public sealed class PhysicsWorldOptions3D
{
    public PhysicsWorldOptions3D();
    public PhysicsWorldOptions3D(SpatialSettings spatialSettings);
    public int TickRate { get; init; } = 60;
    public long FirstTick { get; init; }
    public SpatialSettings SpatialSettings { get; }
    public SpatialProfileId SpatialProfileId { get; init; } = SpatialProfileId.Default;
    public SpatialPosition3 InitialFrameOrigin { get; init; } = new(new ChunkIndex3(0, 0, 0), Vector3.Zero);
    public long InitialFrameRevision { get; init; } = 1;
    public Vector3 Gravity { get; init; }
    public PhysicsNumericalProfile3D NumericalProfile { get; }
    public int Substeps { get; init; } = 4;
    public int VelocityIterations { get; init; } = 12;
    public int PositionIterations { get; init; } = 4;
    public int CcdMaxImpactsPerSubstep { get; init; } = 8;
    public PhysicsCapacityLimits Capacities { get; init; } = new();
}

public sealed class PhysicsWorld2D : IDisposable
{
    public PhysicsWorld2D(PhysicsOwnerContext context, PhysicsWorldOptions2D options);
    public uint OwnerId { get; }
    public SpatialFrame2 Frame { get; }
    public long FrameRevision { get; }
    public PhysicsFault? LastFault { get; }
    public PhysicsWorldStatus Status { get; }
    public long NextTick { get; }
    public bool HasCompletedStep { get; }
    public PhysicsClosure2D? LastClosure { get; }
    public PhysicsCapabilities GetCapabilities();
    public BodyState2D GetBodyState(BodyHandle2D body);
    public PhysicsStepStatistics? GetStatistics();
    public void ReadEvents(PhysicsEventReader2D reader);
    public void ReadStructuralResults(PhysicsStructuralResultReader2D reader);
    public ShapeCookResponse2D CookShape(ShapeDefinition2D definition);
    public void Enqueue(long admissionTick, PhysicsCommandKey key, PhysicsCommand2D command);
    public PhysicsRequestId EnqueueStructural(long admissionTick, PhysicsCommandKey key, PhysicsStructuralCommand2D command);
    public PhysicsStepOutput2D Step(long tick);
    public PhysicsStructuralPreparation2D PrepareStructural(long admissionTick);
    public PhysicsStructuralPreparation2D PrepareStructural(long admissionTick, PhysicsRebaseRequest2D rebase);
    public PhysicsStructuralReceipt2D CommitStructural(PhysicsStructuralPlan2D plan);
    public PhysicsCheckpoint2D CaptureCheckpoint(PhysicsCheckpointMode mode, PhysicsIdentityCatalog2D identities);
    public static PhysicsRestoreResponse2D RestoreCheckpoint(PhysicsOwnerContext context, PhysicsCheckpoint2D checkpoint);
    public QueryResult RayCast(SpatialRay2 ray, QueryFilter2D filter, RayInsidePolicy insidePolicy, Span<PhysicsHit2D> destination);
    public QueryResult RayCast<TVisitor>(SpatialRay2 ray, QueryFilter2D filter, RayInsidePolicy insidePolicy, scoped ref TVisitor visitor)
        where TVisitor : struct, IReadOnlyRefFunc<PhysicsHit2D, bool>, allows ref struct;
    public QueryResult Sweep(ShapeHandle2D shape, BodyPose2D pose, Vector2 translation, QueryFilter2D filter, SweepInsidePolicy insidePolicy, Span<PhysicsHit2D> destination);
    public QueryResult Sweep<TVisitor>(ShapeHandle2D shape, BodyPose2D pose, Vector2 translation, QueryFilter2D filter, SweepInsidePolicy insidePolicy, scoped ref TVisitor visitor)
        where TVisitor : struct, IReadOnlyRefFunc<PhysicsHit2D, bool>, allows ref struct;
    public QueryResult Overlap(ShapeHandle2D shape, BodyPose2D pose, QueryFilter2D filter, Span<OverlapHit2D> destination);
    public QueryResult OverlapPoint(SpatialPosition2 point, QueryFilter2D filter, Span<OverlapHit2D> destination);
    public QueryResult QueryAabb(SpatialBounds2 bounds, QueryFilter2D filter, Span<ColliderHandle2D> destination);
    public ShapeDistanceResult Distance(ShapeHandle2D shapeA, BodyPose2D poseA, ShapeHandle2D shapeB, BodyPose2D poseB, long expectedFrameRevision);
    public PhysicsClosure2D Close();
    public void Dispose();
}

public sealed class PhysicsWorld3D : IDisposable
{
    public PhysicsWorld3D(PhysicsOwnerContext context, PhysicsWorldOptions3D options);
    public uint OwnerId { get; }
    public SpatialFrame3 Frame { get; }
    public long FrameRevision { get; }
    public PhysicsFault? LastFault { get; }
    public PhysicsWorldStatus Status { get; }
    public long NextTick { get; }
    public bool HasCompletedStep { get; }
    public PhysicsClosure3D? LastClosure { get; }
    public PhysicsCapabilities GetCapabilities();
    public BodyState3D GetBodyState(BodyHandle3D body);
    public PhysicsStepStatistics? GetStatistics();
    public void ReadEvents(PhysicsEventReader3D reader);
    public void ReadStructuralResults(PhysicsStructuralResultReader3D reader);
    public ShapeCookResponse3D CookShape(ShapeDefinition3D definition);
    public void Enqueue(long admissionTick, PhysicsCommandKey key, PhysicsCommand3D command);
    public PhysicsRequestId EnqueueStructural(long admissionTick, PhysicsCommandKey key, PhysicsStructuralCommand3D command);
    public PhysicsStepOutput3D Step(long tick);
    public PhysicsStructuralPreparation3D PrepareStructural(long admissionTick);
    public PhysicsStructuralPreparation3D PrepareStructural(long admissionTick, PhysicsRebaseRequest3D rebase);
    public PhysicsStructuralReceipt3D CommitStructural(PhysicsStructuralPlan3D plan);
    public PhysicsCheckpoint3D CaptureCheckpoint(PhysicsCheckpointMode mode, PhysicsIdentityCatalog3D identities);
    public static PhysicsRestoreResponse3D RestoreCheckpoint(PhysicsOwnerContext context, PhysicsCheckpoint3D checkpoint);
    public QueryResult RayCast(SpatialRay3 ray, QueryFilter3D filter, RayInsidePolicy insidePolicy, Span<PhysicsHit3D> destination);
    public QueryResult RayCast<TVisitor>(SpatialRay3 ray, QueryFilter3D filter, RayInsidePolicy insidePolicy, scoped ref TVisitor visitor)
        where TVisitor : struct, IReadOnlyRefFunc<PhysicsHit3D, bool>, allows ref struct;
    public QueryResult Sweep(ShapeHandle3D shape, BodyPose3D pose, Vector3 translation, QueryFilter3D filter, SweepInsidePolicy insidePolicy, Span<PhysicsHit3D> destination);
    public QueryResult Sweep<TVisitor>(ShapeHandle3D shape, BodyPose3D pose, Vector3 translation, QueryFilter3D filter, SweepInsidePolicy insidePolicy, scoped ref TVisitor visitor)
        where TVisitor : struct, IReadOnlyRefFunc<PhysicsHit3D, bool>, allows ref struct;
    public QueryResult Overlap(ShapeHandle3D shape, BodyPose3D pose, QueryFilter3D filter, Span<OverlapHit3D> destination);
    public QueryResult OverlapPoint(SpatialPosition3 point, QueryFilter3D filter, Span<OverlapHit3D> destination);
    public QueryResult QueryAabb(SpatialBounds3 bounds, QueryFilter3D filter, Span<ColliderHandle3D> destination);
    public ShapeDistanceResult Distance(ShapeHandle3D shapeA, BodyPose3D poseA, ShapeHandle3D shapeB, BodyPose3D poseB, long expectedFrameRevision);
    public PhysicsClosure3D Close();
    public void Dispose();
}

```

| Public member contract | 2D API type | 3D API type |
|---|---|---|
| `BodyPose.Position` | `SpatialPosition2` | `SpatialPosition3` |
| Collider/shape-local pose position | `Vector2` | `Vector3` |
| World frame | `SpatialFrame2` | `SpatialFrame3` |
| Frame revision | `long` | `long` |
| Initial frame origin | `SpatialPosition2` | `SpatialPosition3` |
| Rebase request `NewFrameOrigin` | `SpatialPosition2` | `SpatialPosition3` |
| `MassProperties.Inertia` | `float` | `Matrix4x4` |
| Physical scalar members `Mass`, `Distance`, `Fraction`, `Separation`, `Impulse`, `NormalImpulse`, `Residual` | `float` | `float` |

The M03 primitive declaration closure above follows [23 section 1.2](23-custom-physics-shapes-queries-and-ccd.md#12-inert-primitive-validation-and-statistics). Finite-only constructors preserve input bits; null/nonfinite/default-settings misuse throws, while finite invalid geometry returns a named rejection. Contexts are geometry-only; the owning primitive schema version fixes admission and statistics behavior without a serialized or public algorithm identity. `ShapeMeasure` default is canonical zero. Positive measures have normalized 24-bit binary32 significand and exponent `[-512, 512]`; `TryGetSingle` rounds the stored pair nearest-even, succeeds for finite subnormals/zero, and returns false/default positive zero for nonzero underflow/overflow. Unit exponent 12's sphere `(radius 0x28E12FB1, measure 0x3FB67680/-134)` converts to `0x0000B676`, not direct-formula `0x0000B677`. Statistics/faults/responses have no public constructors; a response contains statistics or fault, never both. These types require no PhysicsWorld, process owner or handle.

`PhysicsWorldOptions2D/3D()` delegates to the corresponding `(SpatialSettings.Default)` constructor. Constructors bind get-only `SpatialSettings`/`NumericalProfile`; `SpatialProfileId` defaults to the registered Default. Construction rejects unregistered IDs or settings mismatch before allocation. `PhysicsNumericalProfile2D/3D` retains its complete numerical/material meaning and internal constructor; the primitive contexts do not waive its spacing, density, inertia or other admission. [22 section 1](22-custom-physics-world-and-solver.md#1-scope-coordinates-and-numerical-profile) owns every dimensional physical/numerical default, range, capacity and selected-unit conversion; the declared profile members expose those values with `float`, `TimeSpan` or count types. `Gravity` `init` overrides are already in selected engine units.

Nullable/omitted `ColliderAttachmentDefinition2D/3D.Material` resolves during structural preparation to that world's frozen `DefaultDensity`, `DefaultFriction` and `DefaultRestitution`. Explicit density is already in selected engine-unit density; the prepared graph copies the resolved material and frame changes cannot retune it. `DefaultDensity` follows the owner's physical 1 kg/m^2 or 1 kg/m^3 conversion, not necessarily numeric 1.

`AxisToleranceNumerator/Denominator` preserve the exact dimensionless rational `1/10000000000`. `FirstTick` defaults to 0, `InitialFrameOrigin` to canonical dimensional zero, and `InitialFrameRevision` to 1. The raw origin is interpreted under the explicit settings/profile, not inferred provenance. `PhysicsCapacityLimits` defaults to the owner's ceilings and permits only positive reductions. Validate options before allocation.

`PhysicsOwnerContext` carries thread affinity only. Each physics service allocates its own process-wide `OwnerId`; `Physics` has no `World`, `Entity`, `EngineContext`, or global-origin dependency. Pose positions and scalar fields have the explicit dimensional types in the table above. Body/shape/collider/joint handles, definitions, commands, event/output records, structural statuses and fault records are otherwise the named contracts in [22](22-custom-physics-world-and-solver.md).

`Step` accepts exactly `NextTick`, returns a complete `PhysicsStepOutput2D/3D` on local success, and throws `PhysicsSimulationException` with `PhysicsFault` on unexpected solver failure after staged rollback/fault marking. Structural preparation returns `Prepared` or `Rejected`; only the applicable plan or rejection payload is populated. Commit is owner-thread, single-use, allocation-free, and callback-free.

Each `PhysicsRebaseRequest2D/3D` contains its dimension-matched `NewFrameOrigin` and `ExpectedFrameRevision`. The only public structural boundary is the pair of `PrepareStructural` overloads and `CommitStructural`; a rebase-only boundary is an empty structural batch at an admitted idle tick and has the same preparation/status/plan/receipt semantics. Preparation yields `Prepared` with exactly one plan or `Rejected` with exactly one `PhysicsFault`; it allocates/reserves before live mutation. Plans are owner/revision/tick-bound, single-use, disposable and hold the idle gate until commit or disposal. Commit returns typed structural results and resulting frame/structural revisions, with a dimensional `PhysicsRebaseReceipt2D/3D` only when a frame change was included. Frame changes replace derived caches only; persistent body positions and local poses are not shifted.

The dimensionally typed query methods are members of their matching `PhysicsWorld2D` or `PhysicsWorld3D`. World-space sweep/overlap/distance poses are `BodyPose2D/3D`; overlap points are `SpatialPosition2/3`; AABB bounds are `SpatialBounds2/3`; shape/collider anchor poses remain local `Pose2D/3D`. Distance requires `expectedFrameRevision`, which is compared with the pinned committed frame revision. Their owners, filters/hits/results, visitor overloads, required counts, `Complete`/`BufferTooSmall`/`Indeterminate`/`VisitorStopped` invariants, budgets and sort order are the exact dimensional contracts in [23](23-custom-physics-shapes-queries-and-ccd.md). Queries inspect a pinned local committed revision, never staging. They write no destination and invoke no visitor on `Indeterminate` or `BufferTooSmall`; visitor stop limits delivery only and reports `VisitorStopped` with exact delivered counts. Generic visitors are borrowed by reference through the concrete constrained type; caller state changes survive stop or exception, and scoped hit references cannot escape. Query callbacks are synchronous on the owner thread and cannot step, mutate, structurally commit, or reenter a query. Expected algorithm/budget uncertainty is not `Miss`.

`CaptureCheckpoint` copies an immutable `PhysicsCheckpoint2D/3D` using the caller's frozen dimensional identity catalog; it changes no simulation state and consumes no commands. `RestoreCheckpoint` validates the decoded profile/content and returns `Restored` with a fresh owner/world and immutable GUID-to-handle/request maps, or `Rejected` with a `PhysicsFault` and no world/maps. Unexpected allocation/invariant faults throw after staged cleanup. `PhysicsIdentityCatalog2D/3D`, checkpoint values, and mode fields use chapter [22](22-custom-physics-world-and-solver.md)'s exact fields and validation; restore does not normalize/recompute canonical simulation state.

There is no ECS integration on these classes. `TerraFX.Integration` owns entity/body mapping, enriched historical entity IDs, root-motion authority, tick-stage admission and stage-9 event publication. A local physics success is not a committed world tick.

## 9. Audio and input

```csharp
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX;
using TerraFX.Diagnostics;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Audio
{
public enum AudioOutputMode
{
    NotRequested,
    Native,
    Offline
}

public enum AudioEndpointBackend
{
    Wasapi,
    PipeWire
}

public sealed class AudioEndpointOptions
{
    public AudioEndpointOptions(AudioEndpointBackend backend, string? endpointId);
    public AudioEndpointBackend Backend { get; }
    public string? EndpointId { get; }
}

public sealed class AudioMixerOptions
{
    public AudioMixerOptions(AudioOutputMode outputMode, AudioEndpointOptions? endpoint);
    public AudioOutputMode OutputMode { get; }
    public AudioEndpointOptions? Endpoint { get; }
    public int SampleRate { get; } = 48_000;
    public int OutputChannels { get; } = 2;
    public int MaxNativeBufferFrames { get; } = 8192;
    public int MaxVoices { get; init; } = 256;
    public int MaxStreamingVoices { get; init; } = 32;
    public int MaxBuses { get; init; } = 64;
    public int MaxWorlds { get; init; } = 32;
    public int MaxEmittersPerWorld { get; init; } = 4096;
    public int MaxSnapshotEmitters { get; init; } = 32768;
    public int MaxBlockFrames { get; init; } = 256;
    public int CommandCapacity { get; } = 1024;
    public int StopFadeFrames { get; init; } = 64;
    public int StealFadeFrames { get; init; } = 64;
    public TimeSpan EndpointRecoveryInitialDelay { get; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan EndpointRecoveryMaximumDelay { get; } = TimeSpan.FromSeconds(5);
}

public enum AudioEndpointFailure
{
    PlatformMismatch,
    UnsupportedBackend,
    UnsupportedEndpoint,
    EndpointUnavailable,
    InitializationFailed
}

public sealed class AudioEndpointException : InvalidOperationException
{
    public AudioEndpointException(AudioEndpointFailure failure, string? endpointId, int? nativeResult, string message, Exception? innerException);
    public AudioEndpointFailure Failure { get; }
    public string? EndpointId { get; }
    public int? NativeResult { get; }
}

public enum AudioPlayMode
{
    TwoDimensional,
    ThreeDimensional
}

public readonly struct AudioClipHandle : IEquatable<AudioClipHandle>
{
    internal AudioClipHandle(uint ownerId, uint index, uint generation);
    public uint OwnerId { get; }
    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid { get; }
    public bool Equals(AudioClipHandle other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(AudioClipHandle left, AudioClipHandle right);
    public static bool operator !=(AudioClipHandle left, AudioClipHandle right);
}

public readonly struct AudioBusHandle : IEquatable<AudioBusHandle>
{
    internal AudioBusHandle(uint ownerId, uint index, uint generation);
    public uint OwnerId { get; }
    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid { get; }
    public bool Equals(AudioBusHandle other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(AudioBusHandle left, AudioBusHandle right);
    public static bool operator !=(AudioBusHandle left, AudioBusHandle right);
}

public readonly struct AudioVoiceHandle : IEquatable<AudioVoiceHandle>
{
    internal AudioVoiceHandle(uint ownerId, uint index, uint generation);
    public uint OwnerId { get; }
    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid { get; }
    public bool Equals(AudioVoiceHandle other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(AudioVoiceHandle left, AudioVoiceHandle right);
    public static bool operator !=(AudioVoiceHandle left, AudioVoiceHandle right);
}

public readonly struct AudioWorldHandle : IEquatable<AudioWorldHandle>
{
    internal AudioWorldHandle(uint ownerId, uint index, uint generation);
    public uint OwnerId { get; }
    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid { get; }
    public bool Equals(AudioWorldHandle other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(AudioWorldHandle left, AudioWorldHandle right);
    public static bool operator !=(AudioWorldHandle left, AudioWorldHandle right);
}

public sealed class AudioClipLease : IDisposable
{
    internal AudioClipLease(AudioClipHandle handle, AssetId assetId, IDisposable packageLease);
    public AudioClipHandle Handle { get; }
    public AssetId AssetId { get; }
    public void Dispose();
}

public sealed class AudioAssetStore
{
    internal AudioAssetStore();
    public Task<AudioClipLease> LoadAsync(AssetId assetId, CancellationToken cancellationToken = default);
}

public sealed class AudioPlayRequest
{
    public AudioPlayRequest(AudioClipHandle clip, AudioBusHandle bus, long? scheduledFrame);
    public AudioPlayRequest(AudioClipHandle clip, AudioBusHandle bus, float gain, float pitch, float pan, long? scheduledFrame);
    public AudioPlayRequest(AudioClipHandle clip, AudioBusHandle bus, AudioWorldHandle world, AudioEmitterBinding emitter, long requiredSnapshotSequence, AudioAttenuation attenuation, long? scheduledFrame);
    public AudioPlayRequest(AudioClipHandle clip, AudioBusHandle bus, float gain, float pitch, AudioWorldHandle world, AudioEmitterBinding emitter, long requiredSnapshotSequence, AudioAttenuation attenuation, long? scheduledFrame);
    public AudioClipHandle Clip { get; }
    public AudioPlayMode Mode { get; }
    public AudioBusHandle Bus { get; }
    public float Gain { get; }
    public float Pitch { get; }
    public byte Priority { get; init; } = 128;
    public bool Loop { get; init; }
    public long? ScheduledFrame { get; }
    public float? Pan { get; }
    public AudioWorldHandle? World { get; }
    public AudioEmitterBinding? Emitter { get; }
    public long? RequiredSnapshotSequence { get; }
    public AudioAttenuation? Attenuation { get; }
}

public enum AudioEmitterBindingKind
{
    Tracked,
    FixedPose
}

public abstract class AudioEmitterBinding
{
    private protected AudioEmitterBinding(AudioEmitterBindingKind kind);
    public AudioEmitterBindingKind Kind { get; }
}

public sealed class TrackedEmitterBinding : AudioEmitterBinding
{
    public TrackedEmitterBinding(Guid emitterId);
    public Guid EmitterId { get; }
}

public sealed class FixedPoseEmitterBinding : AudioEmitterBinding
{
    public FixedPoseEmitterBinding(SpatialPosition3 position, Quaternion rotation);
    public SpatialPosition3 Position { get; }
    public Quaternion Rotation { get; }
}

public sealed class AudioWorldOptions
{
    public AudioWorldOptions(SpatialFrame3 initialFrame);
    public SpatialFrame3 InitialFrame { get; }
}

public enum AudioWorldCreationStatus
{
    Created,
    CapacityExceeded
}

public sealed class AudioWorldCreationReceipt
{
    internal AudioWorldCreationReceipt(AudioWorldCreationStatus status, AudioWorldHandle? world);
    public AudioWorldCreationStatus Status { get; }
    public AudioWorldHandle? World { get; }
}

public sealed class AudioWorldDestructionReceipt
{
    internal AudioWorldDestructionReceipt(AudioWorldHandle world, long? finalSnapshotSequence);
    public AudioWorldHandle World { get; }
    public long? FinalSnapshotSequence { get; }
}

public sealed class AudioAttenuation
{
    public AudioAttenuation(float referenceDistance, float maximumDistance, float rolloff);
    public static AudioAttenuation CreateDefault(SpatialSettings settings);
    public float ReferenceDistance { get; }
    public float MaximumDistance { get; }
    public float Rolloff { get; }
}

public readonly struct AudioProcessDurationBucket
{
    public AudioProcessDurationBucket(TimeSpan inclusiveLowerBound, TimeSpan? exclusiveUpperBound, long sampleCount);
    public TimeSpan InclusiveLowerBound { get; }
    public TimeSpan? ExclusiveUpperBound { get; }
    public long SampleCount { get; }
}

public sealed class AudioEmitterSnapshot
{
    public AudioEmitterSnapshot(Guid emitterId, SpatialPosition3 position, Vector3 velocity);
    public Guid EmitterId { get; }
    public SpatialPosition3 Position { get; }
    public Vector3 Velocity { get; }
}

public sealed class AudioWorldSnapshot
{
    public AudioWorldSnapshot(AudioWorldHandle world, long sequence, SpatialFrame3 frame, SpatialPosition3 listenerPosition, Quaternion listenerRotation, ImmutableArray<AudioEmitterSnapshot> emitters);
    public AudioWorldHandle World { get; }
    public long Sequence { get; }
    public SpatialFrame3 Frame { get; }
    public SpatialPosition3 ListenerPosition { get; }
    public Quaternion ListenerRotation { get; }
    public ImmutableArray<AudioEmitterSnapshot> Emitters { get; }
}

public sealed class AudioBusDescription
{
    public AudioBusDescription(Guid busId, Guid? parentBusId, float gain, float lowPassCutoffHz, float maximumGain);
    public Guid BusId { get; }
    public Guid? ParentBusId { get; }
    public float Gain { get; }
    public float LowPassCutoffHz { get; }
    public float MaximumGain { get; }
}

public sealed class AudioBusGraphDescription
{
    public AudioBusGraphDescription(ImmutableArray<AudioBusDescription> buses);
    public ImmutableArray<AudioBusDescription> Buses { get; }
}

public enum AudioCommandKind
{
    Play,
    Stop,
    StopAll,
    SetBusGain,
    SetBusCutoff,
    ReplaceBusGraph
}

public abstract class AudioCommand
{
    private protected AudioCommand(AudioCommandKind kind, long? scheduledFrame);
    public AudioCommandKind Kind { get; }
    public long? ScheduledFrame { get; }
}

public sealed class PlayAudioCommand : AudioCommand
{
    public PlayAudioCommand(AudioPlayRequest request);
    public AudioPlayRequest Request { get; }
}

public sealed class StopVoiceCommand : AudioCommand
{
    public StopVoiceCommand(AudioVoiceHandle voice, int fadeFrames, long? scheduledFrame);
    public AudioVoiceHandle Voice { get; }
    public int FadeFrames { get; }
}

public sealed class StopAllVoicesCommand : AudioCommand
{
    public StopAllVoicesCommand(int fadeFrames, long? scheduledFrame);
    public int FadeFrames { get; }
}

public sealed class SetBusGainCommand : AudioCommand
{
    public SetBusGainCommand(AudioBusHandle bus, float gain, long? scheduledFrame);
    public AudioBusHandle Bus { get; }
    public float Gain { get; }
}

public sealed class SetBusCutoffCommand : AudioCommand
{
    public SetBusCutoffCommand(AudioBusHandle bus, float cutoffHz, long? scheduledFrame);
    public AudioBusHandle Bus { get; }
    public float CutoffHz { get; }
}

public sealed class ReplaceBusGraphCommand : AudioCommand
{
    public ReplaceBusGraphCommand(AudioBusGraphDescription graph, long? scheduledFrame);
    public AudioBusGraphDescription Graph { get; }
}

public enum AudioCommandStatus
{
    Applied,
    Rejected,
    Canceled
}

public enum AudioCommandRejection
{
    InPast,
    OutOfRange,
    MissedDeadline,
    MissingWorld,
    MissingListener,
    MissingEmitter,
    SnapshotSequenceNotAvailable,
    UnsupportedStereo3D,
    StaleHandle,
    NotFound,
    VoiceLimitReached,
    GainPathBudgetExceeded
}

public enum AudioCommandCompletionKind
{
    EmptyClipCompleted
}

public sealed class AudioCommandResult
{
    internal AudioCommandResult(AudioCommandKind kind, AudioCommandStatus status, long acceptanceSequence, long targetFrame, AudioCommandRejection? rejection, AudioVoiceHandle? voice, AudioCommandCompletionKind? completionKind);
    public AudioCommandKind Kind { get; }
    public AudioCommandStatus Status { get; }
    public long AcceptanceSequence { get; }
    public long TargetFrame { get; }
    public AudioCommandRejection? Rejection { get; }
    public AudioVoiceHandle? Voice { get; }
    public AudioCommandCompletionKind? CompletionKind { get; }
}

public enum AudioEventKind
{
    VoiceEnded
}

public enum AudioVoiceEndReason
{
    NaturalEnd,
    Stopped,
    Stolen,
    EmitterRemoved,
    WorldDisposed,
    Disposed,
    StreamFailure
}

public sealed class AudioEvent
{
    internal AudioEvent(AudioEventKind kind, AudioVoiceHandle voice, long frame, AudioVoiceEndReason endReason);
    public AudioEventKind Kind { get; }
    public AudioVoiceHandle Voice { get; }
    public long Frame { get; }
    public AudioVoiceEndReason EndReason { get; }
}

public delegate void AudioEventReader(scoped ReadOnlySpan<AudioEvent> events);

public sealed class AudioDeviceSnapshot
{
    internal AudioDeviceSnapshot(AudioOutputMode outputMode, AudioEndpointBackend? backend, string? requestedEndpointId, string? actualEndpointId, uint deviceEpoch, int? endpointSampleRate, int? endpointChannels, string? endpointFormat, int? outputPeriodFrames, int? outputBufferFrames, double? estimatedQueuedLatencySeconds, bool hardwareLatencyKnown, ImmutableArray<AudioProcessDurationBucket> processDurationHistogram, long clippedSampleCount, long streamUnderrunCount, Diagnostic? lastDiagnostic);
    public AudioOutputMode OutputMode { get; }
    public AudioEndpointBackend? Backend { get; }
    public string? RequestedEndpointId { get; }
    public string? ActualEndpointId { get; }
    public uint DeviceEpoch { get; }
    public int? EndpointSampleRate { get; }
    public int? EndpointChannels { get; }
    public string? EndpointFormat { get; }
    public int? OutputPeriodFrames { get; }
    public int? OutputBufferFrames { get; }
    public double? EstimatedQueuedLatencySeconds { get; }
    public bool HardwareLatencyKnown { get; }
    public ImmutableArray<AudioProcessDurationBucket> ProcessDurationHistogram { get; }
    public long ClippedSampleCount { get; }
    public long StreamUnderrunCount { get; }
    public Diagnostic? LastDiagnostic { get; }
}

public sealed class AudioMixer : IAsyncDisposable
{
    private AudioMixer();
    public static Task<AudioMixer> CreateAsync(AudioMixerOptions options, CancellationToken cancellationToken = default);
    public AudioMixerOptions Options { get; }
    public AudioAssetStore Assets { get; }
    public AudioDeviceSnapshot DeviceSnapshot { get; }
    public long SnapshotReplacementCount { get; }
    public long NextFrameToMix { get; }
    public AudioBusHandle MasterBus { get; }
    public bool TryGetBusHandle(Guid busId, out AudioBusHandle bus);
    public AudioWorldCreationReceipt CreateWorld(AudioWorldOptions options);
    public Task<AudioWorldDestructionReceipt> DestroyWorldAsync(AudioWorldHandle world);
    public Task<AudioCommandResult> PlayAsync(AudioPlayRequest request);
    public Task<AudioCommandResult> StopAsync(AudioVoiceHandle voice, int fadeFrames = 64, long? scheduledFrame = null);
    public Task<AudioCommandResult> StopAllAsync(int fadeFrames = 64, long? scheduledFrame = null);
    public Task<AudioCommandResult> SetBusGainAsync(AudioBusHandle bus, float gain, long? scheduledFrame = null);
    public Task<AudioCommandResult> SetBusCutoffAsync(AudioBusHandle bus, float cutoffHz, long? scheduledFrame = null);
    public Task<AudioCommandResult> ReplaceBusGraphAsync(AudioBusGraphDescription graph, long? scheduledFrame = null);
    public Task<AudioCommandResult> SubmitAsync(AudioCommand command);
    public bool TrySubmit(AudioCommand command, [NotNullWhen(true)] out Task<AudioCommandResult>? completion);
    public void PublishWorldSnapshot(AudioWorldSnapshot snapshot);
    public void ReadEvents(AudioEventReader reader);
    public void RenderFrames(int frameCount, Span<float> interleavedStereo);
    public ValueTask DisposeAsync();
}
}

namespace TerraFX.Input
{
public enum InputMapStatus
{
    Registered,
    Replaced,
    Rejected
}

public sealed class ValidationIssue
{
    internal ValidationIssue(string code, string schemaPath);
    public string Code { get; }
    public string SchemaPath { get; }
}

public sealed class InputMapResponse
{
    internal InputMapResponse(InputMapStatus status, long? publishedRevision, ValidationIssue? rejection);
    public InputMapStatus Status { get; }
    public long? PublishedRevision { get; }
    public ValidationIssue? Rejection { get; }
}

public enum InputRoutingStatus
{
    Applied,
    Rejected
}

public sealed class InputRoutingReceipt
{
    internal InputRoutingReceipt(InputRoutingStatus status, uint recipientId, long? hostSequence, long? assignedTick, ValidationIssue? rejection);
    public InputRoutingStatus Status { get; }
    public uint RecipientId { get; }
    public long? HostSequence { get; }
    public long? AssignedTick { get; }
    public ValidationIssue? Rejection { get; }
}

public enum InputActionKind
{
    Digital,
    Axis1,
    Axis2
}

public enum InputTransitionKind
{
    ValueChanged,
    Pressed,
    Released,
    Canceled
}

public enum InputCancellationReason
{
    InputConsumed,
    ContextMasked,
    Rebound
}

public sealed class InputActionTransition
{
    internal InputActionTransition(Guid mapId, Guid actionId, InputTransitionKind kind, InputCancellationReason? cancellationReason, long hostSequence, long? assignedTick, float axisX, float axisY);
    public Guid MapId { get; }
    public Guid ActionId { get; }
    public InputTransitionKind Kind { get; }
    public InputCancellationReason? CancellationReason { get; }
    public long HostSequence { get; }
    public long? AssignedTick { get; }
    public float AxisX { get; }
    public float AxisY { get; }
}

public sealed class InputActionState
{
    internal InputActionState(Guid mapId, Guid actionId, long mapRevision, InputActionKind kind, bool isDownAtEnd, bool pressedThisTick, bool releasedThisTick, bool valueChangedThisTick, float axisX, float axisY, ImmutableArray<InputActionTransition> transitions);
    public Guid MapId { get; }
    public Guid ActionId { get; }
    public long MapRevision { get; }
    public InputActionKind Kind { get; }
    public bool IsDownAtEnd { get; }
    public bool PressedThisTick { get; }
    public bool ReleasedThisTick { get; }
    public bool ValueChangedThisTick { get; }
    public float AxisX { get; }
    public float AxisY { get; }
    public ImmutableArray<InputActionTransition> Transitions { get; }
}

public sealed class InputActionSnapshot
{
    internal InputActionSnapshot(InputRecipientKind recipientKind, uint recipientId, long? assignedTick, ImmutableArray<InputActionState> actions);
    public InputRecipientKind RecipientKind { get; }
    public uint RecipientId { get; }
    public long? AssignedTick { get; }
    public ImmutableArray<InputActionState> Actions { get; }
}

public enum TextInputPurpose
{
    FreeForm,
    Password,
    Search,
    Email,
    Url,
    Number,
    Telephone
}

[Flags]
public enum TextInputHints
{
    None = 0,
    SpellCheck = 1,
    AutoCorrect = 2,
    Multiline = 4,
    Sensitive = 8
}

public sealed class TextInputSessionOptions
{
    public TextInputSessionOptions(WindowHandle window, TextInputPurpose purpose, long documentRevision, string? surroundingText, int selectionStart, int selectionLength, Vector2 caretPosition, Vector2 caretSize);
    public WindowHandle Window { get; }
    public TextInputPurpose Purpose { get; }
    public long DocumentRevision { get; }
    public string? SurroundingText { get; }
    public int SelectionStart { get; }
    public int SelectionLength { get; }
    public Vector2 CaretPosition { get; }
    public Vector2 CaretSize { get; }
    public TextInputHints Hints { get; init; } = TextInputHints.None;
}

public sealed class TextInputSession : IDisposable
{
    internal TextInputSession();
    public WindowHandle Window { get; }
    public long DocumentRevision { get; }
    public bool IsActive { get; }
    public Diagnostic? LastDiagnostic { get; }
    public void Update(TextInputSessionOptions options);
    public void Dispose();
}

public sealed class InputRouter
{
    internal InputRouter(InputRecipientKind recipientKind, uint recipientId);
    public InputRecipientKind RecipientKind { get; }
    public uint RecipientId { get; }
    public InputMapResponse RegisterMap(InputActionMapV1 map);
    public InputMapResponse ReplaceMap(InputActionMapV1 map, long expectedRevision);
    public InputRoutingReceipt SetActiveContextMask(Guid mapId, long expectedRevision, ulong newMask);
    public InputActionSnapshot GetHostActionSnapshot();
    public InputActionSnapshot GetSimulationActionSnapshot(long assignedTick);
    public TextInputSession BeginTextSession(TextInputSessionOptions options);
}
}
```

`AudioMixer` has no public constructor; `CreateAsync` is the sole creation path and returns only after options validation and endpoint initialization. `AudioOutputMode.Native` requires `AudioEndpointOptions`; `NotRequested` and `Offline` require a null endpoint. `AudioEndpointOptions.EndpointId = null` selects the default endpoint for its selected backend. A platform/backend mismatch, unsupported backend, unavailable endpoint, or native initialization failure faults creation with `AudioEndpointException`; no endpoint or backend fallback is selected. Recovery retries only the selected endpoint, beginning after 250 ms and doubling to a five-second maximum. The frozen internal format is 48-kHz interleaved stereo with a fixed 8192-frame native-buffer ceiling. Defaults are 256 voices, 32 streaming voices, 64 buses, 32 worlds, 4096 emitters per world, 32768 emitters across admitted snapshots, 256 frames per block, 1024 command credits, and 64-frame stop/steal fades. The 1024-credit command capacity, native-buffer ceiling, and internal format are fixed; configurable capacities may be reduced and are validated before allocation. `NotRequested` creates no endpoint and admits no playback/control command; task-returning command methods and `TrySubmit` throw `InvalidOperationException` synchronously rather than returning false or retaining work.

The mixer exposes named `PlayAsync`, `StopAsync`, `StopAllAsync`, `SetBusGainAsync`, `SetBusCutoffAsync`, and `ReplaceBusGraphAsync` operations plus their immutable command/request types; there is no untyped play/write payload. It starts with one `Master` bus at gain 1 and maximum gain 4; `MasterBus` exposes its handle. `TryGetBusHandle` returns false only when a nonempty ID is absent from the currently applied graph, leaving its value output invalid default. A graph replacement invalidates removed/replaced bus generations only when the command applies; resolve new handles after awaiting its receipt. `AudioPlayRequest` has distinct 2D and 3D constructor pairs: the concise forms default gain/pitch to 1 and 2D pan to 0, while the explicit forms accept those values; 2D requires pan, while 3D requires a valid mixer-owned `AudioWorldHandle`, a tracked-emitter or canonical fixed-pose binding, the minimum snapshot sequence, and explicit attenuation. The constructor determines `Mode`; it cannot encode a 2D request with a world or a 3D request with pan. Priority defaults to 128 and looping defaults to false. Gain is finite in `[0,4]`, pitch in `[0.25,4]`, and 2D pan in `[-1,1]`. `AudioAttenuation` values use the world frame's selected distance units; `CreateDefault(settings)` converts the physical defaults `1 m`, `100 m`, and dimensionless rolloff `1` once. The attenuation formula is zero for `d >= maximumDistance`, one for `d <= referenceDistance`, and otherwise `referenceDistance / (referenceDistance + rolloff * (d - referenceDistance))`; require finite `referenceDistance > 0`, `maximumDistance > referenceDistance`, and `rolloff >= 0`.

`CreateWorld` returns `Created` with exactly one valid handle or `CapacityExceeded` with no handle. The world fixes its initial dimensional frame/profile/settings; each `AudioWorldSnapshot` carries that same `AudioWorldHandle`, a nonzero strictly increasing sequence, the complete `SpatialFrame3`, the listener spatial position/rotation, and immutable emitter position/velocity records uniquely sorted by nonempty emitter GUID. Validate finite poses/velocities, normalized listener rotation, matching frame owner/profile/settings, per-world/global snapshot capacities, and canonical emitter ordering before publication; construction/publication copy the records. The latest unconsumed snapshot may be replaced; `SnapshotReplacementCount` reports replacements and reliable commands never use this lossy policy. A frame-revision change replaces only derived spatialization caches; canonical listener/emitter/fixed-pose positions do not move. A raw position has no provenance; the snapshot's frame supplies profile identity/settings/revision without changing canonical positions. `DestroyWorldAsync` invalidates the handle at admission, ends its voices with the normal fade and `WorldDisposed` terminal events, and completes only after those voices/tails and retained asset credits are reclaimed. Its receipt reports the final accepted snapshot sequence, or null if none was published. A stale/foreign handle throws before mutation.

`AudioWorldOptions` deliberately has no duplicate `SpatialProfileId` or `SpatialSettings` option: its required `SpatialFrame3 InitialFrame` is the single source for both. `SpatialFrame3` validates the registered profile/settings pair at construction, and `CreateWorld` rejects a default or otherwise invalid frame before admission. Callers using the default units pass `SpatialProfileId.Default` and `SpatialSettings.Default` when constructing that frame; custom settings require their matching registered profile ID. `AudioMixerOptions` has no spatial profile because it creates no spatial world.

`StopAsync` and `StopAllAsync` default to a 64-frame fade and no scheduled frame; zero-frame untimed stop applies at admission. Bus gain ramps over 64 frames. Graph replacement is a rooted acyclic tree containing exactly one Master bus; each bus has at most one parent and each voice one destination. Before atomic publication, for every possible input bus and every ancestor including itself, the exact product of frozen `MaximumGain` values across that contiguous upward segment must be at most 65536. This checks every segment, not only the product from an input bus through Master, so a small upstream gain cannot hide an overlarge intermediate path. Input accumulation, post-gain/filter values, and retained/replacement graph histories must also satisfy chapter [08](08-binary32-kernels-and-predicates.md)'s binary32 headroom proof. `AudioAssetStore` is mixer-owned; `LoadAsync` returns an `AudioClipLease` that pins immutable package backing, and accepted Play commands take their own lease.

`TrySubmit` returns false only for `QueueFull`, leaves `completion` null and admits nothing; true transfers independent command and asset credits and supplies a task completed on the control thread. In `NotRequested`, `TrySubmit` throws `InvalidOperationException`, so it never introduces a second false outcome. `SubmitAsync` and the named non-Try command methods fault on admission overflow. `AudioCommandResult.Applied` confirms exact sample-frame application, not audible hardware output or voice end. A nonempty applied Play returns its valid voice handle; an empty clip completes at its scheduled frame with `EmptyClipCompleted` and no handle. Accepted-then-rejected application returns its specific rejection reason, including missing world/listener/emitter or an unavailable required snapshot sequence. `VoiceEnded` is a separate reliable `AudioEvent` read on the control thread; an applied Stop confirms fade scheduling, not the last audible sample. `NextFrameToMix` exposes the sample clock. `DeviceSnapshot` cheaply reads the latest control-published immutable observation under [24](24-custom-audio-mixing-and-spatialization.md#2-owned-api-and-assetimport-contract)'s freshness/lifetime rules; it does not build a fresh snapshot or drain events. `AudioDeviceSnapshot` also provides a publication-copied process-duration histogram whose ordered buckets have inclusive lower/exclusive upper `TimeSpan` bounds (`null` upper bound denotes overflow), clipped-sample and stream-underrun counts, and the last diagnostic. The real-time consumer never runs task continuations/user callbacks, waits on I/O, or releases leases. Control setup keeps internal buffer access for the entire real-time callback epoch; public acquisition/window/disposal is forbidden in that callback, and reclamation occurs on the control owner only after quiescence. `AudioWorldSnapshot` is latest-value immutable input with explicit frame/profile identity; it is not an ECS reference or a global double origin.

`RenderFrames` is valid only for `Offline`; any ordinary caller thread may invoke it, and awaiting `CreateAsync` confers no thread affinity. It renders exactly `frameCount` frames into a destination of exactly `frameCount * 2` floats, with checked arithmetic, and accepts at most 65,536 frames per call. Calls use one serialized exclusive consumer admission across the mixer: a concurrent or reentrant call throws a contextual `InvalidOperationException` before changing the output span or sample clock. Offline mode has no native consumer; the normal multithreaded producer API may continue to submit commands. A concurrent `DisposeAsync` closes new render admissions and does not release mixer state or pins until any admitted `RenderFrames` call has returned. It advances the same sample clock/calendar as native mode. `RenderFrames` in `NotRequested` or `Native` throws `InvalidOperationException`; `NotRequested` does not mix, advance, admit commands, or claim audible success.

`InputRouter` is one recipient, either `HostLiveUI` or one `SimulationTimeline`, and is host-created; it cannot inspect/drain another recipient. `RegisterMap` and `ReplaceMap` validate an entire typed `InputActionMapV1`; success reports the published revision, while rejection reports the first stable `ValidationIssue(Code, SchemaPath)` and leaves the prior state unchanged. `SchemaPath` is a format-independent RFC 6901 escaped path, selected using the schema's stable ordinal path order; it is not tied to JSON authoring. `SetActiveContextMask` validates `MapId`, `ExpectedRevision`, and the complete new mask, then reports the applied host sequence or assigned tick; rejected changes carry a validation issue and do not mutate state. `PlatformHost.AssignDeviceAsync` applies assignment at the named simulation recipient's tick boundary. `InputActionSnapshot` is immutable copied action state, not a destructive queue read: digital actions carry end state and ordered edges; axis actions also carry the effective components and value-change transitions. Transitions retain original host sequence and recipient-assigned tick when present.

`InputActionMapV1`, action/binding/context records, physical event envelopes, device generations, recipient inbox limits, overflow/reconciliation, text composition/commit, and timeline assignment are chapter [14](14-custom-input-actions-and-text.md) contracts. `InputRouter` does not own native capture, clipboard, shaping, widget focus, or text rendering. Platform capture fans out one copied envelope per recipient; UI's event-time `UiInputDisposition` is captured with that recipient's copy before its later tick mapping. Headless input uses explicit normalized injection/replay, never a hidden native route.

## 10. Platform, UI, editor, modules, persistence, diagnostics

```csharp
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX;
using TerraFX.Diagnostics;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX
{

public readonly struct PixelSize : IEquatable<PixelSize>
{
    public PixelSize(int width, int height);
    public int Width { get; }
    public int Height { get; }
    public bool Equals(PixelSize other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(PixelSize left, PixelSize right);
    public static bool operator !=(PixelSize left, PixelSize right);
}

public readonly struct WindowHandle : IEquatable<WindowHandle>
{
    internal WindowHandle(uint ownerId, uint index, uint generation);
    public uint OwnerId { get; }
    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid { get; }
    public bool Equals(WindowHandle other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(WindowHandle left, WindowHandle right);
    public static bool operator !=(WindowHandle left, WindowHandle right);
}

public readonly struct MonitorHandle : IEquatable<MonitorHandle>
{
    internal MonitorHandle(uint ownerId, uint index, uint generation);
    public uint OwnerId { get; }
    public uint Index { get; }
    public uint Generation { get; }
    public bool IsValid { get; }
    public bool Equals(MonitorHandle other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(MonitorHandle left, MonitorHandle right);
    public static bool operator !=(MonitorHandle left, MonitorHandle right);
}

public sealed class WindowMetrics
{
    internal WindowMetrics(Vector2 logicalClientSize, PixelSize clientPixelSize, Vector2 contentScale, float? effectiveDpi, DpiSource dpiSource, DpiConfidence dpiConfidence, uint surfaceEpoch, uint swapchainEpoch);
    public Vector2 LogicalClientSize { get; }
    public PixelSize ClientPixelSize { get; }
    public Vector2 ContentScale { get; }
    public float? EffectiveDpi { get; }
    public DpiSource DpiSource { get; }
    public DpiConfidence DpiConfidence { get; }
    public uint SurfaceEpoch { get; }
    public uint SwapchainEpoch { get; }
}

}

namespace TerraFX.Platform
{
public enum ClipboardServicePolicy
{
    NotRequested,
    Optional,
    Required
}

public enum ClipboardServiceStatus
{
    NotRequested,
    Ready,
    Unsupported
}

public sealed class WindowOptions
{
    public WindowOptions(string title);
    public string Title { get; }
    public Vector2 ClientSize { get; init; } = new Vector2(1280, 720);
    public Vector2 MinimumClientSize { get; init; } = new Vector2(1, 1);
    public bool Resizable { get; init; } = true;
    public bool Visible { get; init; } = true;
    public WindowMode Mode { get; init; } = WindowMode.Windowed;
    public MonitorHandle? Monitor { get; init; }
}

public enum WindowCreationStatus
{
    Created,
    Rejected
}

public sealed class WindowCreationReceipt
{
    internal WindowCreationReceipt(WindowCreationStatus status, WindowHandle? window, Diagnostic? rejection);
    public WindowCreationStatus Status { get; }
    public WindowHandle? Window { get; }
    public Diagnostic? Rejection { get; }
}

public sealed class PlatformHost
{
    internal PlatformHost();
    public ClipboardServiceStatus ClipboardStatus { get; }
    public ClipboardTextService? ClipboardText { get; }
    public ImmutableArray<MonitorHandle> GetMonitorHandles();
    public Task<InputRoutingReceipt> AssignDeviceAsync(InputDeviceHandle device, uint simulationRecipientId, PlayerSlot playerSlot, CancellationToken cancellationToken = default);
    public Task<WindowCommandReceipt> SetTitleAsync(WindowHandle window, string title, CancellationToken cancellationToken = default);
    public Task<WindowCommandReceipt> SetModeAsync(WindowHandle window, WindowMode mode, CancellationToken cancellationToken = default);
    public Task<WindowCommandReceipt> CloseWindowAsync(WindowHandle window, CancellationToken cancellationToken = default);
    public WindowMetrics GetMetrics(WindowHandle window);
}

public enum WindowCommandStatus
{
    Applied,
    Disposed,
    Unsupported,
    PermissionDenied,
    Unconfirmed,
    QueueFull
}

public enum WindowCommandKind
{
    SetTitle,
    SetMode,
    Close
}

public sealed class WindowCommandReceipt
{
    internal WindowCommandReceipt(WindowCommandKind operation, WindowHandle window, WindowCommandStatus status, long ownerSequence, Diagnostic? diagnostic);
    public WindowCommandKind Operation { get; }
    public WindowHandle Window { get; }
    public WindowCommandStatus Status { get; }
    public long OwnerSequence { get; }
    public Diagnostic? Diagnostic { get; }
}

public readonly struct UserGestureToken
{
    internal UserGestureToken(long eventSequence, WindowHandle window);
    public long EventSequence { get; }
    public WindowHandle Window { get; }
}

public enum ClipboardStatus
{
    Completed,
    NoSelection,
    Unsupported,
    PermissionDenied,
    SelectionExpired,
    TimedOut,
    InvalidData,
    TooLarge,
    Canceled
}

public sealed class ClipboardReadRequest
{
    public ClipboardReadRequest(WindowHandle window, int maximumUtf8Bytes, UserGestureToken? gestureToken);
    public WindowHandle Window { get; }
    public int MaximumUtf8Bytes { get; }
    public UserGestureToken? GestureToken { get; }
}

public sealed class ClipboardWriteRequest
{
    public ClipboardWriteRequest(WindowHandle window, string text, int maximumUtf8Bytes, UserGestureToken? gestureToken);
    public WindowHandle Window { get; }
    public string Text { get; }
    public int MaximumUtf8Bytes { get; }
    public UserGestureToken? GestureToken { get; }
}

public sealed class ClipboardReadResponse
{
    internal ClipboardReadResponse(ClipboardStatus status, string? text, Diagnostic? diagnostic);
    public ClipboardStatus Status { get; }
    public string? Text { get; }
    public Diagnostic? Diagnostic { get; }
}

public sealed class ClipboardWriteResponse
{
    internal ClipboardWriteResponse(ClipboardStatus status, Diagnostic? diagnostic);
    public ClipboardStatus Status { get; }
    public Diagnostic? Diagnostic { get; }
}

public sealed class ClipboardTextService
{
    internal ClipboardTextService();
    public Task<ClipboardReadResponse> ReadTextAsync(ClipboardReadRequest request, CancellationToken cancellationToken = default);
    public Task<ClipboardWriteResponse> WriteTextAsync(ClipboardWriteRequest request, CancellationToken cancellationToken = default);
}
}

namespace TerraFX.UI
{
public sealed class UiTreeOptions
{
    public UiTreeOptions();
    public int MaximumElements { get; init; } = 131072;
    public int MaximumDepth { get; init; } = 256;
    public int MaximumLayoutPasses { get; init; } = 4;
}

public sealed class UiSnapshot : IDisposable
{
    internal UiSnapshot(long layoutRevision, int drawCount, int hitRegionCount);
    public long LayoutRevision { get; }
    public int DrawCount { get; }
    public int HitRegionCount { get; }
    public void Dispose();
}

public sealed class UiTree : IDisposable
{
    public UiTree(UiTreeOptions options, InputContextId inputContextId);
    public UiElementHandle CreateElement(UiElementDescription description, UiElementHandle? parent);
    public void RemoveElement(UiElementHandle element);
    public void SetText(UiElementHandle element, string text);
    public void SetChecked(UiElementHandle element, bool isChecked);
    public void SetSliderValue(UiElementHandle element, float value);
    public UiLayoutResponse Layout(UiViewport viewport);
    public UiInputDisposition RouteInput(PhysicalInputEvent input);
    public UiSnapshot BuildSnapshot();
    public void Dispose();
}
}

namespace TerraFX.Editor
{
public sealed class EditorSession : IAsyncDisposable
{
    public EditorSession(EngineHost host, EditorSessionOptions options);
    public EditorTransaction BeginTransaction();
    public EditorTransactionReceipt Undo();
    public EditorTransactionReceipt Redo();
    public Task<PlayWorldSession> StartPlayAsync(CancellationToken cancellationToken = default);
    public Task StopPlayAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}

public sealed class EditorTransaction : IDisposable
{
    internal EditorTransaction();
    public EditorValidationResponse Validate();
    public EditorTransactionReceipt Commit();
    public void Dispose();
}
}

namespace TerraFX.Hosting
{
public interface IEngineModule
{
    ModuleManifest Manifest { get; }
    void Register(ModuleRegistry registry);
    void Start(ModuleContext context);
    void Stop();
}

public enum SaveStatus
{
    DurableCommitted,
    BestEffortCommitted,
    DurabilityUnsupported,
    Rejected
}

public sealed class SaveReceipt
{
    internal SaveReceipt(SaveStatus status, long? transactionGeneration, Diagnostic? diagnostic);
    public SaveStatus Status { get; }
    public long? TransactionGeneration { get; }
    public Diagnostic? Diagnostic { get; }
}

public sealed class LoadOptions
{
    public LoadOptions(WorldRestoreIntent restoreIntent);
    public WorldRestoreIntent RestoreIntent { get; }
    public WorldRecordedContinuation? RecordedContinuation { get; init; }
    public Guid? IdentityBranchId { get; init; }
    public Guid? ForkWorldGuid { get; init; }
    public Guid? NewReplayLineageId { get; init; }
    public long MaximumSnapshotBytes { get; init; } = 2_147_483_584;
}

public enum WorldRecordedContinuationPreparationStatus
{
    Prepared,
    Rejected
}

public sealed class WorldRecordedContinuationPreparation : IDisposable
{
    internal WorldRecordedContinuationPreparation(
        WorldRecordedContinuationPreparationStatus status,
        WorldRecordedContinuation? continuation,
        ImmutableArray<Diagnostic> diagnostics);
    public WorldRecordedContinuationPreparationStatus Status { get; }
    public WorldRecordedContinuation? Continuation { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public void Dispose();
}

public sealed class WorldRecordedContinuation : IDisposable
{
    private WorldRecordedContinuation();
    public Guid SourceWorldGuid { get; }
    public Guid ReplayLineageId { get; }
    public Guid IdentityBranchId { get; }
    public long FirstTick { get; }
    public long EndTickExclusive { get; }
    public ReadOnlySpan<byte> SourceCheckpointDigest { get; }
    public ReadOnlySpan<byte> SourcePayloadDigest { get; }
    public ReadOnlySpan<byte> ReplayDigest { get; }
    public void Dispose();
}

public enum WorldLoadPreparationStatus
{
    Prepared,
    Rejected
}

public sealed class WorldLoadPreparation : IDisposable
{
    internal WorldLoadPreparation(WorldLoadPreparationStatus status, WorldLoadPlan? plan, ImmutableArray<Diagnostic> diagnostics);
    public WorldLoadPreparationStatus Status { get; }
    public WorldLoadPlan? Plan { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public void Dispose();
}

public sealed class WorldLoadPlan : IDisposable
{
    internal WorldLoadPlan(World targetWorld, Guid targetWorldGuid, Guid sourceWorldGuid, Guid targetRecordWorldGuid, long snapshotTick);
    public Guid TargetWorldGuid { get; }
    public Guid SourceWorldGuid { get; }
    public Guid TargetRecordWorldGuid { get; }
    public Guid WorldGuid { get; }
    public WorldRestoreIntent RestoreIntent { get; }
    public ReadOnlySpan<byte> SourceCheckpointDigest { get; }
    public ReadOnlySpan<byte> SourcePayloadDigest { get; }
    public long SnapshotTick { get; }
    public void Dispose();
}

public enum WorldLoadStatus
{
    Loaded,
    Rejected
}

public sealed class WorldLoadReceipt
{
    internal WorldLoadReceipt(WorldLoadStatus status, World? replacementWorld, Task? oldWorldRetirement,
        WorldIdentityRemap? identityRemap, Guid? sourceWorldGuid, Guid? targetRecordWorldGuid,
        long? loadedTick, Diagnostic? rejection);
    public WorldLoadStatus Status { get; }
    public World? ReplacementWorld { get; }
    public Task? OldWorldRetirement { get; }
    public WorldIdentityRemap? IdentityRemap { get; }
    public Guid? SourceWorldGuid { get; }
    public Guid? TargetRecordWorldGuid { get; }
    public Guid? WorldGuid { get; }
    public long? LoadedTick { get; }
    public Diagnostic? Rejection { get; }
}

public sealed class WorldPersistence : IAsyncDisposable
{
    public WorldPersistence();
    public Task<SaveReceipt> SaveAsync(World world, SaveOptions options, CancellationToken cancellationToken = default);
    public Task<WorldRecordedContinuationPreparation> PrepareRecordedContinuationAsync(
        World targetWorld,
        string checkpointPath,
        string replayPath,
        long endTickExclusive,
        CancellationToken cancellationToken = default);
    public Task<WorldLoadPreparation> PrepareLoadAsync(World targetWorld, string savePath, LoadOptions options, CancellationToken cancellationToken = default);
    public Task<WorldLoadReceipt> CommitLoadAsync(WorldLoadPlan plan, CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}
}

namespace TerraFX.Diagnostics
{
public enum DiagnosticSeverity
{
    Trace,
    Information,
    Warning,
    Error,
    Critical
}

public readonly struct DiagnosticContextEntry
{
    public DiagnosticContextEntry(string key, string value);
    public string Key { get; }
    public string Value { get; }
}

public sealed class Diagnostic
{
    public Diagnostic(string code, DiagnosticSeverity severity, long monotonicTimestamp, uint? hostId, Guid? worldGuid, uint? deviceId, uint? surfaceId, uint? deviceEpoch, uint? surfaceEpoch, uint? swapchainEpoch, long? tick, Guid? systemId, Guid? passId, int? assetId, string? commandIdentity, string operation, int? nativeResult, string message, ImmutableArray<DiagnosticContextEntry> context);
    public string Code { get; }
    public DiagnosticSeverity Severity { get; }
    public long MonotonicTimestamp { get; }
    public uint? HostId { get; }
    public Guid? WorldGuid { get; }
    public uint? DeviceId { get; }
    public uint? SurfaceId { get; }
    public uint? DeviceEpoch { get; }
    public uint? SurfaceEpoch { get; }
    public uint? SwapchainEpoch { get; }
    public long? Tick { get; }
    public Guid? SystemId { get; }
    public Guid? PassId { get; }
    public int? AssetId { get; }
    public string? CommandIdentity { get; }
    public string Operation { get; }
    public int? NativeResult { get; }
    public string Message { get; }
    public ImmutableArray<DiagnosticContextEntry> Context { get; }
}

`Diagnostic.AssetId` is a primitive nullable integer because Core has no Assets dependency. When present it is interpreted only within the originating store/package operation; it is not globally unique.

public readonly struct DiagnosticQuery
{
    public DiagnosticQuery(int maximumRecords, DiagnosticSeverity minimumSeverity, Guid? worldGuid, long? minimumTick);
    public int MaximumRecords { get; }
    public DiagnosticSeverity MinimumSeverity { get; }
    public Guid? WorldGuid { get; }
    public long? MinimumTick { get; }
}

public sealed class DiagnosticSnapshot
{
    internal DiagnosticSnapshot(ImmutableArray<Diagnostic> records, long droppedCount, long coalescedCount);
    public ImmutableArray<Diagnostic> Records { get; }
    public long DroppedCount { get; }
    public long CoalescedCount { get; }
}

public sealed class DiagnosticsService
{
    internal DiagnosticsService();
    public DiagnosticSnapshot GetSnapshot(DiagnosticQuery query);
    public bool TryGetLatestFault([NotNullWhen(true)] out Diagnostic? diagnostic);
}
}
```

The default `WindowOptions` `ClientSize` is 1280 x 720 logical units, `MinimumClientSize` is 1 x 1, and the window is resizable/visible in `Windowed` mode. Those values are initialized once by the options type; the constructor has no optional arguments. A window is not created in `Headless`. `WindowHandle` and `MonitorHandle` are engine-issued values whose default and zero generation are invalid; removal retires the monitor generation before reuse. `GetMonitorHandles` returns a copied owner-thread snapshot in stable discovery order. A stale monitor handle is rejected before window-request admission; if removal occurs after admission, `CreateWindowAsync` completes with `WindowCreationReceipt.Rejected` and a `MonitorUnavailable` diagnostic, with no window handle. `WindowMetrics` separately reports `LogicalClientSize`, physical `ClientPixelSize`, `ContentScale`, effective DPI/source/confidence, and independent `SurfaceEpoch`/`SwapchainEpoch`. `PlatformHost` is host-created; window mutation tasks complete after owner-thread application and return an operation/status/sequence/diagnostic `WindowCommandReceipt`, not merely after queue admission. `EngineHostOptions.ClipboardServicePolicy` is explicit and defaults to `NotRequested`; `Optional` keeps startup available while exposing `Unsupported`, while `Required` fails startup if unavailable. `PlatformHost.ClipboardStatus` distinguishes `NotRequested`, `Ready`, and `Unsupported`.

Clipboard requests carry the target window, a positive maximum UTF-8 byte limit no greater than 1 MiB, and an optional one-use `UserGestureToken`. Clipboard responses use `ClipboardStatus`; `Text` is present only for `Completed`, and empty text means an actually empty selection. Permission denial, unsupported protocol, expiry, timeout, invalid data, oversize and protocol cancellation remain distinct statuses. Cancellation of the task is separate from a protocol `Canceled` response. No native clipboard storage is returned to the caller.

`UiTree` is a retained owner-thread tree, not a collection of arbitrary executable widgets. `UiElementDescription` names the widget/layout/style/accessibility schema in [25](25-ui-text-and-editor-tools.md). The root's `InputContextId` is from `TerraFX.Input`; the tree consumes copied input after native callbacks have returned and returns an event-time disposition. The tree is limited to 131,072 nodes and depth 256. `Layout` returns `Complete` or a domain `LayoutNotConverged`/overflow response; it never advertises a stale prior layout as current. `BuildSnapshot` returns immutable ordered draw/hit-test data retaining its font/texture versions. `SetText`, `SetChecked`, and `SetSliderValue` validate against the element's registered kind; mismatches throw before mutation.

`EditorSession` owns an edit world distinct from each play world. `EditorTransaction.Validate` returns an editor-specific validation response; `Commit` applies one all-target transaction at a boundary. `Undo`/`Redo` are session operations and return their own receipts. `StartPlayAsync` clones validated authoring state into a fresh world; stopping it cannot mutate the edit world. Inspector operations use registered schema/unit/range/ownership validators, never reflective field writes. [25's authoring mapping](25-ui-text-and-editor-tools.md#authoring-presentation-and-conversion) defines degree/Euler presentation, quantity/frame/bearing semantics and nonmutating preferences; it adds no public runtime angle, unit or coordinate signature. The editor, trust policy and migration/reload contracts are [25](25-ui-text-and-editor-tools.md) and [26](26-csharp-systems-plugins-and-hot-reload.md).

`IEngineModule.Register` is synchronous and limited to the bounded pre-start `ModuleRegistry`; `Start` occurs only after complete admission, and `Stop` occurs during ordered shutdown. Registry calls accept typed component schemas, systems, asset codecs/importers and declared services; registration freezes before a world runs. Modules are trusted, statically compiled members of a generated or explicitly typed closed registry in runtime, editor and tools; no dynamic managed loading or JIT-only registration/reload path exists. The registry is not a sandbox; untrusted packages remain inert bounded data.

`WorldPersistence` is a CPU-only service that can be constructed by standalone callers; `EngineHost.Persistence` is an independently host-owned instance. `SaveAsync` must be called on the World owner and captures synchronously before its first asynchronous file operation. It captures only a committed, nonfaulted snapshot whose canonical mutation revision equals the committed/restored marker, then writes a new checked transaction generation. `DurableCommitted` and `BestEffortCommitted` carry the committed generation and no rejection diagnostic; `DurabilityUnsupported` and `Rejected` carry a diagnostic and no generation. Its task becomes terminal after durable replacement or an explicit non-durable/refused outcome; cancellation before replacement cancels only after its temporary file is safely removed, while cancellation after replacement returns the committed receipt. `PrepareLoadAsync` validates into a disposable plan bound to the exact target World's GUID, registration, and owner generation; it returns `Prepared` with one non-null plan and no diagnostics, or `Rejected` with no plan and diagnostics. `CommitLoadAsync` queues one whole-world replacement at that target's owner boundary, rechecks that the target generation and registration still match, and returns `Loaded` with a non-null `ReplacementWorld`, saved world GUID/tick and an `OldWorldRetirement` task, or `Rejected` with no replacement/loaded identity and a diagnostic. A successful replacement is an atomic state restore and publishes the coherent restored marker without a hidden simulation tick; a host-owned scheduler atomically adopts the replacement, while a standalone caller must use the returned World for subsequent operations. Old state/resources remain alive until their actual consumers retire. A retirement fault after the swap is reported by `OldWorldRetirement` and cannot turn the committed load into a rejection. No current-world mutation occurs during parsing. Disposing before admission prevents commit; accepted work retains independent credits and cannot be revoked by disposal/cancellation. Save schemas, canonical fields, strict allocator/order state, crash-safe generation, recovery selection and replay profiles are chapters [27](27-state-serialization-replay-and-recovery.md) and [10](10-binary-data-and-authoring-formats.md).
`WorldPersistence` is a CPU-only service that can be constructed by standalone callers; `EngineHost.Persistence` is an independently host-owned instance. `SaveAsync` must be called on the World owner and captures synchronously before its first asynchronous file operation. It captures only a committed, nonfaulted snapshot whose canonical mutation revision equals the committed/restored marker, then writes a new checked transaction generation. `DurableCommitted` and `BestEffortCommitted` carry the committed generation and no rejection diagnostic; `DurabilityUnsupported` and `Rejected` carry a diagnostic and no generation. Its task becomes terminal after durable replacement or an explicit non-durable/refused outcome; cancellation before replacement cancels only after its temporary file is safely removed, while cancellation after replacement returns the committed receipt. `PrepareLoadAsync` validates into a disposable plan bound to the exact target World's GUID, registration, and owner generation; it returns `Prepared` with one non-null plan and no diagnostics, or `Rejected` with no plan and diagnostics. `CommitLoadAsync` queues one whole-world replacement at that target's owner boundary, rechecks that the target generation and registration still match, and returns `Loaded` with a non-null `ReplacementWorld`, saved world GUID/tick, independently owned `IdentityRemap` and an `OldWorldRetirement` task, or `Rejected` with no replacement, loaded identity or remap and a diagnostic. A successful replacement is an atomic state restore and publishes the coherent restored marker without a hidden simulation tick; a host-owned scheduler atomically adopts the replacement, while a standalone caller must use the returned World for subsequent operations. Old state/resources remain alive until their actual consumers retire. A retirement fault after the swap is reported by `OldWorldRetirement` and cannot turn the committed load into a rejection. No current-world mutation occurs during parsing. Disposing before admission prevents commit; accepted work retains independent credits and cannot be revoked by disposal/cancellation. Save schemas, canonical fields, strict allocator/order state, crash-safe generation, recovery selection and replay profiles are chapters [27](27-state-serialization-replay-and-recovery.md) and [10](10-binary-data-and-authoring-formats.md).

`LoadOptions` requires an explicit `WorldRestoreIntent` and has no parameterless constructor or `Default` value. The synchronous API front end validates/fixes source-independent option and path shape before I/O or reservations: null required arguments throw `ArgumentNullException`; empty, whitespace or nonabsolute paths throw `ArgumentException`; negative tick endpoints throw `ArgumentOutOfRangeException`; invalid intent, empty GUIDs, conflicting fields and out-of-range snapshot limits throw argument exceptions. This entry phase does not inspect checkpoint compatibility, transformation-dependent lineage requirements or interval completeness. After bounded source inspection, incompatible checkpoint/profile, lineage/branch/fork provenance, endpoint at/before decoded `NextTick`, missing records/hashes or unavailable consumers return `Rejected` with nonempty diagnostics before candidate construction, Core owner issuance, factories/Restore or replacement. An unavailable, mismatched or revoked target binding is stale-target `Rejected`, not synchronous ownership misuse; disposed service/continuation/plan wrappers retain `ObjectDisposedException`. Unexpected I/O/allocation failures fault the task. Preparation results own their returned continuation; the continuation is opaque, engine-issued and disposable, with immutable digest metadata remaining readable after disposal. `WorldLoadPlan.TargetWorldGuid` names the exact old operational target, `SourceWorldGuid` names the checkpoint, and `TargetRecordWorldGuid` names the replacement's semantic records; `WorldGuid` equals `TargetRecordWorldGuid`. On a successful receipt, `WorldGuid` likewise equals `TargetRecordWorldGuid`, and `IdentityRemap` maps only live identities in the verified source checkpoint. Rejection has no remap. Once `CommitLoadAsync` is accepted, caller cancellation no longer cancels the replacement; accepted work uses `CancellationToken.None` and retains its sources until actual consumers unwind.

`DiagnosticQuery` rejects `maximumRecords` outside `[1,8192]`. `GetSnapshot` is bounded, copied and nondestructive: it returns at most the newest matching records in stable insertion order, filtered by minimum severity and optional world/tick, together with the cumulative dropped/coalesced counters observed for that snapshot. A present minimum tick is inclusive and excludes tickless records; a present WorldGuid requires exact equality. Polling never drains or resets the service queue; an editor replaces its own copied view. There is no public diagnostic subscription API. `DiagnosticSnapshot` is immutable copied data; `TryGetLatestFault` returns false only when no fault is retained. Standalone Worlds own their service; host-owned Worlds expose the shared `EngineHost.Diagnostics` service. Disposing a World closes only its standalone service; hosted service lifetime follows the host. A getter on a disposed World throws, but an already returned snapshot remains readable. Stable code/context/severity/native status and the actual operation result are not replaced by logs. The bounded diagnostic queue may coalesce/drop ordinary telemetry only with visible counters; critical last-fault and caller-visible failures have independent retention. [28](28-diagnostics-performance-and-validation.md) owns diagnostic record fields and metric names.

After a standalone service is closed, its `GetSnapshot` and `TryGetLatestFault` methods throw `ObjectDisposedException`; previously returned immutable snapshots remain readable. Closing a hosted World does not close the borrowed host service.

### M03 persistence source and intent bindings

| Owner / public surface | Exact amendment contract |
|---|---|
| Hosting: explicit load/record binding | Retain current WorldRestoreIntent, nondefault LoadOptions, WorldRecordedContinuation/Preparation, PrepareRecordedContinuationAsync, plan/receipt source/target/target-record fields and successful-only IdentityRemap. Apply [M03 8.5](27-state-serialization-replay-and-recovery.md#m03-85-explicit-restore-intent-and-finite-standalone-continuation)'s two-phase option/binding/lease semantics and exact finite source record. Add explicit SourcePayloadDigest metadata to prepared plan/continuation/remap alongside full SourceCheckpointDigest, and copied exact intent metadata where missing. Retain remap GUID methods as explicitly pinned-source queries, never live handles/global or discarded-future fallback. Frozen identity/digest metadata remains readable after wrapper disposal; disposed admission/lookup misuse still throws. |

### Resolved owner contracts

The public signatures above are concrete and are the facade API-review baseline. Supporting values and native semantic data also use their owning chapters' explicit field/constructor tables. No implementation may infer omitted members or replace these signatures with a generic placeholder. Before implementing a coherent surface, inventory every transitively reachable supporting type, bind its namespace, representation, constructor/issuer, fields, nullability and lifetime to an exact declaration or generation descriptor, and reject incomplete entries needed by that unit. The catalogue grows with the implemented surfaces under [35](35-implementation-roadmap.md); absent source implementation is not itself an unresolved design contract. A facade-only compilation with invented support types is not closure evidence. The bounded projections in [37](37-completeness-and-audit.md) do not certify the complete catalogue.

| Surface | Owner contract used by this baseline |
|---|---|
| Spatial settings, metadata-free positions, profile identity, frame/bounds/relative-pose semantics, and exact position conversion | [07](07-spatial-coordinates-and-indexing.md), with the frozen profile record and registry identity in [10](10-binary-data-and-authoring-formats.md). The ID is caller-declared for raw positions and does not prove provenance. |
| Standalone physics values, typed mass/contact records, structural preparation/commit, dimensional rebase receipts, queries, and checkpoints | [22](22-custom-physics-world-and-solver.md) and [23](23-custom-physics-shapes-queries-and-ccd.md). This baseline exposes only `PrepareStructural`/`CommitStructural`; distance is bound to `ExpectedFrameRevision`. |
| Audio mode/endpoint selection, capacities/recovery, command admission/application, voice lifetime, offline rendering, and real-time callback rules | [24](24-custom-audio-mixing-and-spatialization.md). The selected backend/endpoint never falls back silently. |
| Host/standalone asset-store creation and ownership, store capacities, import adapters, tool output limits, and offline cooking | [11](11-assets-import-streaming-and-packages.md) and [09](09-native-assets-and-source-adapters.md). Both ownership modes use `AssetStore.CreateAsync`; cooker code remains tools-only and each accepted operation owns independent input credits. |
| Diagnostic fields, queue bounds, drop/coalesce accounting, and retained faults | [28](28-diagnostics-performance-and-validation.md). The public service uses copied nondestructive polling, not subscriptions or draining. |
| Interactive window-to-surface mapping and close invalidation | [15](15-platform-host-and-devices.md). Successful interactive creation installs the host mapping; close removes it before native retirement. `TerraFX.Graphics` does not reference `TerraFX.Platform`. |
| GPU resource descriptions/metadata, native shader bytecode, reflection, pipeline layouts, and actual pipeline creation | [17](17-graphics-device-and-resources.md) and [18](18-shaders-and-material-pipeline.md). The public resource-description properties, input lease, and typed reflection shape are declared above; shader digests, backend validation, and descriptor semantics remain owned by these chapters. `TerraFX.Graphics` uses core leases and does not reference `TerraFX.Assets`. |
| Typed graph imports, transients, range-scoped versions, access declarations, hazards, and presentation sink | [19](19-render-graph-synchronization-and-presentation.md). The declarations above preserve exact resource ranges, actual owner state, content validity, device epoch, and last-use completion rather than exposing native fences. |
| Committed-world extraction, dimensional camera/frame selection, immutable pinned snapshots, and standard graph construction | [16](16-scenes-transforms-and-cameras.md), [21](21-2d-and-3d-rendering.md), and [32](32-end-to-end-integration-scenarios.md). `TerraFX.Rendering` is the bridge from `TerraFX.Entities`/`TerraFX.Scenes` into `TerraFX.Graphics`; render-thread callbacks receive no live world. |
| Consumer navigation | The informative [31](31-public-api-consumer-guide.md) connects these declarations to owner contracts and usage flows; it defines no alternative record or operation surface. |
| Monitor handles, generation invalidation, window creation, and stale placement | [15](15-platform-host-and-devices.md). Enumeration is a stable copied owner-thread snapshot; a post-admission removal produces a domain receipt. |

## 11. API acceptance obligations

The following new prefix is reserved for this API/roadmap review. These are future implementation obligations, not tests already run.

| ID | Required boundary observation |
|---|---|
| AR01 | Every public namespace obeys [29](29-project-setup-build-and-deployment.md#1-repository-and-assembly-dag)'s physical placement and logical dependency rules; separate consumer projects compile against public APIs with nullable analysis and no internal access, unsafe escape, platform-layer leak, or preview-only signature. EN152 additionally checks thirteen physical products, merged namespace owners/logical layering, one-assembly library packages and consumer dependency/root/publish closures; shared internal accessibility cannot bypass issuer/ownership boundaries. Native ownership preserves declared base types/interfaces and disposal authority: no public `SafeHandle` base/handle escape or new independent `GraphicsDevice.Dispose` contract follows from choosing direct native ownership. The transitive public type review distinguishes foundational interchange/protocols from replaceable implementation services/storage under [02](02-dotnet-api-memory-and-ownership.md#public-type-boundary). Core consumers need no `Stopwatch`/`TimeProvider` object; `ImmutableArray<T>` retains its accepted array interchange, and restore maps expose `IReadOnlyDictionary` with immutable snapshot contents, no writable aliases or concrete implementation/enumeration-order requirement. Timing values have specified domain/unit/frequency/conversion semantics independent of backing-source inference. BF13/EN150-EN151 prove signed integer defaults, concrete-width consumer compatibility, negative/overflow admission, unchanged unsigned representation exceptions and checked wire/native conversion without a blanket CLS or generated-code performance claim. |
| AR02 | Public constructors match the declared consumer surface; no output/service type gains an accidental public parameterless constructor. Constructors/options reproduce the stated defaults and reject invalid values before mutation/allocation; zero-defaulted handles/poses/options never masquerade as valid owners, identity rotations, or configured profiles. Primitive geometry contexts and the four typed definitions are public without PhysicsWorld/handle dependencies; statistics/faults/responses remain engine-constructed. `ShapeMeasure` default is canonical zero. |
| AR03 | Geometry tests distinguish Hamilton `Multiply` from application-order `Compose`; point/direction/rotation/normal paths and `EngineGeometryBinary32Rows2` interchange/profile identity match chapters 06/08. |
| AR04 | Spatial tests cover exact metadata-free 16/24-byte raw positions, explicit-settings interpretation without raw-origin inference, the fixed UUIDv8 `SpatialProfileId.Default` GUID registered before use, thread-safe append-only registration (4096 IDs including Default), idempotent matching registration and conflicting-ID rejection, the registry's fixed basis/geometry/spatial/kernel semantics, one required profile/settings identity across world/physics options and audio frames, and converter resolution of both declarations. Cover negative carries, int32 overflow, both sides of shared closed borders, `[0,C)` persistent canonicalization, 4096-unit default meter cells, SP13's bounded integer unit exponent/exact decimal-rational conversion with one binary32 offset rounding, frame revisions, and bounded nearby conversion without global double coordinates. Both public `ConvertQuantity` overloads additionally prove bounded literal/dyadic conversion, `long.MinValue`, positive-denominator admission, nearest-even subnormals/signed underflow, float signed-zero preservation, exact rational zero and overflow exceptions without internal access. BN09 distinguishes legacy factor/UUID/profile bytes from the new manifest rather than rebinding their identities. |
| AR05 | Buffer tests prove scoped windows, read/write conflicts, one active write-lease window/native operation across copied references, sublease reservation inheritance and parent exclusion until actual completion, owner/lease disposal, reentrant callbacks, zero and large extents, and independent async sublease retention until actual consumer/fence completion; real-time callbacks perform no public acquisition/window/disposal or reclamation, and no prohibited memory/sequence type appears in runtime/tooling source or generated output. Buffer ownership APIs expose neither pointers nor disposable value owners. Ordinary/ref-struct callable consumers prove exact `ref` state, span-escape rejection, chain-stop results and lifetime cleanup without boxing/delegate adaptation. |
| AR06 | Host/ECS/jobs tests prove generated AOT column/query registrations, ordinary/ref-struct chunk callables with caller-visible state and scoped borrow/fault cleanup, owner affinity, callback invalidation, deterministic partition/command ordering, complete job join, admission through 1,048,576 live entities and atomic refusal of the next entity without partial mutation, atomic structural preflight, stage-9 irrevocability, subscriber failure isolation, and no false completion on canceled waits. |
| AR07 | Scene/animation/assets/codecs tests prove native schema authority, one-time adapter conversion, root `SpatialPosition3` storage with zero `LocalTransform.Position`, descendant-local `Vector3` translation, derived-cache-only frame changes, prepared-scene activation at `Begin`, typed playback/event policies, exact EBIN1 consumption, EPKG2/EIDX1 range/checksum/profile checks, and source-format independence. Package replacement validates the complete external digest before the atomic content-set switch; outstanding asset/CPU/GPU/audio leases retain backing through actual consumer completion. No per-asset version, cook receipt, serialized dependency graph, or runtime Tools dependency is required. Collision content uses schema-version-owned dimensional validation/statistics, canonical `ShapeMeasure` encoding and nullable applicability; changed settings require rebuilding affected content into a package selected with the new profile, not implicit revalidation/rebinding. Independent vectors distinguish stored-pair `0x0000B676` from direct-formula `0x0000B677`. Every subsystem reader/writer is gated on its compiled schema registration and focused expected bytes; no field tags are inferred from CLR/property order or reflection. |
| AR08 | Public consumer tests create an interactive window, call `EngineHost.GetSurface` on the owner thread, call `Renderer.Extract` for both dimensions on a committed world with the exact matching frame/unique camera selection/physical extent, and pass each snapshot through `Renderer.BuildGraph`. Add custom work through typed imports/transients and `AddRasterPass`/`AddComputePass`; create a graphics/compute pipeline with `GraphicsDevice.CreatePipelineAsync`, bind `GpuPipeline.Handle` with `CommandRecorder.SetPipeline`, compile using `RenderGraph.Compile`, submit with `Renderer.SubmitAsync`, and independently await the non-null receipt sequence with `Surface.WaitForPresentationAsync`. Prove foreign/stale/no-surface rejection, close invalidation, profile/device/surface/swapchain/frame epoch agreement, exact requested/limit overflow diagnostics with no partial snapshot, graph hazards/capability rejection before submit, and no render-thread ECS access. In `Headless`, prove the non-null CPU `Renderer` extracts a logical snapshot while `GraphicsDevice`/`GpuResidency` remain null and graph build/submit refuse without fabricated GPU output. In `OffscreenNative`, create and use a real same-device/epoch color-attachment target without a surface, execute extraction -> graph/custom pass -> pipeline -> submit, and verify no WSI/presentation claim (`PresentationSequence` null and surface/swapchain epochs zero). Upload complete supported 2D/3D mip subresources from retained read leases with explicit row/slice pitches; read back supported buffer ranges and full texture subresources, verify immutable format/extent/subresource/pitches, zeroed exposed padding, and actual copy/fence/map/CPU-copy completion with no zero-filled success. Exercise offscreen target readback only with declared copy capability. Verify built-in HDR tone mapping and UI composition produce final linear SDR, interactive/offscreen terminals encode it once without double tone mapping, and a raw HDR custom graph must tone-map before selecting its final source. Prove pipeline bytecode credits last through actual backend creation, upload/readback credits through actual transfer completion, snapshot/resource/pipeline/target pins through actual GPU completion, and interactive presentation resources through their actual present fence. Separate request acceptance, upload readiness, GPU fence completion, readback completion and presentation fence completion; never substitute GPU-ready or fallback success. Draw extraction and the CPU particle pool admit their 131,072 limits and reject excess observably; particle saturation returns `RejectSpawn` and does not alter authoritative physics. |
| AR09 | Standalone physics tests compile without ECS/Hosting and execute complete `QueryResult` status/payload/budget/ordering contracts, including ordinary/ref-struct generic visitors, explicit `VisitorStopped` counts and pre-delivery validation, spatial `BodyPose2D/3D` queries, spatial point/AABB arguments, local shape poses, `float` scalar/impulse members and `Matrix4x4` 3D inertia, selected-unit numerical profiles/default gravity, `DefaultDensity` equal to the physical 1 kg/m^2 or 1 kg/m^3 value converted by exact `10^(2*e)`/`10^(3*e)` under frozen `MetersPerUnitExponent` and one final binary32 rounding, nullable/omitted `PhysicsMaterial` resolution to the owning world's frozen dimensional defaults during structural preparation, selected-unit explicit density, copied prepared-graph material and no frame-change retuning, dimensional `PrepareStructural`/`CommitStructural` rebase requests/receipts, local rollback, checkpoint capture/restore, and independent owner/generation checks. M06 primitive cooking preserves the same geometry outcome and `ShapeStatistics2D/3D`; capacity refusal and full-profile admission remain distinct, and mass/inertia derives from robust geometry/density rather than rounded measures or `TryGetSingle`. |
| AR10 | Audio/input/platform tests distinguish explicit `NotRequested`/`Native`/`Offline` mode, reject every `NotRequested` command admission with `InvalidOperationException` without retaining work, and prove `TrySubmit` returns false only for `QueueFull`; they also cover endpoint mismatch/unsupported failures without fallback, typed 2D/3D play defaults, generation-checked audio-world/bus handles, snapshot sequence/order/capacity/replacement and tracked/fixed-pose admission, attenuation and terminal world/voice completion, command admission/application/voice end, exact offline frame output from any ordinary caller thread, exclusive serialized offline-consumer admission with concurrent/reentrant rejection before output/clock mutation, concurrent disposal closing admissions and joining an active render, and continued multithreaded command production. Check every contiguous upward `MaximumGain` segment from each possible bus input through every ancestor including itself using exact frozen products <= 65536; include a case where downstream attenuation hides an overlarge intermediate product from a root-only check, and verify input/post-gain/filter/history bounds under chapter 08. Also cover recipient-specific tick assignment and overflow, clipboard permission/status from task cancellation, stable monitor enumeration and generation invalidation, and explicit headless behavior. Real-time audio access is retained from control setup through the complete callback epoch and reclaimed only after callback quiescence. |
| AR11 | UI/editor/modules/persistence/diagnostics tests prove closed AOT registrations/generated editor metadata and managed-code rebuild/restart without in-process assembly replacement, copied immutable snapshots, admission through the 131,072-node/depth-256 UI ceiling and refusal beyond either bound without partial mutation, schema-validated all-or-nothing edits, play-world isolation, trusted-code admission, atomic save/load generation, bounded nondestructive diagnostic polling, drop/coalesce counters, and caller-visible errors despite telemetry pressure. EN145-EN149 additionally prove chapter 25's explicit rotation presentation/order, quantity/unit/frame/bearing mappings, nonunique-Euler handling and bit-preserving nonmutating display/commit isolation in the AOT editor and tools. |
| AR12 | Public API/layout/schema baselines and complete consumer examples pass mandatory NativeAOT runtime/editor/tool profiles on both supported targets; optional JIT comparisons expose no extra features and cannot substitute for AOT execution; exact NUnit selected/executed test counts and actual native capability results attach to the accepted revision. |

AR01/AR02 additionally require the transitive supporting-type catalogue and complete public-only programs. Build and compile them with their owning implementation units under [35](35-implementation-roadmap.md), completing the full set at M08 rather than requiring the future engine before M02 starts; selected declaration projections cannot replace them. Exercise typed/default/named float/Angle bindings, generic operator and char/UTF-8 protocols, nullable codec flow and rejected scoped-borrow escape. AR06/AR10 cover async-only host disposal, synchronous usage versus admission/execution failures, optional final tokens with required TryInvoke token, repeated disposal versus ValueTask consumption, continuation safety on standalone and host owners, nonnullable bus outputs and cached snapshot freshness/retention. AR07 covers complete/default codec outputs and explicit nullable-reference handling. AR02/AR06/AR07/AR10/AR11 also cover exact scene/UI/text option defaults, lower budget ceilings, Unicode/caret/revision validation, atomic tag edits and independently retained UI snapshots.

AR01/AR02's M03 Runtime observations retain the distinct semantic cases: exact partial-member permission does not widen to a generated sibling or a different accessor, and alias-bearing value copies cannot erase protected storage. Exceptional finally-flow and selected foreach/await/async-disposal paths preserve those ownership guarantees; they add no public API or storage lifetime. Preserve consumer-authorized factories, ordinary handle defaults/copies, valid lower-interface inversion and unrelated geometry conversions. These cases do not replace public-only/AOT consumers or complete EN152's later package/publish matrix.

Selected complete caller compilation units are provided in [31](31-public-api-consumer-guide.md#6-complete-selected-caller-compilation-units). These focused compilations do not substitute for complete engine consumers. API compatibility tooling, NUnit execution, native behavior, throughput and allocation measurements require implementation qualification.