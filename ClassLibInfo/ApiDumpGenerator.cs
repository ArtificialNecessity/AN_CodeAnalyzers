using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace AN.CodeAnalyzers.ClassLibInfo
{
    /// <summary>
    /// Options controlling what the API dump includes.
    /// </summary>
    public sealed class ApiDumpOptions
    {
        /// <summary>"public" for public types only, "all" for all types.</summary>
        public string VisibilityScope { get; set; } = "public";

        /// <summary>"hjson" for structured HJSON, "flat" for keyword-prefixed text.</summary>
        public string OutputFormat { get; set; } = "hjson";

        /// <summary>"none" = no doc comments, "brief" = first ~120 chars, "full" = complete summary.</summary>
        public string DocComments { get; set; } = "brief";
    }

    /// <summary>
    /// Generates a dense HJSON API dump of a compiled assembly's public surface
    /// using System.Reflection.Metadata (SRM). Reads PE metadata directly —
    /// no runtime loading of the assembly.
    ///
    /// Output format: HJSON with namespaces as top-level keys, types as nested objects
    /// with kind/visibility metadata, and members as unquoted string arrays.
    /// </summary>
    public static class ApiDumpGenerator
    {
        private static readonly ApiDumpSignatureProvider _signatureProvider = new ApiDumpSignatureProvider();

        /// <summary>
        /// Generates the API dump as HJSON text from a compiled assembly.
        /// </summary>
        public static string GenerateApiDump(string assemblyFilePath, ApiDumpOptions dumpOptions)
        {
            using var assemblyFileStream = new FileStream(assemblyFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var peReader = new PEReader(assemblyFileStream);
            var metadataReader = peReader.GetMetadataReader();

            // Load XML doc sidecar if doc comments are requested
            XmlDocCommentReader? docCommentReader = null;
            if (dumpOptions.DocComments != "none") {
                docCommentReader = XmlDocCommentReader.TryLoadForAssembly(assemblyFilePath);
            }

            // Collect all types grouped by namespace
            var collectedTypes = collectTypesByNamespace(metadataReader, dumpOptions);

            // Route to flat text formatter if requested
            if (dumpOptions.OutputFormat == "flat") {
                var flatTypes = new Dictionary<string, List<FlatTypeInfo>>(StringComparer.Ordinal);
                foreach (var kvp in collectedTypes) {
                    var flatList = new List<FlatTypeInfo>();
                    foreach (var ct in kvp.Value) {
                        flatList.Add(new FlatTypeInfo { Handle = ct.Handle, TypeName = ct.TypeName, NamespaceName = ct.NamespaceName });
                    }
                    flatTypes[kvp.Key] = flatList;
                }
                return FlatTextFormatter.Format(metadataReader, flatTypes, dumpOptions, docCommentReader);
            }

            // Build HJSON output
            var typesByNamespace = collectedTypes;
            var hjsonBuilder = new StringBuilder();
            hjsonBuilder.AppendLine("{");

            // Header comment
            string assemblyName = metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name);
            var assemblyVersion = metadataReader.GetAssemblyDefinition().Version;
            hjsonBuilder.AppendLine($"  // {assemblyName} {assemblyVersion} — {dumpOptions.VisibilityScope} API via SRM");
            hjsonBuilder.AppendLine();

            // Sort namespaces for stable output
            var sortedNamespaces = typesByNamespace.Keys.OrderBy(ns => ns, StringComparer.Ordinal).ToList();

            foreach (string namespaceName in sortedNamespaces)
            {
                string displayNamespace = string.IsNullOrEmpty(namespaceName) ? "<global>" : namespaceName;
                hjsonBuilder.AppendLine($"  {displayNamespace}: {{");

                var typesInNamespace = typesByNamespace[namespaceName];
                // Sort types by name for stable output
                typesInNamespace.Sort((a, b) => string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal));

                foreach (var typeInfo in typesInNamespace)
                {
                    emitType(typeInfo, metadataReader, dumpOptions, docCommentReader, hjsonBuilder, indent: 4);
                }

                hjsonBuilder.AppendLine("  }");
            }

            hjsonBuilder.AppendLine("}");
            return hjsonBuilder.ToString();
        }

        /// <summary>
        /// Overload that returns List&lt;string&gt; for backward compatibility with placeholder tests.
        /// </summary>
        public static List<string> GenerateApiDump(string assemblyFilePath, string visibilityScope)
        {
            var dumpOptions = new ApiDumpOptions { VisibilityScope = visibilityScope };
            string hjsonOutput = GenerateApiDump(assemblyFilePath, dumpOptions);
            return new List<string>(hjsonOutput.Split(new[] { '\n' }, StringSplitOptions.None));
        }

        // ──────────────────────────────────────────────
        // Type collection
        // ──────────────────────────────────────────────

        private sealed class CollectedTypeInfo
        {
            public TypeDefinitionHandle Handle { get; set; }
            public string TypeName { get; set; } = "";
            public string NamespaceName { get; set; } = "";
        }

        private static Dictionary<string, List<CollectedTypeInfo>> collectTypesByNamespace(
            MetadataReader metadataReader, ApiDumpOptions dumpOptions)
        {
            var typesByNamespace = new Dictionary<string, List<CollectedTypeInfo>>(StringComparer.Ordinal);

            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                string typeName = metadataReader.GetString(typeDef.Name);
                string namespaceName = metadataReader.GetString(typeDef.Namespace);

                // Skip <Module> pseudo-type
                if (typeName == "<Module>") continue;

                // Skip nested types at top level — they'll be emitted inside their parent
                if (typeDef.IsNested) continue;

                // Skip compiler-generated types
                if (isCompilerGenerated(typeName)) continue;

                // Scope filtering
                if (!typeMatchesScope(typeDef, dumpOptions.VisibilityScope, metadataReader)) continue;

                if (!typesByNamespace.TryGetValue(namespaceName, out var typeList))
                {
                    typeList = new List<CollectedTypeInfo>();
                    typesByNamespace[namespaceName] = typeList;
                }

                typeList.Add(new CollectedTypeInfo
                {
                    Handle = typeDefHandle,
                    TypeName = typeName,
                    NamespaceName = namespaceName
                });
            }

            return typesByNamespace;
        }

        // ──────────────────────────────────────────────
        // Type emission
        // ──────────────────────────────────────────────

        private static void emitType(
            CollectedTypeInfo typeInfo,
            MetadataReader metadataReader,
            ApiDumpOptions dumpOptions,
            XmlDocCommentReader? docCommentReader,
            StringBuilder hjsonBuilder,
            int indent)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeInfo.Handle);
            string indentStr = new string(' ', indent);

            string typeKind = getTypeKind(typeDef, metadataReader);
            string visibilityLabel = getVisibilityLabel(typeDef.Attributes);
            string displayTypeName = stripGenericArity(typeInfo.TypeName);

            // Build type header: TypeName: { kind: ..., vis: ..., [modifiers]
            var typeHeaderParts = new List<string>();
            typeHeaderParts.Add($"kind: {typeKind}");
            typeHeaderParts.Add($"vis: {visibilityLabel}");

            // Add modifiers
            if (typeKind == "class")
            {
                bool isAbstract = (typeDef.Attributes & TypeAttributes.Abstract) != 0;
                bool isSealed = (typeDef.Attributes & TypeAttributes.Sealed) != 0;
                if (isAbstract && isSealed)
                {
                    typeHeaderParts.Add("static: true");
                }
                else if (isAbstract)
                {
                    typeHeaderParts.Add("abstract: true");
                }
                else if (isSealed)
                {
                    typeHeaderParts.Add("sealed: true");
                }
            }

            // Check for [Flags] on enums
            if (typeKind == "enum" && hasAttribute(typeDef, metadataReader, "System.FlagsAttribute"))
            {
                typeHeaderParts.Add("flags: true");
            }

            // Check for [Obsolete]
            if (hasAttribute(typeDef, metadataReader, "System.ObsoleteAttribute"))
            {
                typeHeaderParts.Add("obsolete: true");
            }

            // Add generic parameters to display name
            var genericParams = typeDef.GetGenericParameters();
            if (genericParams.Count > 0)
            {
                var genericParamNames = new List<string>();
                foreach (var gpHandle in genericParams)
                {
                    var gp = metadataReader.GetGenericParameter(gpHandle);
                    genericParamNames.Add(metadataReader.GetString(gp.Name));
                }
                displayTypeName += "<" + string.Join(", ", genericParamNames) + ">";
            }

            // Build generic context for this type
            var typeGenericContext = buildTypeGenericContext(typeDef, metadataReader);

            // Emit type-level doc comment
            string typeDocId = XmlDocCommentReader.BuildTypeDocId(typeInfo.NamespaceName, typeInfo.TypeName);
            emitDocComment(docCommentReader, typeDocId, dumpOptions, hjsonBuilder, indentStr);

            hjsonBuilder.AppendLine($"{indentStr}{displayTypeName}: {{ {string.Join(", ", typeHeaderParts)}");

            // Emit members based on type kind
            if (typeKind == "enum")
            {
                emitEnumMembers(typeDef, metadataReader, hjsonBuilder, indent + 2);
            }
            else
            {
                emitTypeMembers(typeDef, typeGenericContext, metadataReader, dumpOptions, docCommentReader, typeInfo.NamespaceName, typeInfo.TypeName, hjsonBuilder, indent + 2);
            }

            hjsonBuilder.AppendLine($"{indentStr}}}");
        }

        // ──────────────────────────────────────────────
        // Enum members
        // ──────────────────────────────────────────────

        private static void emitEnumMembers(
            TypeDefinition enumTypeDef,
            MetadataReader metadataReader,
            StringBuilder hjsonBuilder,
            int indent)
        {
            string indentStr = new string(' ', indent);
            var enumValues = new List<string>();

            foreach (var fieldHandle in enumTypeDef.GetFields())
            {
                var fieldDef = metadataReader.GetFieldDefinition(fieldHandle);
                string fieldName = metadataReader.GetString(fieldDef.Name);

                // Skip the special "value__" field
                if (fieldName == "value__") continue;

                // Must be literal (const)
                if ((fieldDef.Attributes & FieldAttributes.Literal) == 0) continue;

                var constantHandle = fieldDef.GetDefaultValue();
                if (constantHandle.IsNil) continue;

                var constant = metadataReader.GetConstant(constantHandle);
                object? constantValue = readConstantValue(constant, metadataReader);
                enumValues.Add($"{fieldName}: {constantValue}");
            }

            if (enumValues.Count > 0)
            {
                hjsonBuilder.AppendLine($"{indentStr}values: {{ {string.Join(", ", enumValues)} }}");
            }
        }

        // ──────────────────────────────────────────────
        // Type members (methods, properties, fields, events)
        // ──────────────────────────────────────────────

        private static void emitTypeMembers(
            TypeDefinition typeDef,
            GenericContext typeGenericContext,
            MetadataReader metadataReader,
            ApiDumpOptions dumpOptions,
            XmlDocCommentReader? docCommentReader,
            string namespaceName,
            string typeName,
            StringBuilder hjsonBuilder,
            int indent)
        {
            string ind = new string(' ', indent);
            string ind2 = new string(' ', indent + 2);

            // ── Constructors (array of objects) ──
            var ctorObjects = new List<string>();
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                if (!memberMatchesScope(methodDef.Attributes, dumpOptions.VisibilityScope)) continue;
                string methodName = metadataReader.GetString(methodDef.Name);
                if (methodName != ".ctor") continue;

                string argsHjson = buildArgsHjson(methodDef, typeGenericContext, metadataReader);
                ctorObjects.Add(string.IsNullOrEmpty(argsHjson)
                    ? "{ }"
                    : $"{{ args: {{ {argsHjson} }} }}");
            }

            if (ctorObjects.Count > 0)
            {
                hjsonBuilder.AppendLine($"{ind}ctors: [");
                foreach (string ctorObj in ctorObjects)
                {
                    hjsonBuilder.AppendLine($"{ind2}{ctorObj}");
                }
                hjsonBuilder.AppendLine($"{ind}]");
            }

            // ── Methods (object keyed by name, each value is array of overloads) ──
            var methodOverloads = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                if (!memberMatchesScope(methodDef.Attributes, dumpOptions.VisibilityScope)) continue;

                string methodName = metadataReader.GetString(methodDef.Name);
                if (methodName == ".ctor" || methodName == ".cctor") continue;
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_")
                    || methodName.StartsWith("add_") || methodName.StartsWith("remove_")) continue;
                if (isCompilerGenerated(methodName)) continue;

                var methodGenericContext = buildMethodGenericContext(methodDef, typeGenericContext, metadataReader);
                var decodedSig = methodDef.DecodeSignature(_signatureProvider, methodGenericContext);

                string argsHjson = buildArgsHjson(methodDef, methodGenericContext, metadataReader);

                // Build overload object parts
                var overloadParts = new List<string>();
                overloadParts.Add($"rtn: {hjsonSafeValue(decodedSig.ReturnType)}");

                // Generic type parameters
                var methodGenericParams = methodDef.GetGenericParameters();
                if (methodGenericParams.Count > 0)
                {
                    var gpNames = new List<string>();
                    foreach (var gpHandle in methodGenericParams)
                    {
                        var gp = metadataReader.GetGenericParameter(gpHandle);
                        gpNames.Add(metadataReader.GetString(gp.Name));
                    }
                    if (gpNames.Count == 1)
                    {
                        overloadParts.Add($"tparam: {gpNames[0]}");
                    }
                    else
                    {
                        overloadParts.Add($"tparam: [{string.Join(", ", gpNames)}]");
                    }
                }

                if (!string.IsNullOrEmpty(argsHjson))
                {
                    overloadParts.Add($"args: {{ {argsHjson} }}");
                }

                string overloadObj = "{ " + string.Join(", ", overloadParts) + " }";

                if (!methodOverloads.TryGetValue(methodName, out var overloadList))
                {
                    overloadList = new List<string>();
                    methodOverloads[methodName] = overloadList;
                }
                overloadList.Add(overloadObj);
            }

            if (methodOverloads.Count > 0)
            {
                hjsonBuilder.AppendLine($"{ind}methods: {{");
                foreach (var kvp in methodOverloads.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    if (kvp.Value.Count == 1)
                    {
                        hjsonBuilder.AppendLine($"{ind2}{kvp.Key}: [{kvp.Value[0]}]");
                    }
                    else
                    {
                        hjsonBuilder.AppendLine($"{ind2}{kvp.Key}: [");
                        foreach (string overloadObj in kvp.Value)
                        {
                            hjsonBuilder.AppendLine($"{ind2}  {overloadObj}");
                        }
                        hjsonBuilder.AppendLine($"{ind2}]");
                    }
                }
                hjsonBuilder.AppendLine($"{ind}}}");
            }

            // ── Properties (object keyed by name) ──
            var propertyEntries = new List<string>();
            foreach (var propHandle in typeDef.GetProperties())
            {
                var propDef = metadataReader.GetPropertyDefinition(propHandle);
                string propName = metadataReader.GetString(propDef.Name);
                var propSig = propDef.DecodeSignature(_signatureProvider, typeGenericContext);

                var accessors = propDef.GetAccessors();
                bool getterInScope = !accessors.Getter.IsNil && memberMatchesScope(
                    metadataReader.GetMethodDefinition(accessors.Getter).Attributes, dumpOptions.VisibilityScope);
                bool setterInScope = !accessors.Setter.IsNil && memberMatchesScope(
                    metadataReader.GetMethodDefinition(accessors.Setter).Attributes, dumpOptions.VisibilityScope);
                if (!getterInScope && !setterInScope) continue;

                var propParts = new List<string>();
                propParts.Add($"type: {hjsonSafeValue(propSig.ReturnType)}");
                if (getterInScope) propParts.Add("get: true");
                if (setterInScope) propParts.Add("set: true");

                propertyEntries.Add($"{ind2}{propName}: {{ {string.Join(", ", propParts)} }}");
            }

            if (propertyEntries.Count > 0)
            {
                hjsonBuilder.AppendLine($"{ind}properties: {{");
                foreach (string propEntry in propertyEntries)
                {
                    hjsonBuilder.AppendLine(propEntry);
                }
                hjsonBuilder.AppendLine($"{ind}}}");
            }

            // ── Fields (object keyed by name) ──
            var fieldEntries = new List<string>();
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var fieldDef = metadataReader.GetFieldDefinition(fieldHandle);
                if ((fieldDef.Attributes & FieldAttributes.Literal) != 0) continue;
                if (!fieldMatchesScope(fieldDef, dumpOptions.VisibilityScope)) continue;
                string fieldName = metadataReader.GetString(fieldDef.Name);
                if (fieldName.StartsWith("<")) continue;

                string fieldTypeName = fieldDef.DecodeSignature(_signatureProvider, typeGenericContext);
                var fieldParts = new List<string>();
                fieldParts.Add($"type: {hjsonSafeValue(fieldTypeName)}");
                if ((fieldDef.Attributes & FieldAttributes.Static) != 0) fieldParts.Add("static: true");
                if ((fieldDef.Attributes & FieldAttributes.InitOnly) != 0) fieldParts.Add("readonly: true");

                fieldEntries.Add($"{ind2}{fieldName}: {{ {string.Join(", ", fieldParts)} }}");
            }

            if (fieldEntries.Count > 0)
            {
                hjsonBuilder.AppendLine($"{ind}fields: {{");
                foreach (string fieldEntry in fieldEntries)
                {
                    hjsonBuilder.AppendLine(fieldEntry);
                }
                hjsonBuilder.AppendLine($"{ind}}}");
            }

            // ── Consts (object keyed by name) ──
            var constEntries = new List<string>();
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var fieldDef = metadataReader.GetFieldDefinition(fieldHandle);
                if ((fieldDef.Attributes & FieldAttributes.Literal) == 0) continue;
                if (!fieldMatchesScope(fieldDef, dumpOptions.VisibilityScope)) continue;
                string fieldName = metadataReader.GetString(fieldDef.Name);
                string fieldTypeName = fieldDef.DecodeSignature(_signatureProvider, typeGenericContext);

                var constantHandle = fieldDef.GetDefaultValue();
                if (constantHandle.IsNil) continue;
                var constant = metadataReader.GetConstant(constantHandle);
                string formattedValue = formatConstantValue(constant, metadataReader);

                constEntries.Add($"{ind2}{fieldName}: {{ type: {hjsonSafeValue(fieldTypeName)}, value: {formattedValue} }}");
            }

            if (constEntries.Count > 0)
            {
                hjsonBuilder.AppendLine($"{ind}consts: {{");
                foreach (string constEntry in constEntries)
                {
                    hjsonBuilder.AppendLine(constEntry);
                }
                hjsonBuilder.AppendLine($"{ind}}}");
            }

            // ── Events (object keyed by name) ──
            var eventEntries = new List<string>();
            foreach (var eventHandle in typeDef.GetEvents())
            {
                var eventDef = metadataReader.GetEventDefinition(eventHandle);
                string eventName = metadataReader.GetString(eventDef.Name);
                var eventAccessors = eventDef.GetAccessors();
                if (eventAccessors.Adder.IsNil) continue;
                if (!memberMatchesScope(metadataReader.GetMethodDefinition(eventAccessors.Adder).Attributes, dumpOptions.VisibilityScope)) continue;

                var adderMethod = metadataReader.GetMethodDefinition(eventAccessors.Adder);
                var adderSig = adderMethod.DecodeSignature(_signatureProvider, typeGenericContext);
                string eventTypeName = adderSig.ParameterTypes.Length > 0 ? adderSig.ParameterTypes[0] : "?";

                eventEntries.Add($"{ind2}{eventName}: {{ type: {hjsonSafeValue(eventTypeName)} }}");
            }

            if (eventEntries.Count > 0)
            {
                hjsonBuilder.AppendLine($"{ind}events: {{");
                foreach (string eventEntry in eventEntries)
                {
                    hjsonBuilder.AppendLine(eventEntry);
                }
                hjsonBuilder.AppendLine($"{ind}}}");
            }
        }

        /// <summary>
        /// Builds the HJSON args object content: "paramName: paramType, paramName2: paramType2"
        /// </summary>
        private static string buildArgsHjson(
            MethodDefinition methodDef,
            GenericContext genericContext,
            MetadataReader metadataReader)
        {
            var decodedSig = methodDef.DecodeSignature(_signatureProvider, genericContext);
            var argParts = new List<string>();

            foreach (var paramHandle in methodDef.GetParameters())
            {
                var paramDef = metadataReader.GetParameter(paramHandle);
                if (paramDef.SequenceNumber == 0) continue;
                string paramName = metadataReader.GetString(paramDef.Name);
                int paramIndex = paramDef.SequenceNumber - 1;
                if (paramIndex < decodedSig.ParameterTypes.Length)
                {
                    argParts.Add($"{paramName}: {hjsonSafeValue(decodedSig.ParameterTypes[paramIndex])}");
                }
            }

            return string.Join(", ", argParts);
        }

        // ──────────────────────────────────────────────
        // Generic context building
        // ──────────────────────────────────────────────

        private static GenericContext buildTypeGenericContext(TypeDefinition typeDef, MetadataReader metadataReader)
        {
            var typeParamNames = ImmutableArray.CreateBuilder<string>();
            foreach (var gpHandle in typeDef.GetGenericParameters())
            {
                var gp = metadataReader.GetGenericParameter(gpHandle);
                typeParamNames.Add(metadataReader.GetString(gp.Name));
            }
            return new GenericContext(typeParamNames.ToImmutable(), ImmutableArray<string>.Empty);
        }

        private static GenericContext buildMethodGenericContext(
            MethodDefinition methodDef,
            GenericContext typeGenericContext,
            MetadataReader metadataReader)
        {
            var methodParamNames = ImmutableArray.CreateBuilder<string>();
            foreach (var gpHandle in methodDef.GetGenericParameters())
            {
                var gp = metadataReader.GetGenericParameter(gpHandle);
                methodParamNames.Add(metadataReader.GetString(gp.Name));
            }

            if (methodParamNames.Count == 0)
            {
                return typeGenericContext;
            }

            return new GenericContext(typeGenericContext.TypeParameterNames, methodParamNames.ToImmutable());
        }

        // ──────────────────────────────────────────────
        // Scope filtering
        // ──────────────────────────────────────────────

        private static bool typeMatchesScope(TypeDefinition typeDef, string visibilityScope, MetadataReader metadataReader)
        {
            if (visibilityScope == "all") return true;
            return isTypePubliclyVisible(typeDef, metadataReader);
        }

        private static bool isTypePubliclyVisible(TypeDefinition typeDef, MetadataReader metadataReader)
        {
            var visibility = typeDef.Attributes & TypeAttributes.VisibilityMask;

            if (typeDef.IsNested)
            {
                if (visibility != TypeAttributes.NestedPublic) return false;
                var declaringTypeHandle = typeDef.GetDeclaringType();
                var declaringTypeDef = metadataReader.GetTypeDefinition(declaringTypeHandle);
                return isTypePubliclyVisible(declaringTypeDef, metadataReader);
            }

            return visibility == TypeAttributes.Public;
        }

        private static bool memberMatchesScope(MethodAttributes methodAttributes, string visibilityScope)
        {
            if (visibilityScope == "all") return true;
            var accessibility = methodAttributes & MethodAttributes.MemberAccessMask;
            return accessibility == MethodAttributes.Public;
        }

        private static bool fieldMatchesScope(FieldDefinition fieldDef, string visibilityScope)
        {
            if (visibilityScope == "all") return true;
            var accessibility = fieldDef.Attributes & FieldAttributes.FieldAccessMask;
            return accessibility == FieldAttributes.Public;
        }

        // ──────────────────────────────────────────────
        // Type classification
        // ──────────────────────────────────────────────

        private static string getTypeKind(TypeDefinition typeDef, MetadataReader metadataReader)
        {
            if ((typeDef.Attributes & TypeAttributes.Interface) != 0) return "interface";

            var baseTypeHandle = typeDef.BaseType;
            if (!baseTypeHandle.IsNil)
            {
                string baseTypeName = getBaseTypeName(baseTypeHandle, metadataReader);
                if (baseTypeName == "System.Enum") return "enum";
                if (baseTypeName == "System.ValueType") return "struct";
                if (baseTypeName == "System.MulticastDelegate") return "delegate";
            }

            return "class";
        }

        private static string getBaseTypeName(EntityHandle baseTypeHandle, MetadataReader metadataReader)
        {
            if (baseTypeHandle.Kind == HandleKind.TypeReference)
            {
                var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)baseTypeHandle);
                string ns = metadataReader.GetString(typeRef.Namespace);
                string name = metadataReader.GetString(typeRef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
            if (baseTypeHandle.Kind == HandleKind.TypeDefinition)
            {
                var baseTypeDef = metadataReader.GetTypeDefinition((TypeDefinitionHandle)baseTypeHandle);
                string ns = metadataReader.GetString(baseTypeDef.Namespace);
                string name = metadataReader.GetString(baseTypeDef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
            return "";
        }

        private static string getVisibilityLabel(TypeAttributes typeAttributes)
        {
            var visibility = typeAttributes & TypeAttributes.VisibilityMask;
            switch (visibility)
            {
                case TypeAttributes.Public: return "public";
                case TypeAttributes.NotPublic: return "internal";
                case TypeAttributes.NestedPublic: return "public";
                case TypeAttributes.NestedPrivate: return "private";
                case TypeAttributes.NestedFamily: return "protected";
                case TypeAttributes.NestedAssembly: return "internal";
                case TypeAttributes.NestedFamORAssem: return "protected internal";
                case TypeAttributes.NestedFamANDAssem: return "private protected";
                default: return "internal";
            }
        }

        // ──────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Wraps a type name in double quotes if it contains characters that would
        /// break HJSON unquoted value parsing (commas from generic type parameters
        /// like Dictionary&lt;TKey, TValue&gt;, or braces/brackets).
        /// Simple types like "string", "int", "List&lt;T&gt;" stay unquoted.
        /// </summary>
        private static string hjsonSafeValue(string typeName)
        {
            if (typeName.IndexOfAny(new[] { ',', '{', '}', '[', ']' }) >= 0)
            {
                return "\"" + typeName.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
            return typeName;
        }

        /// <summary>
        /// Emits a // doc comment line if a summary exists for the given member ID.
        /// </summary>
        private static void emitDocComment(
            XmlDocCommentReader? docCommentReader,
            string memberDocId,
            ApiDumpOptions dumpOptions,
            StringBuilder hjsonBuilder,
            string indentStr)
        {
            if (docCommentReader == null || dumpOptions.DocComments == "none") return;

            string? summaryText = dumpOptions.DocComments == "brief"
                ? docCommentReader.GetBriefSummary(memberDocId)
                : docCommentReader.GetSummary(memberDocId);

            if (!string.IsNullOrEmpty(summaryText)) {
                hjsonBuilder.AppendLine($"{indentStr}// {summaryText}");
            }
        }

        private static bool isCompilerGenerated(string name)
        {
            return name.StartsWith("<") || name.Contains("<>c") || name.Contains("__");
        }

        private static string stripGenericArity(string typeName)
        {
            int backtickIndex = typeName.IndexOf('`');
            if (backtickIndex >= 0) return typeName.Substring(0, backtickIndex);
            return typeName;
        }

        private static bool hasAttribute(TypeDefinition typeDef, MetadataReader metadataReader, string fullAttributeName)
        {
            foreach (var attrHandle in typeDef.GetCustomAttributes())
            {
                var attr = metadataReader.GetCustomAttribute(attrHandle);
                if (getCustomAttributeName(attr, metadataReader) == fullAttributeName) return true;
            }
            return false;
        }

        private static string getCustomAttributeName(CustomAttribute customAttribute, MetadataReader metadataReader)
        {
            if (customAttribute.Constructor.Kind == HandleKind.MemberReference)
            {
                var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
                if (memberRef.Parent.Kind == HandleKind.TypeReference)
                {
                    var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                    string ns = metadataReader.GetString(typeRef.Namespace);
                    string name = metadataReader.GetString(typeRef.Name);
                    return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                }
            }
            return "";
        }

        private static object? readConstantValue(Constant constant, MetadataReader metadataReader)
        {
            var blobReader = metadataReader.GetBlobReader(constant.Value);
            switch (constant.TypeCode)
            {
                case ConstantTypeCode.Boolean: return blobReader.ReadBoolean();
                case ConstantTypeCode.Char: return blobReader.ReadChar();
                case ConstantTypeCode.SByte: return blobReader.ReadSByte();
                case ConstantTypeCode.Byte: return blobReader.ReadByte();
                case ConstantTypeCode.Int16: return blobReader.ReadInt16();
                case ConstantTypeCode.UInt16: return blobReader.ReadUInt16();
                case ConstantTypeCode.Int32: return blobReader.ReadInt32();
                case ConstantTypeCode.UInt32: return blobReader.ReadUInt32();
                case ConstantTypeCode.Int64: return blobReader.ReadInt64();
                case ConstantTypeCode.UInt64: return blobReader.ReadUInt64();
                case ConstantTypeCode.Single: return blobReader.ReadSingle();
                case ConstantTypeCode.Double: return blobReader.ReadDouble();
                case ConstantTypeCode.String: return blobReader.ReadUTF16(blobReader.Length);
                case ConstantTypeCode.NullReference: return null;
                default: return null;
            }
        }

        private static string formatConstantValue(Constant constant, MetadataReader metadataReader)
        {
            object? value = readConstantValue(constant, metadataReader);
            if (value == null) return "null";
            if (value is string stringVal) return "\"" + stringVal.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            if (value is bool boolVal) return boolVal ? "true" : "false";
            if (value is char charVal) return "'" + charVal + "'";
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                   ?? value.ToString() ?? "?";
        }
    }
}