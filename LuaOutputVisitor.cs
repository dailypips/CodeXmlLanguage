using ICSharpCode.Decompiler;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.PatternMatching;
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
        #region Constructor
        readonly ITextOutput output;

        public LuaOutputVisitor(ITextOutput output)
        {
            if (output == null)
            {
                throw new ArgumentNullException("textWriter");
            }
            this.output = output;
        }
        #endregion

        #region AST
        public enum TOverloadableOperatorType
	{
		None,
		
		Add,
		Subtract,
		Multiply,
		Divide,
		Modulus,
		Concat,
		
		UnaryPlus,
		UnaryMinus,
		
		Not,
		
		BitwiseAnd,
		BitwiseOr,
		ExclusiveOr,
		
		ShiftLeft,
		ShiftRight,
		
		GreaterThan,
		GreaterThanOrEqual,
		Equality,
		InEquality,
		LessThan,
		LessThanOrEqual,
		
		IsTrue,
		IsFalse,
		
		Like,
		Power,
		CType,
		DivideInteger
	}//
        class TParameter
        {
            public string name;
            public string optionValue;
            public string type;
            public TParameter()
            {

            }
        }


        class Statement
        {
            List<TExpression> expressions;
        }
        class TBody
        {
            List<Statement> statements;
        }
        class TAccessor : TEntity
        {
            public string body;
            public TAccessor() : base()
            {

            }
        }
        class TExpression
        {

        }
        class TMemberAccessExpression: TExpression
        {
            public string target;
            public string memberName;
        }
        class TInvocationExpression : TExpression
        {
            public string methodName;
            public string typeArguments;
            public List<TParameter> arguments;
        }
        public enum TCastType
        {
            DirectCast,
            TryCast,
            CType,
            CBool,
            CByte,
            CChar,
            CDate,
            CDec,
            CDbl,
            CInt,
            CLng,
            CObj,
            CSByte,
            CShort,
            CSng,
            CStr,
            CUInt,
            CULng,
            CUShort
        }
        class TCastExpression : TExpression
        {
            public TCastType ctype;
            public string type;
            public TExpression expression;
        }
        class TVariable
        {
            public string name;
            public string typeName;
            public string init;
        }

        #region Declaration
        class TEntity
        {
            public string name;
            public bool isOverride;
            public bool isPublic;
            public bool isPrivate;
            public bool isInternal;
            public bool isStatic;
            public string type;
            public TEntity()
            {
                isPublic = false;
                isInternal = false;
                isPrivate = false;
                isOverride = false;
                isStatic = false;
            }
        }
        class TConstructor : TEntity
        {
            public List<TParameter> parameters;
            public string body;
            public TConstructor() : base()
            {
                parameters = new List<TParameter>();
            }
        }

        class TEvent : TEntity
        {
            public bool isCustom;
            public List<TParameter> parameters;
            public List<TAccessor> accessors;
        }

        class TField : TEntity
        {
            public string init;
            public TField()
                : base()
            {
            }
        }

        class TMethod : TEntity
        {
            public string typeParameters;
            public List<TParameter> parameters;
            public string body;

            public TMethod() : base()
            {
                parameters = new List<TParameter>();
            }
        }

        class TOperator
        {
            public TOverloadableOperatorType toperator;
            public List<TParameter> parameters;
            public string returnType;
            public TOperator()
            {
                parameters = new List<TParameter>();
            }
        }

        class TProperty : TEntity
        {
            public List<TParameter> parameters;
            public bool isPrivateImplemention;
            public TAccessor getter;
            public TAccessor setter;
            public TProperty() : base()
            {
                parameters = new List<TParameter>();
            }
        }

        class TBaseClass
        {

        }

        /*class TDestructor : TEntity
        {
            public List<TParameter> parameters;
            public string body;
            public TDestructor() : base()
            {
                parameters = new List<TParameter>();
            }
        }*/
        class TClass
        {
            public string name;
            public List<TBaseClass> bases;
            public List<TField> fields;
            public List<TProperty> properties;
            public List<TConstructor> constructors;
            //public List<TDestructor> destructors;
            public List<TMethod> methods;
            public TClass()
            {
                bases = new List<TBaseClass>();
                fields = new List<TField>();
                properties = new List<TProperty>();
                constructors = new List<TConstructor>();
                //destructors = new List<TDestructor>();
                methods = new List<TMethod>();
            } 
        }

        class TEnumMember
        {
            public string name;
            //public string type;
            public string init;
        }

        class TEnum
        {
            public string name;
            public List<string> bases;
            public List<TEnumMember> members;
            public TEnum()
            {
                bases = new List<string>();
                members = new List<TEnumMember>();
            }
        }

        class TNamespace
        {
            public string name;
            public List<TClass> classes;
            public List<TEnum> enums;
            public TNamespace()
            {
                classes = new List<TClass>();
                enums = new List<TEnum>();
            }
        }
        #endregion
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
        void processAccessor(Accessor a, TAccessor ta)
        {
            processEntiry(a, ta);
            ta.body = a.Body.GetText();
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
            m.body = md.Body.GetText();
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
            ctor.body = cd.Body.GetText();
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

        void processClass(TypeDeclaration td, TNamespace ns)
        {
            Debug.Assert(td.ClassType != ClassType.Enum);

            TClass tc = new TClass();
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
            ns.enums.Add(te);
        }
        #endregion
        #region process namespace
        public override void VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration)
        {
            base.VisitNamespaceDeclaration(namespaceDeclaration);
            TNamespace ns = new TNamespace();
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

        #region transform
        void MakePropertyToMethod(TNamespace ns)
        {

        }
        void RenameField(TNamespace ns)
        {

        }
        #endregion
        #region output
        void outputlua(TNamespace ns)
        {          
            output.WriteLine("namespace " + ns.name + "{");
            foreach (var item in ns.classes)
            {
                WriteClass(item);
            }

            foreach (var item in ns.enums)
            {
                WriteEnum(item);
            }
            output.WriteLine("} // namespaec " + ns.name);
        }

        void WriteEnum(TEnum te)
        {
            output.WriteLine("enum " + te.name + "{");
            foreach(var item in te.members) {
                output.WriteLine("\t" + item.name + (item.init ==null ? "" : "= " + item.init));
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
                 output.WriteLine( (p.getter != null ? (p.getter.body != "" ? "getter: " +p.getter.body : "getter:{}") : ""));
                 output.WriteLine((p.setter != null ? (p.setter.body != "" ? "setter: " + p.setter.body : "setter:{}") : ""));
             }

             output.WriteLine(" // constructor");
             foreach(var m in tc.constructors)
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
                 output.WriteLine(m.body);
             }

             output.WriteLine("}; // class end");
        }
        #endregion
    }
} 
