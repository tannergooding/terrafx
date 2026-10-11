// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Represents a synchronous function that observes a borrowed argument by readonly reference.</summary>
/// <typeparam name="TArgument">The argument type, which may be a ref struct.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface IReadOnlyRefFunc<TArgument, TResult>
    where TArgument : allows ref struct
{
    /// <summary>Invokes the function with a borrowed readonly reference that cannot be retained.</summary>
    /// <param name="argument">The argument to observe for this invocation.</param>
    /// <returns>The result of the function.</returns>
    TResult Invoke(scoped in TArgument argument);
}
