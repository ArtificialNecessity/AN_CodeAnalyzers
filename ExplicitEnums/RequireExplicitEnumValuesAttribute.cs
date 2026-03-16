// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace AN.CodeAnalyzers.ExplicitEnums
{
    /// <summary>
    /// Requires explicit enum values for the decorated enum, regardless of the project-wide setting.
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    public sealed class RequireExplicitEnumValuesAttribute : Attribute
    {
    }
}
