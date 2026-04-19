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
            "Method '{0}' is marked [PureFunction] and must not modify instance state. Assignment to instance {1} '{2}' is not allowed here.",
            category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Methods marked [PureFunction] must not write to instance fields or properties. This enforces side-effect-free methods for render passes, layout measurement, and hit testing.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(rule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();

            analysisContext.RegisterOperationBlockStartAction(operationBlockStartContext =>
            {
                if (!(operationBlockStartContext.OwningSymbol is IMethodSymbol methodSymbol))
                {
                    return;
                }

                if (!hasPureFunctionAttribute(methodSymbol))
                {
                    return;
                }

                string methodName = methodSymbol.Name;

                // Register for assignment operations
                operationBlockStartContext.RegisterOperationAction(
                    operationContext => analyzeAssignment(operationContext, methodName),
                    OperationKind.SimpleAssignment,
                    OperationKind.CompoundAssignment,
                    OperationKind.Increment,
                    OperationKind.Decrement);

                // Register for argument operations (ref/out)
                operationBlockStartContext.RegisterOperationAction(
                    operationContext => analyzeArgument(operationContext, methodName),
                    OperationKind.Argument);
            });
        }

        private void analyzeAssignment(OperationAnalysisContext operationContext, string methodName)
        {
            IOperation? target = null;

            switch (operationContext.Operation)
            {
                case ISimpleAssignmentOperation simpleAssignment:
                    target = simpleAssignment.Target;
                    break;
                case ICompoundAssignmentOperation compoundAssignment:
                    target = compoundAssignment.Target;
                    break;
                case IIncrementOrDecrementOperation incrementOrDecrement:
                    target = incrementOrDecrement.Target;
                    break;
            }

            if (target == null)
            {
                return;
            }

            if (tryGetInstanceMemberInfo(target, out string? memberKind, out string? memberName))
            {
                operationContext.ReportDiagnostic(Diagnostic.Create(
                    rule,
                    operationContext.Operation.Syntax.GetLocation(),
                    methodName,
                    memberKind,
                    memberName));
            }
        }

        private void analyzeArgument(OperationAnalysisContext operationContext, string methodName)
        {
            var argumentOperation = (IArgumentOperation)operationContext.Operation;

            if (argumentOperation.Parameter == null)
            {
                return;
            }

            if (argumentOperation.Parameter.RefKind != RefKind.Ref &&
                argumentOperation.Parameter.RefKind != RefKind.Out)
            {
                return;
            }

            // The value of a ref/out argument may be wrapped in a conversion or
            // other transparent operation. Walk through to find the underlying reference.
            var value = argumentOperation.Value;

            // Unwrap IConversionOperation if present
            while (value is IConversionOperation conversionOperation)
            {
                value = conversionOperation.Operand;
            }

            if (tryGetInstanceMemberInfo(value, out string? memberKind, out string? memberName))
            {
                operationContext.ReportDiagnostic(Diagnostic.Create(
                    rule,
                    argumentOperation.Syntax.GetLocation(),
                    methodName,
                    memberKind,
                    memberName));
            }
        }

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
                    // Skip static fields (Instance would be null for statics, but guard explicitly)
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

        /// <summary>
        /// Checks if the method has [PureFunction] either directly or inherited via the override chain.
        /// Roslyn's GetAttributes() only returns directly-applied attributes, so we must
        /// manually walk the OverriddenMethod chain for Inherited = true behavior.
        /// </summary>
        private static bool hasPureFunctionAttribute(IMethodSymbol methodSymbol)
        {
            // Walk the override chain
            var current = methodSymbol;

            while (current != null)
            {
                if (hasDirectPureFunctionAttribute(current))
                {
                    return true;
                }

                current = current.OverriddenMethod;
            }

            // Check explicit interface implementations
            foreach (var interfaceMethod in methodSymbol.ExplicitInterfaceImplementations)
            {
                if (hasDirectPureFunctionAttribute(interfaceMethod))
                {
                    return true;
                }
            }

            // Check implicit interface implementations
            foreach (var iface in methodSymbol.ContainingType.AllInterfaces)
            {
                foreach (var ifaceMember in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    var implementation = methodSymbol.ContainingType.FindImplementationForInterfaceMember(ifaceMember);

                    if (SymbolEqualityComparer.Default.Equals(implementation, methodSymbol) &&
                        hasDirectPureFunctionAttribute(ifaceMember))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool hasDirectPureFunctionAttribute(IMethodSymbol methodSymbol)
        {
            return methodSymbol.GetAttributes().Any(attributeData =>
                attributeData.AttributeClass?.Name == nameof(PureFunctionAttribute) ||
                attributeData.AttributeClass?.Name == "PureFunction");
        }
    }
}