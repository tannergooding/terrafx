// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Declares the tracked state and content validity for one imported graph resource range.</summary>
public sealed class GraphImportOptions
{
    /// <summary>Initializes one imported resource declaration.</summary>
    /// <param name="initialState">The resource owner's actual current state for the imported range.</param>
    /// <param name="contentsValid">Whether the imported range contains initialized contents.</param>
    /// <param name="completionPolicy">How the graph handles the resource owner's prior uses.</param>
    /// <exception cref="ArgumentOutOfRangeException">A state or completion policy is undefined.</exception>
    /// <exception cref="ArgumentException">Undefined state is paired with valid contents.</exception>
    public GraphImportOptions(
        GraphResourceState initialState,
        bool contentsValid,
        GraphImportCompletionPolicy completionPolicy)
    {
        if (!Enum.IsDefined(initialState))
        {
            throw new ArgumentOutOfRangeException(nameof(initialState));
        }

        if (!Enum.IsDefined(completionPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(completionPolicy));
        }

        if ((initialState == GraphResourceState.Undefined) && contentsValid)
        {
            throw new ArgumentException("An undefined resource range cannot claim initialized contents.", nameof(contentsValid));
        }

        InitialState = initialState;
        ContentsValid = contentsValid;
        CompletionPolicy = completionPolicy;
    }

    /// <summary>Gets the policy for prior resource uses.</summary>
    public GraphImportCompletionPolicy CompletionPolicy { get; }

    /// <summary>Gets whether the imported range has initialized contents.</summary>
    public bool ContentsValid { get; }

    /// <summary>Gets the actual tracked state of the imported range.</summary>
    public GraphResourceState InitialState { get; }
}
