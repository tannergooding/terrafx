// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Input;
using TerraFX.UI;

namespace TerraFX.Hosting;

internal readonly record struct PlatformWindowInputRoutingResult(
    UiInputDisposition UiDisposition,
    InputRoutingReceipt? HostRecipient,
    InputRoutingReceipt? SimulationRecipient,
    long HostDiscardedInputCount,
    long SimulationDiscardedInputCount,
    bool RequiresStateReconciliation);
