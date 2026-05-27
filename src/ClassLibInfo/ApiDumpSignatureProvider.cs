using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace AN.CodeAnalyzers.ClassLibInfo
{
    /// <summary>
    /// Carries generic parameter names for both the containing type and the current method,
    /// so the signature decoder can resolve generic parameter indices to actual names
    /// (e.g. index 0 → "T", index 1 → "TValue") instead of raw "!0", "!!0".
    /// </summary>
    public sealed class GenericContext
    {
        /// <summary>
        /// Generic parameter names for the containing type (e.g. ["TKey", "TValue"] for Dictionary).
        /// Indexed by GenericTypeParameter index.
        /// </summary>
        public ImmutableArray<string> TypeParameterNames { get; }

        /// <summary>
        /// Generic parameter names for the current method (e.g. ["TResult"] for Select&lt;TResult&gt;).
        /// Indexed by GenericMethodParameter index.
        /// </summary>
        public ImmutableArray<string> MethodParameterNames { get; }

        public GenericContext(ImmutableArray<string> typeParameterNames, ImmutableArray<string> methodParameterNames)
        {
            TypeParameterNames = typeParameterNames;
            MethodParameterNames = methodParameterNames;
        }

        public static GenericContext Empty { get; } = new GenericContext(
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty);
    }

    /// <summary>
    /// ISignatureTypeProvider that decodes SRM type signatures into C# display strings.
    /// Resolves generic parameters to their actual names using the provided GenericContext.
    /// Produces namespace-qualified names for cross-assembly type references.
    /// </summary>
    public sealed class ApiDumpSignatureProvider : ISignatureTypeProvider<string, GenericContext>
    {
        public string GetPrimitiveType(PrimitiveTypeCode primitiveTypeCode)
        {
            switch (primitiveTypeCode) {
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
                case PrimitiveTypeCode.TypedReference: return "TypedReference";
                default: return primitiveTypeCode.ToString();
            }
        }

        public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle typeDefHandle, byte rawTypeKind)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            string typeName = metadataReader.GetString(typeDef.Name);

            // Strip generic arity suffix (e.g. "Dictionary`2" → "Dictionary")
            typeName = stripGenericAritySuffix(typeName);

            // For nested types, prepend the declaring type
            if (typeDef.IsNested) {
                var declaringTypeHandle = typeDef.GetDeclaringType();
                string declaringTypeName = GetTypeFromDefinition(metadataReader, declaringTypeHandle, 0);
                return declaringTypeName + "." + typeName;
            }

            // Include namespace for top-level types
            string typeNamespace = metadataReader.GetString(typeDef.Namespace);
            if (!string.IsNullOrEmpty(typeNamespace)) {
                return typeNamespace + "." + typeName;
            }

            return typeName;
        }

        public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle typeRefHandle, byte rawTypeKind)
        {
            var typeRef = metadataReader.GetTypeReference(typeRefHandle);
            string typeName = metadataReader.GetString(typeRef.Name);

            // Strip generic arity suffix
            typeName = stripGenericAritySuffix(typeName);

            // Include namespace
            string typeNamespace = metadataReader.GetString(typeRef.Namespace);
            if (!string.IsNullOrEmpty(typeNamespace)) {
                return typeNamespace + "." + typeName;
            }

            return typeName;
        }

        public string GetTypeFromSpecification(MetadataReader metadataReader, GenericContext genericContext, TypeSpecificationHandle typeSpecHandle, byte rawTypeKind)
        {
            var typeSpec = metadataReader.GetTypeSpecification(typeSpecHandle);
            return typeSpec.DecodeSignature(this, genericContext);
        }

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetArrayType(string elementType, ArrayShape arrayShape) => elementType + "[]";

        public string GetByReferenceType(string elementType) => "ref " + elementType;

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPinnedType(string elementType) => elementType;

        public string GetGenericInstantiation(string genericTypeName, ImmutableArray<string> typeArguments)
        {
            return genericTypeName + "<" + string.Join(", ", typeArguments) + ">";
        }

        public string GetGenericMethodParameter(GenericContext genericContext, int parameterIndex)
        {
            if (genericContext != null
                && parameterIndex < genericContext.MethodParameterNames.Length) {
                return genericContext.MethodParameterNames[parameterIndex];
            }
            // Fallback: unnamed generic method parameter
            return "!!" + parameterIndex;
        }

        public string GetGenericTypeParameter(GenericContext genericContext, int parameterIndex)
        {
            if (genericContext != null
                && parameterIndex < genericContext.TypeParameterNames.Length) {
                return genericContext.TypeParameterNames[parameterIndex];
            }
            // Fallback: unnamed generic type parameter
            return "!" + parameterIndex;
        }

        public string GetFunctionPointerType(MethodSignature<string> methodSignature) => "delegate*";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        // ──────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Strips the generic arity suffix from a type name.
        /// E.g. "Dictionary`2" → "Dictionary", "List`1" → "List".
        /// Non-generic types are returned unchanged.
        /// </summary>
        private static string stripGenericAritySuffix(string typeName)
        {
            int backtickIndex = typeName.IndexOf('`');
            if (backtickIndex >= 0) {
                return typeName.Substring(0, backtickIndex);
            }
            return typeName;
        }
    }
}