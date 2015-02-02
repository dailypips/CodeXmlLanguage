using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QuantKit
{
    #region AST
    /*public enum TOverloadableOperatorType
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
	}*/
    public class TParameter
    {
        public string name;
        public string optionValue;
        public string type;
        public TParameter()
        {

        }
    }


    /*class Statement
    {
        List<TExpression> expressions;
    }*/
    public class TInvoke
    {
        public string target;
        public string member;
        public string type;
    }

    public class TBody
    {
        public string text;
        public List<TInvoke> invokes;
        public TBody()
        {
            invokes = new List<TInvoke>();
        }
    }

    public class TAccessor : TEntity
    {
        public TBody body;
        public TAccessor()
            : base()
        {
            body = new TBody();
        }
    }
    /*class TExpression
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
    }*/
    public class TVariable
    {
        public string name;
        public string typeName;
        public string init;
    }

    #region Declaration
    public class TEntity
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
    public class TConstructor : TEntity
    {
        public List<TParameter> parameters;
        public TBody body;
        public TConstructor()
            : base()
        {
            parameters = new List<TParameter>();
            body = new TBody();
        }
    }

    public class TEvent : TEntity
    {
        public bool isCustom;
        public List<TParameter> parameters;
        public List<TAccessor> accessors;
    }

    public class TField : TEntity
    {
        public string init;
        public TField()
            : base()
        {
        }
    }

    public class TMethod : TEntity
    {
        public string typeParameters;
        public List<TParameter> parameters;
        public TBody body;

        public TMethod()
            : base()
        {
            parameters = new List<TParameter>();
            body = new TBody();
        }
    }

    /*class TOperator
    {
        public TOverloadableOperatorType toperator;
        public List<TParameter> parameters;
        public string returnType;
        public TOperator()
        {
            parameters = new List<TParameter>();
        }
    }*/

    public class TProperty : TEntity
    {
        public List<TParameter> parameters;
        public bool isPrivateImplemention;
        public TAccessor getter;
        public TAccessor setter;
        public TProperty()
            : base()
        {
            parameters = new List<TParameter>();
        }
    }

    /*class TBaseClass
    {

    }*/

    /*class TDestructor : TEntity
    {
        public List<TParameter> parameters;
        public string body;
        public TDestructor() : base()
        {
            parameters = new List<TParameter>();
        }
    }*/
    public class TIndexer : TEntity
    {
        public List<TParameter> parameters;
        public TAccessor getter;
        public TAccessor setter;

        public TIndexer()
            : base()
        {
            parameters = new List<TParameter>();
        }
    }

    public class TTypeDecl
    {
        public string name;
        public List<string> bases;
        public TTypeDecl()
        {
            bases = new List<string>();
        }
    }
    public class TClass : TTypeDecl
    {
        public List<TField> fields;
        public List<TProperty> properties;
        public List<TConstructor> constructors;
        //public List<TDestructor> destructors;
        public List<TMethod> methods;
        public List<TIndexer> indexers;
        public TClass() : base()
        {
            fields = new List<TField>();
            properties = new List<TProperty>();
            constructors = new List<TConstructor>();
            //destructors = new List<TDestructor>();
            methods = new List<TMethod>();
            indexers = new List<TIndexer>();
        }
    }

    public class TEnumMember
    {
        public string name;
        //public string type;
        public string init;
    }

    public class TEnum : TTypeDecl
    {
        public List<TEnumMember> members;
        public TEnum() : base()
        {
            members = new List<TEnumMember>();
        }
    }

    public class TNamespace
    {
        public string name;
        public List<TTypeDecl> classes;
        public TNamespace()
        {
            classes = new List<TTypeDecl>();
        }
    }
    #endregion
    #endregion
}
