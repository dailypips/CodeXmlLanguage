using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Ast;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.PatternMatching;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace QuantKit
{
    class LuaOutputVisitor : DepthFirstAstVisitor
    {
        #region TODO
        /*
         * 1.Parameter (array index??)
         * 2.Event & delegate
         * */
        #endregion
        #region field
        public TNamespace ns = new TNamespace();
        #endregion

        #region Constructor
        readonly ITextOutput output;

        public LuaOutputVisitor(ITextOutput output)
        {
            if (output == null)
            {
                throw new ArgumentNullException("output");
            }
            this.output = output;
        }

        public LuaOutputVisitor()
        {
            this.output = null;
        }
        #endregion

        #region process class , not tested!
        // test ok
        void processEntiry(EntityDeclaration ed, TEntity te)
        {
            if (ed.HasModifier(Modifiers.Public))
                te.isPublic = true;
            if (ed.HasModifier(Modifiers.Private))
                te.isPrivate = true;
            if (ed.HasModifier(Modifiers.Internal))
                te.isInternal = true;
            if (ed.HasModifier(Modifiers.Override))
                te.isOverride = true;
            if (ed.HasModifier(Modifiers.Static))
                te.isStatic = true;
            te.name = ed.Name;
            te.type = ed.ReturnType.GetText();
            foreach(var attr in ed.Attributes)
            {
                te.attributes += attr.GetText();
            }
        }
        // test ok
        void processField(FieldDeclaration fd, TClass tc)
        {
            TField field = new TField();
            processEntiry(fd, field);

            Debug.Assert(fd.Variables.Count() == 1);
            var text = fd.Variables.ToList()[0].GetText();
            var split = text.Split('=');
            field.name = split[0];
            if (split.Count() >= 2)
                field.init = split[1];

            tc.fields.Add(field);
        }
        // helper method
        string getModifies(TEntity p)
        {
            return (p.isStatic ? "static " : "") + (p.isPublic ? "public " : "") + (p.isPrivate ? "private " : "") + (p.isInternal ? "internal " : "") + (p.isOverride ? "override " : "");
        }
        // test ok, if body == "" means default access, no body

        public static TypeReference GetTypeRef(AstNode expr)
        {
            var td = expr.Annotation<TypeDefinition>();
            if (td != null)
            {
                return td;
            }

            var tr = expr.Annotation<TypeReference>();
            if (tr != null)
            {
                return tr;
            }

            var ti = expr.Annotation<ICSharpCode.Decompiler.Ast.TypeInformation>();
            if (ti != null)
            {
                return ti.InferredType;
            }

            var ilv = expr.Annotation<ICSharpCode.Decompiler.ILAst.ILVariable>();
            if (ilv != null)
            {
                return ilv.Type;
            }

            var fr = expr.Annotation<FieldDefinition>();
            if (fr != null)
            {
                return fr.FieldType;
            }

            var pr = expr.Annotation<PropertyDefinition>();
            if (pr != null)
            {
                return pr.PropertyType;
            }

            var ie = expr as IndexerExpression;
            if (ie != null)
            {
                var it = GetTypeRef(ie.Target);
                if (it != null && it.IsArray)
                {
                    return it.GetElementType();
                }
            }

            return null;
        }

        public TypeReference GetTargetTypeRef(MemberReferenceExpression memberReferenceExpression)
        {
            var pd = memberReferenceExpression.Annotation<PropertyDefinition>();
            if (pd != null)
            {
                return pd.DeclaringType;
            }

            var fd = memberReferenceExpression.Annotation<FieldDefinition>();
            if (fd == null)
                fd = memberReferenceExpression.Annotation<FieldReference>() as FieldDefinition;
            if (fd != null)
            {
                return fd.DeclaringType;
            }

            return Helpers.GetTypeRef(memberReferenceExpression.Target);
        }

        string TypeToString(TypeReference type, ICustomAttributeProvider typeAttributes = null)
        {
            if (type == null)
                return "NULL TYPE";

            ConvertTypeOptions options = ConvertTypeOptions.IncludeTypeParameterDefinitions | ConvertTypeOptions.IncludeNamespace;

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

        string GetTargetTypeString(MemberReferenceExpression memberReferenceExpression)
        {
            TypeReference reference = GetTargetTypeRef(memberReferenceExpression);
            return TypeToString(reference);
        }

        class FindAllMemberReference : DepthFirstAstVisitor
        {
            //public List<InvocationExpression>  InvokeList = new List<InvocationExpression>();
            public List<MemberReferenceExpression> memberRefList = new List<MemberReferenceExpression>();

            /*public override void VisitInvocationExpression(InvocationExpression invocationExpression)
            {
                base.VisitInvocationExpression(invocationExpression);
                InvokeList.Add(invocationExpression);
            }*/

            public override void VisitMemberReferenceExpression(MemberReferenceExpression memberReferenceExpression)
            {
                base.VisitMemberReferenceExpression(memberReferenceExpression);
                memberRefList.Add(memberReferenceExpression);
            }
    
        }

        void processBody(BlockStatement bs, TBody tb)
        {
            tb.text = bs.GetText();
            var ivisitor = new FindAllMemberReference();
            bs.AcceptVisitor(ivisitor);
            var ilist = ivisitor.memberRefList;
            foreach (var expr in ilist)
            {
                var invoke = new TInvoke();
                invoke.target = expr.Target.GetText();
                invoke.member = expr.MemberName;
                invoke.type = GetTargetTypeString(expr);
                tb.invokes.Add(invoke);
            }
        }

        void processAccessor(Accessor a, TAccessor ta)
        {
            processEntiry(a, ta);
            processBody(a.Body, ta.body);
        }
        // test ok
        void processProperty(PropertyDeclaration pd, TClass tc)
        {
            TProperty p = new TProperty();
            processEntiry(pd, p);

            Accessor getter = pd.Getter;
            if (!getter.IsNull)
            {
                p.getter = new TAccessor();
                processAccessor(getter, p.getter);
            };

            Accessor setter = pd.Setter;
            if (!setter.IsNull)
            {
                p.setter = new TAccessor();
                processAccessor(setter, p.setter);
            };

            tc.properties.Add(p);
        }
        // test ok
        void processMethod(MethodDeclaration md, TClass tc)
        {
            TMethod m = new TMethod();
            processEntiry(md, m);



            var plist = md.Descendants.OfType<ParameterDeclaration>().ToList();
            foreach (var item in plist)
            {
                TParameter p = new TParameter();
                p.name = item.Name;
                p.type = item.Type.GetText();
                var text = item.GetText();
                var split = text.Split('=');
                if (split.Count() > 1)
                    p.optionValue = split[split.Count() - 1];
                m.parameters.Add(p);
                
            }
            processBody(md.Body, m.body);
            tc.methods.Add(m);
        }

        void processConstructor(ConstructorDeclaration cd, TClass tc)
        {
            TConstructor ctor = new TConstructor();
            processEntiry(cd, ctor);

            var plist = cd.Descendants.OfType<ParameterDeclaration>().ToList();
            foreach (var item in plist)
            {
                TParameter p = new TParameter();
                p.name = item.Name;
                p.type = item.Type.GetText();
                var text = item.GetText();
                var split = text.Split('=');
                if (split.Count() > 1)
                    p.optionValue = split[split.Count() - 1];
                ctor.parameters.Add(p);

            }
            processBody(cd.Body, ctor.body);
            tc.constructors.Add(ctor);
        }

        /*void processDestructor(DestructorDeclaration dd, TClass tc)
        {
            TDestructor dtor = new TDestructor();
            processEntiry(dd, dtor);

            var plist = dd.Descendants.OfType<ParameterDeclaration>().ToList();
            foreach (var item in plist)
            {
                TParameter p = new TParameter();
                p.name = item.Name;
                p.type = item.Type.GetText();
                var text = item.GetText();
                var split = text.Split('=');
                if (split.Count() > 1)
                    p.optionValue = split[split.Count() - 1];
                dtor.parameters.Add(p);

            }
            dtor.body = dd.Body.GetText();
            tc.destructors.Add(dtor);
        }*/

        void processIndexer(IndexerDeclaration indexer, TClass tc)
        {
            TIndexer i = new TIndexer();
            processEntiry(indexer, i);

            var plist = indexer.Descendants.OfType<ParameterDeclaration>().ToList();
            foreach (var item in plist)
            {
                TParameter p = new TParameter();
                p.name = item.Name;
                p.type = item.Type.GetText();
                var text = item.GetText();
                var split = text.Split('=');
                if (split.Count() > 1)
                    p.optionValue = split[split.Count() - 1];
                i.parameters.Add(p);

            }

            Accessor getter = indexer.Getter;
            if (!getter.IsNull)
            {
                i.getter = new TAccessor();
                processAccessor(getter, i.getter);
            };

            Accessor setter = indexer.Setter;
            if (!setter.IsNull)
            {
                i.setter = new TAccessor();
                processAccessor(setter, i.setter);
            };

            tc.indexers.Add(i);
        }
        // test ok
        void processClass(TypeDeclaration td, TNamespace ns)
        {
            Debug.Assert(td.ClassType != ClassType.Enum);

            TClass tc = new TClass();
            tc.name = td.Name;


            foreach (var item in td.BaseTypes)
            {
                tc.bases.Add(TypeToString(GetTypeRef(item)));
            }

            var flist = td.Descendants.OfType<FieldDeclaration>().ToList();
            foreach (var item in flist)
            {
                processField(item, tc);
            }
            var plist = td.Descendants.OfType<PropertyDeclaration>().ToList();
            foreach(var item in plist){
                processProperty(item, tc);
            }
            var mlist = td.Descendants.OfType<MethodDeclaration>().ToList();
            foreach (var item in mlist)
            {
                processMethod(item, tc);
            }

            var clist = td.Descendants.OfType<ConstructorDeclaration>().ToList();
            foreach (var item in clist)
            {
                processConstructor(item, tc);
            }
            /*var dlist = td.Descendants.OfType<DestructorDeclaration>().ToList();
            foreach(var item in dlist)
            {
                processDestructor(item, tc);
            }*/
            var ilist = td.Descendants.OfType<IndexerDeclaration>().ToList();
            foreach (var item in ilist)
            {
                processIndexer(item, tc);
            }


            ns.classes.Add(tc);
        }
        #endregion
        
        #region process enum , has been tested!
        void processEnum(TypeDeclaration td, TNamespace ns)
        {
            Debug.Assert(td.ClassType == ClassType.Enum);
            TEnum te = new TEnum();
            te.name = td.Name;
            var blist = td.BaseTypes;
            foreach(var item in blist)
            {
                te.bases.Add(item.GetText());
            }
            var mlist = td.Descendants.OfType<EnumMemberDeclaration>().ToList();
            foreach (var item in mlist) {
                var member = new TEnumMember();
                var text = item.GetText();
                var split = text.Split('=');
                member.name = split[0];
                if (split.Count() >= 2)
                    member.init = split[1];

                te.members.Add(member);
            }
            ns.classes.Add(te);
        }
        #endregion
        #region process namespace
        public override void VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration)
        {
            base.VisitNamespaceDeclaration(namespaceDeclaration);
            ns.name = namespaceDeclaration.Name;
            var clist = namespaceDeclaration.Descendants.OfType<TypeDeclaration>().ToList();
            foreach (var item in clist)
            {
                if (item.ClassType != ClassType.Enum)
                {
                    processClass(item, ns);
                }
            }

            foreach(var item in clist)
            {
                if (item.ClassType == ClassType.Enum)
                {
                    processEnum(item, ns);
                }
            }
            outputlua(ns);
        }
        
        #endregion

        #region output
        void outputlua(TNamespace ns)
        {
            var transform = new LuaTransform();
            transform.Run(ns);

            output.WriteLine("namespace " + ns.name + "{");
            foreach (var item in ns.classes)
            {
                var c = item as TClass;
                if (c != null)
                    WriteClass(c);
                else
                    WriteEnum(item as TEnum);
            }
            output.WriteLine("} // namespaec " + ns.name);
        }

        void WriteEnum(TEnum te)
        {
            output.WriteLine("enum " + te.name + "{");
            foreach (var item in te.members)
            {
                output.WriteLine("\t" + item.name + (item.init == null ? "" : "= " + item.init));
            }
            output.WriteLine("}");
        }
        void WriteClass(TClass tc)
        {
            output.WriteLine("class " + tc.name + "{");
            output.WriteLine("// fields");
            foreach (var item in tc.fields)
            {
                output.WriteLine("\t" + item.type + " " + item.name + (item.init == null ? "" : item.init));
            }
            output.WriteLine("// properties");
            foreach (var p in tc.properties)
            {
                output.WriteLine(getModifies(p) + " " + p.type + " " + p.name + " ");
                output.WriteLine((p.getter != null ? (p.getter.body.text != "" ? "getter: " + p.getter.body.text : "getter:{}") : ""));
                output.WriteLine((p.setter != null ? (p.setter.body.text != "" ? "setter: " + p.setter.body.text : "setter:{}") : ""));
                if (p.getter != null)
                {
                    foreach (var i in p.getter.body.invokes)
                    {
                        //output.WriteLine(i.target + ": " + i.type + " (" + i.member + ")");
                    }
                }
                if (p.setter != null)
                {
                    foreach (var i in p.setter.body.invokes)
                    {
                       // output.WriteLine(i.target + ": " + i.type + " (" + i.member + ")");
                    }
                }
            }

            output.WriteLine(" // constructor");
            foreach (var m in tc.constructors)
            {
                bool isfirst = true;
                output.Write(getModifies(m) + " " + m.name + "(");
                foreach (var p in m.parameters)
                {
                    if (isfirst)
                        isfirst = false;
                    else
                        output.Write(", ");
                    output.Write(p.type + " " + p.name + " " + (p.optionValue != null ? " = " + p.optionValue : ""));
                }
                output.Write(")");
                output.WriteLine();
                output.WriteLine(m.body.text);
                foreach (var i in m.body.invokes)
                {
                    //output.WriteLine(i.target + ": " + i.type + " (" + i.member + ")");
                }
            }

            /*output.WriteLine(" // destructor");
            foreach (var m in tc.destructors)
            {
                bool isfirst = true;
                output.Write(getModifies(m) + " " + m.name + "(");
                foreach (var p in m.parameters)
                {
                    if (isfirst)
                        isfirst = false;
                    else
                        output.Write(", ");
                    output.Write(p.type + " " + p.name + " " + (p.optionValue != null ? " = " + p.optionValue : ""));
                }
                output.Write(")");
                output.WriteLine();
                output.WriteLine(m.body);
            }*/

            output.WriteLine(" // methods");
            foreach (var m in tc.methods)
            {
                bool isfirst = true;
                output.Write(getModifies(m) + " " + m.type + " " + m.name + "(");
                foreach (var p in m.parameters)
                {
                    if (isfirst)
                        isfirst = false;
                    else
                        output.Write(", ");
                    output.Write(p.type + " " + p.name + " " + (p.optionValue != null ? " = " + p.optionValue : ""));
                }
                output.Write(")");
                output.WriteLine();
                output.WriteLine(m.body.text);
                foreach (var i in m.body.invokes)
                {
                    //output.WriteLine(i.target + ": " + i.type + " (" + i.member + ")");
                }
            }

            output.WriteLine("}; // class end");
        }
        #endregion

    }
} 
