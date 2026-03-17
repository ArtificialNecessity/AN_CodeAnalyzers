using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace AN.CodeAnalyzers.StableABIVerification
{
    /// <summary>
    /// Generates a .stableapi snapshot by reading a compiled assembly using
    /// System.Reflection.Metadata. Extracts the public API surface and ABI-relevant
    /// facts: type declarations, method signatures, properties, events, fields,
    /// const values, enum members, default parameters, and struct layouts.
    /// Output is a sorted, deterministic, one-fact-per-line text format.
    ///
    /// NOTE: P/Invoke signatures are NOT tracked here. This tool verifies the
    /// consumed external public API surface. P/Invoke verification (native function
    /// signatures, DLL bindings) is handled by a separate analyzer.
    /// </summary>
    public static class StableABISnapshotGenerator
    {
        /// <summary>
        /// Current snapshot format version.
        /// Version 1: enums, consts, default params, struct layouts, P/Invoke signatures.
        /// Version 2: adds type declarations, method signatures, properties, events, public fields.
        /// </summary>
        public const int CurrentFormatVersion = 2;

        /// <summary>
        /// Generates the full snapshot content from a compiled assembly.
        /// </summary>
        /// <param name="assemblyFilePath">Path to the compiled DLL.</param>
        /// <param name="snapshotScope">"public" for public types only, "all" for all types.</param>
        /// <returns>Sorted snapshot content string, one fact per line, newline-terminated.</returns>
        public static string GenerateSnapshot(string assemblyFilePath, string snapshotScope)
        {
            var snapshotLines = new List<string>();

            using var fileStream = new FileStream(assemblyFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var peReader = new PEReader(fileStream);
            var metadataReader = peReader.GetMetadataReader();

            foreach (var typeDefinitionHandle in metadataReader.TypeDefinitions)
            {
                var typeDefinition = metadataReader.GetTypeDefinition(typeDefinitionHandle);
                string typeName = metadataReader.GetString(typeDefinition.Name);
                string typeNamespace = metadataReader.GetString(typeDefinition.Namespace);

                // Skip the <Module> pseudo-type
                if (typeName == "<Module>")
                {
                    continue;
                }

                if (!typeMatchesScope(typeDefinition, snapshotScope, metadataReader))
                {
                    continue;
                }

                string typeDisplayName = buildTypeDisplayName(typeName, typeNamespace, typeDefinition, metadataReader);

                // Emit type declaration
                collectTypeFact(typeDefinition, typeDisplayName, metadataReader, snapshotLines);

                if (isEnum(typeDefinition, metadataReader))
                {
                    collectEnumFacts(typeDefinition, typeDisplayName, metadataReader, snapshotLines);
                }
                else if (isStruct(typeDefinition, metadataReader))
                {
                    collectStructFacts(typeDefinition, typeDisplayName, metadataReader, snapshotLines);
                    collectConstFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectFieldFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectMethodFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectPropertyFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectEventFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectDefaultParameterFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                }
                else
                {
                    collectConstFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectFieldFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectMethodFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectPropertyFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectEventFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                    collectDefaultParameterFacts(typeDefinition, typeDisplayName, snapshotScope, metadataReader, snapshotLines);
                }
            }

            snapshotLines.Sort(StringComparer.Ordinal);

            if (snapshotLines.Count == 0)
            {
                return "";
            }

            // Prepend version header (not sorted — always first line)
            return $"__stableApiVersion: {CurrentFormatVersion}\n" + string.Join("\n", snapshotLines) + "\n";
        }

        // ──────────────────────────────────────────────
        // Type classification helpers
        // ──────────────────────────────────────────────

        private static bool isEnum(TypeDefinition typeDefinition, MetadataReader metadataReader)
        {
            var baseTypeHandle = typeDefinition.BaseType;
            if (baseTypeHandle.IsNil)
            {
                return false;
            }

            string baseTypeName = getBaseTypeName(baseTypeHandle, metadataReader);
            return baseTypeName == "System.Enum";
        }

        private static bool isStruct(TypeDefinition typeDefinition, MetadataReader metadataReader)
        {
            if ((typeDefinition.Attributes & TypeAttributes.Interface) != 0)
            {
                return false;
            }

            var baseTypeHandle = typeDefinition.BaseType;
            if (baseTypeHandle.IsNil)
            {
                return false;
            }

            string baseTypeName = getBaseTypeName(baseTypeHandle, metadataReader);
            return baseTypeName == "System.ValueType" && !isEnum(typeDefinition, metadataReader);
        }

        private static string getBaseTypeName(EntityHandle baseTypeHandle, MetadataReader metadataReader)
        {
            if (baseTypeHandle.Kind == HandleKind.TypeReference)
            {
                var typeReference = metadataReader.GetTypeReference((TypeReferenceHandle)baseTypeHandle);
                string baseNamespace = metadataReader.GetString(typeReference.Namespace);
                string baseName = metadataReader.GetString(typeReference.Name);
                return string.IsNullOrEmpty(baseNamespace) ? baseName : $"{baseNamespace}.{baseName}";
            }

            if (baseTypeHandle.Kind == HandleKind.TypeDefinition)
            {
                var baseTypeDef = metadataReader.GetTypeDefinition((TypeDefinitionHandle)baseTypeHandle);
                string baseNamespace = metadataReader.GetString(baseTypeDef.Namespace);
                string baseName = metadataReader.GetString(baseTypeDef.Name);
                return string.IsNullOrEmpty(baseNamespace) ? baseName : $"{baseNamespace}.{baseName}";
            }

            return "";
        }

        // ──────────────────────────────────────────────
        // Scope filtering
        // ──────────────────────────────────────────────

        private static bool typeMatchesScope(TypeDefinition typeDefinition, string snapshotScope, MetadataReader metadataReader)
        {
            if (snapshotScope == "all") {
                return true;
            }

            return isTypePubliclyVisible(typeDefinition, metadataReader);
        }

        /// <summary>
        /// Checks whether a type is publicly visible to assembly consumers.
        /// For nested types, walks the entire declaring type chain — a NestedPublic type
        /// inside an internal class is NOT publicly visible.
        /// </summary>
        private static bool isTypePubliclyVisible(TypeDefinition typeDefinition, MetadataReader metadataReader)
        {
            var visibility = typeDefinition.Attributes & TypeAttributes.VisibilityMask;

            if (typeDefinition.IsNested) {
                // Nested type must be NestedPublic
                if (visibility != TypeAttributes.NestedPublic) {
                    return false;
                }
                // Walk up: declaring type must also be publicly visible
                var declaringTypeHandle = typeDefinition.GetDeclaringType();
                var declaringTypeDefinition = metadataReader.GetTypeDefinition(declaringTypeHandle);
                return isTypePubliclyVisible(declaringTypeDefinition, metadataReader);
            }

            return visibility == TypeAttributes.Public;
        }

        private static bool fieldMatchesScope(FieldDefinition fieldDefinition, string snapshotScope)
        {
            if (snapshotScope == "all")
            {
                return true;
            }

            var accessibility = fieldDefinition.Attributes & FieldAttributes.FieldAccessMask;
            return accessibility == FieldAttributes.Public;
        }

        private static bool methodMatchesScope(MethodDefinition methodDefinition, string snapshotScope)
        {
            if (snapshotScope == "all")
            {
                return true;
            }

            var accessibility = methodDefinition.Attributes & MethodAttributes.MemberAccessMask;
            return accessibility == MethodAttributes.Public;
        }

        // ──────────────────────────────────────────────
        // Type display name
        // ──────────────────────────────────────────────

        private static string buildTypeDisplayName(
            string typeName,
            string typeNamespace,
            TypeDefinition typeDefinition,
            MetadataReader metadataReader)
        {
            // For nested types, prepend the declaring type name
            if (typeDefinition.IsNested)
            {
                var declaringTypeHandle = typeDefinition.GetDeclaringType();
                var declaringType = metadataReader.GetTypeDefinition(declaringTypeHandle);
                string declaringTypeName = metadataReader.GetString(declaringType.Name);
                string declaringTypeNamespace = metadataReader.GetString(declaringType.Namespace);
                string parentDisplayName = buildTypeDisplayName(
                    declaringTypeName, declaringTypeNamespace, declaringType, metadataReader);
                return $"{parentDisplayName}.{typeName}";
            }

            // For top-level types, just use the type name (namespace omitted per spec)
            return typeName;
        }

        // ──────────────────────────────────────────────
        // Enum facts
        // ──────────────────────────────────────────────

        private static void collectEnumFacts(
            TypeDefinition enumTypeDefinition,
            string typeDisplayName,
            MetadataReader metadataReader,
            List<string> snapshotLines)
        {
            string underlyingTypeName = "int"; // default
            bool hasFlagsAttribute = false;

            // Check custom attributes for [Flags]
            foreach (var customAttributeHandle in enumTypeDefinition.GetCustomAttributes())
            {
                var customAttribute = metadataReader.GetCustomAttribute(customAttributeHandle);
                string attributeName = getCustomAttributeName(customAttribute, metadataReader);
                if (attributeName == "System.FlagsAttribute")
                {
                    hasFlagsAttribute = true;
                }
            }

            // Find the underlying type from the special "value__" field
            foreach (var fieldHandle in enumTypeDefinition.GetFields())
            {
                var fieldDefinition = metadataReader.GetFieldDefinition(fieldHandle);
                string fieldName = metadataReader.GetString(fieldDefinition.Name);

                if (fieldName == "value__")
                {
                    underlyingTypeName = decodeFieldTypeName(fieldDefinition, metadataReader);
                    continue;
                }

                // Skip non-literal fields
                if ((fieldDefinition.Attributes & FieldAttributes.Literal) == 0)
                {
                    continue;
                }

                // Read the constant value
                var constantHandle = fieldDefinition.GetDefaultValue();
                if (constantHandle.IsNil)
                {
                    continue;
                }

                var constant = metadataReader.GetConstant(constantHandle);
                object? constantValue = readConstantValue(constant, metadataReader);
                snapshotLines.Add($"enum.{typeDisplayName}.{fieldName}: {constantValue}");
            }

            snapshotLines.Add($"enum.{typeDisplayName}._type: {underlyingTypeName}");

            if (hasFlagsAttribute)
            {
                snapshotLines.Add($"enum.{typeDisplayName}._flags: true");
            }
        }

        // ──────────────────────────────────────────────
        // Const facts
        // ──────────────────────────────────────────────

        private static void collectConstFacts(
            TypeDefinition containingTypeDefinition,
            string typeDisplayName,
            string snapshotScope,
            MetadataReader metadataReader,
            List<string> snapshotLines)
        {
            foreach (var fieldHandle in containingTypeDefinition.GetFields())
            {
                var fieldDefinition = metadataReader.GetFieldDefinition(fieldHandle);

                // Must be literal (const) and static
                if ((fieldDefinition.Attributes & FieldAttributes.Literal) == 0)
                {
                    continue;
                }

                if (!fieldMatchesScope(fieldDefinition, snapshotScope))
                {
                    continue;
                }

                string fieldName = metadataReader.GetString(fieldDefinition.Name);
                string fieldTypeName = decodeFieldTypeName(fieldDefinition, metadataReader);

                var constantHandle = fieldDefinition.GetDefaultValue();
                if (constantHandle.IsNil)
                {
                    continue;
                }

                var constant = metadataReader.GetConstant(constantHandle);
                string formattedValue = formatConstantValue(constant, metadataReader);
                snapshotLines.Add($"const.{typeDisplayName}.{fieldName}: {fieldTypeName} {formattedValue}");
            }
        }

        // ──────────────────────────────────────────────
        // Default parameter facts
        // ──────────────────────────────────────────────

        private static void collectDefaultParameterFacts(
            TypeDefinition containingTypeDefinition,
            string typeDisplayName,
            string snapshotScope,
            MetadataReader metadataReader,
            List<string> snapshotLines)
        {
            foreach (var methodHandle in containingTypeDefinition.GetMethods())
            {
                var methodDefinition = metadataReader.GetMethodDefinition(methodHandle);

                // Skip property accessors, event accessors
                string methodName = metadataReader.GetString(methodDefinition.Name);
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_")
                    || methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
                {
                    continue;
                }

                if (!methodMatchesScope(methodDefinition, snapshotScope))
                {
                    continue;
                }

                foreach (var parameterHandle in methodDefinition.GetParameters())
                {
                    var parameterDefinition = metadataReader.GetParameter(parameterHandle);

                    // Skip return value parameter (sequence number 0)
                    if (parameterDefinition.SequenceNumber == 0)
                    {
                        continue;
                    }

                    if ((parameterDefinition.Attributes & ParameterAttributes.HasDefault) == 0)
                    {
                        continue;
                    }

                    var defaultValueHandle = parameterDefinition.GetDefaultValue();
                    if (defaultValueHandle.IsNil)
                    {
                        continue;
                    }

                    string parameterName = metadataReader.GetString(parameterDefinition.Name);
                    var defaultConstant = metadataReader.GetConstant(defaultValueHandle);
                    string paramTypeName = getTypeKeywordFromConstantType(defaultConstant.TypeCode);
                    string formattedDefaultValue = formatConstantValue(defaultConstant, metadataReader);

                    snapshotLines.Add(
                        $"default.{typeDisplayName}.{methodName}.{parameterName}: {paramTypeName} {formattedDefaultValue}");
                }
            }
        }

        // ──────────────────────────────────────────────
        // Struct facts
        // ──────────────────────────────────────────────

        private static void collectStructFacts(
            TypeDefinition structTypeDefinition,
            string typeDisplayName,
            MetadataReader metadataReader,
            List<string> snapshotLines)
        {
            var structLayout = structTypeDefinition.GetLayout();
            string layoutKindName = "Sequential"; // C# struct default

            // Check StructLayout attribute
            var layoutAttributes = structTypeDefinition.Attributes & TypeAttributes.LayoutMask;
            if (layoutAttributes == TypeAttributes.ExplicitLayout)
            {
                layoutKindName = "Explicit";
            }
            else if (layoutAttributes == TypeAttributes.AutoLayout)
            {
                layoutKindName = "Auto";
            }

            int packValue = structLayout.PackingSize;
            int sizeValue = structLayout.Size;

            snapshotLines.Add($"struct.{typeDisplayName}._layout: {layoutKindName}");
            snapshotLines.Add($"struct.{typeDisplayName}._pack: {packValue}");
            snapshotLines.Add($"struct.{typeDisplayName}._size: {sizeValue}");

            // Collect instance fields in metadata order
            int fieldOrdinal = 0;
            foreach (var fieldHandle in structTypeDefinition.GetFields())
            {
                var fieldDefinition = metadataReader.GetFieldDefinition(fieldHandle);

                // Skip static and const fields
                if ((fieldDefinition.Attributes & FieldAttributes.Static) != 0)
                {
                    continue;
                }

                string fieldName = metadataReader.GetString(fieldDefinition.Name);
                string fieldTypeName = decodeFieldTypeName(fieldDefinition, metadataReader);

                // Get explicit offset if available
                int fieldOffset = fieldDefinition.GetOffset();
                string offsetSuffix = fieldOffset >= 0 ? $" @{fieldOffset}" : "";

                snapshotLines.Add(
                    $"struct.{typeDisplayName}.field{fieldOrdinal}.{fieldName}: {fieldTypeName}{offsetSuffix}");
                fieldOrdinal++;
            }
        }

        // ──────────────────────────────────────────────
        // Type declaration fact
        // ──────────────────────────────────────────────

        private static void collectTypeFact(
            TypeDefinition typeDefinition,
            string typeDisplayName,
            MetadataReader metadataReader,
            List<string> snapshotLines)
        {
            string typeKindLabel;
            if ((typeDefinition.Attributes & TypeAttributes.Interface) != 0)
            {
                typeKindLabel = "interface";
            }
            else if (isEnum(typeDefinition, metadataReader))
            {
                typeKindLabel = "enum";
            }
            else if (isStruct(typeDefinition, metadataReader))
            {
                typeKindLabel = "struct";
            }
            else if (isDelegate(typeDefinition, metadataReader))
            {
                typeKindLabel = "delegate";
            }
            else
            {
                bool isAbstract = (typeDefinition.Attributes & TypeAttributes.Abstract) != 0;
                bool isSealed = (typeDefinition.Attributes & TypeAttributes.Sealed) != 0;
                if (isAbstract && isSealed)
                {
                    typeKindLabel = "static class";
                }
                else if (isAbstract)
                {
                    typeKindLabel = "abstract class";
                }
                else if (isSealed)
                {
                    typeKindLabel = "sealed class";
                }
                else
                {
                    typeKindLabel = "class";
                }
            }

            snapshotLines.Add($"type.{typeDisplayName}: {typeKindLabel}");
        }

        private static bool isDelegate(TypeDefinition typeDefinition, MetadataReader metadataReader)
        {
            var baseTypeHandle = typeDefinition.BaseType;
            if (baseTypeHandle.IsNil)
            {
                return false;
            }
            string baseTypeName = getBaseTypeName(baseTypeHandle, metadataReader);
            return baseTypeName == "System.MulticastDelegate";
        }

        // ──────────────────────────────────────────────
        // Method facts (full signatures)
        // ──────────────────────────────────────────────

        private static void collectMethodFacts(
            TypeDefinition containingTypeDefinition,
            string typeDisplayName,
            string snapshotScope,
            MetadataReader metadataReader,
            List<string> snapshotLines)
        {
            foreach (var methodHandle in containingTypeDefinition.GetMethods())
            {
                var methodDefinition = metadataReader.GetMethodDefinition(methodHandle);

                if (!methodMatchesScope(methodDefinition, snapshotScope))
                {
                    continue;
                }

                string methodName = metadataReader.GetString(methodDefinition.Name);

                // Skip property/event accessors (they're covered by property/event facts)
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_")
                    || methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
                {
                    continue;
                }

                // Skip compiler-generated methods
                if (methodName == ".cctor")
                {
                    continue;
                }

                var signatureDecoder = methodDefinition.DecodeSignature(new SignatureTypeNameProvider(), null);
                string returnTypeName = signatureDecoder.ReturnType;

                // Build parameter list with names
                var parameterNames = new List<string>();
                foreach (var parameterHandle in methodDefinition.GetParameters())
                {
                    var parameterDefinition = metadataReader.GetParameter(parameterHandle);
                    if (parameterDefinition.SequenceNumber == 0)
                    {
                        continue; // skip return value
                    }
                    string paramName = metadataReader.GetString(parameterDefinition.Name);
                    int paramIndex = parameterDefinition.SequenceNumber - 1;
                    if (paramIndex < signatureDecoder.ParameterTypes.Length)
                    {
                        parameterNames.Add($"{signatureDecoder.ParameterTypes[paramIndex]} {paramName}");
                    }
                }

                string parameterList = string.Join(", ", parameterNames);
                string displayMethodName = methodName == ".ctor" ? ".ctor" : methodName;

                snapshotLines.Add($"method.{typeDisplayName}.{displayMethodName}: {returnTypeName}({parameterList})");
            }
        }

        // ──────────────────────────────────────────────
        // Property facts
        // ──────────────────────────────────────────────

        private static void collectPropertyFacts(
            TypeDefinition containingTypeDefinition,
            string typeDisplayName,
            string snapshotScope,
            MetadataReader metadataReader,
            List<string> snapshotLines)
        {
            foreach (var propertyHandle in containingTypeDefinition.GetProperties())
            {
                var propertyDefinition = metadataReader.GetPropertyDefinition(propertyHandle);
                string propertyName = metadataReader.GetString(propertyDefinition.Name);

                var propertySignature = propertyDefinition.DecodeSignature(new SignatureTypeNameProvider(), null);
                string propertyTypeName = propertySignature.ReturnType;

                var accessors = propertyDefinition.GetAccessors();
                bool hasGetter = !accessors.Getter.IsNil;
                bool hasSetter = !accessors.Setter.IsNil;

                // Check if at least one accessor is in scope
                bool getterInScope = hasGetter && methodMatchesScope(metadataReader.GetMethodDefinition(accessors.Getter), snapshotScope);
                bool setterInScope = hasSetter && methodMatchesScope(metadataReader.GetMethodDefinition(accessors.Setter), snapshotScope);

                if (!getterInScope && !setterInScope)
                {
                    continue;
                }

                string accessorDescription;
                if (getterInScope && setterInScope)
                {
                    accessorDescription = "{ get; set; }";
                }
                else if (getterInScope)
                {
                    accessorDescription = "{ get; }";
                }
                else
                {
                    accessorDescription = "{ set; }";
                }

                snapshotLines.Add($"property.{typeDisplayName}.{propertyName}: {propertyTypeName} {accessorDescription}");
            }
        }

        // ──────────────────────────────────────────────
        // Event facts
        // ──────────────────────────────────────────────

        private static void collectEventFacts(
            TypeDefinition containingTypeDefinition,
            string typeDisplayName,
            string snapshotScope,
            MetadataReader metadataReader,
            List<string> snapshotLines)
        {
            foreach (var eventHandle in containingTypeDefinition.GetEvents())
            {
                var eventDefinition = metadataReader.GetEventDefinition(eventHandle);
                string eventName = metadataReader.GetString(eventDefinition.Name);

                var eventAccessors = eventDefinition.GetAccessors();
                bool adderInScope = !eventAccessors.Adder.IsNil
                    && methodMatchesScope(metadataReader.GetMethodDefinition(eventAccessors.Adder), snapshotScope);

                if (!adderInScope)
                {
                    continue;
                }

                // Decode event type from the adder's parameter
                string eventTypeName = "?";
                if (!eventAccessors.Adder.IsNil)
                {
                    var adderMethod = metadataReader.GetMethodDefinition(eventAccessors.Adder);
                    var adderSignature = adderMethod.DecodeSignature(new SignatureTypeNameProvider(), null);
                    if (adderSignature.ParameterTypes.Length > 0)
                    {
                        eventTypeName = adderSignature.ParameterTypes[0];
                    }
                }

                snapshotLines.Add($"event.{typeDisplayName}.{eventName}: {eventTypeName}");
            }
        }

        // ──────────────────────────────────────────────
        // Public field facts (non-const, non-struct-instance)
        // ──────────────────────────────────────────────

        private static void collectFieldFacts(
            TypeDefinition containingTypeDefinition,
            string typeDisplayName,
            string snapshotScope,
            MetadataReader metadataReader,
            List<string> snapshotLines)
        {
            foreach (var fieldHandle in containingTypeDefinition.GetFields())
            {
                var fieldDefinition = metadataReader.GetFieldDefinition(fieldHandle);

                // Skip const fields (handled by collectConstFacts)
                if ((fieldDefinition.Attributes & FieldAttributes.Literal) != 0)
                {
                    continue;
                }

                if (!fieldMatchesScope(fieldDefinition, snapshotScope))
                {
                    continue;
                }

                string fieldName = metadataReader.GetString(fieldDefinition.Name);
                string fieldTypeName = decodeFieldTypeName(fieldDefinition, metadataReader);

                bool isStatic = (fieldDefinition.Attributes & FieldAttributes.Static) != 0;
                bool isReadonly = (fieldDefinition.Attributes & FieldAttributes.InitOnly) != 0;

                string modifiers = "";
                if (isStatic && isReadonly)
                {
                    modifiers = "static readonly ";
                }
                else if (isStatic)
                {
                    modifiers = "static ";
                }
                else if (isReadonly)
                {
                    modifiers = "readonly ";
                }

                snapshotLines.Add($"field.{typeDisplayName}.{fieldName}: {modifiers}{fieldTypeName}");
            }
        }

        // ──────────────────────────────────────────────
        // Metadata reading helpers
        // ──────────────────────────────────────────────

        private static string getCustomAttributeName(CustomAttribute customAttribute, MetadataReader metadataReader)
        {
            if (customAttribute.Constructor.Kind == HandleKind.MemberReference)
            {
                var memberReference = metadataReader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
                if (memberReference.Parent.Kind == HandleKind.TypeReference)
                {
                    var typeReference = metadataReader.GetTypeReference((TypeReferenceHandle)memberReference.Parent);
                    string attrNamespace = metadataReader.GetString(typeReference.Namespace);
                    string attrName = metadataReader.GetString(typeReference.Name);
                    return string.IsNullOrEmpty(attrNamespace) ? attrName : $"{attrNamespace}.{attrName}";
                }
            }
            return "";
        }

        private static string decodeFieldTypeName(FieldDefinition fieldDefinition, MetadataReader metadataReader)
        {
            var signatureDecoder = fieldDefinition.DecodeSignature(new SignatureTypeNameProvider(), null);
            return signatureDecoder;
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
            object? constantValue = readConstantValue(constant, metadataReader);

            if (constantValue == null)
            {
                return "null";
            }

            if (constantValue is string stringValue)
            {
                return "\"" + stringValue.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }

            if (constantValue is bool boolValue)
            {
                return boolValue ? "true" : "false";
            }

            if (constantValue is char charValue)
            {
                return "'" + charValue + "'";
            }

            return Convert.ToString(constantValue, System.Globalization.CultureInfo.InvariantCulture)
                   ?? constantValue.ToString()
                   ?? "?";
        }

        private static string getTypeKeywordFromConstantType(ConstantTypeCode typeCode)
        {
            switch (typeCode)
            {
                case ConstantTypeCode.Boolean: return "bool";
                case ConstantTypeCode.Char: return "char";
                case ConstantTypeCode.SByte: return "sbyte";
                case ConstantTypeCode.Byte: return "byte";
                case ConstantTypeCode.Int16: return "short";
                case ConstantTypeCode.UInt16: return "ushort";
                case ConstantTypeCode.Int32: return "int";
                case ConstantTypeCode.UInt32: return "uint";
                case ConstantTypeCode.Int64: return "long";
                case ConstantTypeCode.UInt64: return "ulong";
                case ConstantTypeCode.Single: return "float";
                case ConstantTypeCode.Double: return "double";
                case ConstantTypeCode.String: return "string";
                case ConstantTypeCode.NullReference: return "object";
                default: return "unknown";
            }
        }

        /// <summary>
        /// Minimal ISignatureTypeProvider that decodes type signatures to C# keyword names.
        /// </summary>
        private sealed class SignatureTypeNameProvider : ISignatureTypeProvider<string, object?>
        {
            public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            {
                switch (typeCode)
                {
                    case PrimitiveTypeCode.Boolean: return "bool";
                    case PrimitiveTypeCode.Byte: return "byte";
                    case PrimitiveTypeCode.SByte: return "sbyte";
                    case PrimitiveTypeCode.Int16: return "short";
                    case PrimitiveTypeCode.UInt16: return "ushort";
                    case PrimitiveTypeCode.Int32: return "int";
                    case PrimitiveTypeCode.UInt32: return "uint";
                    case PrimitiveTypeCode.Int64: return "long";
                    case PrimitiveTypeCode.UInt64: return "ulong";
                    case PrimitiveTypeCode.Single: return "float";
                    case PrimitiveTypeCode.Double: return "double";
                    case PrimitiveTypeCode.Char: return "char";
                    case PrimitiveTypeCode.String: return "string";
                    case PrimitiveTypeCode.Object: return "object";
                    case PrimitiveTypeCode.IntPtr: return "IntPtr";
                    case PrimitiveTypeCode.UIntPtr: return "UIntPtr";
                    case PrimitiveTypeCode.Void: return "void";
                    default: return typeCode.ToString();
                }
            }

            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                var typeDef = reader.GetTypeDefinition(handle);
                return reader.GetString(typeDef.Name);
            }

            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                var typeRef = reader.GetTypeReference(handle);
                return reader.GetString(typeRef.Name);
            }

            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
            public string GetByReferenceType(string elementType) => "ref " + elementType;
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments)
                => genericType + "<" + string.Join(", ", typeArguments) + ">";
            public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
            public string GetPinnedType(string elementType) => elementType;
            public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            {
                var typeSpec = reader.GetTypeSpecification(handle);
                return typeSpec.DecodeSignature(this, genericContext);
            }
            public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        }
    }
}