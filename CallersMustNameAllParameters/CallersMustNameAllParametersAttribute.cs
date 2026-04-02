using System;

namespace AN.CodeAnalyzers.CallersMustNameAllParameters
{
    /// <summary>
    /// Requires all arguments at call sites to use named parameter syntax.
    /// Enforced by analyzer AN0103.
    /// 
    /// Single-parameter methods are always exempt (clear enough without naming).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    public sealed class CallersMustNameAllParametersAttribute : Attribute
    {
    }
}