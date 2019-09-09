// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.UI
{
    /// <summary>Provides access to a window subsystem.</summary>
    public interface IWindowProvider
    {
        /// <summary>Gets the handle for the instance.</summary>
        IntPtr Handle { get; }

        /// <summary>Gets the <see cref="IWindow" /> objects created by the instance.</summary>
        IEnumerable<IWindow> Windows { get; }

        /// <summary>Create a new <see cref="IWindow" /> instance.</summary>
        /// <returns>A new <see cref="IWindow" /> instance</returns>
        IWindow CreateWindow();
    }
}
