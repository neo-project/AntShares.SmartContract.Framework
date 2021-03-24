extern alias scfx;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neo.IO.Json;
using Neo.SmartContract;
using Neo.VM;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Neo.Compiler
{
    public class CompilationContext
    {
        private readonly List<string> supportedStandards = new();
        private readonly List<AbiMethod> methodsExported = new();
        private readonly List<AbiEvent> eventsExported = new();
        private readonly PermissionBuilder permissions = new();
        private readonly JObject manifestExtra = new();
        private readonly Dictionary<IMethodSymbol, MethodConvert> methodsConverted = new();
        private readonly List<MethodToken> methodTokens = new();
        private readonly Dictionary<IFieldSymbol, byte> staticFields = new();
        private Instruction[] instructions;

        public string ContractName { get; private set; }

        public static CompilationContext Compile(params string[] sourceFiles)
        {
            CompilationContext context = new();
            IEnumerable<SyntaxTree> syntaxTrees = sourceFiles.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p)));
            string coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            MetadataReference[] references = new[]
            {
                MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(string).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DisplayNameAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(scfx.Neo.SmartContract.Framework.SmartContract).Assembly.Location)
            };
            CSharpCompilation compilation = CSharpCompilation.Create(null, syntaxTrees, references);
            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                context.ProcessCompilationUnit(model, tree.GetCompilationUnitRoot());
            }
            context.instructions = context.methodsConverted.SelectMany(p => p.Value.Instructions).ToArray();
            for (int i = 0, offset = 0; i < context.instructions.Length; i++)
            {
                Instruction instruction = context.instructions[i];
                instruction.Offset = offset;
                offset += instruction.Size;
            }
            foreach (Instruction instruction in context.instructions)
            {
                if (instruction.Target is null) continue;
                if (instruction.OpCode == OpCode.TRY_L)
                {
                    int offset1 = (instruction.Target?.Instruction?.Offset - instruction.Offset) ?? 0;
                    int offset2 = (instruction.Target2?.Instruction?.Offset - instruction.Offset) ?? 0;
                    instruction.Operand = new byte[sizeof(int) + sizeof(int)];
                    BinaryPrimitives.WriteInt32LittleEndian(instruction.Operand, offset1);
                    BinaryPrimitives.WriteInt32LittleEndian(instruction.Operand.AsSpan(sizeof(int)), offset2);
                }
                else
                {
                    int offset = instruction.Target.Instruction.Offset - instruction.Offset;
                    instruction.Operand = BitConverter.GetBytes(offset);
                }
            }
            return context;
        }

        public NefFile CreateExecutable()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            var titleAttribute = assembly.GetCustomAttribute<AssemblyTitleAttribute>();
            var versionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            NefFile nef = new()
            {
                Compiler = $"{titleAttribute.Title} {versionAttribute.InformationalVersion}",
                Tokens = methodTokens.ToArray(),
                Script = instructions.Select(p => p.ToArray()).SelectMany(p => p).ToArray()
            };
            nef.CheckSum = NefFile.ComputeChecksum(nef);
            return nef;
        }

        public JObject CreateManifest()
        {
            return new JObject
            {
                ["name"] = ContractName,
                ["groups"] = new JArray(),
                ["supportedstandards"] = supportedStandards.Select(p => (JString)p).ToArray(),
                ["abi"] = new JObject
                {
                    ["methods"] = methodsExported.Select(p => new JObject
                    {
                        ["name"] = p.Name,
                        ["offset"] = methodsConverted[p.Symbol].Instructions[0].Offset,
                        ["safe"] = p.Safe,
                        ["returntype"] = p.ReturnType,
                        ["parameters"] = p.Parameters.Select(p => p.ToJson()).ToArray()
                    }).ToArray(),
                    ["events"] = eventsExported.Select(p => new JObject
                    {
                        ["name"] = p.Name,
                        ["parameters"] = p.Parameters.Select(p => p.ToJson()).ToArray()
                    }).ToArray()
                },
                ["permissions"] = permissions.ToJson(),
                ["trusts"] = new JArray(),
                ["extra"] = manifestExtra
            };
        }

        private void ProcessCompilationUnit(SemanticModel model, CompilationUnitSyntax syntax)
        {
            foreach (MemberDeclarationSyntax member in syntax.Members)
                ProcessMemberDeclaration(model, member);
        }

        private void ProcessMemberDeclaration(SemanticModel model, MemberDeclarationSyntax syntax)
        {
            switch (syntax)
            {
                case NamespaceDeclarationSyntax @namespace:
                    foreach (MemberDeclarationSyntax member in @namespace.Members)
                        ProcessMemberDeclaration(model, member);
                    break;
                case ClassDeclarationSyntax @class:
                    ProcessClass(model, model.GetDeclaredSymbol(@class));
                    break;
            }
        }

        private void ProcessClass(SemanticModel model, INamedTypeSymbol symbol)
        {
            if (symbol.DeclaredAccessibility != Accessibility.Public) return;
            if (symbol.IsAbstract) return;
            if (symbol.BaseType.Name != nameof(scfx.Neo.SmartContract.Framework.SmartContract)) return;
            ContractName = symbol.Name;
            foreach (var attribute in symbol.GetAttributes())
            {
                switch (attribute.AttributeClass.Name)
                {
                    case nameof(DisplayNameAttribute):
                        ContractName = (string)attribute.ConstructorArguments[0].Value;
                        break;
                    case nameof(scfx.Neo.SmartContract.Framework.ManifestExtraAttribute):
                        manifestExtra[(string)attribute.ConstructorArguments[0].Value] = (string)attribute.ConstructorArguments[1].Value;
                        break;
                    case nameof(scfx.Neo.SmartContract.Framework.ContractPermissionAttribute):
                        permissions.Add((string)attribute.ConstructorArguments[0].Value, attribute.ConstructorArguments[1].Values.Select(p => (string)p.Value).ToArray());
                        break;
                    case nameof(scfx.Neo.SmartContract.Framework.SupportedStandardsAttribute):
                        supportedStandards.AddRange(attribute.ConstructorArguments[0].Values.Select(p => (string)p.Value));
                        break;
                }
            }
            foreach (ISymbol member in symbol.GetMembers())
            {
                switch (member)
                {
                    case IEventSymbol @event:
                        ProcessEvent(model, @event);
                        break;
                    case IMethodSymbol method:
                        ProcessMethod(model, method);
                        break;
                }
            }
        }

        private void ProcessEvent(SemanticModel model, IEventSymbol symbol)
        {
            if (symbol.DeclaredAccessibility != Accessibility.Public) return;
            string displayName = (string)symbol.GetAttributes().FirstOrDefault(p => p.AttributeClass.Name == nameof(DisplayNameAttribute))?.ConstructorArguments[0].Value;
            INamedTypeSymbol typeSymbol = (INamedTypeSymbol)symbol.Type;
            eventsExported.Add(new AbiEvent()
            {
                Name = displayName ?? symbol.Name,
                Parameters = typeSymbol.DelegateInvokeMethod.Parameters.Select(p => p.ToAbiParameter()).ToArray()
            });
        }

        private void ProcessMethod(SemanticModel model, IMethodSymbol symbol)
        {
            if (symbol.DeclaredAccessibility != Accessibility.Public) return;
            if (symbol.MethodKind != MethodKind.Ordinary && symbol.MethodKind != MethodKind.PropertyGet) return;
            methodsExported.Add(new AbiMethod
            {
                Symbol = symbol,
                Name = symbol.GetDisplayName(),
                Safe = symbol.GetAttributes().Any(p => p.AttributeClass.Name == nameof(scfx.Neo.SmartContract.Framework.SafeAttribute)),
                Parameters = symbol.Parameters.Select(p => p.ToAbiParameter()).ToArray(),
                ReturnType = symbol.ReturnType.GetContractParameterType()
            });
            ConvertMethod(model, symbol);
        }

        internal MethodConvert ConvertMethod(SemanticModel model, IMethodSymbol symbol)
        {
            if (!methodsConverted.TryGetValue(symbol, out MethodConvert method))
            {
                method = new MethodConvert();
                methodsConverted.Add(symbol, method);
                method.Convert(this, model, symbol);
            }
            return method;
        }

        internal ushort AddMethodToken(UInt160 hash, string method, ushort parametersCount, bool hasReturnValue, CallFlags callFlags)
        {
            int index = methodTokens.FindIndex(p => p.Hash == hash && p.Method == method && p.ParametersCount == parametersCount && p.HasReturnValue == hasReturnValue && p.CallFlags == callFlags);
            if (index >= 0) return (ushort)index;
            methodTokens.Add(new MethodToken
            {
                Hash = hash,
                Method = method,
                ParametersCount = parametersCount,
                HasReturnValue = hasReturnValue,
                CallFlags = callFlags
            });
            permissions.Add(hash.ToString(), method);
            return (ushort)(methodTokens.Count - 1);
        }

        internal byte AddStaticField(IFieldSymbol symbol)
        {
            if (!staticFields.TryGetValue(symbol, out byte index))
            {
                index = (byte)staticFields.Count;
                staticFields.Add(symbol, index);
            }
            return index;
        }
    }
}