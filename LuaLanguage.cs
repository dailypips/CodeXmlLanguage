// Copyright (c) 2015 Xiaojun Gao
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//    http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Ast;
using ICSharpCode.Decompiler.Ast.Transforms;
using ICSharpCode.ILSpy.Options;
using ICSharpCode.ILSpy.XmlDoc;
using ICSharpCode.NRefactory.CSharp;
using Mono.Cecil;
using System.Text;
using ICSharpCode.ILSpy;
using System.Diagnostics;

namespace QuantKit
{
    /// <summary>
    /// Decompiler logic for C#.
    /// </summary>
    [Export(typeof(Language))]
    public class LuaLanguage : Language
    {
        string name = "Lua";
        bool showAllMembers = false;
        Predicate<IAstTransform> transformAbortCondition = null;

        public LuaLanguage()
        {
        }

        public override string Name
        {
            get { return name; }
        }

        public override string FileExtension
        {
            get { return ".lua"; }
        }

        public override string ProjectFileExtension
        {
            get { return ".luapro"; }
        }

        #region override

        public override void DecompileMethod(MethodDefinition method, ITextOutput output, DecompilationOptions options)
        {
            WriteCommentLine(output, TypeToString(method.DeclaringType, includeNamespace: true));
            AstBuilder codeDomBuilder = CreateAstBuilder(options, currentType: method.DeclaringType, isSingleMember: true);
            if (method.IsConstructor && !method.IsStatic && !method.DeclaringType.IsValueType)
            {
                // also fields and other ctors so that the field initializers can be shown as such
                AddFieldsAndCtors(codeDomBuilder, method.DeclaringType, method.IsStatic);
                RunTransformsAndGenerateCode(codeDomBuilder, output, options, new SelectCtorTransform(method));
            }
            else
            {
                codeDomBuilder.AddMethod(method);
                RunTransformsAndGenerateCode(codeDomBuilder, output, options);
            }
        }

        class SelectCtorTransform : IAstTransform
        {
            readonly MethodDefinition ctorDef;

            public SelectCtorTransform(MethodDefinition ctorDef)
            {
                this.ctorDef = ctorDef;
            }

            public void Run(AstNode compilationUnit)
            {
                ConstructorDeclaration ctorDecl = null;
                foreach (var node in compilationUnit.Children)
                {
                    ConstructorDeclaration ctor = node as ConstructorDeclaration;
                    if (ctor != null)
                    {
                        if (ctor.Annotation<MethodDefinition>() == ctorDef)
                        {
                            ctorDecl = ctor;
                        }
                        else
                        {
                            // remove other ctors
                            ctor.Remove();
                        }
                    }


                    // Remove any fields without initializers
                    FieldDeclaration fd = node as FieldDeclaration;
                    if (fd != null && fd.Variables.All(v => v.Initializer.IsNull))
                        fd.Remove();
                }
                if (ctorDecl.Initializer.ConstructorInitializerType == ConstructorInitializerType.This)
                {
                    // remove all fields
                    foreach (var node in compilationUnit.Children)
                        if (node is FieldDeclaration)
                            node.Remove();
                }
            }
        }

        public override void DecompileProperty(PropertyDefinition property, ITextOutput output, DecompilationOptions options)
        {
            WriteCommentLine(output, TypeToString(property.DeclaringType, includeNamespace: true));
            AstBuilder codeDomBuilder = CreateAstBuilder(options, currentType: property.DeclaringType, isSingleMember: true);
            codeDomBuilder.AddProperty(property);
            RunTransformsAndGenerateCode(codeDomBuilder, output, options);
        }

        public override void DecompileField(FieldDefinition field, ITextOutput output, DecompilationOptions options)
        {
            WriteCommentLine(output, TypeToString(field.DeclaringType, includeNamespace: true));
            AstBuilder codeDomBuilder = CreateAstBuilder(options, currentType: field.DeclaringType, isSingleMember: true);
            if (field.IsLiteral)
            {
                codeDomBuilder.AddField(field);
            }
            else
            {
                // also decompile ctors so that the field initializer can be shown
                AddFieldsAndCtors(codeDomBuilder, field.DeclaringType, field.IsStatic);
            }
            RunTransformsAndGenerateCode(codeDomBuilder, output, options, new SelectFieldTransform(field));
        }

        /// <summary>
        /// Removes all top-level members except for the specified fields.
        /// </summary>
        sealed class SelectFieldTransform : IAstTransform
        {
            readonly FieldDefinition field;

            public SelectFieldTransform(FieldDefinition field)
            {
                this.field = field;
            }

            public void Run(AstNode compilationUnit)
            {
                foreach (var child in compilationUnit.Children)
                {
                    if (child is EntityDeclaration)
                    {
                        if (child.Annotation<FieldDefinition>() != field)
                            child.Remove();
                    }
                }
            }
        }

        void AddFieldsAndCtors(AstBuilder codeDomBuilder, TypeDefinition declaringType, bool isStatic)
        {
            foreach (var field in declaringType.Fields)
            {
                if (field.IsStatic == isStatic)
                    codeDomBuilder.AddField(field);
            }
            foreach (var ctor in declaringType.Methods)
            {
                if (ctor.IsConstructor && ctor.IsStatic == isStatic)
                    codeDomBuilder.AddMethod(ctor);
            }
        }

        public override void DecompileEvent(EventDefinition ev, ITextOutput output, DecompilationOptions options)
        {
            WriteCommentLine(output, TypeToString(ev.DeclaringType, includeNamespace: true));
            AstBuilder codeDomBuilder = CreateAstBuilder(options, currentType: ev.DeclaringType, isSingleMember: true);
            codeDomBuilder.AddEvent(ev);
            RunTransformsAndGenerateCode(codeDomBuilder, output, options);
        }

        public override void DecompileType(TypeDefinition type, ITextOutput output, DecompilationOptions options)
        {
            AstBuilder codeDomBuilder = CreateAstBuilder(options, currentType: type);
            codeDomBuilder.AddType(type);
            RunTransformsAndGenerateCode(codeDomBuilder, output, options);
        }

        void RunTransformsAndGenerateCode(AstBuilder astBuilder, ITextOutput output, DecompilationOptions options, IAstTransform additionalTransform = null)
        {
            astBuilder.RunTransformations(transformAbortCondition);
            if (additionalTransform != null)
            {
                additionalTransform.Run(astBuilder.SyntaxTree);
            }

            GenerateCode(astBuilder, output);
        }

        class IncludeVisitor : DepthFirstAstVisitor
        {
            public string typename = "";
            public string namespacename = "";
            public bool foundClass = false;

            public override void VisitTypeDeclaration(TypeDeclaration typeDeclaration)
            {
                base.VisitTypeDeclaration(typeDeclaration);
                typename = typeDeclaration.Name;
                if (typeDeclaration.ClassType == ClassType.Class)
                    foundClass = true;
            }

            public override void VisitDelegateDeclaration(DelegateDeclaration delegateDeclaration)
            {
                base.VisitDelegateDeclaration(delegateDeclaration);
                typename = delegateDeclaration.Name;
            }
        }

        void GenerateCode(AstBuilder astBuilder, ITextOutput output)
        {
            var syntaxTree = astBuilder.SyntaxTree;
            syntaxTree.AcceptVisitor(new InsertParenthesesVisitor { InsertParenthesesForReadability = true });

            // generate AST
            /*var transform = new CSharpToCpp();
            transform.Run(syntaxTree);

            var include = new IncludeVisitor();
            syntaxTree.AcceptVisitor(include);

            // generate include
            string include_name = include.typename + ".h";
            output.WriteLine("#include <QuantKit/Event/" + include_name + ">");
            output.WriteLine("#include <QuantKit/EventType.h>");
            output.WriteLine("#include \"../Event_p.h\"");
            output.WriteLine("#include \"DataObject_p.h\"");
            output.WriteLine("#include \"Tick_p.h\"");
            output.WriteLine();

            //Generate cpp Code
            var outputFormatter = new TextOutputFormatter(output) { FoldBraces = true };
            var formattingPolicy = FormattingOptionsFactory.CreateAllman();
            syntaxTree.AcceptVisitor(new PrivateHppOutputVisitor(outputFormatter, formattingPolicy));
            syntaxTree.AcceptVisitor(new PrivateCppOutputVisitor(outputFormatter, formattingPolicy));
            syntaxTree.AcceptVisitor(new CppOutputVisitor(outputFormatter, formattingPolicy));
            */
            syntaxTree.AcceptVisitor(new LuaOutputVisitor(output));
            // generate endif
            output.WriteLine();
        }

        public static string GetPlatformDisplayName(ModuleDefinition module)
        {
            switch (module.Architecture)
            {
                case TargetArchitecture.I386:
                    if ((module.Attributes & ModuleAttributes.Preferred32Bit) == ModuleAttributes.Preferred32Bit)
                        return "AnyCPU (32-bit preferred)";
                    else if ((module.Attributes & ModuleAttributes.Required32Bit) == ModuleAttributes.Required32Bit)
                        return "x86";
                    else
                        return "AnyCPU (64-bit preferred)";
                case TargetArchitecture.AMD64:
                    return "x64";
                case TargetArchitecture.IA64:
                    return "Itanium";
                default:
                    return module.Architecture.ToString();
            }
        }

        public static string GetPlatformName(ModuleDefinition module)
        {
            switch (module.Architecture)
            {
                case TargetArchitecture.I386:
                    if ((module.Attributes & ModuleAttributes.Preferred32Bit) == ModuleAttributes.Preferred32Bit)
                        return "AnyCPU";
                    else if ((module.Attributes & ModuleAttributes.Required32Bit) == ModuleAttributes.Required32Bit)
                        return "x86";
                    else
                        return "AnyCPU";
                case TargetArchitecture.AMD64:
                    return "x64";
                case TargetArchitecture.IA64:
                    return "Itanium";
                default:
                    return module.Architecture.ToString();
            }
        }

        public override void DecompileAssembly(LoadedAssembly assembly, ITextOutput output, DecompilationOptions options)
        {
            if (options.FullDecompilation && options.SaveAsProjectDirectory != null)
            {
                HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var nsList = new List<TNamespace>();
                var dict = new Dictionary<string, TNamespace>();
                ParserFilesInProject(assembly.ModuleDefinition, options, directories, nsList);
                var default_key = "NULL";
                var default_ns = new TNamespace();
                default_ns.name = default_key;
                dict.Add(default_ns.name, default_ns);
                foreach(var ns in nsList)
                {
                    string key;
                    if (ns.name == null)
                        key = default_key;
                    else
                        key = ns.name;

                    if (dict.ContainsKey(key))
                    {
                        var mns = dict[key];
                        foreach (var c in ns.classes)
                        {
                            mns.classes.Add(c);
                        }
                    }
                    else
                    {
                        dict[ns.name] = ns;
                    }
                }
                ModuleTransform(dict["SmartQuant"]);
                WriteFilesInProject(assembly.ModuleDefinition, options, directories, dict["SmartQuant"]);
            }
            else
            {
                base.DecompileAssembly(assembly, output, options);
                output.WriteLine();
                ModuleDefinition mainModule = assembly.ModuleDefinition;
                if (mainModule.EntryPoint != null)
                {
                    output.Write("// Entry point: ");
                    output.WriteReference(mainModule.EntryPoint.DeclaringType.FullName + "." + mainModule.EntryPoint.Name, mainModule.EntryPoint);
                    output.WriteLine();
                }
                output.WriteLine("// Architecture: " + GetPlatformDisplayName(mainModule));
                if ((mainModule.Attributes & ModuleAttributes.ILOnly) == 0)
                {
                    output.WriteLine("// This assembly contains unmanaged code.");
                }
                switch (mainModule.Runtime)
                {
                    case TargetRuntime.Net_1_0:
                        output.WriteLine("// Runtime: .NET 1.0");
                        break;
                    case TargetRuntime.Net_1_1:
                        output.WriteLine("// Runtime: .NET 1.1");
                        break;
                    case TargetRuntime.Net_2_0:
                        output.WriteLine("// Runtime: .NET 2.0");
                        break;
                    case TargetRuntime.Net_4_0:
                        output.WriteLine("// Runtime: .NET 4.0");
                        break;
                }
                output.WriteLine();

                // don't automatically load additional assemblies when an assembly node is selected in the tree view
                using (options.FullDecompilation ? null : LoadedAssembly.DisableAssemblyLoad())
                {
                    AstBuilder codeDomBuilder = CreateAstBuilder(options, currentModule: assembly.ModuleDefinition);
                    codeDomBuilder.AddAssembly(assembly.ModuleDefinition, onlyAssemblyLevel: !options.FullDecompilation);
                    codeDomBuilder.RunTransformations(transformAbortCondition);
                    GenerateCode(codeDomBuilder, output);
                }
            }
        }
        #endregion

        #region WriteProjectFile

        void WriteTypeAsHpp(ITextOutput output, TTypeDecl tc)
        {
            output.WriteLine(tc.name);
        }

        void WriteTypeAsCpp(ITextOutput output, TTypeDecl tc)
        {
            output.WriteLine(tc.name);
        }

        #region transform
        void RunTransform(TNamespace ns)
        {
            var transform = new LuaTransform();
            transform.Run(ns);
        }

        void ModuleTransform(TNamespace root)
        {
            RunTransform(root);
        }
        #endregion

        #endregion

        #region WriteCodeFilesInProject
        bool IncludeTypeWhenDecompilingProject(TypeDefinition type, DecompilationOptions options)
        {
            if (type.Name == "<Module>" || AstBuilder.MemberIsHidden(type, options.DecompilerSettings))
                return false;
            if (type.Namespace == "XamlGeneratedNamespace" && type.Name == "GeneratedInternalTypeHelper")
                return false;
            return true;
        }

        public static string CleanUpName(string text)
        {
            int pos = text.IndexOf(':');
            if (pos > 0)
                text = text.Substring(0, pos);
            pos = text.IndexOf('`');
            if (pos > 0)
                text = text.Substring(0, pos);
            text = text.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                text = text.Replace(c, '-');
            return text;
        }

        void ParserFilesInProject(ModuleDefinition module, DecompilationOptions options, HashSet<string> directories, List<TNamespace> nsList)
        {
            var files = module.Types.Where(t => IncludeTypeWhenDecompilingProject(t, options)).GroupBy(
                delegate(TypeDefinition type)
                {
                    string file = CleanUpName(type.Name) + this.FileExtension;
                    if (string.IsNullOrEmpty(type.Namespace))
                    {
                        return file;
                    }
                    else
                    {
                        string dir = CleanUpName(type.Namespace);
                        if (directories.Add(dir))
                            Directory.CreateDirectory(Path.Combine(options.SaveAsProjectDirectory, dir));
                        return Path.Combine(dir, file);
                    }
                }, StringComparer.OrdinalIgnoreCase).ToList();
            AstMethodBodyBuilder.ClearUnhandledOpcodes();

            Parallel.ForEach(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                delegate(IGrouping<string, TypeDefinition> file)
                {

                    AstBuilder codeDomBuilder = CreateAstBuilder(options, currentModule: module);
                    foreach (TypeDefinition type in file)
                    {
                        codeDomBuilder.AddType(type);
                    }
                    codeDomBuilder.RunTransformations(transformAbortCondition);

                    var visitor = new LuaOutputVisitor();
                    codeDomBuilder.SyntaxTree.AcceptVisitor(visitor);

                    lock (nsList)
                    {
                        nsList.Add(visitor.ns);
                    }

                });
        }

        void WriteFilesInProject(ModuleDefinition module, DecompilationOptions options, HashSet<string> directories, TNamespace ns)
        {
            string dir = Path.Combine(options.SaveAsProjectDirectory, ns.name + "_lua");
            Directory.CreateDirectory(dir);
            
            foreach (var c in ns.classes)
            {
                string file = CleanUpName(c.name) + ".h";
                using (StreamWriter w = new StreamWriter(Path.Combine(dir, file)))
                {
                    WriteTypeAsHpp(new PlainTextOutput(w), c);
                }
            }

            foreach (var c in ns.classes)
            {
                string file = CleanUpName(c.name) + ".cpp";
                using (StreamWriter w = new StreamWriter(Path.Combine(dir, file)))
                {
                    WriteTypeAsCpp(new PlainTextOutput(w), c);
                }
            }
        }

        #endregion

        #region help class
        AstBuilder CreateAstBuilder(DecompilationOptions options, ModuleDefinition currentModule = null, TypeDefinition currentType = null, bool isSingleMember = false)
        {
            if (currentModule == null)
                currentModule = currentType.Module;
            DecompilerSettings settings = options.DecompilerSettings;
            if (isSingleMember)
            {
                settings = settings.Clone();
                settings.UsingDeclarations = false;
            }
            return new AstBuilder(
                new DecompilerContext(currentModule)
                {
                    CancellationToken = options.CancellationToken,
                    CurrentType = currentType,
                    Settings = settings
                });
        }

        public override string TypeToString(TypeReference type, bool includeNamespace, ICustomAttributeProvider typeAttributes = null)
        {
            ConvertTypeOptions options = ConvertTypeOptions.IncludeTypeParameterDefinitions;
            if (includeNamespace)
                options |= ConvertTypeOptions.IncludeNamespace;

            return TypeToString(options, type, typeAttributes);
        }

        string TypeToString(ConvertTypeOptions options, TypeReference type, ICustomAttributeProvider typeAttributes = null)
        {
            AstType astType = AstBuilder.ConvertType(type, typeAttributes, options);

            StringWriter w = new StringWriter();
            if (type.IsByReference)
            {
                ParameterDefinition pd = typeAttributes as ParameterDefinition;
                if (pd != null && (!pd.IsIn && pd.IsOut))
                    w.Write("out ");
                else
                    w.Write("ref ");

                if (astType is ComposedType && ((ComposedType)astType).PointerRank > 0)
                    ((ComposedType)astType).PointerRank--;
            }

            astType.AcceptVisitor(new CSharpOutputVisitor(w, FormattingOptionsFactory.CreateAllman()));
            return w.ToString();
        }

        public override string FormatPropertyName(PropertyDefinition property, bool? isIndexer)
        {
            if (property == null)
                throw new ArgumentNullException("property");

            if (!isIndexer.HasValue)
            {
                isIndexer = property.IsIndexer();
            }
            if (isIndexer.Value)
            {
                var buffer = new System.Text.StringBuilder();
                var accessor = property.GetMethod ?? property.SetMethod;
                if (accessor.HasOverrides)
                {
                    var declaringType = accessor.Overrides.First().DeclaringType;
                    buffer.Append(TypeToString(declaringType, includeNamespace: true));
                    buffer.Append(@".");
                }
                buffer.Append(@"this[");
                bool addSeparator = false;
                foreach (var p in property.Parameters)
                {
                    if (addSeparator)
                        buffer.Append(@", ");
                    else
                        addSeparator = true;
                    buffer.Append(TypeToString(p.ParameterType, includeNamespace: true));
                }
                buffer.Append(@"]");
                return buffer.ToString();
            }
            else
                return property.Name;
        }

        public override string FormatTypeName(TypeDefinition type)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            return TypeToString(ConvertTypeOptions.DoNotUsePrimitiveTypeNames | ConvertTypeOptions.IncludeTypeParameterDefinitions, type);
        }

        public override bool ShowMember(MemberReference member)
        {
            return showAllMembers || !AstBuilder.MemberIsHidden(member, new DecompilationOptions().DecompilerSettings);
        }

        public override MemberReference GetOriginalCodeLocation(MemberReference member)
        {
            if (showAllMembers)// || !DecompilerSettingsPanel.CurrentDecompilerSettings.AnonymousMethods)
                return member;
            else
                return Helpers.GetOriginalCodeLocation(member);
        }

        public override string GetTooltip(MemberReference member)
        {
            MethodDefinition md = member as MethodDefinition;
            PropertyDefinition pd = member as PropertyDefinition;
            EventDefinition ed = member as EventDefinition;
            FieldDefinition fd = member as FieldDefinition;
            if (md != null || pd != null || ed != null || fd != null)
            {
                AstBuilder b = new AstBuilder(new DecompilerContext(member.Module) { Settings = new DecompilerSettings { UsingDeclarations = false } });
                b.DecompileMethodBodies = false;
                if (md != null)
                    b.AddMethod(md);
                else if (pd != null)
                    b.AddProperty(pd);
                else if (ed != null)
                    b.AddEvent(ed);
                else
                    b.AddField(fd);
                b.RunTransformations();
                foreach (var attribute in b.SyntaxTree.Descendants.OfType<AttributeSection>())
                    attribute.Remove();

                StringWriter w = new StringWriter();
                GenerateCode(b, new PlainTextOutput(w));
                return Regex.Replace(w.ToString(), @"\s+", " ").TrimEnd();
            }

            return base.GetTooltip(member);
        }
        #endregion
    }
}
