// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Represents a synchronous function that consumes a borrowed argument.</summary>
/// <typeparam name="TArgument">The argument type, which may be a ref struct.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface IFunc<TArgument, TResult>
    where TArgument : allows ref struct
{
    /// <summary>Invokes the function with a borrowed argument that cannot be retained.</summary>
    /// <param name="argument">The argument to consume for this invocation.</param>
    /// <returns>The result of the function.</returns>
    TResult Invoke(scoped TArgument argument);
}
