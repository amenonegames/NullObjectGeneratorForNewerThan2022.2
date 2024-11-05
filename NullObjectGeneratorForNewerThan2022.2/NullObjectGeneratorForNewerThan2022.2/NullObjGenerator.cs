using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Amenonegames.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.DotnetRuntime.Extensions;
using Microsoft.CodeAnalysis.Text;


namespace Amenonegames.AutoNullObjGenerator
{
    [Generator]
    public class NullObjGenerator : IIncrementalGenerator
    {
        
        public void Initialize(IncrementalGeneratorInitializationContext  context)
        {
            context.RegisterPostInitializationOutput(static x => SetDefaultAttribute(x));
            
            var providerInh = context.SyntaxProvider.ForAttributeWithMetadataName
                (
                    context,
                    "NullObjectGenerator.InheritsToNullObjAttribute",
                    static (node, cancellation) => node is ClassDeclarationSyntax,
                    static (cont, cancellation) => cont
                )
                .Combine(context.CompilationProvider)
                .WithComparer(Comparer.Instance);
            
            var providerInt = context.SyntaxProvider.ForAttributeWithMetadataName
                (
                    context,
                    "NullObjectGenerator.InterfaceToNullObjAttribute",
                    static (node, cancellation) => node is InterfaceDeclarationSyntax,
                    static (cont, cancellation) => cont
                )
                .Combine(context.CompilationProvider)
                .WithComparer(Comparer.Instance);

            var combinedProvider = providerInh.Collect().Combine(providerInt.Collect());

            
            context.RegisterSourceOutput(
                context.CompilationProvider.Combine(combinedProvider),
                static (sourceProductionContext, t) =>
                {
                    var (compilation, array) = t;
                    var (left , right) = array;
                    
                    var references = ReferenceSymbols.Create(compilation);
                    if (references is null)
                    {
                        return;
                    }
                    
                    var codeWriter = new CodeWriter();
                    var typeMetaList = new List<ClassTypeMeta>();
                    
                    foreach (var (x,y) in left)
                    {
                            typeMetaList.Add
                            (
                                new ClassTypeMeta(y,
                                    (TypeDeclarationSyntax)x.TargetNode,
                                    (INamedTypeSymbol)x.TargetSymbol,
                                    x.Attributes,
                                    references)
                            );
                    }
                    
                    foreach (var (x,y) in right)
                    {
                        typeMetaList.Add
                        (
                            new ClassTypeMeta(y,
                                (TypeDeclarationSyntax)x.TargetNode,
                                (INamedTypeSymbol)x.TargetSymbol,
                                x.Attributes,
                                references)
                        );
                    }

                    foreach (var classTypeMeta in typeMetaList)
                    {
                        if (TryEmit(classTypeMeta, codeWriter, references, sourceProductionContext))
                        {
                            var className = classTypeMeta.SourceSymbol.Name;
                            sourceProductionContext.AddSource($"{className}AsNullObj.g.cs", codeWriter.ToString());
                        }
                        codeWriter.Clear();
                    }


                }); 
        }
        

        static bool TryEmit(
            ClassTypeMeta classTypeMeta,
            CodeWriter codeWriter,
            ReferenceSymbols references,
            in SourceProductionContext context)
        {
            INamedTypeSymbol classSymbol = classTypeMeta.SourceSymbol;
            TypeDeclarationSyntax? typeSyntax = classTypeMeta.SourceSyntax;
            var error = false;
            
            try
            {
            
                if (classSymbol is null || typeSyntax is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.ClassNotFound,
                        classTypeMeta.SourceSyntax.GetLocation(),
                        String.Join("/",classTypeMeta.SourceSyntax.ToString())));
                    error = true;
                }
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnexpectedErrorDescriptor,
                    Location.None,
                    ex.ToString()));
                return false;
            }
            

            try
            {

                foreach (var usingStr in classTypeMeta.Usings)
                {
                    codeWriter.Append("using ");
                    codeWriter.Append(usingStr,false);
                    codeWriter.Append(";",false);
                    codeWriter.AppendLineBreak(false);
                }
                
                
                var nameSpaceIsGlobal = classSymbol != null && classSymbol.ContainingNamespace.IsGlobalNamespace;
                var nameSpaceStr = nameSpaceIsGlobal ? "" : $"namespace {classSymbol.ContainingNamespace.ToDisplayString()}";
                var classAccessiblity = classSymbol?.DeclaredAccessibility.ToString().ToLower();
                var interfaces = classTypeMeta.Interfaces;
                var interfacesStr = interfaces.Any() ? $": {string.Join(",", interfaces)}" : "";
                
                codeWriter.AppendLine(nameSpaceStr);
                if(!nameSpaceIsGlobal) codeWriter.BeginBlock();
                
                codeWriter.AppendLine("// This class is generated by NullObjectGenerator.");
                codeWriter.AppendLine($"{classAccessiblity} class {classSymbol.Name}AsNullObj {interfacesStr}");
                codeWriter.BeginBlock();
                
                codeWriter.AppendLine($"{classAccessiblity} {classSymbol.Name}AsNullObj()");
                codeWriter.BeginBlock();
                codeWriter.EndBlock();
                
                foreach (var proprety in classTypeMeta.Properties)
                {
                    
                    var accessiblity = proprety.DeclaredAccessibility.ToString().ToLower();
                    
                    codeWriter.AppendLine($@"{accessiblity} {proprety.Type} {proprety.Name} ");
                    codeWriter.BeginBlock();
                    if (proprety.GetMethod != null)
                    {
                        codeWriter.AppendLine($@"get");
                        codeWriter.BeginBlock();
                        AppendLog(codeWriter, classTypeMeta.NullObjLogArgument, $@"{proprety.Name} is null. return default value.");
                        codeWriter.AppendLine($@"return default;");
                        codeWriter.EndBlock();
                    }
                    if (proprety.SetMethod != null)
                    {
                        codeWriter.AppendLine("set"); 
                        codeWriter.BeginBlock();
                        AppendLog(codeWriter, classTypeMeta.NullObjLogArgument, $@"{proprety.Name} is null. do nothing.");
                        codeWriter.EndBlock();
                    }
                    
                    codeWriter.EndBlock();
                }


                foreach (var method in classTypeMeta.Methods)
                {
                    IParameterSymbol outModifierParam = null;

                    var accessiblity = method.DeclaredAccessibility.ToString().ToLower();
                    codeWriter.AppendLineBreak(false);
                    codeWriter.Append($@"{accessiblity} {method.ReturnType} {method.Name}(");
                    
                    for (var i = 0; i < method.Parameters.Length; i++)
                    {
                        var isOutModifier = false;
                        var param = method.Parameters[i];
                        if (i != 0) codeWriter.Append(",",false);

                        if (param.RefKind != RefKind.None)
                        {
                            string modifier = "";
                            switch (param.RefKind)
                            {
                                case RefKind.Ref:
                                    modifier = "ref ";
                                    break;
                                case RefKind.Out:
                                    isOutModifier = true;
                                    modifier = "out ";
                                    break;
                                case RefKind.In:
                                    modifier = "in ";
                                    break;
                            }

                            ;
                            codeWriter.Append(modifier,false);
                        }

                        codeWriter.Append(param.Type.ToDisplayString(),false);
                        codeWriter.Append(" ",false);
                        codeWriter.Append(param.Name,false);

                        if (isOutModifier)
                            outModifierParam = param;
                    }

                    codeWriter.Append(@")", false);
                    codeWriter.AppendLineBreak(false);
                    codeWriter.BeginBlock();

                    if (outModifierParam != null)
                    {
                        codeWriter.AppendLine($@"{outModifierParam.Name} = default;");
                    }

                    AppendLog(codeWriter, classTypeMeta.NullObjLogArgument, $@"{method.Name} is null. do nothing.");

                    if(method.ReturnType.Name == "UniTask")
                    {
                        codeWriter.AppendLine($@"return UniTask.CompletedTask;");
                    }
                    else if(method.ReturnType.SpecialType != SpecialType.System_Void)
                    {
                        codeWriter.AppendLine($@"return default;");
                    }
                    
                    codeWriter.EndBlock();                    
                }
                
                
                
                codeWriter.EndBlock();
                if(!nameSpaceIsGlobal) codeWriter.EndBlock();

                return true;
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnexpectedErrorDescriptor,
                    Location.None,
                    ex.ToString()));
                return false;
            }
            
        }

        private static void AppendLog( CodeWriter sb, NullObjLog nullObjLog , string message)
        {
            if (nullObjLog.HasFlag(NullObjLog.DebugLog))
            {
                sb.AppendLine($@"UnityEngine.Debug.Log(@""{message}"");");
            }
            if (nullObjLog.HasFlag(NullObjLog.DebugLogErr))
            {
                sb.AppendLine($@"UnityEngine.Debug.LogError(@""{message}"");");
            }
            if (nullObjLog.HasFlag(NullObjLog.DebugLogWarn))
            {
                sb.AppendLine($@"UnityEngine.Debug.LogWarning(@""{message}"");");
            }
            if (nullObjLog.HasFlag(NullObjLog.ThrowException))
            {
                sb.AppendLine($@"throw new System.Exception(@""{message}"");");
            }

        }

        static string? GetNamespace(ClassDeclarationSyntax classDeclaration)
        {
            var current = classDeclaration.Parent;
            while (current != null)
            {
                if (current is NamespaceDeclarationSyntax namespaceDeclaration)
                {
                    return namespaceDeclaration.Name.ToString();
                }
                current = current.Parent;
            }

            return null; // グローバル名前空間にある場合
        }
        
        private static void SetDefaultAttribute(IncrementalGeneratorPostInitializationContext context)
        {
            // AutoPropertyAttributeのコード本体
            const string AttributeText = @"
using System;
namespace NullObjectGenerator
{
    [AttributeUsage(AttributeTargets.Class,
                    Inherited = false, AllowMultiple = false)]
    sealed class InheritsToNullObjAttribute : Attribute
    {
        public NullObjLog LogType { get; }
        public InheritsToNullObjAttribute( NullObjLog logType = NullObjLog.None)
        {
            LogType = logType;
        }
    }

    [AttributeUsage(AttributeTargets.Interface,
                    Inherited = false, AllowMultiple = false)]
    sealed class InterfaceToNullObjAttribute : Attribute
    {
        public NullObjLog LogType { get; }
        public InterfaceToNullObjAttribute( NullObjLog logType = NullObjLog.None)
        {
            LogType = logType;
        }
    }

    [Flags]
    internal enum NullObjLog
    {
        None = 0,
        DebugLog = 1,
        DebugLogErr = 1 << 1,
        DebugLogWarn = 1 << 2,
        ThrowException = 1 << 3,
    }
}
";               
            //コンパイル時に参照するアセンブリを追加
            context.AddSource
            (
                "NullObjAttribute.cs",
                SourceText.From(AttributeText,Encoding.UTF8)
            );
        }
        
        private static string GetPropertyName(string fieldName)
        {
            
            // 最初の大文字に変換可能な文字を探す
            for (int i = 0; i < fieldName.Length; i++)
            {
                if (char.IsLower(fieldName[i]))
                {
                    // 大文字に変換して、残りの文字列を結合
                    return char.ToUpper(fieldName[i]) + fieldName.Substring(i + 1);
                }
            }

            // 大文字に変換可能な文字がない場合
            return "NoLetterCanUppercase";
        }

        
    }

    class Comparer : IEqualityComparer<(GeneratorAttributeSyntaxContext, Compilation)>
    {
        public static readonly Comparer Instance = new();

        public bool Equals((GeneratorAttributeSyntaxContext, Compilation) x, (GeneratorAttributeSyntaxContext, Compilation) y)
        {
            return x.Item1.TargetNode.Equals(y.Item1.TargetNode);
        }

        public int GetHashCode((GeneratorAttributeSyntaxContext, Compilation) obj)
        {
            return obj.Item1.TargetNode.GetHashCode();
        }
    }
}