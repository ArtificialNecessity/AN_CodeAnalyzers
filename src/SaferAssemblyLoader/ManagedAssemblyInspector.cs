using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ArtificialNecessity.SaferAssemblyLoader
{
    /// <summary>
    /// Result of inspecting an assembly for managed-only compliance.
    /// </summary>
    internal sealed class AssemblyInspectionResult
    {
        public bool IsManagedOnly => Violations.Count == 0;
        public List<string> Violations { get; } = new List<string>();
    }

    /// <summary>
    /// Inspects .NET assembly PE metadata to detect any unmanaged code surface.
    /// Uses System.Reflection.Metadata to read the PE file WITHOUT loading the assembly.
    /// </summary>
    internal static class ManagedAssemblyInspector
    {
        /// <summary>
        /// Inspect an assembly on disk. Opens the file read-only, never loads it.
        /// </summary>
        public static AssemblyInspectionResult Inspect(string assemblyPath)
        {
            using var assemblyFileStream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var portableExecutableReader = new PEReader(assemblyFileStream);
            return InspectPE(portableExecutableReader);
        }

        /// <summary>
        /// Inspect an assembly from raw bytes. Never loads it.
        /// </summary>
        public static AssemblyInspectionResult Inspect(byte[] rawAssemblyBytes)
        {
            using var assemblyMemoryStream = new MemoryStream(rawAssemblyBytes, writable: false);
            using var portableExecutableReader = new PEReader(assemblyMemoryStream);
            return InspectPE(portableExecutableReader);
        }

        private static AssemblyInspectionResult InspectPE(PEReader portableExecutableReader)
        {
            var inspectionResult = new AssemblyInspectionResult();

            // Check 1: Is this even a managed assembly?
            if (!portableExecutableReader.HasMetadata)
            {
                inspectionResult.Violations.Add("[not-managed] File does not contain .NET metadata");
                return inspectionResult;
            }

            // Check 2: Mixed-mode assembly (ILOnly flag)
            CheckMixedMode(portableExecutableReader, inspectionResult);

            var metadataReader = portableExecutableReader.GetMetadataReader();

            // Check 3: DllImport / PInvokeImpl methods
            CheckPInvokeMethods(metadataReader, inspectionResult);

            // Check 4: LibraryImport attribute on methods
            CheckLibraryImportAttributes(metadataReader, inspectionResult);

            // Check 5: Native/Unmanaged method implementations
            CheckNativeMethodImpls(metadataReader, inspectionResult);

            // Check 6: IntPtr/UIntPtr in field signatures
            CheckIntPtrFields(metadataReader, inspectionResult);

            // Check 7: IntPtr/UIntPtr in method signatures
            CheckIntPtrMethodSignatures(metadataReader, inspectionResult);

            // Check 8: Marshal.* member references
            CheckMarshalCalls(metadataReader, inspectionResult);

            // Check 9: Unsafe IL in method bodies
            CheckUnsafeIL(portableExecutableReader, metadataReader, inspectionResult);

            return inspectionResult;
        }

        // ──────────────────────────────────────────────
        // Check 1: Mixed-mode assembly
        // ──────────────────────────────────────────────

        private static void CheckMixedMode(PEReader portableExecutableReader, AssemblyInspectionResult inspectionResult)
        {
            var corHeader = portableExecutableReader.PEHeaders.CorHeader;
            if (corHeader == null)
            {
                inspectionResult.Violations.Add("[mixed-mode] No COR header found — not a managed assembly");
                return;
            }

            if ((corHeader.Flags & CorFlags.ILOnly) == 0)
            {
                inspectionResult.Violations.Add("[mixed-mode] Assembly is not IL-only (contains native code)");
            }
        }

        // ──────────────────────────────────────────────
        // Check 3: DllImport / PInvokeImpl methods
        // ──────────────────────────────────────────────

        private static void CheckPInvokeMethods(MetadataReader metadataReader, AssemblyInspectionResult inspectionResult)
        {
            foreach (var methodDefinitionHandle in metadataReader.MethodDefinitions)
            {
                var methodDefinition = metadataReader.GetMethodDefinition(methodDefinitionHandle);

                // Check for PInvokeImpl flag
                if ((methodDefinition.Attributes & MethodAttributes.PinvokeImpl) == 0)
                {
                    continue;
                }

                string methodName = metadataReader.GetString(methodDefinition.Name);
                string declaringTypeName = GetDeclaringTypeName(methodDefinition, metadataReader);

                // Try to get the DLL name from the import
                var methodImport = methodDefinition.GetImport();
                string targetDllName = "unknown";
                if (!methodImport.Module.IsNil)
                {
                    var moduleReference = metadataReader.GetModuleReference(methodImport.Module);
                    targetDllName = metadataReader.GetString(moduleReference.Name);
                }

                inspectionResult.Violations.Add($"[DllImport] {declaringTypeName}.{methodName}() -> {targetDllName}");
            }
        }

        // ──────────────────────────────────────────────
        // Check 4: LibraryImport attribute
        // ──────────────────────────────────────────────

        private static void CheckLibraryImportAttributes(MetadataReader metadataReader, AssemblyInspectionResult inspectionResult)
        {
            foreach (var methodDefinitionHandle in metadataReader.MethodDefinitions)
            {
                var methodDefinition = metadataReader.GetMethodDefinition(methodDefinitionHandle);

                // Already caught by PInvokeImpl check — skip if it has that flag
                if ((methodDefinition.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    continue;
                }

                foreach (var customAttributeHandle in methodDefinition.GetCustomAttributes())
                {
                    var customAttribute = metadataReader.GetCustomAttribute(customAttributeHandle);
                    string attributeTypeName = GetCustomAttributeTypeName(customAttribute, metadataReader);

                    if (attributeTypeName == "System.Runtime.InteropServices.LibraryImportAttribute")
                    {
                        string methodName = metadataReader.GetString(methodDefinition.Name);
                        string declaringTypeName = GetDeclaringTypeName(methodDefinition, metadataReader);
                        inspectionResult.Violations.Add($"[LibraryImport] {declaringTypeName}.{methodName}()");
                        break; // one violation per method is enough
                    }
                }
            }
        }

        // ──────────────────────────────────────────────
        // Check 5: Native/Unmanaged method implementations
        // ──────────────────────────────────────────────

        private static void CheckNativeMethodImpls(MetadataReader metadataReader, AssemblyInspectionResult inspectionResult)
        {
            foreach (var methodDefinitionHandle in metadataReader.MethodDefinitions)
            {
                var methodDefinition = metadataReader.GetMethodDefinition(methodDefinitionHandle);
                var implAttributes = methodDefinition.ImplAttributes;

                bool isNative = (implAttributes & MethodImplAttributes.Native) != 0;
                bool isUnmanaged = (implAttributes & MethodImplAttributes.Unmanaged) != 0;

                if (isNative || isUnmanaged)
                {
                    // Skip methods already caught by PInvokeImpl
                    if ((methodDefinition.Attributes & MethodAttributes.PinvokeImpl) != 0)
                    {
                        continue;
                    }

                    string methodName = metadataReader.GetString(methodDefinition.Name);
                    string declaringTypeName = GetDeclaringTypeName(methodDefinition, metadataReader);
                    string flagDescription = isNative && isUnmanaged ? "Native+Unmanaged" : isNative ? "Native" : "Unmanaged";
                    inspectionResult.Violations.Add($"[native method] {declaringTypeName}.{methodName}() [{flagDescription}]");
                }
            }
        }

        // ──────────────────────────────────────────────
        // Check 6: IntPtr/UIntPtr fields
        // ──────────────────────────────────────────────

        private static void CheckIntPtrFields(MetadataReader metadataReader, AssemblyInspectionResult inspectionResult)
        {
            var intPtrDetector = new IntPtrSignatureTypeProvider();

            foreach (var typeDefinitionHandle in metadataReader.TypeDefinitions)
            {
                var typeDefinition = metadataReader.GetTypeDefinition(typeDefinitionHandle);
                string typeName = GetFullTypeName(typeDefinition, metadataReader);

                foreach (var fieldDefinitionHandle in typeDefinition.GetFields())
                {
                    var fieldDefinition = metadataReader.GetFieldDefinition(fieldDefinitionHandle);
                    string fieldName = metadataReader.GetString(fieldDefinition.Name);

                    // Skip compiler-generated backing fields
                    if (fieldName.StartsWith("<"))
                    {
                        continue;
                    }

                    bool fieldTypeContainsIntPtr = fieldDefinition.DecodeSignature(intPtrDetector, null);
                    if (fieldTypeContainsIntPtr)
                    {
                        inspectionResult.Violations.Add($"[IntPtr field] {typeName}.{fieldName}");
                    }
                }
            }
        }

        // ──────────────────────────────────────────────
        // Check 7: IntPtr/UIntPtr in method signatures
        // ──────────────────────────────────────────────

        private static void CheckIntPtrMethodSignatures(MetadataReader metadataReader, AssemblyInspectionResult inspectionResult)
        {
            var intPtrDetector = new IntPtrSignatureTypeProvider();

            foreach (var methodDefinitionHandle in metadataReader.MethodDefinitions)
            {
                var methodDefinition = metadataReader.GetMethodDefinition(methodDefinitionHandle);
                string methodName = metadataReader.GetString(methodDefinition.Name);

                // Skip compiler-generated methods
                if (methodName.StartsWith("<") || methodName == ".cctor")
                {
                    continue;
                }

                // Skip property accessors and event accessors — they'll be caught via their field types
                if (methodName.StartsWith("get_") || methodName.StartsWith("set_")
                    || methodName.StartsWith("add_") || methodName.StartsWith("remove_"))
                {
                    continue;
                }

                // Skip methods already caught by PInvokeImpl (they'll have IntPtr in signatures naturally)
                if ((methodDefinition.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    continue;
                }

                var decodedSignature = methodDefinition.DecodeSignature(intPtrDetector, null);

                bool returnTypeHasIntPtr = decodedSignature.ReturnType;
                bool anyParameterHasIntPtr = false;
                foreach (bool parameterHasIntPtr in decodedSignature.ParameterTypes)
                {
                    if (parameterHasIntPtr)
                    {
                        anyParameterHasIntPtr = true;
                        break;
                    }
                }

                if (returnTypeHasIntPtr || anyParameterHasIntPtr)
                {
                    string declaringTypeName = GetDeclaringTypeName(methodDefinition, metadataReader);
                    inspectionResult.Violations.Add($"[IntPtr signature] {declaringTypeName}.{methodName}()");
                }
            }
        }

        // ──────────────────────────────────────────────
        // Check 8: Marshal.* member references
        // ──────────────────────────────────────────────

        private static void CheckMarshalCalls(MetadataReader metadataReader, AssemblyInspectionResult inspectionResult)
        {
            foreach (var memberReferenceHandle in metadataReader.MemberReferences)
            {
                var memberReference = metadataReader.GetMemberReference(memberReferenceHandle);
                string memberName = metadataReader.GetString(memberReference.Name);

                // Check if the parent type is System.Runtime.InteropServices.Marshal
                if (memberReference.Parent.Kind == HandleKind.TypeReference)
                {
                    var parentTypeReference = metadataReader.GetTypeReference((TypeReferenceHandle)memberReference.Parent);
                    string parentNamespace = metadataReader.GetString(parentTypeReference.Namespace);
                    string parentTypeName = metadataReader.GetString(parentTypeReference.Name);

                    if (parentNamespace == "System.Runtime.InteropServices" && parentTypeName == "Marshal")
                    {
                        inspectionResult.Violations.Add($"[Marshal call] Marshal.{memberName}");
                    }
                }
            }
        }

        // ──────────────────────────────────────────────
        // Check 9: Unsafe IL in method bodies
        // ──────────────────────────────────────────────

        private static void CheckUnsafeIL(PEReader portableExecutableReader, MetadataReader metadataReader, AssemblyInspectionResult inspectionResult)
        {
            foreach (var methodDefinitionHandle in metadataReader.MethodDefinitions)
            {
                var methodDefinition = metadataReader.GetMethodDefinition(methodDefinitionHandle);

                // Skip abstract, extern, PInvoke methods — they have no IL body
                if (methodDefinition.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                var methodBodyBlock = portableExecutableReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
                if (methodBodyBlock == null)
                {
                    continue;
                }

                var ilBytes = methodBodyBlock.GetILBytes();
                if (ilBytes == null || ilBytes.Length == 0)
                {
                    continue;
                }

                if (ContainsUnsafeOpcodes(ilBytes))
                {
                    string methodName = metadataReader.GetString(methodDefinition.Name);

                    // Skip compiler-generated methods
                    if (methodName.StartsWith("<") || methodName == ".cctor")
                    {
                        continue;
                    }

                    string declaringTypeName = GetDeclaringTypeName(methodDefinition, metadataReader);
                    inspectionResult.Violations.Add($"[unsafe IL] {declaringTypeName}.{methodName}()");
                }
            }
        }

        /// <summary>
        /// Scans IL bytes for opcodes that indicate unsafe/pointer operations.
        /// Properly walks the instruction stream, advancing past operand bytes
        /// to avoid false positives from metadata tokens matching opcode values.
        /// </summary>
        private static bool ContainsUnsafeOpcodes(byte[] ilBytes)
        {
            int offset = 0;
            while (offset < ilBytes.Length)
            {
                byte opcodeByte = ilBytes[offset];

                // Two-byte opcodes (0xFE prefix)
                if (opcodeByte == 0xFE)
                {
                    if (offset + 1 >= ilBytes.Length) break;
                    byte secondByte = ilBytes[offset + 1];

                    // Unsafe two-byte opcodes
                    if (secondByte == 0x0F) return true; // localloc
                    if (secondByte == 0x17) return true; // cpblk
                    if (secondByte == 0x18) return true; // initblk

                    offset += 2 + GetTwoByteOpcodeOperandSize(secondByte);
                    continue;
                }

                // Check unsafe single-byte opcodes at this position
                if (IsUnsafeSingleByteOpcode(opcodeByte)) return true;

                // Handle switch instruction specially (variable-length operand)
                if (opcodeByte == 0x45)
                {
                    if (offset + 4 >= ilBytes.Length) break;
                    int switchTargetCount = BitConverter.ToInt32(ilBytes, offset + 1);
                    offset += 1 + 4 + (switchTargetCount * 4);
                    continue;
                }

                // Advance past opcode + its fixed-size operand
                offset += 1 + GetSingleByteOpcodeOperandSize(opcodeByte);
            }

            return false;
        }

        private static bool IsUnsafeSingleByteOpcode(byte opcodeByte)
        {
            // ldind.* (0x46-0x50) — load indirect through pointer
            if (opcodeByte >= 0x46 && opcodeByte <= 0x50) return true;
            // stind.* (0x51-0x56) — store indirect through pointer
            if (opcodeByte >= 0x51 && opcodeByte <= 0x56) return true;
            // cpobj (0x70), ldobj (0x71) — through pointer
            if (opcodeByte == 0x70 || opcodeByte == 0x71) return true;
            // stobj (0x81) — store through pointer
            if (opcodeByte == 0x81) return true;
            return false;
        }

        /// <summary>
        /// Returns operand size in bytes for single-byte opcodes.
        /// </summary>
        private static int GetSingleByteOpcodeOperandSize(byte opcodeByte)
        {
            switch (opcodeByte)
            {
                // 8-byte operand: ldc.i8, ldc.r8
                case 0x21: case 0x23: return 8;
                // 4-byte operand: ldc.i4, ldc.r4, jmp, call, calli, br, brfalse, brtrue,
                // beq, bge, bgt, ble, blt, bne.un, bge.un, bgt.un, ble.un, blt.un,
                // callvirt, cpobj, ldobj, ldstr, newobj, castclass, isinst, unbox,
                // ldfld, ldflda, stfld, ldsfld, ldsflda, stsfld, stobj,
                // box, newarr, ldelema, ldelem, stelem, unbox.any,
                // refanyval, mkrefany, ldtoken, leave
                case 0x20:
                case 0x22: // ldc.i4, ldc.r4
                case 0x27:
                case 0x28:
                case 0x29: // jmp, call, calli
                case 0x38:
                case 0x39:
                case 0x3A:
                case 0x3B:
                case 0x3C:
                case 0x3D:
                case 0x3E:
                case 0x3F: // br..blt
                case 0x40:
                case 0x41:
                case 0x42:
                case 0x43:
                case 0x44: // bne.un..blt.un
                case 0x6F: // callvirt
                case 0x70:
                case 0x71:
                case 0x72:
                case 0x73:
                case 0x74:
                case 0x75: // cpobj..isinst
                case 0x79: // unbox
                case 0x7B:
                case 0x7C:
                case 0x7D:
                case 0x7E:
                case 0x7F:
                case 0x80:
                case 0x81: // ldfld..stobj
                case 0x8C:
                case 0x8D: // box, newarr
                case 0x8F: // ldelema
                case 0xA3:
                case 0xA4:
                case 0xA5: // ldelem, stelem, unbox.any
                case 0xC2:
                case 0xC6: // refanyval, mkrefany
                case 0xD0: // ldtoken
                case 0xDD: // leave
                    return 4;
                // 1-byte operand: ldarg.s, ldarga.s, starg.s, ldloc.s, ldloca.s, stloc.s,
                // ldc.i4.s, br.s, brfalse.s, brtrue.s, beq.s..blt.un.s, leave.s, unaligned.
                case 0x0E:
                case 0x0F:
                case 0x10:
                case 0x11:
                case 0x12:
                case 0x13: // ldarg.s..stloc.s
                case 0x1F: // ldc.i4.s
                case 0x2B:
                case 0x2C:
                case 0x2D:
                case 0x2E:
                case 0x2F: // br.s..bge.s
                case 0x30:
                case 0x31:
                case 0x32:
                case 0x33:
                case 0x34:
                case 0x35:
                case 0x36:
                case 0x37: // bgt.s..blt.un.s
                case 0xDE: // leave.s
                    return 1;
                // switch (0x45) handled separately in caller
                default: return 0;
            }
        }

        /// <summary>
        /// Returns operand size in bytes for two-byte opcodes (after 0xFE prefix).
        /// </summary>
        private static int GetTwoByteOpcodeOperandSize(byte secondByte)
        {
            switch (secondByte)
            {
                // 2-byte operand: ldarg, ldarga, starg, ldloc, ldloca, stloc
                case 0x09: case 0x0A: case 0x0B: case 0x0C: case 0x0D: case 0x0E: return 2;
                // 4-byte operand: ldftn, ldvirtftn, initobj, constrained., sizeof
                case 0x06: case 0x07: case 0x15: case 0x16: case 0x1C: return 4;
                // Everything else (ceq, cgt, clt, localloc, cpblk, initblk, etc.): 0
                default: return 0;
            }
        }

        // ──────────────────────────────────────────────
        // Helper: Get declaring type name for a method
        // ──────────────────────────────────────────────

        private static string GetDeclaringTypeName(MethodDefinition methodDefinition, MetadataReader metadataReader)
        {
            var declaringTypeHandle = methodDefinition.GetDeclaringType();
            var declaringType = metadataReader.GetTypeDefinition(declaringTypeHandle);
            return GetFullTypeName(declaringType, metadataReader);
        }

        private static string GetFullTypeName(TypeDefinition typeDefinition, MetadataReader metadataReader)
        {
            string typeName = metadataReader.GetString(typeDefinition.Name);
            string typeNamespace = metadataReader.GetString(typeDefinition.Namespace);

            if (typeDefinition.IsNested)
            {
                var declaringTypeHandle = typeDefinition.GetDeclaringType();
                var declaringType = metadataReader.GetTypeDefinition(declaringTypeHandle);
                string parentName = GetFullTypeName(declaringType, metadataReader);
                return $"{parentName}.{typeName}";
            }

            return string.IsNullOrEmpty(typeNamespace) ? typeName : $"{typeNamespace}.{typeName}";
        }

        // ──────────────────────────────────────────────
        // Helper: Get custom attribute type name
        // ──────────────────────────────────────────────

        private static string GetCustomAttributeTypeName(CustomAttribute customAttribute, MetadataReader metadataReader)
        {
            if (customAttribute.Constructor.Kind == HandleKind.MemberReference)
            {
                var constructorMemberReference = metadataReader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
                if (constructorMemberReference.Parent.Kind == HandleKind.TypeReference)
                {
                    var attributeTypeReference = metadataReader.GetTypeReference((TypeReferenceHandle)constructorMemberReference.Parent);
                    string attributeNamespace = metadataReader.GetString(attributeTypeReference.Namespace);
                    string attributeName = metadataReader.GetString(attributeTypeReference.Name);
                    return string.IsNullOrEmpty(attributeNamespace) ? attributeName : $"{attributeNamespace}.{attributeName}";
                }
            }

            if (customAttribute.Constructor.Kind == HandleKind.MethodDefinition)
            {
                var constructorMethodDefinition = metadataReader.GetMethodDefinition((MethodDefinitionHandle)customAttribute.Constructor);
                var declaringTypeHandle = constructorMethodDefinition.GetDeclaringType();
                var declaringType = metadataReader.GetTypeDefinition(declaringTypeHandle);
                return GetFullTypeName(declaringType, metadataReader);
            }

            return "";
        }

        // ──────────────────────────────────────────────
        // IntPtr detection signature type provider
        // ──────────────────────────────────────────────

        /// <summary>
        /// Minimal ISignatureTypeProvider that returns true if the type is IntPtr or UIntPtr,
        /// false otherwise. Used to detect IntPtr/UIntPtr in field and method signatures.
        /// </summary>
        private sealed class IntPtrSignatureTypeProvider : ISignatureTypeProvider<bool, object?>
        {
            public bool GetPrimitiveType(PrimitiveTypeCode typeCode)
            {
                return typeCode == PrimitiveTypeCode.IntPtr || typeCode == PrimitiveTypeCode.UIntPtr;
            }

            public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;
            public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                // Check if the referenced type is System.IntPtr or System.UIntPtr
                var typeReference = reader.GetTypeReference(handle);
                string referencedNamespace = reader.GetString(typeReference.Namespace);
                string referencedName = reader.GetString(typeReference.Name);
                return referencedNamespace == "System" && (referencedName == "IntPtr" || referencedName == "UIntPtr");
            }

            public bool GetSZArrayType(bool elementType) => elementType;
            public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;
            public bool GetByReferenceType(bool elementType) => elementType;
            public bool GetPointerType(bool elementType) => true; // pointer types are always unsafe
            public bool GetGenericInstantiation(bool genericType, System.Collections.Immutable.ImmutableArray<bool> typeArguments)
            {
                if (genericType) return true;
                foreach (bool typeArgumentHasIntPtr in typeArguments)
                {
                    if (typeArgumentHasIntPtr) return true;
                }
                return false;
            }
            public bool GetGenericMethodParameter(object? genericContext, int index) => false;
            public bool GetGenericTypeParameter(object? genericContext, int index) => false;
            public bool GetPinnedType(bool elementType) => elementType;
            public bool GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            {
                var typeSpecification = reader.GetTypeSpecification(handle);
                return typeSpecification.DecodeSignature(this, genericContext);
            }
            public bool GetFunctionPointerType(MethodSignature<bool> signature) => true; // function pointers are unsafe
            public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => unmodifiedType;
        }
    }
}