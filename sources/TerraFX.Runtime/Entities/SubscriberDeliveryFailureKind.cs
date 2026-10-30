// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies one subscriber-delivery failure category.</summary>
public enum SubscriberDeliveryFailureKind
{
    /// <summary>The subscriber callback threw an ordinary exception.</summary>
    SubscriberException = 0,
}
