// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Describes whether one subscriber received a committed publication.</summary>
public enum SubscriberDeliveryStatus
{
    /// <summary>The subscriber completed successfully.</summary>
    Delivered = 0,

    /// <summary>The subscriber threw after the publication committed.</summary>
    Failed = 1,
}
