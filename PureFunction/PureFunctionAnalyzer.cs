using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace AN.CodeAnalyzers.PureFunction
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PureFunctionAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AN0501";
        private const string category = "Correctness";

        private static readonly DiagnosticDescriptor rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Instance state mutation in [PureFunction] method",
            "Method '{0}' is marked [PureFunction] and must not modify \"this\" instance state. {1}",
            category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Methods marked [PureFunction] must not write to instance fields or properties, " +
                         "including through calls to private/internal helpers on the same instance. " +
                         "This enforces side-effect-free methods for render passes, layout measurement, and hit testing.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(rule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();

            analysisContext.RegisterOperationBlockAction(operationBlockContext =>
            {
                if (!(operationBlockContext.OwningSymbol is IMethodSymbol methodSymbol))
                {
                    return;
                }

                bool transitive;
                if (!tryGetPureFunctionAttribute(methodSymbol, out transitive))
                {
                    return;
                }

                var callerType = methodSymbol.ContainingType;
                var compilation = operationBlockContext.Compilation;
                var visited = transitive
                    ? new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)
                    : null;

                foreach (var operationBlock in operationBlockContext.OperationBlocks)
                {
                    var violations = new List<MutationViolation>();
                    findMutations(operationBlock, callerType, compilation, visited,
                        callChain: ImmutableArray<string>.Empty, violations: violations);

                    foreach (var violation in violations)
                    {
                        string detail;

                        if (violation.CallChain.Length == 0)
                        {
                            // Direct mutation in the [PureFunction] method
                            detail = $"Assignment to instance {violation.MemberKind} '{violation.MemberName}' is not allowed here.";
                        }
                        else
                        {
                            // Transitive mutation via call chain
                            var chain = string.Join(" \u2192 ", violation.CallChain);
                            detail = $"Assignment to instance {violation.MemberKind} '{violation.MemberName}' is not allowed (via call chain: {methodSymbol.Name} \u2192 {chain}).";
                        }

                        operationBlockContext.ReportDiagnostic(Diagnostic.Create(
                            rule,
                            violation.Location,
                            methodSymbol.Name,
                            detail));
                    }
                }
            });
        }

        // ════════════════════════════════════════════════════════════════
        //  Core recursive mutation finder
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recursively walks an operation tree looking for instance state mutations.
        /// For invocations on <c>this</c> where the callee is a concrete non-virtual
        /// method in the same compilation, recurses into the callee's body.
        /// </summary>
        private static void findMutations(
            IOperation operation,
            INamedTypeSymbol callerType,
            Compilation compilation,
            HashSet<IMethodSymbol>? visited,
            ImmutableArray<string> callChain,
            List<MutationViolation> violations)
        {
            // Check this operation for direct mutations
            checkOperationForMutation(operation, callChain, violations);

            // Check for transitive mutations via this.Method() calls
            if (visited != null && operation is IInvocationOperation invocation)
            {
                checkTransitiveCall(invocation, callerType, compilation, visited, callChain, violations);
            }

            // Recurse into children
            foreach (var child in operation.Children)
            {
                findMutations(child, callerType, compilation, visited, callChain, violations);
            }
        }

        /// <summary>
        /// Checks a single operation for direct instance state mutation:
        /// assignments, compound assignments, increment/decrement, and ref/out arguments.
        /// </summary>
        private static void checkOperationForMutation(
            IOperation operation,
            ImmutableArray<string> callChain,
            List<MutationViolation> violations)
        {
            switch (operation)
            {
                case ISimpleAssignmentOperation simpleAssignment:
                    checkTarget(simpleAssignment.Target, operation, callChain, violations);
                    break;

                case ICompoundAssignmentOperation compoundAssignment:
                    checkTarget(compoundAssignment.Target, operation, callChain, violations);
                    break;

                case IIncrementOrDecrementOperation incrementOrDecrement:
                    checkTarget(incrementOrDecrement.Target, operation, callChain, violations);
                    break;

                case IArgumentOperation argument:
                    checkRefOutArgument(argument, callChain, violations);
                    break;
            }
        }

        private static void checkTarget(
            IOperation target,
            IOperation reportOn,
            ImmutableArray<string> callChain,
            List<MutationViolation> violations)
        {
            if (tryGetInstanceMemberInfo(target, out string? memberKind, out string? memberName))
            {
                violations.Add(new MutationViolation(
                    reportOn.Syntax.GetLocation(), memberKind!, memberName!, callChain));
            }
        }

        private static void checkRefOutArgument(
            IArgumentOperation argument,
            ImmutableArray<string> callChain,
            List<MutationViolation> violations)
        {
            if (argument.Parameter == null)
            {
                return;
            }

            if (argument.Parameter.RefKind != RefKind.Ref &&
                argument.Parameter.RefKind != RefKind.Out)
            {
                return;
            }

            var value = argument.Value;

            while (value is IConversionOperation conversion)
            {
                value = conversion.Operand;
            }

            if (tryGetInstanceMemberInfo(value, out string? memberKind, out string? memberName))
            {
                violations.Add(new MutationViolation(
                    argument.Syntax.GetLocation(), memberKind!, memberName!, callChain));
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  Transitive call analysis
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// If the invocation is a call on <c>this</c> to a concrete, non-virtual method
        /// in the same compilation, resolve the callee's body and recursively check it
        /// for mutations to our instance state.
        /// </summary>
        private static void checkTransitiveCall(
            IInvocationOperation invocation,
            INamedTypeSymbol callerType,
            Compilation compilation,
            HashSet<IMethodSymbol> visited,
            ImmutableArray<string> callChain,
            List<MutationViolation> violations)
        {
            var calleeMethod = invocation.TargetMethod;

            // Only follow calls on 'this' (implicit or explicit).
            // Calls on _logger, someParam, localVar, etc. are not our instance — skip.
            if (invocation.Instance != null && !(invocation.Instance is IInstanceReferenceOperation))
            {
                return;
            }

            // Static calls don't mutate our instance
            if (calleeMethod.IsStatic)
            {
                return;
            }

            // Skip virtual/abstract/interface calls — we can't know which override runs.
            // Those overrides are independently enforced if they have [PureFunction].
            if (calleeMethod.IsVirtual || calleeMethod.IsAbstract || calleeMethod.IsOverride)
            {
                return;
            }

            // Only follow methods on our own type (not base type methods we can't see)
            if (!SymbolEqualityComparer.Default.Equals(calleeMethod.ContainingType, callerType))
            {
                return;
            }

            // Cycle guard
            if (!visited.Add(calleeMethod))
            {
                return;
            }

            // Must be in the same compilation (we need the source body)
            if (calleeMethod.DeclaringSyntaxReferences.Length == 0)
            {
                return;
            }

            // Get the callee's operation tree
            var calleeSyntaxRef = calleeMethod.DeclaringSyntaxReferences[0];
            var calleeSyntax = calleeSyntaxRef.GetSyntax();
            var calleeSyntaxTree = calleeSyntax.SyntaxTree;
            var semanticModel = compilation.GetSemanticModel(calleeSyntaxTree);
            var calleeBodyOperation = semanticModel.GetOperation(calleeSyntax);

            if (calleeBodyOperation == null)
            {
                return;
            }

            // Recurse with the callee name appended to the call chain
            var extendedChain = callChain.Add(calleeMethod.Name);
            findMutations(calleeBodyOperation, callerType, compilation, visited, extendedChain, violations);
        }

        // ════════════════════════════════════════════════════════════════
        //  Instance member detection (unchanged from v1)
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Checks whether an operation is a reference to an instance field or property on <c>this</c>.
        /// Returns the member kind ("field" or "property") and name if so.
        /// </summary>
        private static bool tryGetInstanceMemberInfo(IOperation operation, out string? memberKind, out string? memberName)
        {
            switch (operation)
            {
                case IFieldReferenceOperation fieldRef
                    when fieldRef.Instance is IInstanceReferenceOperation:
                    if (fieldRef.Field.IsStatic)
                    {
                        memberKind = null;
                        memberName = null;
                        return false;
                    }
                    memberKind = "field";
                    memberName = fieldRef.Field.Name;
                    return true;

                case IPropertyReferenceOperation propertyRef
                    when propertyRef.Instance is IInstanceReferenceOperation:
                    if (propertyRef.Property.IsStatic)
                    {
                        memberKind = null;
                        memberName = null;
                        return false;
                    }
                    memberKind = "property";
                    memberName = propertyRef.Property.Name;
                    return true;

                default:
                    memberKind = null;
                    memberName = null;
                    return false;
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  [PureFunction] attribute detection
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Checks if the method has [PureFunction] either directly or inherited via the
        /// override chain or interface implementations. Returns the effective Transitive
        /// value from the closest attribute found (default true).
        /// </summary>
        private static bool tryGetPureFunctionAttribute(IMethodSymbol methodSymbol, out bool transitive)
        {
            var current = methodSymbol;
            transitive = true;

            while (current != null)
            {
                if (tryGetDirectPureFunctionAttribute(current, out transitive))
                {
                    return true;
                }

                current = current.OverriddenMethod;
            }

            foreach (var interfaceMethod in methodSymbol.ExplicitInterfaceImplementations)
            {
                if (tryGetDirectPureFunctionAttribute(interfaceMethod, out transitive))
                {
                    return true;
                }
            }

            foreach (var iface in methodSymbol.ContainingType.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var implementation = methodSymbol.ContainingType.FindImplementationForInterfaceMember(ifaceMember);

                    if (SymbolEqualityComparer.Default.Equals(implementation, methodSymbol) &&
                        tryGetDirectPureFunctionAttribute(ifaceMember, out transitive))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool tryGetDirectPureFunctionAttribute(IMethodSymbol methodSymbol, out bool transitive)
        {
            foreach (var attributeData in methodSymbol.GetAttributes())
            {
                if (attributeData.AttributeClass?.Name == nameof(PureFunctionAttribute) ||
                    attributeData.AttributeClass?.Name == "PureFunction")
                {
                    // Read the Transitive named argument (default: true)
                    transitive = true;

                    foreach (var namedArg in attributeData.NamedArguments)
                    {
                        if (namedArg.Key == "Transitive" && namedArg.Value.Value is bool value)
                        {
                            transitive = value;
                        }
                    }

                    return true;
                }
            }

            transitive = true;
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        //  Violation data
        // ════════════════════════════════════════════════════════════════

        private readonly struct MutationViolation
        {
            public readonly Location Location;
            public readonly string MemberKind;
            public readonly string MemberName;
            public readonly ImmutableArray<string> CallChain;

            public MutationViolation(
                Location location,
                string memberKind,
                string memberName,
                ImmutableArray<string> callChain)
            {
                Location = location;
                MemberKind = memberKind;
                MemberName = memberName;
                CallChain = callChain;
            }
        }
    }
}