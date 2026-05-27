using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace AN.CodeAnalyzers.ProhibitPlatformImports
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ProhibitPlatformImportsAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AN0104";
        private const string category = "TypeSafety";

        // Rule for [DllImport], [LibraryImport], [UnmanagedCallersOnly] attribute-based imports
        private static readonly DiagnosticDescriptor platformImportAttributeRule = new DiagnosticDescriptor(
            DiagnosticId,
            "Platform import prohibited",
            "Platform import '{0}' is prohibited because <ProhibitPlatformImports> is set to '{1}'",
            category,
            DiagnosticSeverity.Error, // default severity; overridden by config
            isEnabledByDefault: true);

        // Rule for NativeLibrary.Load / NativeLibrary.TryLoad calls
        private static readonly DiagnosticDescriptor nativeLibraryCallRule = new DiagnosticDescriptor(
            DiagnosticId,
            "Native library call prohibited",
            "Call to '{0}' is prohibited because <ProhibitPlatformImports> is set to '{1}'",
            category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(platformImportAttributeRule, nativeLibraryCallRule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();

            // Register for method declarations (to catch [DllImport], [LibraryImport], [UnmanagedCallersOnly])
            analysisContext.RegisterSyntaxNodeAction(analyzeMethodDeclaration, SyntaxKind.MethodDeclaration);

            // Register for invocation expressions (to catch NativeLibrary.Load / TryLoad)
            analysisContext.RegisterSyntaxNodeAction(analyzeInvocationExpression, SyntaxKind.InvocationExpression);
        }

        private void analyzeMethodDeclaration(SyntaxNodeAnalysisContext nodeContext)
        {
            var configValue = getConfigValue(nodeContext);
            if (configValue == "disabled")
                return;

            var effectiveSeverity = configValue == "error"
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;

            var methodDeclaration = (MethodDeclarationSyntax)nodeContext.Node;

            foreach (var attributeList in methodDeclaration.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var attributeName = attribute.Name.ToString();

                    if (isPlatformImportAttribute(attributeName))
                    {
                        var methodName = methodDeclaration.Identifier.Text;

                        // Span from the attribute list start through the method name
                        var diagnosticSpanStart = attributeList.SpanStart;
                        var diagnosticSpanEnd = methodDeclaration.Identifier.Span.End;
                        var diagnosticSpan = TextSpan.FromBounds(diagnosticSpanStart, diagnosticSpanEnd);
                        var diagnosticLocation = Location.Create(methodDeclaration.SyntaxTree, diagnosticSpan);

                        reportDiagnostic(nodeContext, diagnosticLocation,
                            platformImportAttributeRule, methodName, configValue, effectiveSeverity);

                        // Only report once per method even if it has multiple platform import attributes
                        return;
                    }
                }
            }
        }

        private void analyzeInvocationExpression(SyntaxNodeAnalysisContext nodeContext)
        {
            var configValue = getConfigValue(nodeContext);
            if (configValue == "disabled")
                return;

            var effectiveSeverity = configValue == "error"
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;

            var invocationExpression = (InvocationExpressionSyntax)nodeContext.Node;

            // Check if this is a NativeLibrary.Load or NativeLibrary.TryLoad call
            if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccessExpression)
            {
                var memberName = memberAccessExpression.Name.Identifier.Text;
                if (memberName != "Load" && memberName != "TryLoad")
                    return;

                // Verify it's actually System.Runtime.InteropServices.NativeLibrary
                var symbolInfo = nodeContext.SemanticModel.GetSymbolInfo(invocationExpression, nodeContext.CancellationToken);
                if (symbolInfo.Symbol is IMethodSymbol calledMethodSymbol &&
                    calledMethodSymbol.ContainingType != null &&
                    calledMethodSymbol.ContainingType.Name == "NativeLibrary" &&
                    calledMethodSymbol.ContainingType.ContainingNamespace?.ToDisplayString() == "System.Runtime.InteropServices")
                {
                    var calledMethodFullName = $"NativeLibrary.{memberName}";
                    reportDiagnostic(nodeContext, invocationExpression.GetLocation(),
                        nativeLibraryCallRule, calledMethodFullName, configValue, effectiveSeverity);
                }
            }
        }

        private static bool isPlatformImportAttribute(string attributeName)
        {
            return attributeName == "DllImport" || attributeName == "DllImportAttribute" ||
                   attributeName == "System.Runtime.InteropServices.DllImport" ||
                   attributeName == "System.Runtime.InteropServices.DllImportAttribute" ||
                   attributeName == "LibraryImport" || attributeName == "LibraryImportAttribute" ||
                   attributeName == "System.Runtime.InteropServices.LibraryImport" ||
                   attributeName == "System.Runtime.InteropServices.LibraryImportAttribute" ||
                   attributeName == "UnmanagedCallersOnly" || attributeName == "UnmanagedCallersOnlyAttribute" ||
                   attributeName == "System.Runtime.InteropServices.UnmanagedCallersOnly" ||
                   attributeName == "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute";
        }

        private static void reportDiagnostic(
            SyntaxNodeAnalysisContext nodeContext,
            Location diagnosticLocation,
            DiagnosticDescriptor diagnosticRule,
            string symbolName,
            string configValue,
            DiagnosticSeverity effectiveSeverity)
        {
            var effectiveRule = new DiagnosticDescriptor(
                diagnosticRule.Id,
                diagnosticRule.Title,
                diagnosticRule.MessageFormat,
                diagnosticRule.Category,
                effectiveSeverity,
                diagnosticRule.IsEnabledByDefault);

            var diagnostic = Diagnostic.Create(effectiveRule, diagnosticLocation, symbolName, configValue);
            nodeContext.ReportDiagnostic(diagnostic);
        }

        private static string getConfigValue(SyntaxNodeAnalysisContext nodeContext)
        {
            var analyzerConfigOptions = nodeContext.Options.AnalyzerConfigOptionsProvider
                .GetOptions(nodeContext.Node.SyntaxTree);

            if (analyzerConfigOptions.TryGetValue("build_property.ProhibitPlatformImports", out var configValue)
                && !string.IsNullOrEmpty(configValue))
            {
                return configValue.ToLowerInvariant();
            }

            // Default is "disabled"
            return "disabled";
        }
    }
}