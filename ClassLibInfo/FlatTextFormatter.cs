using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;

namespace AN.CodeAnalyzers.ClassLibInfo
{
    /// <summary>
    /// Formats API dump data as keyword-prefixed flat text.
    /// Each line starts with a keyword: namespace, class, struct, enum, interface,
    /// delegate, method, ctor, prop, field, const, event, val.
    /// Indentation encodes nesting (2 spaces per level).
    /// Doc comments use // before the member they describe.
    /// </summary>
    public static class FlatTextFormatter
    {
        private static readonly ApiDumpSignatureProvider _signatureProvider = new ApiDumpSignatureProvider();

        public static string Format(
            MetadataReader metadataReader,
            Dictionary<string, List<FlatTypeInfo>> typesByNamespace,
            ApiDumpOptions dumpOptions,
            XmlDocCommentReader? docCommentReader = null)
        {
            var outputBuilder = new StringBuilder();

            // Header
            string assemblyName = metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name);
            var assemblyVersion = metadataReader.GetAssemblyDefinition().Version;
            outputBuilder.AppendLine($"// {assemblyName} {assemblyVersion} — {dumpOptions.VisibilityScope} API via SRM");
            outputBuilder.AppendLine("//");

            var sortedNamespaces = new List<string>(typesByNamespace.Keys);
            sortedNamespaces.Sort(StringComparer.Ordinal);

            foreach (string namespaceName in sortedNamespaces) {
                string displayNamespace = string.IsNullOrEmpty(namespaceName) ? "<global>" : namespaceName;
                outputBuilder.AppendLine($"namespace {displayNamespace}");

                var typesInNamespace = typesByNamespace[namespaceName];
                typesInNamespace.Sort((a, b) => string.Compare(a.TypeName, b.TypeName, StringComparison.Ordinal));

                foreach (var typeInfo in typesInNamespace) {
                    emitFlatType(typeInfo, metadataReader, dumpOptions, docCommentReader, outputBuilder, indent: 2);
                }

                outputBuilder.AppendLine();
            }

            return outputBuilder.ToString();
        }

        private static void emitFlatType(
            FlatTypeInfo typeInfo,
            MetadataReader metadataReader,
            ApiDumpOptions dumpOptions,
            XmlDocCommentReader? docCommentReader,
            StringBuilder outputBuilder,
            int indent)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeInfo.Handle);
            string ind = new string(' ', indent);

            string typeKind = getTypeKind(typeDef, metadataReader);
            string displayTypeName = stripGenericArity(typeInfo.TypeName);

            // Add generic parameters to display name
            var genericParams = typeDef.GetGenericParameters();
            if (genericParams.Count > 0) {
                var gpNames = new List<string>();
                foreach (var gpHandle in genericParams) {
                    var gp = metadataReader.GetGenericParameter(gpHandle);
                    gpNames.Add(metadataReader.GetString(gp.Name));
                }
                displayTypeName += "<" + string.Join(", ", gpNames) + ">";
            }

            // Build modifier list
            var modifierList = new List<string>();
            modifierList.Add(getVisibilityLabel(typeDef.Attributes));

            if (typeKind == "class") {
                bool isAbstract = (typeDef.Attributes & TypeAttributes.Abstract) != 0;
                bool isSealed = (typeDef.Attributes & TypeAttributes.Sealed) != 0;
                if (isAbstract && isSealed) modifierList.Add("static");
                else if (isAbstract) modifierList.Add("abstract");
                else if (isSealed) modifierList.Add("sealed");
            }

            if (typeKind == "enum" && hasAttribute(typeDef, metadataReader, "System.FlagsAttribute")) {
                modifierList.Add("Flags");
            }
            if (hasAttribute(typeDef, metadataReader, "System.ObsoleteAttribute")) {
                modifierList.Add("Obsolete");
            }

            // Emit type-level doc comment
            emitFlatDocComment(docCommentReader, XmlDocCommentReader.BuildTypeDocId(typeInfo.NamespaceName, typeInfo.TypeName), dumpOptions, outputBuilder, ind);

            outputBuilder.AppendLine($"{ind}{typeKind} {displayTypeName} [{string.Join(", ", modifierList)}]");

            var typeGenericContext = buildTypeGenericContext(typeDef, metadataReader);
            string memberInd = new string(' ', indent + 2);

            if (typeKind == "enum") {
                emitFlatEnumMembers(typeDef, metadataReader, outputBuilder, memberInd);
            } else {
                emitFlatTypeMembers(typeDef, typeGenericContext, metadataReader, dumpOptions, docCommentReader, typeInfo.NamespaceName, typeInfo.TypeName, outputBuilder, memberInd);
            }
        }

        private static void emitFlatEnumMembers(
            TypeDefinition enumTypeDef,
            MetadataReader metadataReader,
            StringBuilder outputBuilder,
            string memberIndent)
        {
            foreach (var fieldHandle in enumTypeDef.GetFields()) {
                var fieldDef = metadataReader.GetFieldDefinition(fieldHandle);
                string fieldName = metadataReader.GetString(fieldDef.Name);
                if (fieldName == "value__") continue;
                if ((fieldDef.Attributes & FieldAttributes.Literal) == 0) continue;

                var constantHandle = fieldDef.GetDefaultValue();
                if (constantHandle.IsNil) continue;

                var constant = metadataReader.GetConstant(constantHandle);
                object? constantValue = readConstantValue(constant, metadataReader);
                outputBuilder.AppendLine($"{memberIndent}val {fieldName} = {constantValue}");
            }
        }

        private static void emitFlatTypeMembers(
            TypeDefinition typeDef,
            GenericContext typeGenericContext,
            MetadataReader metadataReader,
            ApiDumpOptions dumpOptions,
            XmlDocCommentReader? docCommentReader,
            string namespaceName,
            string typeName,
            StringBuilder outputBuilder,
            string memberIndent)
        {
            // Constructors
            foreach (var methodHandle in typeDef.GetMethods()) {
                var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                if (!memberMatchesScope(methodDef.Attributes, dumpOptions.VisibilityScope)) continue;
                string methodName = metadataReader.GetString(methodDef.Name);
                if (methodName != ".ctor") continue;

                string paramList = buildParamList(methodDef, typeGenericContext, metadataReader);
                outputBuilder.AppendLine($"{memberIndent}ctor({paramList})");
            }

            // Methods
            foreach (var methodHandle in typeDef.GetMethods()) {
                var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                if (!memberMatchesScope(methodDef.Attributes, dumpOptions.VisibilityScope)) continue;

                string methodName = metadataReader.GetString(methodDef.Name);
                if (methodName == ".ctor" || methodName == ".cctor") continue;
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_")
                    || methodName.StartsWith("add_") || methodName.StartsWith("remove_")) continue;
                if (isCompilerGenerated(methodName)) continue;

                var methodGenericContext = buildMethodGenericContext(methodDef, typeGenericContext, metadataReader);
                var decodedSig = methodDef.DecodeSignature(_signatureProvider, methodGenericContext);

                // Add generic method parameters to name
                string displayMethodName = methodName;
                var methodGenericParams = methodDef.GetGenericParameters();
                if (methodGenericParams.Count > 0) {
                    var gpNames = new List<string>();
                    foreach (var gpHandle in methodGenericParams) {
                        var gp = metadataReader.GetGenericParameter(gpHandle);
                        gpNames.Add(metadataReader.GetString(gp.Name));
                    }
                    displayMethodName += "<" + string.Join(", ", gpNames) + ">";
                }

                string paramList = buildParamList(methodDef, methodGenericContext, metadataReader);
                outputBuilder.AppendLine($"{memberIndent}method {decodedSig.ReturnType} {displayMethodName}({paramList})");
            }

            // Properties
            foreach (var propHandle in typeDef.GetProperties()) {
                var propDef = metadataReader.GetPropertyDefinition(propHandle);
                string propName = metadataReader.GetString(propDef.Name);
                var propSig = propDef.DecodeSignature(_signatureProvider, typeGenericContext);

                var accessors = propDef.GetAccessors();
                bool getterInScope = !accessors.Getter.IsNil && memberMatchesScope(
                    metadataReader.GetMethodDefinition(accessors.Getter).Attributes, dumpOptions.VisibilityScope);
                bool setterInScope = !accessors.Setter.IsNil && memberMatchesScope(
                    metadataReader.GetMethodDefinition(accessors.Setter).Attributes, dumpOptions.VisibilityScope);
                if (!getterInScope && !setterInScope) continue;

                string accessorDesc;
                if (getterInScope && setterInScope) accessorDesc = "{ get; set; }";
                else if (getterInScope) accessorDesc = "{ get; }";
                else accessorDesc = "{ set; }";

                outputBuilder.AppendLine($"{memberIndent}prop {propSig.ReturnType} {propName} {accessorDesc}");
            }

            // Fields (non-const)
            foreach (var fieldHandle in typeDef.GetFields()) {
                var fieldDef = metadataReader.GetFieldDefinition(fieldHandle);
                if ((fieldDef.Attributes & FieldAttributes.Literal) != 0) continue;
                if (!fieldMatchesScope(fieldDef, dumpOptions.VisibilityScope)) continue;
                string fieldName = metadataReader.GetString(fieldDef.Name);
                if (fieldName.StartsWith("<")) continue;

                string fieldTypeName = fieldDef.DecodeSignature(_signatureProvider, typeGenericContext);
                bool isStatic = (fieldDef.Attributes & FieldAttributes.Static) != 0;
                bool isReadonly = (fieldDef.Attributes & FieldAttributes.InitOnly) != 0;

                string modifiers = "";
                if (isStatic) modifiers += "static ";
                if (isReadonly) modifiers += "readonly ";

                outputBuilder.AppendLine($"{memberIndent}field {modifiers}{fieldTypeName} {fieldName}");
            }

            // Consts
            foreach (var fieldHandle in typeDef.GetFields()) {
                var fieldDef = metadataReader.GetFieldDefinition(fieldHandle);
                if ((fieldDef.Attributes & FieldAttributes.Literal) == 0) continue;
                if (!fieldMatchesScope(fieldDef, dumpOptions.VisibilityScope)) continue;
                string fieldName = metadataReader.GetString(fieldDef.Name);
                string fieldTypeName = fieldDef.DecodeSignature(_signatureProvider, typeGenericContext);

                var constantHandle = fieldDef.GetDefaultValue();
                if (constantHandle.IsNil) continue;
                var constant = metadataReader.GetConstant(constantHandle);
                string formattedValue = formatConstantValue(constant, metadataReader);

                outputBuilder.AppendLine($"{memberIndent}const {fieldTypeName} {fieldName} = {formattedValue}");
            }

            // Events
            foreach (var eventHandle in typeDef.GetEvents()) {
                var eventDef = metadataReader.GetEventDefinition(eventHandle);
                string eventName = metadataReader.GetString(eventDef.Name);
                var eventAccessors = eventDef.GetAccessors();
                if (eventAccessors.Adder.IsNil) continue;
                if (!memberMatchesScope(metadataReader.GetMethodDefinition(eventAccessors.Adder).Attributes, dumpOptions.VisibilityScope)) continue;

                var adderMethod = metadataReader.GetMethodDefinition(eventAccessors.Adder);
                var adderSig = adderMethod.DecodeSignature(_signatureProvider, typeGenericContext);
                string eventTypeName = adderSig.ParameterTypes.Length > 0 ? adderSig.ParameterTypes[0] : "?";

                outputBuilder.AppendLine($"{memberIndent}event {eventTypeName} {eventName}");
            }
        }

        // ──────────────────────────────────────────────
        // Helpers (duplicated from ApiDumpGenerator to keep FlatTextFormatter self-contained)
        // ──────────────────────────────────────────────

        private static void emitFlatDocComment(
            XmlDocCommentReader? docCommentReader,
            string memberDocId,
            ApiDumpOptions dumpOptions,
            StringBuilder outputBuilder,
            string indentStr)
        {
            if (docCommentReader == null || dumpOptions.DocComments == "none") return;

            string? summaryText = dumpOptions.DocComments == "brief"
                ? docCommentReader.GetBriefSummary(memberDocId)
                : docCommentReader.GetSummary(memberDocId);

            if (!string.IsNullOrEmpty(summaryText)) {
                outputBuilder.AppendLine($"{indentStr}// {summaryText}");
            }
        }

        private static string buildParamList(MethodDefinition methodDef, GenericContext genericContext, MetadataReader metadataReader)
        {
            var decodedSig = methodDef.DecodeSignature(_signatureProvider, genericContext);
            var paramParts = new List<string>();
            foreach (var paramHandle in methodDef.GetParameters()) {
                var paramDef = metadataReader.GetParameter(paramHandle);
                if (paramDef.SequenceNumber == 0) continue;
                string paramName = metadataReader.GetString(paramDef.Name);
                int paramIndex = paramDef.SequenceNumber - 1;
                if (paramIndex < decodedSig.ParameterTypes.Length) {
                    paramParts.Add($"{decodedSig.ParameterTypes[paramIndex]} {paramName}");
                }
            }
            return string.Join(", ", paramParts);
        }

        private static GenericContext buildTypeGenericContext(TypeDefinition typeDef, MetadataReader metadataReader)
        {
            var typeParamNames = ImmutableArray.CreateBuilder<string>();
            foreach (var gpHandle in typeDef.GetGenericParameters()) {
                var gp = metadataReader.GetGenericParameter(gpHandle);
                typeParamNames.Add(metadataReader.GetString(gp.Name));
            }
            return new GenericContext(typeParamNames.ToImmutable(), ImmutableArray<string>.Empty);
        }

        private static GenericContext buildMethodGenericContext(MethodDefinition methodDef, GenericContext typeGenericContext, MetadataReader metadataReader)
        {
            var methodParamNames = ImmutableArray.CreateBuilder<string>();
            foreach (var gpHandle in methodDef.GetGenericParameters()) {
                var gp = metadataReader.GetGenericParameter(gpHandle);
                methodParamNames.Add(metadataReader.GetString(gp.Name));
            }
            if (methodParamNames.Count == 0) return typeGenericContext;
            return new GenericContext(typeGenericContext.TypeParameterNames, methodParamNames.ToImmutable());
        }

        private static string getTypeKind(TypeDefinition typeDef, MetadataReader metadataReader)
        {
            if ((typeDef.Attributes & TypeAttributes.Interface) != 0) return "interface";
            var baseTypeHandle = typeDef.BaseType;
            if (!baseTypeHandle.IsNil) {
                string baseTypeName = getBaseTypeName(baseTypeHandle, metadataReader);
                if (baseTypeName == "System.Enum") return "enum";
                if (baseTypeName == "System.ValueType") return "struct";
                if (baseTypeName == "System.MulticastDelegate") return "delegate";
            }
            return "class";
        }

        private static string getBaseTypeName(EntityHandle baseTypeHandle, MetadataReader metadataReader)
        {
            if (baseTypeHandle.Kind == HandleKind.TypeReference) {
                var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)baseTypeHandle);
                string ns = metadataReader.GetString(typeRef.Namespace);
                string name = metadataReader.GetString(typeRef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
            if (baseTypeHandle.Kind == HandleKind.TypeDefinition) {
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
            switch (visibility) {
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

        private static bool memberMatchesScope(MethodAttributes methodAttributes, string visibilityScope)
        {
            if (visibilityScope == "all") return true;
            return (methodAttributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
        }

        private static bool fieldMatchesScope(FieldDefinition fieldDef, string visibilityScope)
        {
            if (visibilityScope == "all") return true;
            return (fieldDef.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public;
        }

        private static bool hasAttribute(TypeDefinition typeDef, MetadataReader metadataReader, string fullAttributeName)
        {
            foreach (var attrHandle in typeDef.GetCustomAttributes()) {
                var attr = metadataReader.GetCustomAttribute(attrHandle);
                if (attr.Constructor.Kind == HandleKind.MemberReference) {
                    var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                    if (memberRef.Parent.Kind == HandleKind.TypeReference) {
                        var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                        string ns = metadataReader.GetString(typeRef.Namespace);
                        string name = metadataReader.GetString(typeRef.Name);
                        string fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                        if (fullName == fullAttributeName) return true;
                    }
                }
            }
            return false;
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

        private static object? readConstantValue(Constant constant, MetadataReader metadataReader)
        {
            var blobReader = metadataReader.GetBlobReader(constant.Value);
            switch (constant.TypeCode) {
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

    /// <summary>
    /// Type info used by the flat text formatter. Shared between formatters.
    /// </summary>
    public sealed class FlatTypeInfo
    {
        public TypeDefinitionHandle Handle { get; set; }
        public string TypeName { get; set; } = "";
        public string NamespaceName { get; set; } = "";
    }
}