// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Represents a synchronous operation that consumes a borrowed argument.</summary>
/// <typeparam name="TArgument">The argument type, which may be a ref struct.</typeparam>
public interface IAction<TArgument>
    where TArgument : allows ref struct
{
    /// <summary>Invokes the operation with a borrowed argument that cannot be retained.</summary>
    /// <param name="argument">The argument to consume for this invocation.</param>
    void Invoke(scoped TArgument argument);
}
