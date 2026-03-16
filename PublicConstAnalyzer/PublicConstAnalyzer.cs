using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AN.CodeAnalyzers.StableABIVerification
{
    /// <summary>
    /// AN0002: Warns when a public const field is declared in a library.
    /// Public const values are baked into callers at compile time, meaning
    /// changing the value requires recompiling all consumers.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PublicConstAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AN0002";
        private const string category = "BinaryCompatibility";

        private static readonly DiagnosticDescriptor rule = new DiagnosticDescriptor(
            DiagnosticId,
            "Avoid public const in libraries",
            "Public const '{0}' is baked into callers at compile time. Consider 'public static readonly' unless the value is a true universal constant.",
            category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Public const values are inlined into caller assemblies at compile time. Changing the value requires recompiling all consumers. Use 'public static readonly' for values that may change between versions.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(rule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();
            analysisContext.RegisterSyntaxNodeAction(analyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
        }

        private void analyzeFieldDeclaration(SyntaxNodeAnalysisContext syntaxNodeContext)
        {
            var fieldDeclarationSyntax = (FieldDeclarationSyntax)syntaxNodeContext.Node;

            // Must have both 'public' and 'const' modifiers
            bool hasPublicModifier = false;
            bool hasConstModifier = false;

            foreach (var modifier in fieldDeclarationSyntax.Modifiers) {
                if (modifier.IsKind(SyntaxKind.PublicKeyword)) {
                    hasPublicModifier = true;
                } else if (modifier.IsKind(SyntaxKind.ConstKeyword)) {
                    hasConstModifier = true;
                }
            }

            if (!hasPublicModifier || !hasConstModifier) {
                return;
            }

            // Check that the containing type is also public (otherwise the const isn't truly public)
            var containingTypeDeclaration = fieldDeclarationSyntax.Parent as TypeDeclarationSyntax;
            if (containingTypeDeclaration == null) {
                return;
            }

            var containingTypeSymbol = syntaxNodeContext.SemanticModel.GetDeclaredSymbol(containingTypeDeclaration);
            if (containingTypeSymbol == null) {
                return;
            }

            // Walk up the type hierarchy to ensure all containing types are public
            if (!isEffectivelyPublic(containingTypeSymbol)) {
                return;
            }

            // Report a diagnostic for each variable declarator in the field declaration
            foreach (var variableDeclarator in fieldDeclarationSyntax.Declaration.Variables) {
                var fieldSymbol = syntaxNodeContext.SemanticModel.GetDeclaredSymbol(variableDeclarator);
                if (fieldSymbol == null) {
                    continue;
                }

                // Skip fields decorated with [PermanentConst]
                if (hasPermanentConstAttribute(fieldSymbol)) {
                    continue;
                }

                string fullyQualifiedFieldName = $"{containingTypeSymbol.Name}.{variableDeclarator.Identifier.Text}";

                var diagnostic = Diagnostic.Create(
                    rule,
                    variableDeclarator.Identifier.GetLocation(),
                    fullyQualifiedFieldName);

                syntaxNodeContext.ReportDiagnostic(diagnostic);
            }
        }

        private static bool hasPermanentConstAttribute(ISymbol fieldSymbol)
        {
            return fieldSymbol.GetAttributes().Any(attrData =>
                attrData.AttributeClass?.Name == nameof(PermanentConstAttribute) ||
                attrData.AttributeClass?.Name == "PermanentConst");
        }

        private static bool isEffectivelyPublic(INamedTypeSymbol typeSymbol)
        {
            INamedTypeSymbol? currentType = typeSymbol;

            while (currentType != null) {
                if (currentType.DeclaredAccessibility != Accessibility.Public) {
                    return false;
                }
                currentType = currentType.ContainingType;
            }

            return true;
        }
    }
}