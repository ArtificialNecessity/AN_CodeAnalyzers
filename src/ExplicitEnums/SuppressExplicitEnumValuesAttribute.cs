// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace AN.CodeAnalyzers.ExplicitEnums
{
    /// <summary>
    /// Suppresses the explicit enum values analyzer for the decorated enum.
    /// Use this for internal throwaway enums or cases where auto-increment is genuinely desired.
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    public sealed class SuppressExplicitEnumValuesAttribute : Attribute
    {
    }
}
