using System;

namespace AN.CodeAnalyzers.StableABIVerification
{
    /// <summary>
    /// Suppresses AN0002 for the decorated const field, indicating this value
    /// is a true universal constant that will never change (e.g. mathematical
    /// constants, protocol version numbers baked into a spec).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class PermanentConstAttribute : Attribute
    {
    }
}