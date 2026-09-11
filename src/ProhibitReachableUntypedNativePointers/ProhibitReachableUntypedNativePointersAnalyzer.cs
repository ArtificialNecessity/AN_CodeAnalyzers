using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace AN.CodeAnalyzers.ProhibitReachableUntypedNativePointers
{
    /// <summary>
    /// AN0102 — untyped native pointers that REACH our code without being spelled.
    ///
    /// AN0100 catches the untyped native pointers we spelled: the IntPtr / UIntPtr / void* tokens.
    /// AN0102 catches the ones we did not spell but that move through our code anyway because
    /// someone else's signature carries them:
    ///
    ///     var h = File.OpenHandle(path, FileMode.Open);      // returns SafeFileHandle  → SOURCE
    ///     RandomAccess.Read(h, buffer, 0);                   // takes  SafeFileHandle  → SINK
    ///     new SafeFileHandle((nint)0x1234, ownsHandle: false); // ctor takes IntPtr   → SINK (+ type reference)
    ///
    /// A value's TYPE is an untyped native pointer if it is IntPtr/UIntPtr (not nint/nuint used as
    /// integers), void*, a SafeHandle/CriticalHandle subclass, or a struct whose instance fields make
    /// it one ("IntPtr in a costume": GCHandle, HandleRef, RuntimeTypeHandle, struct HWND { IntPtr Value; }).
    /// Classes are never untyped by membership: FileStream, Process, Type are managed objects; only
    /// touching their handle members fires.
    ///
    /// The analyzer fires on every SOURCE (member returning one), every SINK (member accepting one as a
    /// parameter or setter, regardless of how the argument was spelled), and every DECLARATION or
    /// inference of such a type. One diagnostic per syntax node.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ProhibitReachableUntypedNativePointersAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AN0102";
        public const string BuildPropertyName = "ProhibitReachableUntypedNativePointers";
        private const string category = "TypeSafety";

        private const string helpLinkUrl =
            "https://github.com/ArtificialNecessity/AN_CodeAnalyzers/blob/main/docs/TypeSafePInvoke.md";

        // Every message in the AN0100/AN0102 family ends with the same idiom block so it is impossible
        // to read the error and not know what to type instead.
        private const string neverLine = "\n        Never IntPtr, never void*, never SafeHandle.";
        private const string seeLine = "\n        See: " + helpLinkUrl;

        // {M} is the suggested marker struct name (HFILE, HWND, HANDLE_NAME), always the LAST argument.
        private const string idiomHandle =
            "\n        Handles are typed opaque pointers — an empty marker struct, and the pointer IS the handle:" +
            "\n            public unsafe struct {M} {{ }}     {M}* h;     ({M}*)null for \"no handle\"";

        private const string idiomPointer =
            "\n        Pointers name their pointee:  byte* buffer;  OVERLAPPED* ov;  HPCON** phPC;" +
            "\n        Wrap a T* in a span with MemoryMarshal.CreateSpan(ref *p, n); copy with Unsafe.CopyBlock(ref byte, ref byte, uint);" +
            "\n        allocate with stackalloc or GC.AllocateUninitializedArray<T>(n, pinned: true) + fixed, or your own P/Invoke returning T*.";

        // ---- SOURCE: a member returns / yields an untyped native pointer ----
        // {0} member, {1} type, {2} marker
        private static readonly DiagnosticDescriptor sourceHandleRule = new DiagnosticDescriptor(
            DiagnosticId,
            "Untyped native handle reaches this code",
            "'{0}' returns '{1}', an untyped native handle (IntPtr with a class name on it)." +
            idiomHandle.Replace("{M}", "{2}") +
            neverLine + " Or use a managed API that does not expose the handle (FileStream)." + seeLine,
            category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: helpLinkUrl);

        // {0} member, {1} type
        private static readonly DiagnosticDescriptor sourcePointerRule = new DiagnosticDescriptor(
            DiagnosticId,
            "Untyped native pointer reaches this code",
            "'{0}' returns '{1}', an untyped native pointer." + idiomPointer + neverLine + seeLine,
            category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: helpLinkUrl);

        // ---- SINK: a member accepts an untyped native pointer (parameter or setter) ----
        // {0} member, {1} type, {2} parameter, {3} marker
        private static readonly DiagnosticDescriptor sinkHandleRule = new DiagnosticDescriptor(
            DiagnosticId,
            "Untyped native handle handed to native code",
            "'{0}' takes '{1}' ({2}), an untyped native handle — any integer can be wrapped and handed here." +
            idiomHandle.Replace("{M}", "{3}") +
            neverLine + " Or use a managed API that does not expose the handle (FileStream)." + seeLine,
            category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: helpLinkUrl);

        // {0} member, {1} type, {2} parameter
        private static readonly DiagnosticDescriptor sinkPointerRule = new DiagnosticDescriptor(
            DiagnosticId,
            "Untyped native pointer handed to native code",
            "'{0}' takes '{1}' ({2}), an untyped native pointer." + idiomPointer + neverLine + seeLine,
            category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: helpLinkUrl);

        // ---- TYPE: our code names or infers an untyped native pointer type ----
        // {0} type, {1} reason, {2} marker
        private static readonly DiagnosticDescriptor typeRule = new DiagnosticDescriptor(
            DiagnosticId,
            "Untyped native pointer type",
            "'{0}' is an untyped native pointer type: {1}." +
            idiomHandle.Replace("{M}", "{2}") + neverLine + seeLine,
            category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: helpLinkUrl);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(sourceHandleRule, sourcePointerRule, sinkHandleRule, sinkPointerRule, typeRule);

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            analysisContext.EnableConcurrentExecution();

            analysisContext.RegisterCompilationStartAction(compilationStartContext =>
            {
                var classifier = new UntypedTypeClassifier(compilationStartContext.Compilation);

                compilationStartContext.RegisterOperationAction(
                    operationContext => analyzeOperation(operationContext, classifier),
                    OperationKind.Invocation,
                    OperationKind.ObjectCreation,
                    OperationKind.PropertyReference,
                    OperationKind.FieldReference,
                    OperationKind.EventReference,
                    OperationKind.MethodReference);

                compilationStartContext.RegisterSyntaxNodeAction(
                    nodeContext => analyzeTypeName(nodeContext, classifier),
                    SyntaxKind.IdentifierName,
                    SyntaxKind.GenericName);
            });
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // A — sources and sinks (symbol-based, via IOperation)
        // ──────────────────────────────────────────────────────────────────────────────────

        private static void analyzeOperation(OperationAnalysisContext operationContext, UntypedTypeClassifier classifier)
        {
            var operation = operationContext.Operation;
            if (operation.IsImplicit)
                return;

            var effectiveSeverity = getEffectiveSeverity(operationContext.Options, operation.Syntax.SyntaxTree);
            if (effectiveSeverity == null)
                return;

            switch (operation)
            {
                case IInvocationOperation invocation:
                    reportMethodUse(operationContext, classifier, invocation.TargetMethod, invocation.Instance, operation.Syntax, effectiveSeverity.Value);
                    break;

                case IObjectCreationOperation objectCreation when objectCreation.Constructor != null:
                    reportMethodUse(operationContext, classifier, objectCreation.Constructor, null, operation.Syntax, effectiveSeverity.Value);
                    break;

                case IMethodReferenceOperation methodReference:
                    reportMethodUse(operationContext, classifier, methodReference.Method, methodReference.Instance, operation.Syntax, effectiveSeverity.Value);
                    break;

                case IPropertyReferenceOperation propertyReference:
                    reportMemberUse(operationContext, classifier, propertyReference.Property, propertyReference.Property.Type,
                        propertyReference.Instance, isWriteTarget(propertyReference), operation.Syntax, effectiveSeverity.Value);
                    break;

                case IFieldReferenceOperation fieldReference:
                    reportMemberUse(operationContext, classifier, fieldReference.Field, fieldReference.Field.Type,
                        fieldReference.Instance, isWriteTarget(fieldReference), operation.Syntax, effectiveSeverity.Value);
                    break;

                case IEventReferenceOperation eventReference:
                    reportMemberUse(operationContext, classifier, eventReference.Event, eventReference.Event.Type,
                        eventReference.Instance, isWriteTarget: false, operation.Syntax, effectiveSeverity.Value);
                    break;
            }
        }

        private static bool isWriteTarget(IOperation memberReference)
        {
            return memberReference.Parent is IAssignmentOperation assignment && assignment.Target == memberReference;
        }

        /// <summary>Method / constructor / delegate-bound method: parameters are sinks, the return is a source.</summary>
        private static void reportMethodUse(
            OperationAnalysisContext operationContext,
            UntypedTypeClassifier classifier,
            IMethodSymbol method,
            IOperation? instance,
            SyntaxNode syntax,
            DiagnosticSeverity effectiveSeverity)
        {
            var memberDisplay = displayMember(method);

            // SINK: any parameter of untyped type, regardless of what argument we passed.
            foreach (var parameter in method.Parameters)
            {
                var parameterInfo = classifier.Classify(parameter.Type);
                if (!parameterInfo.IsUntyped || isSpelledNativeInt(parameter, parameter.Type))
                    continue;

                reportSink(operationContext, syntax, effectiveSeverity, memberDisplay, parameter.Type, "parameter '" + parameter.Name + "'", parameterInfo);
                return;
            }

            // `: base(...)` / `: this(...)` construct nothing new into our hands; the base-type reference already fired.
            if (syntax is ConstructorInitializerSyntax)
                return;

            // SOURCE: the return type (constructors: the constructed type).
            var producedType = method.MethodKind == MethodKind.Constructor ? method.ContainingType : method.ReturnType;
            var producedInfo = classifier.Classify(producedType);
            if (producedInfo.IsUntyped && !isSpelledNativeInt(method, producedType))
            {
                reportSource(operationContext, syntax, effectiveSeverity, memberDisplay, producedType, producedInfo);
                return;
            }

            reportReceiverIfUntyped(operationContext, classifier, method, instance, syntax, effectiveSeverity);
        }

        /// <summary>Property / field / event reference: a read is a source, a write is a sink.</summary>
        private static void reportMemberUse(
            OperationAnalysisContext operationContext,
            UntypedTypeClassifier classifier,
            ISymbol member,
            ITypeSymbol memberType,
            IOperation? instance,
            bool isWriteTarget,
            SyntaxNode syntax,
            DiagnosticSeverity effectiveSeverity)
        {
            var memberInfo = classifier.Classify(memberType);
            if (memberInfo.IsUntyped && !isSpelledNativeInt(member, memberType))
            {
                var memberDisplay = displayMember(member);
                if (isWriteTarget)
                    reportSink(operationContext, syntax, effectiveSeverity, memberDisplay, memberType, "setter", memberInfo);
                else
                    reportSource(operationContext, syntax, effectiveSeverity, memberDisplay, memberType, memberInfo);
                return;
            }

            reportReceiverIfUntyped(operationContext, classifier, member, instance, syntax, effectiveSeverity);
        }

        /// <summary>A-3: an instance member of an untyped type (h.IsInvalid, h.DangerousGetHandle()).</summary>
        private static void reportReceiverIfUntyped(
            OperationAnalysisContext operationContext,
            UntypedTypeClassifier classifier,
            ISymbol member,
            IOperation? instance,
            SyntaxNode syntax,
            DiagnosticSeverity effectiveSeverity)
        {
            if (member.IsStatic || instance == null)
                return;

            var receiverInfo = classifier.Classify(member.ContainingType);
            if (!receiverInfo.IsUntyped)
                return;

            reportDiagnostic(operationContext.ReportDiagnostic, typeRule, syntax.GetLocation(), effectiveSeverity,
                member.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), receiverInfo.Reason, receiverInfo.SuggestedMarker);
        }

        private static void reportSource(
            OperationAnalysisContext operationContext, SyntaxNode syntax, DiagnosticSeverity effectiveSeverity,
            string memberDisplay, ITypeSymbol type, UntypedTypeInfo info)
        {
            var typeDisplay = UntypedTypeClassifier.display(type);
            if (info.IsHandle)
                reportDiagnostic(operationContext.ReportDiagnostic, sourceHandleRule, syntax.GetLocation(), effectiveSeverity, memberDisplay, typeDisplay, info.SuggestedMarker);
            else
                reportDiagnostic(operationContext.ReportDiagnostic, sourcePointerRule, syntax.GetLocation(), effectiveSeverity, memberDisplay, typeDisplay);
        }

        private static void reportSink(
            OperationAnalysisContext operationContext, SyntaxNode syntax, DiagnosticSeverity effectiveSeverity,
            string memberDisplay, ITypeSymbol type, string parameterDisplay, UntypedTypeInfo info)
        {
            var typeDisplay = UntypedTypeClassifier.display(type);
            if (info.IsHandle)
                reportDiagnostic(operationContext.ReportDiagnostic, sinkHandleRule, syntax.GetLocation(), effectiveSeverity, memberDisplay, typeDisplay, parameterDisplay, info.SuggestedMarker);
            else
                reportDiagnostic(operationContext.ReportDiagnostic, sinkPointerRule, syntax.GetLocation(), effectiveSeverity, memberDisplay, typeDisplay, parameterDisplay);
        }

        private static string displayMember(ISymbol member)
        {
            var containing = member.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var name = member is IMethodSymbol { MethodKind: MethodKind.Constructor } ? "new" : member.Name;
            return containing == null ? name : containing + "." + name;
        }

        /// <summary>
        /// On .NET 7+ nint and System.IntPtr are the same symbol, so for a symbol declared in OUR source we look at
        /// how the author spelled it: `nint`/`nuint` is an integer (clean here; AN0100 review-prompts it in P/Invoke),
        /// `IntPtr` is AN0100's token. Metadata symbols have no spelling and are never exempted.
        /// </summary>
        private static bool isSpelledNativeInt(ISymbol declaringSymbol, ITypeSymbol type)
        {
            if (!isIntPtrFamily(type))
                return false;

            foreach (var reference in declaringSymbol.DeclaringSyntaxReferences)
            {
                var typeSyntax = declaredTypeSyntax(reference.GetSyntax());
                if (typeSyntax == null)
                    continue;

                var text = typeSyntax is PredefinedTypeSyntax predefined ? predefined.Keyword.Text
                         : typeSyntax is IdentifierNameSyntax identifier ? identifier.Identifier.Text
                         : null;
                if (text == "nint" || text == "nuint")
                    return true;
            }
            return false;
        }

        private static TypeSyntax? declaredTypeSyntax(SyntaxNode declaration)
        {
            return declaration switch
            {
                ParameterSyntax parameter => parameter.Type,
                MethodDeclarationSyntax method => method.ReturnType,
                PropertyDeclarationSyntax property => property.Type,
                DelegateDeclarationSyntax @delegate => @delegate.ReturnType,
                VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax variableDeclaration } => variableDeclaration.Type,
                _ => null,
            };
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // B — declarations and inferred types (type-based, via type-name syntax)
        // ──────────────────────────────────────────────────────────────────────────────────

        private static void analyzeTypeName(SyntaxNodeAnalysisContext nodeContext, UntypedTypeClassifier classifier)
        {
            var nameSyntax = (SimpleNameSyntax)nodeContext.Node;

            // Either side of a member access is not a declaration: the name-part (x.Foo) is a member, and the
            // expression-part of a static access (GCHandle.Alloc) is a lookup that holds no value. The member
            // reference itself is judged as a source or sink (A).
            if (nameSyntax.Parent is MemberAccessExpressionSyntax)
                return;

            // AN0100 owns the literal IntPtr / UIntPtr tokens (and nint / nuint in P/Invoke); do not double-report.
            if (nameSyntax is IdentifierNameSyntax identifierName && isIntPtrFamilyToken(identifierName.Identifier.Text))
                return;

            // A generic that is untyped only because of a literally spelled IntPtr / void* argument (Span<IntPtr>)
            // is also AN0100's: the token is right there. Span<nint> is integers.
            if (nameSyntax is GenericNameSyntax && spellsIntPtrOrVoidPointer(nameSyntax))
                return;

            var effectiveSeverity = getEffectiveSeverity(nodeContext.Options, nameSyntax.SyntaxTree);
            if (effectiveSeverity == null)
                return;

            ITypeSymbol? typeSymbol;
            if (nameSyntax is IdentifierNameSyntax { IsVar: true })
            {
                // Inferred type: `var h = File.OpenHandle(...)` — the declaration holds an untyped value.
                typeSymbol = nodeContext.SemanticModel.GetTypeInfo(nameSyntax, nodeContext.CancellationToken).Type;

                // `var n = a + b` on native ints is arithmetic; `var p = Marshal.AllocHGlobal(16)` is already reported
                // at the invocation (source). IntPtr/nint are the same symbol on .NET 7+, so B stays out of it.
                if (typeSymbol != null && isIntPtrFamily(typeSymbol))
                    return;
            }
            else
            {
                typeSymbol = nodeContext.SemanticModel.GetSymbolInfo(nameSyntax, nodeContext.CancellationToken).Symbol as ITypeSymbol;
            }

            if (typeSymbol == null || typeSymbol.TypeKind == TypeKind.Error)
                return;

            var info = classifier.Classify(typeSymbol);
            if (!info.IsUntyped)
                return;

            reportDiagnostic(nodeContext.ReportDiagnostic, typeRule, nameSyntax.GetLocation(), effectiveSeverity.Value,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), info.Reason, info.SuggestedMarker);
        }

        private static bool spellsIntPtrOrVoidPointer(SyntaxNode node)
        {
            foreach (var descendant in node.DescendantNodes())
            {
                if (descendant is IdentifierNameSyntax id && isIntPtrFamilyToken(id.Identifier.Text))
                    return true;
                if (descendant is PointerTypeSyntax { ElementType: PredefinedTypeSyntax pre } && pre.Keyword.IsKind(SyntaxKind.VoidKeyword))
                    return true;
            }
            return false;
        }

        private static bool isIntPtrFamilyToken(string text) =>
            text == "IntPtr" || text == "UIntPtr" || text == "nint" || text == "nuint";

        private static bool isIntPtrFamily(ITypeSymbol type) =>
            type.SpecialType == SpecialType.System_IntPtr || type.SpecialType == SpecialType.System_UIntPtr;

        // ──────────────────────────────────────────────────────────────────────────────────
        // Reporting and configuration
        // ──────────────────────────────────────────────────────────────────────────────────

        private static void reportDiagnostic(
            System.Action<Diagnostic> report,
            DiagnosticDescriptor rule,
            Location location,
            DiagnosticSeverity effectiveSeverity,
            params object[] messageArguments)
        {
            var effectiveRule = new DiagnosticDescriptor(
                rule.Id, rule.Title, rule.MessageFormat, rule.Category,
                effectiveSeverity, rule.IsEnabledByDefault, helpLinkUri: rule.HelpLinkUri);

            report(Diagnostic.Create(effectiveRule, location, messageArguments));
        }

        /// <summary>null = ignore; otherwise Warning (warn, default) or Error (disallow).</summary>
        private static DiagnosticSeverity? getEffectiveSeverity(AnalyzerOptions options, SyntaxTree syntaxTree)
        {
            var analyzerConfigOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            var enforcementLevel = "warn";
            if (analyzerConfigOptions.TryGetValue("build_property." + BuildPropertyName, out var configValue) &&
                !string.IsNullOrEmpty(configValue))
            {
                enforcementLevel = configValue.ToLowerInvariant();
            }

            return enforcementLevel switch
            {
                "ignore" => null,
                "disallow" => DiagnosticSeverity.Error,
                _ => DiagnosticSeverity.Warning,
            };
        }
    }

    /// <summary>Result of classifying a type. Kind is "handle" (SafeHandle family, costume structs) or "pointer" (IntPtr, void*).</summary>
    internal readonly struct UntypedTypeInfo
    {
        public static readonly UntypedTypeInfo Typed = new UntypedTypeInfo(false, false, string.Empty, string.Empty);

        public readonly bool IsUntyped;
        public readonly bool IsHandle;
        public readonly string Reason;
        public readonly string SuggestedMarker;

        public UntypedTypeInfo(bool isUntyped, bool isHandle, string reason, string suggestedMarker)
        {
            IsUntyped = isUntyped;
            IsHandle = isHandle;
            Reason = reason;
            SuggestedMarker = suggestedMarker;
        }

        public static UntypedTypeInfo Handle(string reason, string marker) => new UntypedTypeInfo(true, true, reason, marker);
        public static UntypedTypeInfo Pointer(string reason) => new UntypedTypeInfo(true, false, reason, "HANDLE_NAME");
    }

    /// <summary>
    /// Decides whether a type IS an untyped native pointer. Memoized per compilation; the costume-struct
    /// rule walks metadata fields and must not be recomputed per reference.
    /// </summary>
    internal sealed class UntypedTypeClassifier
    {
        private readonly ConcurrentDictionary<ITypeSymbol, UntypedTypeInfo> memo =
            new ConcurrentDictionary<ITypeSymbol, UntypedTypeInfo>(SymbolEqualityComparer.Default);

        private readonly INamedTypeSymbol? safeHandleType;
        private readonly INamedTypeSymbol? criticalHandleType;

        public UntypedTypeClassifier(Compilation compilation)
        {
            safeHandleType = compilation.GetTypeByMetadataName("System.Runtime.InteropServices.SafeHandle");
            criticalHandleType = compilation.GetTypeByMetadataName("System.Runtime.InteropServices.CriticalHandle");
        }

        public UntypedTypeInfo Classify(ITypeSymbol type)
        {
            if (memo.TryGetValue(type, out var cached))
                return cached;

            var result = classifyCore(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
            memo[type] = result;
            return result;
        }

        private UntypedTypeInfo classifyCore(ITypeSymbol type, HashSet<ITypeSymbol> visiting)
        {
            if (memo.TryGetValue(type, out var cached))
                return cached;

            // Cycle guard: struct Node { Node* next; } — a type currently being classified is assumed typed.
            if (!visiting.Add(type))
                return UntypedTypeInfo.Typed;

            try
            {
                switch (type)
                {
                    case IPointerTypeSymbol pointer:
                        if (pointer.PointedAtType.SpecialType == SpecialType.System_Void)
                            return UntypedTypeInfo.Pointer("void* says \"points at something\" and refuses to say what");
                        return classifyCore(pointer.PointedAtType, visiting);

                    case IArrayTypeSymbol array:
                        return classifyCore(array.ElementType, visiting);

                    case INamedTypeSymbol named:
                        return classifyNamed(named, visiting);

                    default:
                        return UntypedTypeInfo.Typed;
                }
            }
            finally
            {
                visiting.Remove(type);
            }
        }

        private UntypedTypeInfo classifyNamed(INamedTypeSymbol named, HashSet<ITypeSymbol> visiting)
        {
            // 1. IntPtr / UIntPtr.
            //    On .NET 7+ the compiler UNIFIES nint with System.IntPtr (RuntimeFeature.NumericIntPtr): the symbol
            //    is the same and IsNativeIntegerType is true for both, so metadata cannot tell Marshal.AllocHGlobal's
            //    IntPtr from Unsafe.Add's nint. A native-int-sized value crossing into code we cannot see is treated
            //    as a pointer. For symbols declared in OUR source the spelling is available and `nint`/`nuint` are
            //    exempted by the caller (see ProhibitReachableUntypedNativePointersAnalyzer.isSpelledNativeInt).
            if (named.SpecialType == SpecialType.System_IntPtr || named.SpecialType == SpecialType.System_UIntPtr)
            {
                var spelled = named.SpecialType == SpecialType.System_IntPtr ? "IntPtr" : "UIntPtr";
                return UntypedTypeInfo.Pointer(spelled + " is a pointer-sized value that names nothing about what it points at");
            }

            // 5. Wrappers that carry the payload type through: Nullable<T>, Span<T>, ReadOnlySpan<T>, Memory<T>, ReadOnlyMemory<T>.
            if (named.IsGenericType && named.TypeArguments.Length == 1 && isSystemNamespace(named) &&
                (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
                 named.Name == "Span" || named.Name == "ReadOnlySpan" || named.Name == "Memory" || named.Name == "ReadOnlyMemory"))
            {
                return classifyCore(named.TypeArguments[0], visiting);
            }

            // 3. SafeHandle / CriticalHandle and every subclass.
            if (named.TypeKind == TypeKind.Class)
            {
                for (var baseType = named; baseType != null; baseType = baseType.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(baseType, safeHandleType))
                        return UntypedTypeInfo.Handle("derives from SafeHandle = IntPtr + finalizer; new " + named.Name + "(IntPtr, bool) wraps any integer", suggestMarker(named.Name));
                    if (SymbolEqualityComparer.Default.Equals(baseType, criticalHandleType))
                        return UntypedTypeInfo.Handle("derives from CriticalHandle = IntPtr + finalizer", suggestMarker(named.Name));
                }
                return UntypedTypeInfo.Typed;   // classes are never untyped by membership
            }

            // 4. Struct that IS an untyped pointer with a name painted on: IntPtr in a costume.
            //    (a) an instance field of untyped type (struct HWND { IntPtr Value; } in a real assembly);
            //    (b) a public instance property/field of untyped type, or a user-defined conversion to/from one.
            //    Clause (b) is required because the compiler builds against REFERENCE assemblies, where GenAPI
            //    replaces private struct fields with _dummyPrimitive/_dummy — GCHandle's `IntPtr _handle` is
            //    invisible at compile time, but its `explicit operator IntPtr` is not. HandleRef.Handle and
            //    RuntimeTypeHandle.Value are the same shape.
            if (named.TypeKind == TypeKind.Struct)
            {
                foreach (var member in named.GetMembers())
                {
                    if (member.IsStatic && member is not IMethodSymbol { MethodKind: MethodKind.Conversion })
                        continue;

                    switch (member)
                    {
                        case IFieldSymbol field when !field.IsConst:
                            if (classifyCore(field.Type, visiting).IsUntyped)
                                return costume(named, "field '" + field.Name + "' is '" + display(field.Type) + "'");
                            break;

                        case IPropertySymbol property when property.DeclaredAccessibility == Accessibility.Public && !property.IsIndexer:
                            if (classifyCore(property.Type, visiting).IsUntyped)
                                return costume(named, "property '" + property.Name + "' is '" + display(property.Type) + "'");
                            break;

                        case IMethodSymbol { MethodKind: MethodKind.Conversion } conversion when conversion.DeclaredAccessibility == Accessibility.Public:
                            if (classifyCore(conversion.ReturnType, visiting).IsUntyped)
                                return costume(named, "converts to '" + display(conversion.ReturnType) + "'");
                            if (conversion.Parameters.Length == 1 && classifyCore(conversion.Parameters[0].Type, visiting).IsUntyped)
                                return costume(named, "converts from '" + display(conversion.Parameters[0].Type) + "'");
                            break;
                    }
                }
            }

            return UntypedTypeInfo.Typed;
        }

        private static UntypedTypeInfo costume(INamedTypeSymbol named, string why)
        {
            return UntypedTypeInfo.Handle(
                "IntPtr in a costume — " + why + ". The struct must be EMPTY and the pointer IS the handle",
                suggestMarker(named.Name));
        }

        /// <summary>On .NET 7+ IntPtr renders as 'nint' (unified symbol); the message must say what the metadata says.</summary>
        internal static string display(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_IntPtr) return "IntPtr";
            if (type.SpecialType == SpecialType.System_UIntPtr) return "UIntPtr";
            return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        private static bool isSystemNamespace(INamedTypeSymbol named)
        {
            var ns = named.ContainingNamespace;
            return ns != null && ns.Name == "System" && (ns.ContainingNamespace == null || ns.ContainingNamespace.IsGlobalNamespace);
        }

        /// <summary>SafeFileHandle → HFILE, SafeProcessHandle → HPROCESS, HWND → HWND, anything else → HANDLE_NAME.</summary>
        private static string suggestMarker(string typeName)
        {
            if (typeName.StartsWith("Safe", System.StringComparison.Ordinal) && typeName.EndsWith("Handle", System.StringComparison.Ordinal) && typeName.Length > 10)
            {
                var core = typeName.Substring(4, typeName.Length - 10);
                return "H" + core.ToUpperInvariant();
            }

            if (typeName.Length >= 3 && typeName[0] == 'H' && typeName.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '_'))
                return typeName;

            return "HANDLE_NAME";
        }
    }
}