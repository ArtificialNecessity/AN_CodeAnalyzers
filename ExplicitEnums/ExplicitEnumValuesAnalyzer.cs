// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AN.CodeAnalyzers.ExplicitEnums
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ExplicitEnumValuesAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AN001";
        private const string category = "Design";

        private static readonly DiagnosticDescriptor rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Enum member must have explicit value",
            "Enum member '{0}' must have an explicit value assigned",
            category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Enum members should have explicit values to prevent accidental changes when members are reordered.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(rule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();
            analysisContext.RegisterSyntaxNodeAction(analyzeEnumMember, SyntaxKind.EnumMemberDeclaration);
        }

        private void analyzeEnumMember(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
        {
            var enumMemberDeclarationSyntax = (EnumMemberDeclarationSyntax)syntaxNodeAnalysisContext.Node;

            // Check if member already has an explicit value
            if (enumMemberDeclarationSyntax.EqualsValue != null) {
                return;
            }

            // Get the containing enum declaration
            var enumDeclarationSyntax = enumMemberDeclarationSyntax.Parent as EnumDeclarationSyntax;
            if (enumDeclarationSyntax == null) {
                return;
            }

            // Check if enum has the suppression attribute
            var enumSymbol = syntaxNodeAnalysisContext.SemanticModel.GetDeclaredSymbol(enumDeclarationSyntax);
            if (enumSymbol == null) {
                return;
            }

            // Check if enum has the suppression attribute (takes precedence)
            if (hasSuppressAttribute(enumSymbol)) {
                return;
            }

            // Check if enum has the require attribute (forces enforcement)
            bool hasRequireAttribute = hasRequireExplicitAttribute(enumSymbol);

            // Read the EnforceExplicitEnumValues MSBuild property
            var enforcementScope = getEnforcementScope(syntaxNodeAnalysisContext);

            // Check if we should enforce based on the scope setting or require attribute
            if (!hasRequireAttribute && !shouldEnforce(enforcementScope, enumSymbol.DeclaredAccessibility)) {
                return;
            }

            // Report diagnostic
            var diagnostic = Diagnostic.Create(
                rule,
                enumMemberDeclarationSyntax.Identifier.GetLocation(),
                enumMemberDeclarationSyntax.Identifier.Text);

            syntaxNodeAnalysisContext.ReportDiagnostic(diagnostic);
        }

        private bool hasSuppressAttribute(INamedTypeSymbol enumSymbol)
        {
            return enumSymbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == nameof(SuppressExplicitEnumValuesAttribute) ||
                attr.AttributeClass?.Name == "SuppressExplicitEnumValues");
        }

        private bool hasRequireExplicitAttribute(INamedTypeSymbol enumSymbol)
        {
            return enumSymbol.GetAttributes().Any(attr =>
                attr.AttributeClass?.Name == nameof(RequireExplicitEnumValuesAttribute) ||
                attr.AttributeClass?.Name == "RequireExplicitEnumValues");
        }

        private string getEnforcementScope(SyntaxNodeAnalysisContext syntaxNodeAnalysisContext)
        {
            var options = syntaxNodeAnalysisContext.Options.AnalyzerConfigOptionsProvider.GetOptions(syntaxNodeAnalysisContext.Node.SyntaxTree);

            if (options.TryGetValue("build_property.EnforceExplicitEnumValues", out var scopeValue)
                && !string.IsNullOrEmpty(scopeValue)) {
                return scopeValue.ToLowerInvariant();
            }

            // Default must always remain "public" — public enums are the ones that break
            // binary compatibility in shipped DLLs when their values silently shift.
            return "public";
        }

        private bool shouldEnforce(string enforcementScope, Accessibility enumAccessibility)
        {
            switch (enforcementScope) {
                case "all":
                    return true;

                case "public":
                    return enumAccessibility == Accessibility.Public;

                case "explicit":
                    // Only enforce when enum has [RequireExplicitEnumValues] attribute
                    // This is handled by the hasRequireAttribute check in the main logic
                    return false;

                case "none":
                default:
                    return false;
            }
        }
    }
}
