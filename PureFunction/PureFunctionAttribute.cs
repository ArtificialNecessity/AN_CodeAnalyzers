using System;

namespace AN.CodeAnalyzers.PureFunction
{
    /// <summary>
    /// Marks a method as a pure function that must not modify instance state.
    /// Any assignment to instance fields or properties inside the method body
    /// produces a compile-time AN0501 error.
    ///
    /// <c>Inherited = true</c> ensures overrides automatically inherit the constraint
    /// without needing to re-apply the attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public sealed class PureFunctionAttribute : Attribute
    {
    }
}