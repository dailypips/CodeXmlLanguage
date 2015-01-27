// Copyright (c) AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.NRefactory.PatternMatching;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory;

namespace ICSharpCode.ILSpy
{
    /// <summary>
    /// Outputs the AST.
    /// </summary>
    public class CodeXmlOutputVisitor : IAstVisitor
    {
        #region Constructor
        readonly IOutputFormatter formatter;
        readonly CSharpFormattingOptions policy;
        readonly Stack<AstNode> containerStack = new Stack<AstNode>();
        readonly Stack<AstNode> positionStack = new Stack<AstNode>();
        bool needEscapeConvert = true;
        bool InCDataSection = false;

        /// <summary>
        /// Used to insert the minimal amount of spaces so that the lexer recognizes the tokens that were written.
        /// </summary>
        LastWritten lastWritten;

        enum LastWritten
        {
            Whitespace,
            Other,
            KeywordOrIdentifier,
            Plus,
            Minus,
            Ampersand,
            QuestionMark,
            Division
        }

        public CodeXmlOutputVisitor(TextWriter textWriter, CSharpFormattingOptions formattingPolicy)
        {
            if (textWriter == null)
            {
                throw new ArgumentNullException("textWriter");
            }
            if (formattingPolicy == null)
            {
                throw new ArgumentNullException("formattingPolicy");
            }
            this.formatter = new TextWriterOutputFormatter(textWriter);
            this.policy = formattingPolicy;
        }

        public CodeXmlOutputVisitor(IOutputFormatter formatter, CSharpFormattingOptions formattingPolicy)
        {
            if (formatter == null)
            {
                throw new ArgumentNullException("formatter");
            }
            if (formattingPolicy == null)
            {
                throw new ArgumentNullException("formattingPolicy");
            }
            this.formatter = formatter;
            this.policy = formattingPolicy;
        }
        #endregion

        #region StartNode/EndNode
        void StartNode(AstNode node)
        {
            // Ensure that nodes are visited in the proper nested order.
            // Jumps to different subtrees are allowed only for the child of a placeholder node.
            Debug.Assert(containerStack.Count == 0 || node.Parent == containerStack.Peek() || containerStack.Peek().NodeType == NodeType.Pattern);
            if (positionStack.Count > 0)
            {
                WriteSpecialsUpToNode(node);
            }
            containerStack.Push(node);
            positionStack.Push(node.FirstChild);
            formatter.StartNode(node);
        }

        void EndNode(AstNode node)
        {
            Debug.Assert(node == containerStack.Peek());
            AstNode pos = positionStack.Pop();
            Debug.Assert(pos == null || pos.Parent == node);
            WriteSpecials(pos, null);
            containerStack.Pop();
            formatter.EndNode(node);
        }
        #endregion

        #region WriteSpecials
        /// <summary>
        /// Writes all specials from start to end (exclusive). Does not touch the positionStack.
        /// </summary>
        void WriteSpecials(AstNode start, AstNode end)
        {
            for (AstNode pos = start; pos != end; pos = pos.NextSibling)
            {
                if (pos.Role == Roles.Comment || pos.Role == Roles.NewLine || pos.Role == Roles.PreProcessorDirective)
                {
                    pos.AcceptVisitor(this);
                }
            }
        }

        /// <summary>
        /// Writes all specials between the current position (in the positionStack) and the next
        /// node with the specified role. Advances the current position.
        /// </summary>
        void WriteSpecialsUpToRole(Role role)
        {
            WriteSpecialsUpToRole(role, null);
        }

        void WriteSpecialsUpToRole(Role role, AstNode nextNode)
        {
            if (positionStack.Count == 0)
            {
                return;
            }
            // Look for the role between the current position and the nextNode.
            for (AstNode pos = positionStack.Peek(); pos != null && pos != nextNode; pos = pos.NextSibling)
            {
                if (pos.Role == role)
                {
                    WriteSpecials(positionStack.Pop(), pos);
                    // Push the next sibling because the node matching the role is not a special,
                    // and should be considered to be already handled.
                    positionStack.Push(pos.NextSibling);
                    // This is necessary for OptionalComma() to work correctly.
                    break;
                }
            }
        }

        /// <summary>
        /// Writes all specials between the current position (in the positionStack) and the specified node.
        /// Advances the current position.
        /// </summary>
        void WriteSpecialsUpToNode(AstNode node)
        {
            if (positionStack.Count == 0)
            {
                return;
            }
            for (AstNode pos = positionStack.Peek(); pos != null; pos = pos.NextSibling)
            {
                if (pos == node)
                {
                    WriteSpecials(positionStack.Pop(), pos);
                    // Push the next sibling because the node itself is not a special,
                    // and should be considered to be already handled.
                    positionStack.Push(pos.NextSibling);
                    // This is necessary for OptionalComma() to work correctly.
                    break;
                }
            }
        }
        #endregion

        #region Comma
        /// <summary>
        /// Writes a comma.
        /// </summary>
        /// <param name="nextNode">The next node after the comma.</param>
        /// <param name="noSpaceAfterComma">When set prevents printing a space after comma.</param>
        void Comma(AstNode nextNode, bool noSpaceAfterComma = false)
        {
            WriteSpecialsUpToRole(Roles.Comma, nextNode);
            Space(policy.SpaceBeforeBracketComma);
            // TODO: Comma policy has changed.
            formatter.WriteToken(",");
            lastWritten = LastWritten.Other;
            Space(!noSpaceAfterComma && policy.SpaceAfterBracketComma);
            // TODO: Comma policy has changed.
        }

        /// <summary>
        /// Writes an optional comma, e.g. at the end of an enum declaration or in an array initializer
        /// </summary>
        void OptionalComma()
        {
            // Look if there's a comma after the current node, and insert it if it exists.
            AstNode pos = positionStack.Peek();
            while (pos != null && pos.NodeType == NodeType.Whitespace)
            {
                pos = pos.NextSibling;
            }
            if (pos != null && pos.Role == Roles.Comma)
            {
                Comma(null, noSpaceAfterComma: true);
            }
        }

        /// <summary>
        /// Writes an optional semicolon, e.g. at the end of a type or namespace declaration.
        /// </summary>
        void OptionalSemicolon()
        {
            // Look if there's a semicolon after the current node, and insert it if it exists.
            AstNode pos = positionStack.Peek();
            while (pos != null && pos.NodeType == NodeType.Whitespace)
            {
                pos = pos.NextSibling;
            }
            if (pos != null && pos.Role == Roles.Semicolon)
            {
                Semicolon();
            }
        }

        void WriteCommaSeparatedList(IEnumerable<AstNode> list)
        {
            bool isFirst = true;
            foreach (AstNode node in list)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    Comma(node);
                }
                node.AcceptVisitor(this);
            }
        }

        void WriteCommaSeparatedListInParenthesis(IEnumerable<AstNode> list, bool spaceWithin)
        {
            LPar();
            if (list.Any())
            {
                Space(spaceWithin);
                WriteCommaSeparatedList(list);
                Space(spaceWithin);
            }
            RPar();
        }


        void WriteCommaSeparatedListInBrackets(IEnumerable<ParameterDeclaration> list, bool spaceWithin)
        {
            WriteToken(Roles.LBracket);
            if (list.Any())
            {
                Space(spaceWithin);
                WriteCommaSeparatedList(list);
                Space(spaceWithin);
            }
            WriteToken(Roles.RBracket);
        }

        void WriteCommaSeparatedListInBrackets(IEnumerable<Expression> list)
        {
            WriteToken(Roles.LBracket);
            if (list.Any())
            {
                Space(policy.SpacesWithinBrackets);
                WriteCommaSeparatedList(list);
                Space(policy.SpacesWithinBrackets);
            }
            WriteToken(Roles.RBracket);
        }
        #endregion

        #region Write tokens
        /// <summary>
        /// Writes a keyword, and all specials up to
        /// </summary>
        void WriteKeyword(TokenRole tokenRole)
        {
            WriteKeyword(tokenRole.Token, tokenRole);
        }

        void WriteKeyword(string token, Role tokenRole = null)
        {
            if (tokenRole != null)
            {
                WriteSpecialsUpToRole(tokenRole);
            }
            if (lastWritten == LastWritten.KeywordOrIdentifier)
            {
                formatter.Space();
            }
            formatter.WriteKeyword(checkXmlEsc(token));
            lastWritten = LastWritten.KeywordOrIdentifier;
        }

        void WriteIdentifier(string identifier, Role<Identifier> identifierRole = null)
        {
            WriteSpecialsUpToRole(identifierRole ?? Roles.Identifier);
            if (IsKeyword(identifier, containerStack.Peek()))
            {
                if (lastWritten == LastWritten.KeywordOrIdentifier)
                {
                    Space();
                }
                // this space is not strictly required, so we call Space()
                formatter.WriteToken("@");
            }
            else if (lastWritten == LastWritten.KeywordOrIdentifier)
            {
                formatter.Space();
                // this space is strictly required, so we directly call the formatter
            }
            formatter.WriteIdentifier(checkXmlEsc(identifier));
            lastWritten = LastWritten.KeywordOrIdentifier;
        }

        string checkXmlEsc(string identifier)
        {
            if (needEscapeConvert && !InCDataSection)
            {
                return identifier.Replace("'", "&apos;").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
            }
            else
            {
                return identifier;
            }
        }

        void WriteToken(TokenRole tokenRole)
        {
            WriteToken(tokenRole.Token, tokenRole);
        }

        void WriteToken(string token, Role tokenRole)
        {
            WriteSpecialsUpToRole(tokenRole);
            // Avoid that two +, - or ? tokens are combined into a ++, -- or ?? token.
            // Note that we don't need to handle tokens like = because there's no valid
            // C# program that contains the single token twice in a row.
            // (for +, - and &, this can happen with unary operators;
            // for ?, this can happen in "a is int? ? b : c" or "a as int? ?? 0";
            // and for /, this can happen with "1/ *ptr" or "1/ //comment".)
            if (lastWritten == LastWritten.Plus && token[0] == '+'
                || lastWritten == LastWritten.Minus && token[0] == '-'
                || lastWritten == LastWritten.Ampersand && token[0] == '&'
                || lastWritten == LastWritten.QuestionMark && token[0] == '?'
                || lastWritten == LastWritten.Division && token[0] == '*')
            {
                formatter.Space();
            }
            formatter.WriteToken(checkXmlEsc(token));
            if (token == "+")
            {
                lastWritten = LastWritten.Plus;
            }
            else if (token == "-")
            {
                lastWritten = LastWritten.Minus;
            }
            else if (token == "&")
            {
                lastWritten = LastWritten.Ampersand;
            }
            else if (token == "?")
            {
                lastWritten = LastWritten.QuestionMark;
            }
            else if (token == "/")
            {
                lastWritten = LastWritten.Division;
            }
            else
            {
                lastWritten = LastWritten.Other;
            }
        }

        void LPar()
        {
            WriteToken(Roles.LPar);
        }

        void RPar()
        {
            WriteToken(Roles.RPar);
        }

        /// <summary>
        /// Marks the end of a statement
        /// </summary>
        void Semicolon()
        {
            Role role = containerStack.Peek().Role;
            // get the role of the current node
            if (!(role == ForStatement.InitializerRole || role == ForStatement.IteratorRole || role == UsingStatement.ResourceAcquisitionRole))
            {
                WriteToken(Roles.Semicolon);
                NewLine();
            }
        }

        /// <summary>
        /// Writes a space depending on policy.
        /// </summary>
        void Space(bool addSpace = true)
        {
            if (addSpace)
            {
                formatter.Space();
                lastWritten = LastWritten.Whitespace;
            }
        }

        void NewLine()
        {
            formatter.NewLine();
            lastWritten = LastWritten.Whitespace;
        }

        void OpenBrace(BraceStyle style)
        {
            WriteSpecialsUpToRole(Roles.LBrace);
            formatter.OpenBrace(style);
            lastWritten = LastWritten.Other;
        }

        void CloseBrace(BraceStyle style)
        {
            WriteSpecialsUpToRole(Roles.RBrace);
            formatter.CloseBrace(style);
            lastWritten = LastWritten.Other;
        }

        void WritePropertyStart(string prop)
        {
            needEscapeConvert = false;
            WriteIdentifier(" " + prop + " = \"");
            needEscapeConvert = true;
        }
        
        void WritePropertyEnd()
        {
            needEscapeConvert = false;
            WriteIdentifier("\"");
            needEscapeConvert = true;
        }


        void WriteTagStart()
        {
            needEscapeConvert = false;
            WriteIdentifier("<");
            needEscapeConvert = true;
        }
        void WriteTagEnd()
        {
            needEscapeConvert = false;
            WriteIdentifier(">");
            needEscapeConvert = true;
            NewLine();
        }

        void WriteElementStart(string element)
        {
            needEscapeConvert = false;
            WriteIdentifier( "<" + element);
            needEscapeConvert = true;
        }

        void WriteElementEnd(string tag)
        {
            NewLine();
            needEscapeConvert = false;
            WriteIdentifier("</" + tag + ">");
            needEscapeConvert = true;
            NewLine();
        }
        void WriteElementTail()
        {
            needEscapeConvert = false;
            WriteIdentifier(" />");
            needEscapeConvert = true;
        }
        void WriteCDataStart()
        {
            NewLine();
            needEscapeConvert = false;
            WriteIdentifier("<![CDATA[");
            InCDataSection = true;
            needEscapeConvert = true;
            NewLine();
        }

        void WriteCDataEnd()
        {
            NewLine();
            needEscapeConvert = false;
            WriteIdentifier("]]>");
            InCDataSection = false;
            needEscapeConvert = true;
            NewLine();

        }
        #endregion

        #region IsKeyword Test
        static readonly HashSet<string> unconditionalKeywords = new HashSet<string> {
			"abstract", "as", "base", "bool", "break", "byte", "case", "catch",
			"char", "checked", "class", "const", "continue", "decimal", "default", "delegate",
			"do", "double", "else", "enum", "event", "explicit", "extern", "false",
			"finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
			"in", "int", "interface", "internal", "is", "lock", "long", "namespace",
			"new", "null", "object", "operator", "out", "override", "params", "private",
			"protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
			"sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
			"true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
			"using", "virtual", "void", "volatile", "while"
		};
        static readonly HashSet<string> queryKeywords = new HashSet<string> {
			"from", "where", "join", "on", "equals", "into", "let", "orderby",
			"ascending", "descending", "select", "group", "by"
		};

        /// <summary>
        /// Determines whether the specified identifier is a keyword in the given context.
        /// </summary>
        public static bool IsKeyword(string identifier, AstNode context)
        {
            if (unconditionalKeywords.Contains(identifier))
            {
                return true;
            }
            foreach (AstNode ancestor in context.Ancestors)
            {
                if (ancestor is QueryExpression && queryKeywords.Contains(identifier))
                {
                    return true;
                }
                if (identifier == "await")
                {
                    // with lambdas/anonymous methods,
                    if (ancestor is LambdaExpression)
                    {
                        return ((LambdaExpression)ancestor).IsAsync;
                    }
                    if (ancestor is AnonymousMethodExpression)
                    {
                        return ((AnonymousMethodExpression)ancestor).IsAsync;
                    }
                    if (ancestor is EntityDeclaration)
                    {
                        return (((EntityDeclaration)ancestor).Modifiers & Modifiers.Async) == Modifiers.Async;
                    }
                }
            }
            return false;
        }
        #endregion

        #region Write constructs
        void WriteTypeArguments(IEnumerable<AstType> typeArguments)
        {
            if (typeArguments.Any())
            {
                WriteToken(Roles.LChevron);
                WriteCommaSeparatedList(typeArguments);
                WriteToken(Roles.RChevron);
            }
        }

        public void WriteTypeParameters(IEnumerable<TypeParameterDeclaration> typeParameters)
        {
            if (typeParameters.Any())
            {
                WriteToken(Roles.LChevron);
                WriteCommaSeparatedList(typeParameters);
                WriteToken(Roles.RChevron);
            }
        }

        void WriteModifiers(IEnumerable<CSharpModifierToken> modifierTokens)
        {
            foreach (CSharpModifierToken modifier in modifierTokens)
            {
                modifier.AcceptVisitor(this);
            }
        }

        void WriteQualifiedIdentifier(IEnumerable<Identifier> identifiers)
        {
            bool first = true;
            foreach (Identifier ident in identifiers)
            {
                if (first)
                {
                    first = false;
                    if (lastWritten == LastWritten.KeywordOrIdentifier)
                    {
                        formatter.Space();
                    }
                }
                else
                {
                    WriteSpecialsUpToRole(Roles.Dot, ident);
                    formatter.WriteToken(".");
                    lastWritten = LastWritten.Other;
                }
                WriteSpecialsUpToNode(ident);
                formatter.WriteIdentifier(checkXmlEsc(ident.Name));
                lastWritten = LastWritten.KeywordOrIdentifier;
            }
        }

        void WriteEmbeddedStatement(Statement embeddedStatement)
        {
            if (embeddedStatement.IsNull)
            {
                NewLine();
                return;
            }
            BlockStatement block = embeddedStatement as BlockStatement;
            if (block != null)
            {
                VisitBlockStatement(block);
            }
            else
            {
                NewLine();
                formatter.Indent();
                embeddedStatement.AcceptVisitor(this);
                formatter.Unindent();
            }
        }

        void WriteMethodBody(BlockStatement body)
        {
            if (body.IsNull)
            {
                //Semicolon();
                WriteElementStart("EmptyStatement"); WriteElementTail();
            }
            else
            {
                VisitBlockStatement(body);
            }
        }

        void WriteAttributes(IEnumerable<AttributeSection> attributes)
        {
            foreach (AttributeSection attr in attributes)
            {
                attr.AcceptVisitor(this);
            }
        }

        void WritePrivateImplementationType(AstType privateImplementationType)
        {
            if (!privateImplementationType.IsNull)
            {
                privateImplementationType.AcceptVisitor(this);
                WriteToken(Roles.Dot);
            }
        }

        #endregion

        #region Expressions
        public void VisitAnonymousMethodExpression(AnonymousMethodExpression anonymousMethodExpression)
        {
            StartNode(anonymousMethodExpression);
            WriteElementStart("AnonymousMethodExpression");
            if (anonymousMethodExpression.IsAsync)
            {
                WritePropertyStart(AnonymousMethodExpression.AsyncModifierRole.ToString());
                WritePropertyEnd();
                //WriteKeyword(AnonymousMethodExpression.AsyncModifierRole);
                //Space();
            }
            WritePropertyStart(AnonymousMethodExpression.DelegateKeywordRole.ToString());
            WritePropertyEnd();
            WriteTagEnd();
            //WriteKeyword(AnonymousMethodExpression.DelegateKeywordRole);
            if (anonymousMethodExpression.HasParameterList)
            {
                WriteElementStart("Parameters"); WriteTagEnd();
                anonymousMethodExpression.Parameters.AcceptVisitor(this);
                WriteElementEnd("Parameters");
                //Space(policy.SpaceBeforeMethodDeclarationParentheses);
                //WriteCommaSeparatedListInParenthesis(anonymousMethodExpression.Parameters, policy.SpaceWithinMethodDeclarationParentheses);
            }
            WriteElementStart("Body"); WriteTagEnd();
            anonymousMethodExpression.Body.AcceptVisitor(this);
            WriteElementEnd("Body");
            WriteElementEnd("AnonymousMethodExpression");
            EndNode(anonymousMethodExpression);
        }

        public void VisitUndocumentedExpression(UndocumentedExpression undocumentedExpression)
        {
            //UNUSED
            StartNode(undocumentedExpression);
            switch (undocumentedExpression.UndocumentedExpressionType)
            {
                case UndocumentedExpressionType.ArgList:
                case UndocumentedExpressionType.ArgListAccess:
                    WriteKeyword(UndocumentedExpression.ArglistKeywordRole);
                    break;
                case UndocumentedExpressionType.MakeRef:
                    WriteKeyword(UndocumentedExpression.MakerefKeywordRole);
                    break;
                case UndocumentedExpressionType.RefType:
                    WriteKeyword(UndocumentedExpression.ReftypeKeywordRole);
                    break;
                case UndocumentedExpressionType.RefValue:
                    WriteKeyword(UndocumentedExpression.RefvalueKeywordRole);
                    break;
            }
            if (undocumentedExpression.Arguments.Count > 0)
            {
                Space(policy.SpaceBeforeMethodCallParentheses);
                WriteCommaSeparatedListInParenthesis(undocumentedExpression.Arguments, policy.SpaceWithinMethodCallParentheses);
            }
            EndNode(undocumentedExpression);
        }

        public void VisitArrayCreateExpression(ArrayCreateExpression arrayCreateExpression)
        {
            StartNode(arrayCreateExpression);
            WriteElementStart("ArrayCreateExpression");
            //WriteKeyword(ArrayCreateExpression.NewKeywordRole);
            WritePropertyStart("Type");
            arrayCreateExpression.Type.AcceptVisitor(this);
            WritePropertyEnd();
            WriteTagEnd();

            if (arrayCreateExpression.Arguments.Count > 0)
            {
                WriteElementStart("Arguments"); WriteTagEnd();
                arrayCreateExpression.Arguments.AcceptVisitor(this);
                WriteElementEnd("Arguments");
                //WriteCommaSeparatedListInBrackets(arrayCreateExpression.Arguments);
            }
            foreach (var specifier in arrayCreateExpression.AdditionalArraySpecifiers)
            {
                WriteElementStart("Specifier"); WriteTagEnd();
                specifier.AcceptVisitor(this);
                WriteElementEnd("Specifier");
            }
            WriteElementStart("Initializer"); WriteTagEnd();
            arrayCreateExpression.Initializer.AcceptVisitor(this);
            WriteElementEnd("Initializer");
            WriteElementEnd("ArrayCreateExpression");
            EndNode(arrayCreateExpression);
        }

        public void VisitArrayInitializerExpression(ArrayInitializerExpression arrayInitializerExpression)
        {
            StartNode(arrayInitializerExpression);
            // "new List<int> { { 1 } }" and "new List<int> { 1 }" are the same semantically.
            // We also use the same AST for both: we always use two nested ArrayInitializerExpressions
            // for collection initializers, even if the user did not write nested brackets.
            // The output visitor will output nested braces only if they are necessary,
            // or if the braces tokens exist in the AST.
            bool bracesAreOptional = arrayInitializerExpression.Elements.Count == 1
                    && IsObjectOrCollectionInitializer(arrayInitializerExpression.Parent)
                    && !CanBeConfusedWithObjectInitializer(arrayInitializerExpression.Elements.Single());
            if (bracesAreOptional && arrayInitializerExpression.LBraceToken.IsNull)
            {
                arrayInitializerExpression.Elements.Single().AcceptVisitor(this);
            }
            else
            {
                PrintInitializerElements(arrayInitializerExpression.Elements);
            }
            EndNode(arrayInitializerExpression);
        }

        bool CanBeConfusedWithObjectInitializer(Expression expr)
        {
            // "int a; new List<int> { a = 1 };" is an object initalizers and invalid, but
            // "int a; new List<int> { { a = 1 } };" is a valid collection initializer.
            AssignmentExpression ae = expr as AssignmentExpression;
            return ae != null && ae.Operator == AssignmentOperatorType.Assign;
        }

        bool IsObjectOrCollectionInitializer(AstNode node)
        {
            if (!(node is ArrayInitializerExpression))
            {
                return false;
            }
            if (node.Parent is ObjectCreateExpression)
            {
                return node.Role == ObjectCreateExpression.InitializerRole;
            }
            if (node.Parent is NamedExpression)
            {
                return node.Role == Roles.Expression;
            }
            return false;
        }

        void PrintInitializerElements(AstNodeCollection<Expression> elements)
        {
            BraceStyle style;
            if (policy.ArrayInitializerWrapping == Wrapping.WrapAlways)
            {
                style = BraceStyle.NextLine;
            }
            else
            {
                style = BraceStyle.EndOfLine;
            }
            OpenBrace(style);
            bool isFirst = true;
            foreach (AstNode node in elements)
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    Comma(node, noSpaceAfterComma: true);
                    NewLine();
                }
                node.AcceptVisitor(this);
            }
            OptionalComma();
            NewLine();
            CloseBrace(style);
        }

        public void VisitAsExpression(AsExpression asExpression)
        {
            StartNode(asExpression);
            asExpression.Expression.AcceptVisitor(this);
            Space();
            WriteKeyword(AsExpression.AsKeywordRole);
            Space();
            asExpression.Type.AcceptVisitor(this);
            EndNode(asExpression);
        }

        public void VisitAssignmentExpression(AssignmentExpression assignmentExpression)
        {
            StartNode(assignmentExpression);
            WriteElementStart("AssignmentExpression"); WriteTagEnd();
            WriteElementStart("Left"); WriteTagEnd();
            assignmentExpression.Left.AcceptVisitor(this);
            WriteElementEnd("Left");
            //Space(policy.SpaceAroundAssignment);
            WriteElementStart("Operator"); WriteTagEnd();
            WriteToken(AssignmentExpression.GetOperatorRole(assignmentExpression.Operator));
            WriteElementEnd("Operator");
            //Space(policy.SpaceAroundAssignment);
            WriteElementStart("Right"); WriteTagEnd();
            assignmentExpression.Right.AcceptVisitor(this);
            WriteElementEnd("Right");
            WriteElementEnd("AssignmentExpression");
            EndNode(assignmentExpression);
        }

        public void VisitBaseReferenceExpression(BaseReferenceExpression baseReferenceExpression)
        {
            StartNode(baseReferenceExpression);
            WriteKeyword("base", baseReferenceExpression.Role);
            EndNode(baseReferenceExpression);
        }

        public void VisitBinaryOperatorExpression(BinaryOperatorExpression binaryOperatorExpression)
        {
            StartNode(binaryOperatorExpression);
            binaryOperatorExpression.Left.AcceptVisitor(this);
            bool spacePolicy;
            switch (binaryOperatorExpression.Operator)
            {
                case BinaryOperatorType.BitwiseAnd:
                case BinaryOperatorType.BitwiseOr:
                case BinaryOperatorType.ExclusiveOr:
                    spacePolicy = policy.SpaceAroundBitwiseOperator;
                    break;
                case BinaryOperatorType.ConditionalAnd:
                case BinaryOperatorType.ConditionalOr:
                    spacePolicy = policy.SpaceAroundLogicalOperator;
                    break;
                case BinaryOperatorType.GreaterThan:
                case BinaryOperatorType.GreaterThanOrEqual:
                case BinaryOperatorType.LessThanOrEqual:
                case BinaryOperatorType.LessThan:
                    spacePolicy = policy.SpaceAroundRelationalOperator;
                    break;
                case BinaryOperatorType.Equality:
                case BinaryOperatorType.InEquality:
                    spacePolicy = policy.SpaceAroundEqualityOperator;
                    break;
                case BinaryOperatorType.Add:
                case BinaryOperatorType.Subtract:
                    spacePolicy = policy.SpaceAroundAdditiveOperator;
                    break;
                case BinaryOperatorType.Multiply:
                case BinaryOperatorType.Divide:
                case BinaryOperatorType.Modulus:
                    spacePolicy = policy.SpaceAroundMultiplicativeOperator;
                    break;
                case BinaryOperatorType.ShiftLeft:
                case BinaryOperatorType.ShiftRight:
                    spacePolicy = policy.SpaceAroundShiftOperator;
                    break;
                case BinaryOperatorType.NullCoalescing:
                    spacePolicy = true;
                    break;
                default:
                    throw new NotSupportedException("Invalid value for BinaryOperatorType");
            }
            Space(spacePolicy);
            WriteToken(BinaryOperatorExpression.GetOperatorRole(binaryOperatorExpression.Operator));
            Space(spacePolicy);
            binaryOperatorExpression.Right.AcceptVisitor(this);
            EndNode(binaryOperatorExpression);
        }

        public void VisitCastExpression(CastExpression castExpression)
        {
            StartNode(castExpression);
            WriteElementStart("CastExpression");
            //LPar();
            //Space(policy.SpacesWithinCastParentheses);
            WritePropertyStart("Type");
            castExpression.Type.AcceptVisitor(this);
            WritePropertyEnd();
            //Space(policy.SpacesWithinCastParentheses);
            //RPar();
            //Space(policy.SpaceAfterTypecast);\
            WriteTagEnd();
            WriteElementStart("Expression"); WriteTagEnd();
            castExpression.Expression.AcceptVisitor(this);
            WriteElementEnd("CastExpression");
            EndNode(castExpression);
        }

        public void VisitCheckedExpression(CheckedExpression checkedExpression)
        {
            StartNode(checkedExpression);
            WriteKeyword(CheckedExpression.CheckedKeywordRole);
            LPar();
            Space(policy.SpacesWithinCheckedExpressionParantheses);
            checkedExpression.Expression.AcceptVisitor(this);
            Space(policy.SpacesWithinCheckedExpressionParantheses);
            RPar();
            EndNode(checkedExpression);
        }

        public void VisitConditionalExpression(ConditionalExpression conditionalExpression)
        {
            StartNode(conditionalExpression);
            conditionalExpression.Condition.AcceptVisitor(this);

            Space(policy.SpaceBeforeConditionalOperatorCondition);
            WriteToken(ConditionalExpression.QuestionMarkRole);
            Space(policy.SpaceAfterConditionalOperatorCondition);

            conditionalExpression.TrueExpression.AcceptVisitor(this);

            Space(policy.SpaceBeforeConditionalOperatorSeparator);
            WriteToken(ConditionalExpression.ColonRole);
            Space(policy.SpaceAfterConditionalOperatorSeparator);

            conditionalExpression.FalseExpression.AcceptVisitor(this);

            EndNode(conditionalExpression);
        }

        public void VisitDefaultValueExpression(DefaultValueExpression defaultValueExpression)
        {
            StartNode(defaultValueExpression);

            WriteKeyword(DefaultValueExpression.DefaultKeywordRole);
            LPar();
            Space(policy.SpacesWithinTypeOfParentheses);
            defaultValueExpression.Type.AcceptVisitor(this);
            Space(policy.SpacesWithinTypeOfParentheses);
            RPar();

            EndNode(defaultValueExpression);
        }

        public void VisitDirectionExpression(DirectionExpression directionExpression)
        {
            StartNode(directionExpression);

            switch (directionExpression.FieldDirection)
            {
                case FieldDirection.Out:
                    WriteKeyword(DirectionExpression.OutKeywordRole);
                    break;
                case FieldDirection.Ref:
                    WriteKeyword(DirectionExpression.RefKeywordRole);
                    break;
                default:
                    throw new NotSupportedException("Invalid value for FieldDirection");
            }
            Space();
            directionExpression.Expression.AcceptVisitor(this);

            EndNode(directionExpression);
        }

        public void VisitIdentifierExpression(IdentifierExpression identifierExpression)
        {
            StartNode(identifierExpression);
            WriteIdentifier(identifierExpression.Identifier);
            WriteTypeArguments(identifierExpression.TypeArguments);
            EndNode(identifierExpression);
        }

        public void VisitIndexerExpression(IndexerExpression indexerExpression)
        {
            StartNode(indexerExpression);
            indexerExpression.Target.AcceptVisitor(this);
            Space(policy.SpaceBeforeMethodCallParentheses);
            WriteCommaSeparatedListInBrackets(indexerExpression.Arguments);
            EndNode(indexerExpression);
        }

        public void VisitInvocationExpression(InvocationExpression invocationExpression)
        {
            StartNode(invocationExpression);
            invocationExpression.Target.AcceptVisitor(this);
            Space(policy.SpaceBeforeMethodCallParentheses);
            WriteCommaSeparatedListInParenthesis(invocationExpression.Arguments, policy.SpaceWithinMethodCallParentheses);
            EndNode(invocationExpression);
        }

        public void VisitIsExpression(IsExpression isExpression)
        {
            StartNode(isExpression);
            isExpression.Expression.AcceptVisitor(this);
            Space();
            WriteKeyword(IsExpression.IsKeywordRole);
            isExpression.Type.AcceptVisitor(this);
            EndNode(isExpression);
        }

        public void VisitLambdaExpression(LambdaExpression lambdaExpression)
        {
            StartNode(lambdaExpression);
            if (lambdaExpression.IsAsync)
            {
                WriteKeyword(LambdaExpression.AsyncModifierRole);
                Space();
            }
            if (LambdaNeedsParenthesis(lambdaExpression))
            {
                WriteCommaSeparatedListInParenthesis(lambdaExpression.Parameters, policy.SpaceWithinMethodDeclarationParentheses);
            }
            else
            {
                lambdaExpression.Parameters.Single().AcceptVisitor(this);
            }
            Space();
            WriteToken(LambdaExpression.ArrowRole);
            Space();
            lambdaExpression.Body.AcceptVisitor(this);
            EndNode(lambdaExpression);
        }

        bool LambdaNeedsParenthesis(LambdaExpression lambdaExpression)
        {
            if (lambdaExpression.Parameters.Count != 1)
            {
                return true;
            }
            var p = lambdaExpression.Parameters.Single();
            return !(p.Type.IsNull && p.ParameterModifier == ParameterModifier.None);
        }

        public void VisitMemberReferenceExpression(MemberReferenceExpression memberReferenceExpression)
        {
            StartNode(memberReferenceExpression);
            WriteElementStart("MemberReferenceExpression");
            WritePropertyStart("Target");
            memberReferenceExpression.Target.AcceptVisitor(this);
            WritePropertyEnd();
            //WriteToken(Roles.Dot);
            WritePropertyStart("MemberName");
            WriteIdentifier(memberReferenceExpression.MemberName);
            WriteTypeArguments(memberReferenceExpression.TypeArguments);
            WritePropertyEnd();
            WriteElementTail();
            EndNode(memberReferenceExpression);
        }

        public void VisitNamedArgumentExpression(NamedArgumentExpression namedArgumentExpression)
        {
            StartNode(namedArgumentExpression);
            namedArgumentExpression.NameToken.AcceptVisitor(this);
            WriteToken(Roles.Colon);
            Space();
            namedArgumentExpression.Expression.AcceptVisitor(this);
            EndNode(namedArgumentExpression);
        }

        public void VisitNamedExpression(NamedExpression namedExpression)
        {
            StartNode(namedExpression);
            namedExpression.NameToken.AcceptVisitor(this);
            Space();
            WriteToken(Roles.Assign);
            Space();
            namedExpression.Expression.AcceptVisitor(this);
            EndNode(namedExpression);
        }

        public void VisitNullReferenceExpression(NullReferenceExpression nullReferenceExpression)
        {
            StartNode(nullReferenceExpression);
            WriteKeyword("null", nullReferenceExpression.Role);
            EndNode(nullReferenceExpression);
        }

        public void VisitObjectCreateExpression(ObjectCreateExpression objectCreateExpression)
        {
            StartNode(objectCreateExpression);
            WriteKeyword(ObjectCreateExpression.NewKeywordRole);
            objectCreateExpression.Type.AcceptVisitor(this);
            bool useParenthesis = objectCreateExpression.Arguments.Any() || objectCreateExpression.Initializer.IsNull;
            // also use parenthesis if there is an '(' token
            if (!objectCreateExpression.LParToken.IsNull)
            {
                useParenthesis = true;
            }
            if (useParenthesis)
            {
                Space(policy.SpaceBeforeMethodCallParentheses);
                WriteCommaSeparatedListInParenthesis(objectCreateExpression.Arguments, policy.SpaceWithinMethodCallParentheses);
            }
            objectCreateExpression.Initializer.AcceptVisitor(this);
            EndNode(objectCreateExpression);
        }

        public void VisitAnonymousTypeCreateExpression(AnonymousTypeCreateExpression anonymousTypeCreateExpression)
        {
            StartNode(anonymousTypeCreateExpression);
            WriteKeyword(AnonymousTypeCreateExpression.NewKeywordRole);
            PrintInitializerElements(anonymousTypeCreateExpression.Initializers);
            EndNode(anonymousTypeCreateExpression);
        }

        public void VisitParenthesizedExpression(ParenthesizedExpression parenthesizedExpression)
        {
            StartNode(parenthesizedExpression);
            LPar();
            Space(policy.SpacesWithinParentheses);
            parenthesizedExpression.Expression.AcceptVisitor(this);
            Space(policy.SpacesWithinParentheses);
            RPar();
            EndNode(parenthesizedExpression);
        }

        public void VisitPointerReferenceExpression(PointerReferenceExpression pointerReferenceExpression)
        {
            StartNode(pointerReferenceExpression);
            pointerReferenceExpression.Target.AcceptVisitor(this);
            WriteToken(PointerReferenceExpression.ArrowRole);
            WriteIdentifier(pointerReferenceExpression.MemberName);
            WriteTypeArguments(pointerReferenceExpression.TypeArguments);
            EndNode(pointerReferenceExpression);
        }

        public void VisitEmptyExpression(EmptyExpression emptyExpression)
        {
            StartNode(emptyExpression);
            EndNode(emptyExpression);
        }

        #region VisitPrimitiveExpression
        public void VisitPrimitiveExpression(PrimitiveExpression primitiveExpression)
        {
            StartNode(primitiveExpression);
            if (!string.IsNullOrEmpty(primitiveExpression.LiteralValue))
            {
                formatter.WriteToken(checkXmlEsc(primitiveExpression.LiteralValue));
            }
            else
            {
                WritePrimitiveValue(primitiveExpression.Value);
            }
            EndNode(primitiveExpression);
        }

        public static string PrintPrimitiveValue(object val)
        {
            StringWriter writer = new StringWriter();
            CodeXmlOutputVisitor visitor = new CodeXmlOutputVisitor(writer, new ICSharpCode.NRefactory.CSharp.CSharpFormattingOptions());
            visitor.WritePrimitiveValue(val);
            return writer.ToString();
        }

        void WritePrimitiveValue(object val)
        {
            if (val == null)
            {
                // usually NullReferenceExpression should be used for this, but we'll handle it anyways
                WriteKeyword("null");
                return;
            }

            if (val is bool)
            {
                if ((bool)val)
                {
                    WriteKeyword("true");
                }
                else
                {
                    WriteKeyword("false");
                }
                return;
            }

            if (val is string)
            {
                formatter.WriteToken(checkXmlEsc("\"" + ConvertString(val.ToString()) + "\""));
                lastWritten = LastWritten.Other;
            }
            else if (val is char)
            {
                formatter.WriteToken(checkXmlEsc("'" + ConvertCharLiteral((char)val) + "'"));
                lastWritten = LastWritten.Other;
            }
            else if (val is decimal)
            {
                formatter.WriteToken(checkXmlEsc(((decimal)val).ToString(NumberFormatInfo.InvariantInfo) + "m"));
                lastWritten = LastWritten.Other;
            }
            else if (val is float)
            {
                float f = (float)val;
                if (float.IsInfinity(f) || float.IsNaN(f))
                {
                    // Strictly speaking, these aren't PrimitiveExpressions;
                    // but we still support writing these to make life easier for code generators.
                    WriteKeyword("float");
                    WriteToken(Roles.Dot);
                    if (float.IsPositiveInfinity(f))
                    {
                        WriteIdentifier("PositiveInfinity");
                    }
                    else if (float.IsNegativeInfinity(f))
                    {
                        WriteIdentifier("NegativeInfinity");
                    }
                    else
                    {
                        WriteIdentifier("NaN");
                    }
                    return;
                }
                if (f == 0 && 1 / f == double.NegativeInfinity)
                {
                    // negative zero is a special case
                    // (again, not a primitive expression, but it's better to handle
                    // the special case here than to do it in all code generators)
                    formatter.WriteToken("-");
                }
                formatter.WriteToken(checkXmlEsc(f.ToString("R", NumberFormatInfo.InvariantInfo) + "f"));
                lastWritten = LastWritten.Other;
            }
            else if (val is double)
            {
                double f = (double)val;
                if (double.IsInfinity(f) || double.IsNaN(f))
                {
                    // Strictly speaking, these aren't PrimitiveExpressions;
                    // but we still support writing these to make life easier for code generators.
                    WriteKeyword("double");
                    WriteToken(Roles.Dot);
                    if (double.IsPositiveInfinity(f))
                    {
                        WriteIdentifier("PositiveInfinity");
                    }
                    else if (double.IsNegativeInfinity(f))
                    {
                        WriteIdentifier("NegativeInfinity");
                    }
                    else
                    {
                        WriteIdentifier("NaN");
                    }
                    return;
                }
                if (f == 0 && 1 / f == double.NegativeInfinity)
                {
                    // negative zero is a special case
                    // (again, not a primitive expression, but it's better to handle
                    // the special case here than to do it in all code generators)
                    formatter.WriteToken("-");
                }
                string number = f.ToString("R", NumberFormatInfo.InvariantInfo);
                if (number.IndexOf('.') < 0 && number.IndexOf('E') < 0)
                {
                    number += ".0";
                }
                formatter.WriteToken(checkXmlEsc(number));
                // needs space if identifier follows number; this avoids mistaking the following identifier as type suffix
                lastWritten = LastWritten.KeywordOrIdentifier;
            }
            else if (val is IFormattable)
            {
                StringBuilder b = new StringBuilder();
                //				if (primitiveExpression.LiteralFormat == LiteralFormat.HexadecimalNumber) {
                //					b.Append("0x");
                //					b.Append(((IFormattable)val).ToString("x", NumberFormatInfo.InvariantInfo));
                //				} else {
                b.Append(((IFormattable)val).ToString(null, NumberFormatInfo.InvariantInfo));
                //				}
                if (val is uint || val is ulong)
                {
                    b.Append("u");
                }
                if (val is long || val is ulong)
                {
                    b.Append("L");
                }
                formatter.WriteToken(checkXmlEsc(b.ToString()));
                // needs space if identifier follows number; this avoids mistaking the following identifier as type suffix
                lastWritten = LastWritten.KeywordOrIdentifier;
            }
            else
            {
                formatter.WriteToken(checkXmlEsc(val.ToString()));
                lastWritten = LastWritten.Other;
            }
        }

        static string ConvertCharLiteral(char ch)
        {
            if (ch == '\'')
            {
                return "\\'";
            }
            return ConvertChar(ch);
        }

        /// <summary>
        /// Gets the escape sequence for the specified character.
        /// </summary>
        /// <remarks>This method does not convert ' or ".</remarks>
        public static string ConvertChar(char ch)
        {
            switch (ch)
            {
                case '\\':
                    return "\\\\";
                case '\0':
                    return "\\0";
                case '\a':
                    return "\\a";
                case '\b':
                    return "\\b";
                case '\f':
                    return "\\f";
                case '\n':
                    return "\\n";
                case '\r':
                    return "\\r";
                case '\t':
                    return "\\t";
                case '\v':
                    return "\\v";
                default:
                    if (char.IsControl(ch) || char.IsSurrogate(ch) ||
                        // print all uncommon white spaces as numbers
                        (char.IsWhiteSpace(ch) && ch != ' '))
                    {
                        return "\\u" + ((int)ch).ToString("x4");
                    }
                    else
                    {
                        return ch.ToString();
                    }
            }
        }

        /// <summary>
        /// Converts special characters to escape sequences within the given string.
        /// </summary>
        public static string ConvertString(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in str)
            {
                if (ch == '"')
                {
                    sb.Append("\\\"");
                }
                else
                {
                    sb.Append(ConvertChar(ch));
                }
            }
            return sb.ToString();
        }

        #endregion

        public void VisitSizeOfExpression(SizeOfExpression sizeOfExpression)
        {
            StartNode(sizeOfExpression);

            WriteKeyword(SizeOfExpression.SizeofKeywordRole);
            LPar();
            Space(policy.SpacesWithinSizeOfParentheses);
            sizeOfExpression.Type.AcceptVisitor(this);
            Space(policy.SpacesWithinSizeOfParentheses);
            RPar();

            EndNode(sizeOfExpression);
        }

        public void VisitStackAllocExpression(StackAllocExpression stackAllocExpression)
        {
            StartNode(stackAllocExpression);
            WriteKeyword(StackAllocExpression.StackallocKeywordRole);
            stackAllocExpression.Type.AcceptVisitor(this);
            WriteCommaSeparatedListInBrackets(new[] { stackAllocExpression.CountExpression });
            EndNode(stackAllocExpression);
        }

        public void VisitThisReferenceExpression(ThisReferenceExpression thisReferenceExpression)
        {
            StartNode(thisReferenceExpression);
            WriteKeyword("this", thisReferenceExpression.Role);
            EndNode(thisReferenceExpression);
        }

        public void VisitTypeOfExpression(TypeOfExpression typeOfExpression)
        {
            StartNode(typeOfExpression);

            WriteKeyword(TypeOfExpression.TypeofKeywordRole);
            LPar();
            Space(policy.SpacesWithinTypeOfParentheses);
            typeOfExpression.Type.AcceptVisitor(this);
            Space(policy.SpacesWithinTypeOfParentheses);
            RPar();

            EndNode(typeOfExpression);
        }

        public void VisitTypeReferenceExpression(TypeReferenceExpression typeReferenceExpression)
        {
            StartNode(typeReferenceExpression);
            typeReferenceExpression.Type.AcceptVisitor(this);
            EndNode(typeReferenceExpression);
        }

        public void VisitUnaryOperatorExpression(UnaryOperatorExpression unaryOperatorExpression)
        {
            StartNode(unaryOperatorExpression);
            UnaryOperatorType opType = unaryOperatorExpression.Operator;
            var opSymbol = UnaryOperatorExpression.GetOperatorRole(opType);
            if (opType == UnaryOperatorType.Await)
            {
                WriteKeyword(opSymbol);
            }
            else if (!(opType == UnaryOperatorType.PostIncrement || opType == UnaryOperatorType.PostDecrement))
            {
                WriteToken(opSymbol);
            }
            unaryOperatorExpression.Expression.AcceptVisitor(this);
            if (opType == UnaryOperatorType.PostIncrement || opType == UnaryOperatorType.PostDecrement)
            {
                WriteToken(opSymbol);
            }
            EndNode(unaryOperatorExpression);
        }

        public void VisitUncheckedExpression(UncheckedExpression uncheckedExpression)
        {
            StartNode(uncheckedExpression);
            WriteKeyword(UncheckedExpression.UncheckedKeywordRole);
            LPar();
            Space(policy.SpacesWithinCheckedExpressionParantheses);
            uncheckedExpression.Expression.AcceptVisitor(this);
            Space(policy.SpacesWithinCheckedExpressionParantheses);
            RPar();
            EndNode(uncheckedExpression);
        }

        #endregion

        #region Query Expressions
        public void VisitQueryExpression(QueryExpression queryExpression)
        {
            //UNUSED
            StartNode(queryExpression);
            bool indent = !(queryExpression.Parent is QueryContinuationClause);
            if (indent)
            {
                formatter.Indent();
                NewLine();
            }
            bool first = true;
            foreach (var clause in queryExpression.Clauses)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    if (!(clause is QueryContinuationClause))
                    {
                        NewLine();
                    }
                }
                clause.AcceptVisitor(this);
            }
            if (indent)
            {
                formatter.Unindent();
            }
            EndNode(queryExpression);
        }

        public void VisitQueryContinuationClause(QueryContinuationClause queryContinuationClause)
        {
            //UNUSED
            StartNode(queryContinuationClause);
            queryContinuationClause.PrecedingQuery.AcceptVisitor(this);
            Space();
            WriteKeyword(QueryContinuationClause.IntoKeywordRole);
            Space();
            queryContinuationClause.IdentifierToken.AcceptVisitor(this);
            EndNode(queryContinuationClause);
        }

        public void VisitQueryFromClause(QueryFromClause queryFromClause)
        {
            //UNUSED
            StartNode(queryFromClause);
            WriteKeyword(QueryFromClause.FromKeywordRole);
            queryFromClause.Type.AcceptVisitor(this);
            Space();
            queryFromClause.IdentifierToken.AcceptVisitor(this);
            Space();
            WriteKeyword(QueryFromClause.InKeywordRole);
            Space();
            queryFromClause.Expression.AcceptVisitor(this);
            EndNode(queryFromClause);
        }

        public void VisitQueryLetClause(QueryLetClause queryLetClause)
        {
            //UNUSED
            StartNode(queryLetClause);
            WriteKeyword(QueryLetClause.LetKeywordRole);
            Space();
            queryLetClause.IdentifierToken.AcceptVisitor(this);
            Space(policy.SpaceAroundAssignment);
            WriteToken(Roles.Assign);
            Space(policy.SpaceAroundAssignment);
            queryLetClause.Expression.AcceptVisitor(this);
            EndNode(queryLetClause);
        }

        public void VisitQueryWhereClause(QueryWhereClause queryWhereClause)
        {
            //UNUSED
            StartNode(queryWhereClause);
            WriteKeyword(QueryWhereClause.WhereKeywordRole);
            Space();
            queryWhereClause.Condition.AcceptVisitor(this);
            EndNode(queryWhereClause);
        }

        public void VisitQueryJoinClause(QueryJoinClause queryJoinClause)
        {
            //UNUSED
            StartNode(queryJoinClause);
            WriteKeyword(QueryJoinClause.JoinKeywordRole);
            queryJoinClause.Type.AcceptVisitor(this);
            Space();
            WriteIdentifier(queryJoinClause.JoinIdentifier, QueryJoinClause.JoinIdentifierRole);
            Space();
            WriteKeyword(QueryJoinClause.InKeywordRole);
            Space();
            queryJoinClause.InExpression.AcceptVisitor(this);
            Space();
            WriteKeyword(QueryJoinClause.OnKeywordRole);
            Space();
            queryJoinClause.OnExpression.AcceptVisitor(this);
            Space();
            WriteKeyword(QueryJoinClause.EqualsKeywordRole);
            Space();
            queryJoinClause.EqualsExpression.AcceptVisitor(this);
            if (queryJoinClause.IsGroupJoin)
            {
                Space();
                WriteKeyword(QueryJoinClause.IntoKeywordRole);
                WriteIdentifier(queryJoinClause.IntoIdentifier, QueryJoinClause.IntoIdentifierRole);
            }
            EndNode(queryJoinClause);
        }

        public void VisitQueryOrderClause(QueryOrderClause queryOrderClause)
        {
            //UNUSED
            StartNode(queryOrderClause);
            WriteKeyword(QueryOrderClause.OrderbyKeywordRole);
            Space();
            WriteCommaSeparatedList(queryOrderClause.Orderings);
            EndNode(queryOrderClause);
        }

        public void VisitQueryOrdering(QueryOrdering queryOrdering)
        {
            //UNUSED
            StartNode(queryOrdering);
            queryOrdering.Expression.AcceptVisitor(this);
            switch (queryOrdering.Direction)
            {
                case QueryOrderingDirection.Ascending:
                    Space();
                    WriteKeyword(QueryOrdering.AscendingKeywordRole);
                    break;
                case QueryOrderingDirection.Descending:
                    Space();
                    WriteKeyword(QueryOrdering.DescendingKeywordRole);
                    break;
            }
            EndNode(queryOrdering);
        }

        public void VisitQuerySelectClause(QuerySelectClause querySelectClause)
        {
            //UNUSED
            StartNode(querySelectClause);
            WriteKeyword(QuerySelectClause.SelectKeywordRole);
            Space();
            querySelectClause.Expression.AcceptVisitor(this);
            EndNode(querySelectClause);
        }

        public void VisitQueryGroupClause(QueryGroupClause queryGroupClause)
        {
            //UNUSED
            StartNode(queryGroupClause);
            WriteKeyword(QueryGroupClause.GroupKeywordRole);
            Space();
            queryGroupClause.Projection.AcceptVisitor(this);
            Space();
            WriteKeyword(QueryGroupClause.ByKeywordRole);
            Space();
            queryGroupClause.Key.AcceptVisitor(this);
            EndNode(queryGroupClause);
        }

        #endregion

        #region GeneralScope
        public void VisitAttribute(ICSharpCode.NRefactory.CSharp.Attribute attribute)
        {
            StartNode(attribute);
            attribute.Type.AcceptVisitor(this);
            if (attribute.Arguments.Count != 0 || !attribute.GetChildByRole(Roles.LPar).IsNull)
            {
                Space(policy.SpaceBeforeMethodCallParentheses);
                WriteCommaSeparatedListInParenthesis(attribute.Arguments, policy.SpaceWithinMethodCallParentheses);
            }
            EndNode(attribute);
        }

        public void VisitAttributeSection(AttributeSection attributeSection)
        {
            StartNode(attributeSection);
            WriteToken(Roles.LBracket);
            if (!string.IsNullOrEmpty(attributeSection.AttributeTarget))
            {
                WriteToken(attributeSection.AttributeTarget, Roles.AttributeTargetRole);
                WriteToken(Roles.Colon);
                Space();
            }
            WriteCommaSeparatedList(attributeSection.Attributes);
            WriteToken(Roles.RBracket);
            if (attributeSection.Parent is ParameterDeclaration || attributeSection.Parent is TypeParameterDeclaration)
            {
                Space();
            }
            else
            {
                NewLine();
            }
            EndNode(attributeSection);
        }

        public void VisitDelegateDeclaration(DelegateDeclaration delegateDeclaration)
        {
            StartNode(delegateDeclaration);
            WriteElementStart(Roles.DelegateKeyword.ToString());

            if (delegateDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(delegateDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (delegateDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(delegateDeclaration.ModifierTokens);
                WritePropertyEnd();
            }
            
            WritePropertyStart("ReturnType");
            delegateDeclaration.ReturnType.AcceptVisitor(this);
            WritePropertyEnd();

            WritePropertyStart("Name");
            delegateDeclaration.NameToken.AcceptVisitor(this);
            WritePropertyEnd();
            if (delegateDeclaration.TypeParameters.Count() > 0)
            {
                WritePropertyStart("TypeParameters");
                delegateDeclaration.TypeParameters.AcceptVisitor(this);
                WritePropertyEnd();
            }

            if (delegateDeclaration.Constraints.Count() > 0)
            {
                WritePropertyStart("Constraints");
                foreach (Constraint constraint in delegateDeclaration.Constraints)
                {
                    constraint.AcceptVisitor(this);
                }
                WritePropertyEnd();
            }

            WriteTagEnd();
            formatter.Indent();

            //WriteCommaSeparatedListInParenthesis(delegateDeclaration.Parameters, policy.SpaceWithinMethodDeclarationParentheses);
            if (delegateDeclaration.Parameters.Count() > 0)
            {
                WriteElementStart("Parameters");WriteTagEnd();
                foreach (var param in delegateDeclaration.Parameters)
                    VisitParameterDeclaration(param);
                WriteElementEnd("Parameters");
            }

            formatter.Unindent();
            WriteElementEnd(Roles.DelegateKeyword.ToString());
            EndNode(delegateDeclaration);
        }

        public void VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration)
        {
            StartNode(namespaceDeclaration);

            WriteElementStart(Roles.NamespaceKeyword.ToString());
            
            
            WritePropertyStart("Name");
            WriteQualifiedIdentifier(namespaceDeclaration.Identifiers);
            WritePropertyEnd();
            WriteTagEnd();

            formatter.Indent();
            foreach (var member in namespaceDeclaration.Members)
            {
                member.AcceptVisitor(this);
            }
            formatter.Unindent();
            WriteElementEnd(Roles.NamespaceKeyword.ToString());
            EndNode(namespaceDeclaration);
        }

        public void VisitTypeDeclaration(TypeDeclaration typeDeclaration)
        {
            StartNode(typeDeclaration);
            
            WriteElementStart(typeDeclaration.ClassType.ToString());
            
            WritePropertyStart("Name");
            typeDeclaration.NameToken.AcceptVisitor(this);
            WritePropertyEnd();

            if (typeDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(typeDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (typeDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(typeDeclaration.ModifierTokens);
                WritePropertyEnd();
            }

            if (typeDeclaration.TypeParameters.Count() > 0)
            {
                WritePropertyStart("TypeParameters");
                WriteTypeParameters(typeDeclaration.TypeParameters);
                WritePropertyEnd();
            }

            
            if (typeDeclaration.BaseTypes.Any())
            {
                WritePropertyStart("Bases");
                WriteCommaSeparatedList(typeDeclaration.BaseTypes);
                WritePropertyEnd();
            }

            if (typeDeclaration.Constraints.Count() > 0)
            {
                WritePropertyStart("Constraints");
                foreach (Constraint constraint in typeDeclaration.Constraints)
                {
                    constraint.AcceptVisitor(this);
                }
                WritePropertyEnd();
            }
            WriteTagEnd();
            formatter.Indent();

            if (typeDeclaration.ClassType == ClassType.Enum)
            {
                foreach (var member in typeDeclaration.Members)
                {
                      member.AcceptVisitor(this);
                }
            }
            else
            {
                foreach (var member in typeDeclaration.Members)
                {
                    member.AcceptVisitor(this);
                }
            }
            formatter.Unindent();
            NewLine();
            WriteElementEnd(typeDeclaration.ClassType.ToString());
            EndNode(typeDeclaration);
        }

        public void VisitUsingAliasDeclaration(UsingAliasDeclaration usingAliasDeclaration)
        {
            StartNode(usingAliasDeclaration);
            /*WriteKeyword(UsingAliasDeclaration.UsingKeywordRole);
            WriteIdentifier(usingAliasDeclaration.Alias, UsingAliasDeclaration.AliasRole);
            Space(policy.SpaceAroundEqualityOperator);
            WriteToken(Roles.Assign);
            Space(policy.SpaceAroundEqualityOperator);
            usingAliasDeclaration.Import.AcceptVisitor(this);
            Semicolon();*/
            EndNode(usingAliasDeclaration);
        }

        public void VisitUsingDeclaration(UsingDeclaration usingDeclaration)
        {
            StartNode(usingDeclaration);
            //WriteKeyword(UsingDeclaration.UsingKeywordRole);
            //usingDeclaration.Import.AcceptVisitor(this);
            //Semicolon();
            EndNode(usingDeclaration);
        }

        public void VisitExternAliasDeclaration(ExternAliasDeclaration externAliasDeclaration)
        {
            StartNode(externAliasDeclaration);
            /*WriteKeyword(Roles.ExternKeyword);
            Space();
            WriteKeyword(Roles.AliasKeyword);
            Space();
            externAliasDeclaration.NameToken.AcceptVisitor(this);
            Semicolon();*/
            EndNode(externAliasDeclaration);
        }

        #endregion

        #region Statements
        public void VisitBlockStatement(BlockStatement blockStatement)
        {
            StartNode(blockStatement);
            /*BraceStyle style;
            if (blockStatement.Parent is AnonymousMethodExpression || blockStatement.Parent is LambdaExpression)
            {
                style = policy.AnonymousMethodBraceStyle;
            }
            else if (blockStatement.Parent is ConstructorDeclaration)
            {
                style = policy.ConstructorBraceStyle;
            }
            else if (blockStatement.Parent is DestructorDeclaration)
            {
                style = policy.DestructorBraceStyle;
            }
            else if (blockStatement.Parent is MethodDeclaration)
            {
                style = policy.MethodBraceStyle;
            }
            else if (blockStatement.Parent is Accessor)
            {
                if (blockStatement.Parent.Role == PropertyDeclaration.GetterRole)
                {
                    style = policy.PropertyGetBraceStyle;
                }
                else if (blockStatement.Parent.Role == PropertyDeclaration.SetterRole)
                {
                    style = policy.PropertySetBraceStyle;
                }
                else if (blockStatement.Parent.Role == CustomEventDeclaration.AddAccessorRole)
                {
                    style = policy.EventAddBraceStyle;
                }
                else if (blockStatement.Parent.Role == CustomEventDeclaration.RemoveAccessorRole)
                {
                    style = policy.EventRemoveBraceStyle;
                }
                else
                {
                    style = policy.StatementBraceStyle;
                }
            }
            else
            {
                style = policy.StatementBraceStyle;
            }
            OpenBrace(style);*/
            WriteElementStart("BlockStatement"); WriteTagEnd();
            foreach (var node in blockStatement.Statements)
            {
                node.AcceptVisitor(this);
            }
            WriteElementEnd("BlockStatement");
            //CloseBrace(style);
            if (!(blockStatement.Parent is Expression))
                NewLine();
            EndNode(blockStatement);
        }

        public void VisitBreakStatement(BreakStatement breakStatement)
        {
            StartNode(breakStatement);
            WriteElementStart("BreakStatement"); WriteTagEnd();
            //WriteKeyword("break");
            //Semicolon();
            WriteElementEnd("BreakStatement");
            EndNode(breakStatement);
        }

        public void VisitCheckedStatement(CheckedStatement checkedStatement)
        {
            StartNode(checkedStatement);
            //WriteKeyword(CheckedStatement.CheckedKeywordRole);
            WriteElementStart("CheckedStatement"); WriteTagEnd();
            checkedStatement.Body.AcceptVisitor(this);
            WriteElementEnd("CheckedStatement");
            EndNode(checkedStatement);
        }

        public void VisitContinueStatement(ContinueStatement continueStatement)
        {
            StartNode(continueStatement);
            WriteElementStart("ContinueStatement"); WriteTagEnd();
            // WriteKeyword("continue");
            // Semicolon();
            WriteElementEnd("ContinueStatement");
            EndNode(continueStatement);
        }

        public void VisitDoWhileStatement(DoWhileStatement doWhileStatement)
        {
            StartNode(doWhileStatement);
            WriteElementStart("DoWhileStatement"); WriteTagEnd();
            WriteElementStart("do"); WriteTagEnd();
            //WriteKeyword(DoWhileStatement.DoKeywordRole);
            WriteEmbeddedStatement(doWhileStatement.EmbeddedStatement);
            WriteElementEnd("do");
            WriteElementStart("while"); WriteTagEnd();
            //WriteKeyword(DoWhileStatement.WhileKeywordRole);
            //Space(policy.SpaceBeforeWhileParentheses);
            //LPar();
            //Space(policy.SpacesWithinWhileParentheses);
            doWhileStatement.Condition.AcceptVisitor(this);
            WriteElementEnd("while");
            //Space(policy.SpacesWithinWhileParentheses);
            //RPar();
            //Semicolon();
            WriteElementEnd("DoWhileStatement");
            EndNode(doWhileStatement);
        }

        public void VisitEmptyStatement(EmptyStatement emptyStatement)
        {
            StartNode(emptyStatement);
            WriteElementStart("EmptyStatement"); WriteElementTail();
            //Semicolon();
            EndNode(emptyStatement);
        }

        public void VisitExpressionStatement(ExpressionStatement expressionStatement)
        {
            StartNode(expressionStatement);
            WriteElementStart("ExpressionStatement"); WriteTagEnd();
            expressionStatement.Expression.AcceptVisitor(this);
            WriteElementEnd("ExpressionStatement");
            //Semicolon();
            EndNode(expressionStatement);
        }

        public void VisitFixedStatement(FixedStatement fixedStatement)
        {
            //USED IN ANONYMOUS CLASS, SO NO USEFUL
            StartNode(fixedStatement);
            /*WriteKeyword(FixedStatement.FixedKeywordRole);
            Space(policy.SpaceBeforeUsingParentheses);
            LPar();
            Space(policy.SpacesWithinUsingParentheses);
            fixedStatement.Type.AcceptVisitor(this);
            Space();
            WriteCommaSeparatedList(fixedStatement.Variables);
            Space(policy.SpacesWithinUsingParentheses);
            RPar();
            WriteEmbeddedStatement(fixedStatement.EmbeddedStatement);*/
            EndNode(fixedStatement);
        }

        public void VisitForeachStatement(ForeachStatement foreachStatement)
        {
            StartNode(foreachStatement);
            WriteElementStart("ForeachStatement");
            //WriteKeyword(ForeachStatement.ForeachKeywordRole);
            //Space(policy.SpaceBeforeForeachParentheses);
            //LPar();
            //Space(policy.SpacesWithinForeachParentheses);
            WritePropertyStart("VariableType");
            foreachStatement.VariableType.AcceptVisitor(this);
            WritePropertyEnd();
            //Space();
            WritePropertyStart("VariableName");
            foreachStatement.VariableNameToken.AcceptVisitor(this);
            WritePropertyEnd();
            WriteTagEnd();

            WriteElementStart("in"); WriteTagEnd();
            //WriteKeyword(ForeachStatement.InKeywordRole);
            //Space();
            foreachStatement.InExpression.AcceptVisitor(this);
            WriteElementEnd("in");
            //Space(policy.SpacesWithinForeachParentheses);
            //RPar();
            WriteElementStart("Body"); WriteTagEnd();
            WriteEmbeddedStatement(foreachStatement.EmbeddedStatement);
            WriteElementEnd("Body");
            WriteElementEnd("ForeachStatement");
            EndNode(foreachStatement);
        }

        public void VisitForStatement(ForStatement forStatement)
        {
            StartNode(forStatement);
            WriteKeyword(ForStatement.ForKeywordRole);
            Space(policy.SpaceBeforeForParentheses);
            LPar();
            Space(policy.SpacesWithinForParentheses);

            WriteCommaSeparatedList(forStatement.Initializers);
            Space(policy.SpaceBeforeForSemicolon);
            WriteToken(Roles.Semicolon);
            Space(policy.SpaceAfterForSemicolon);

            forStatement.Condition.AcceptVisitor(this);
            Space(policy.SpaceBeforeForSemicolon);
            WriteToken(Roles.Semicolon);
            if (forStatement.Iterators.Any())
            {
                Space(policy.SpaceAfterForSemicolon);
                WriteCommaSeparatedList(forStatement.Iterators);
            }

            Space(policy.SpacesWithinForParentheses);
            RPar();
            WriteEmbeddedStatement(forStatement.EmbeddedStatement);
            EndNode(forStatement);
        }

        public void VisitGotoCaseStatement(GotoCaseStatement gotoCaseStatement)
        {
            StartNode(gotoCaseStatement);
            WriteKeyword(GotoCaseStatement.GotoKeywordRole);
            WriteKeyword(GotoCaseStatement.CaseKeywordRole);
            Space();
            gotoCaseStatement.LabelExpression.AcceptVisitor(this);
            Semicolon();
            EndNode(gotoCaseStatement);
        }

        public void VisitGotoDefaultStatement(GotoDefaultStatement gotoDefaultStatement)
        {
            StartNode(gotoDefaultStatement);
            WriteKeyword(GotoDefaultStatement.GotoKeywordRole);
            WriteKeyword(GotoDefaultStatement.DefaultKeywordRole);
            Semicolon();
            EndNode(gotoDefaultStatement);
        }

        public void VisitGotoStatement(GotoStatement gotoStatement)
        {
            StartNode(gotoStatement);
            WriteKeyword(GotoStatement.GotoKeywordRole);
            WriteIdentifier(gotoStatement.Label);
            Semicolon();
            EndNode(gotoStatement);
        }

        public void VisitIfElseStatement(IfElseStatement ifElseStatement)
        {
            StartNode(ifElseStatement);
            WriteElementStart("IfElseStatement"); WriteTagEnd();
            //WriteKeyword(IfElseStatement.IfKeywordRole);
            //Space(policy.SpaceBeforeIfParentheses);
            //LPar();
            //Space(policy.SpacesWithinIfParentheses);
            WriteElementStart("if"); WriteTagEnd();
            ifElseStatement.Condition.AcceptVisitor(this);
            WriteElementEnd("if");
            //Space(policy.SpacesWithinIfParentheses);
            //RPar();
            WriteElementStart("BODY"); WriteTagEnd();
            WriteEmbeddedStatement(ifElseStatement.TrueStatement);
            WriteElementEnd("BODY");
            if (!ifElseStatement.FalseStatement.IsNull)
            {
                WriteElementStart("else"); WriteTagEnd();
                //WriteKeyword(IfElseStatement.ElseKeywordRole);
                WriteEmbeddedStatement(ifElseStatement.FalseStatement);
                WriteElementEnd("else");
            }
            WriteElementEnd("IfElseStatement");
            EndNode(ifElseStatement);
        }

        public void VisitLabelStatement(LabelStatement labelStatement)
        {
            StartNode(labelStatement);
            WriteIdentifier(labelStatement.Label);
            WriteToken(Roles.Colon);
            bool foundLabelledStatement = false;
            for (AstNode tmp = labelStatement.NextSibling; tmp != null; tmp = tmp.NextSibling)
            {
                if (tmp.Role == labelStatement.Role)
                {
                    foundLabelledStatement = true;
                }
            }
            if (!foundLabelledStatement)
            {
                // introduce an EmptyStatement so that the output becomes syntactically valid
                WriteToken(Roles.Semicolon);
            }
            NewLine();
            EndNode(labelStatement);
        }

        public void VisitLockStatement(LockStatement lockStatement)
        {
            StartNode(lockStatement);
            WriteKeyword(LockStatement.LockKeywordRole);
            Space(policy.SpaceBeforeLockParentheses);
            LPar();
            Space(policy.SpacesWithinLockParentheses);
            lockStatement.Expression.AcceptVisitor(this);
            Space(policy.SpacesWithinLockParentheses);
            RPar();
            WriteEmbeddedStatement(lockStatement.EmbeddedStatement);
            EndNode(lockStatement);
        }

        public void VisitReturnStatement(ReturnStatement returnStatement)
        {
            StartNode(returnStatement);
            WriteKeyword(ReturnStatement.ReturnKeywordRole);
            if (!returnStatement.Expression.IsNull)
            {
                Space();
                returnStatement.Expression.AcceptVisitor(this);
            }
            Semicolon();
            EndNode(returnStatement);
        }

        public void VisitSwitchStatement(SwitchStatement switchStatement)
        {
            StartNode(switchStatement);
            WriteKeyword(SwitchStatement.SwitchKeywordRole);
            Space(policy.SpaceBeforeSwitchParentheses);
            LPar();
            Space(policy.SpacesWithinSwitchParentheses);
            switchStatement.Expression.AcceptVisitor(this);
            Space(policy.SpacesWithinSwitchParentheses);
            RPar();
            OpenBrace(policy.StatementBraceStyle);
            if (!policy.IndentSwitchBody)
            {
                formatter.Unindent();
            }

            foreach (var section in switchStatement.SwitchSections)
            {
                section.AcceptVisitor(this);
            }

            if (!policy.IndentSwitchBody)
            {
                formatter.Indent();
            }
            CloseBrace(policy.StatementBraceStyle);
            NewLine();
            EndNode(switchStatement);
        }

        public void VisitSwitchSection(SwitchSection switchSection)
        {
            StartNode(switchSection);
            bool first = true;
            foreach (var label in switchSection.CaseLabels)
            {
                if (!first)
                {
                    NewLine();
                }
                label.AcceptVisitor(this);
                first = false;
            }
            bool isBlock = switchSection.Statements.Count == 1 && switchSection.Statements.Single() is BlockStatement;
            if (policy.IndentCaseBody && !isBlock)
            {
                formatter.Indent();
            }

            if (!isBlock)
                NewLine();

            foreach (var statement in switchSection.Statements)
            {
                statement.AcceptVisitor(this);
            }

            if (policy.IndentCaseBody && !isBlock)
            {
                formatter.Unindent();
            }

            EndNode(switchSection);
        }

        public void VisitCaseLabel(CaseLabel caseLabel)
        {
            StartNode(caseLabel);
            if (caseLabel.Expression.IsNull)
            {
                WriteKeyword(CaseLabel.DefaultKeywordRole);
            }
            else
            {
                WriteKeyword(CaseLabel.CaseKeywordRole);
                Space();
                caseLabel.Expression.AcceptVisitor(this);
            }
            WriteToken(Roles.Colon);
            EndNode(caseLabel);
        }

        public void VisitThrowStatement(ThrowStatement throwStatement)
        {
            StartNode(throwStatement);
            WriteKeyword(ThrowStatement.ThrowKeywordRole);
            if (!throwStatement.Expression.IsNull)
            {
                Space();
                throwStatement.Expression.AcceptVisitor(this);
            }
            Semicolon();
            EndNode(throwStatement);
        }

        public void VisitTryCatchStatement(TryCatchStatement tryCatchStatement)
        {
            StartNode(tryCatchStatement);
            WriteKeyword(TryCatchStatement.TryKeywordRole);
            tryCatchStatement.TryBlock.AcceptVisitor(this);
            foreach (var catchClause in tryCatchStatement.CatchClauses)
            {
                catchClause.AcceptVisitor(this);
            }
            if (!tryCatchStatement.FinallyBlock.IsNull)
            {
                WriteKeyword(TryCatchStatement.FinallyKeywordRole);
                tryCatchStatement.FinallyBlock.AcceptVisitor(this);
            }
            EndNode(tryCatchStatement);
        }

        public void VisitCatchClause(CatchClause catchClause)
        {
            StartNode(catchClause);
            WriteKeyword(CatchClause.CatchKeywordRole);
            if (!catchClause.Type.IsNull)
            {
                Space(policy.SpaceBeforeCatchParentheses);
                LPar();
                Space(policy.SpacesWithinCatchParentheses);
                catchClause.Type.AcceptVisitor(this);
                if (!string.IsNullOrEmpty(catchClause.VariableName))
                {
                    Space();
                    catchClause.VariableNameToken.AcceptVisitor(this);
                }
                Space(policy.SpacesWithinCatchParentheses);
                RPar();
            }
            catchClause.Body.AcceptVisitor(this);
            EndNode(catchClause);
        }

        public void VisitUncheckedStatement(UncheckedStatement uncheckedStatement)
        {
            //UNUSED
            StartNode(uncheckedStatement);
            WriteKeyword(UncheckedStatement.UncheckedKeywordRole);
            uncheckedStatement.Body.AcceptVisitor(this);
            EndNode(uncheckedStatement);
        }

        public void VisitUnsafeStatement(UnsafeStatement unsafeStatement)
        {
            //UNUSED
            StartNode(unsafeStatement);
            WriteKeyword(UnsafeStatement.UnsafeKeywordRole);
            unsafeStatement.Body.AcceptVisitor(this);
            EndNode(unsafeStatement);
        }

        public void VisitUsingStatement(UsingStatement usingStatement)
        {
            StartNode(usingStatement);
            WriteKeyword(UsingStatement.UsingKeywordRole);
            Space(policy.SpaceBeforeUsingParentheses);
            LPar();
            Space(policy.SpacesWithinUsingParentheses);

            usingStatement.ResourceAcquisition.AcceptVisitor(this);

            Space(policy.SpacesWithinUsingParentheses);
            RPar();

            WriteEmbeddedStatement(usingStatement.EmbeddedStatement);

            EndNode(usingStatement);
        }

        public void VisitVariableDeclarationStatement(VariableDeclarationStatement variableDeclarationStatement)
        {
            StartNode(variableDeclarationStatement);
            WriteModifiers(variableDeclarationStatement.GetChildrenByRole(VariableDeclarationStatement.ModifierRole));
            variableDeclarationStatement.Type.AcceptVisitor(this);
            Space();
            WriteCommaSeparatedList(variableDeclarationStatement.Variables);
            Semicolon();
            EndNode(variableDeclarationStatement);
        }

        public void VisitWhileStatement(WhileStatement whileStatement)
        {
            StartNode(whileStatement);
            WriteKeyword(WhileStatement.WhileKeywordRole);
            Space(policy.SpaceBeforeWhileParentheses);
            LPar();
            Space(policy.SpacesWithinWhileParentheses);
            whileStatement.Condition.AcceptVisitor(this);
            Space(policy.SpacesWithinWhileParentheses);
            RPar();
            WriteEmbeddedStatement(whileStatement.EmbeddedStatement);
            EndNode(whileStatement);
        }

        public void VisitYieldBreakStatement(YieldBreakStatement yieldBreakStatement)
        {
            //UNUSED
            StartNode(yieldBreakStatement);
            WriteKeyword(YieldBreakStatement.YieldKeywordRole);
            WriteKeyword(YieldBreakStatement.BreakKeywordRole);
            Semicolon();
            EndNode(yieldBreakStatement);
        }

        public void VisitYieldReturnStatement(YieldReturnStatement yieldReturnStatement)
        {
            //UNUSED
            StartNode(yieldReturnStatement);
            WriteKeyword(YieldReturnStatement.YieldKeywordRole);
            WriteKeyword(YieldReturnStatement.ReturnKeywordRole);
            Space();
            yieldReturnStatement.Expression.AcceptVisitor(this);
            Semicolon();
            EndNode(yieldReturnStatement);
        }

        #endregion

        #region TypeMembers
        public void VisitAccessor(Accessor accessor)
        {
            StartNode(accessor);        
            if (accessor.Role == PropertyDeclaration.GetterRole)
            {
                WriteElementStart("get");
            }
            else if (accessor.Role == PropertyDeclaration.SetterRole)
            {
                WriteElementStart("set");
            }
            else if (accessor.Role == CustomEventDeclaration.AddAccessorRole)
            {
                WriteElementStart("add");
            }
            else if (accessor.Role == CustomEventDeclaration.RemoveAccessorRole)
            {
                WriteElementStart("remove");
            }
            if (accessor.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(accessor.Attributes);
                WritePropertyEnd();
            }
            if (accessor.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(accessor.ModifierTokens);
                WritePropertyEnd();
            }
            WriteTagEnd();
            
            if (!accessor.Body.IsNull)
            {
                WriteCDataStart();
                WriteMethodBody(accessor.Body);
                WriteCDataEnd();
            }

            if (accessor.Role == PropertyDeclaration.GetterRole)
            {
                WriteElementEnd("get");
            }
            else if (accessor.Role == PropertyDeclaration.SetterRole)
            {
                WriteElementEnd("set");
            }
            else if (accessor.Role == CustomEventDeclaration.AddAccessorRole)
            {
                WriteElementEnd("add");
            }
            else if (accessor.Role == CustomEventDeclaration.RemoveAccessorRole)
            {
                WriteElementEnd("remove");
            }
            EndNode(accessor);
        }

        public void VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration)
        {
            StartNode(constructorDeclaration);
            WriteElementStart("constructor");

            if (constructorDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(constructorDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (constructorDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(constructorDeclaration.ModifierTokens);
                WritePropertyEnd();
            }

            WritePropertyStart("Name");
            TypeDeclaration type = constructorDeclaration.Parent as TypeDeclaration;
            StartNode(constructorDeclaration.NameToken);
            WriteIdentifier(type != null ? type.Name : constructorDeclaration.Name);
            EndNode(constructorDeclaration.NameToken);
            WritePropertyEnd();

            if (!constructorDeclaration.Initializer.IsNull)
            {
                WritePropertyStart("Initializer");
                constructorDeclaration.Initializer.AcceptVisitor(this);
                WritePropertyEnd();
            }
            WriteTagEnd();

            if (constructorDeclaration.Parameters.Count() > 0)
            {
                WriteElementStart("Parameters"); WriteTagEnd();
                foreach (var param in constructorDeclaration.Parameters)
                    VisitParameterDeclaration(param);
                //WriteCommaSeparatedListInParenthesis(constructorDeclaration.Parameters, policy.SpaceWithinMethodDeclarationParentheses);
                WriteElementEnd("Parameters");
            }
            
            WriteElementStart("body"); WriteTagEnd();
            if (!constructorDeclaration.Body.IsNull)
            {
                WriteCDataStart();
                WriteMethodBody(constructorDeclaration.Body);
                WriteCDataEnd();
            }
            WriteElementEnd("body");
            WriteElementEnd("constructor");
            EndNode(constructorDeclaration);
        }

        public void VisitConstructorInitializer(ConstructorInitializer constructorInitializer)
        {
            //UNUSED
            StartNode(constructorInitializer);
            WriteToken(Roles.Colon);
            Space();
            if (constructorInitializer.ConstructorInitializerType == ConstructorInitializerType.This)
            {
                WriteKeyword(ConstructorInitializer.ThisKeywordRole);
            }
            else
            {
                WriteKeyword(ConstructorInitializer.BaseKeywordRole);
            }
            Space(policy.SpaceBeforeMethodCallParentheses);
            WriteCommaSeparatedListInParenthesis(constructorInitializer.Arguments, policy.SpaceWithinMethodCallParentheses);
            EndNode(constructorInitializer);
        }

        public void VisitDestructorDeclaration(DestructorDeclaration destructorDeclaration)
        {
            StartNode(destructorDeclaration);
            WriteElementStart("destructor");
            if (destructorDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(destructorDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (destructorDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(destructorDeclaration.ModifierTokens);
                WritePropertyEnd();
            }

            WritePropertyStart("Name");
            TypeDeclaration type = destructorDeclaration.Parent as TypeDeclaration;
            StartNode(destructorDeclaration.NameToken);
            WriteIdentifier(type != null ? type.Name : destructorDeclaration.Name);
            EndNode(destructorDeclaration.NameToken);
            WritePropertyEnd();
            WriteTagEnd();
            WriteElementStart("body"); WriteTagEnd();
            if (!destructorDeclaration.Body.IsNull)
            {
                WriteCDataStart();
                WriteMethodBody(destructorDeclaration.Body);
                WriteCDataEnd();
            }
            WriteElementEnd("body");
            WriteElementEnd("destructor");
            EndNode(destructorDeclaration);
        }

        public void VisitEnumMemberDeclaration(EnumMemberDeclaration enumMemberDeclaration)
        {
            StartNode(enumMemberDeclaration);
            WriteElementStart("item");
            if (enumMemberDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(enumMemberDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (enumMemberDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(enumMemberDeclaration.ModifierTokens);
                WritePropertyEnd();
            }
            
            WritePropertyStart("Name");
            enumMemberDeclaration.NameToken.AcceptVisitor(this);
            WritePropertyEnd();

            if (!enumMemberDeclaration.Initializer.IsNull)
            {
                WritePropertyStart("Value");
                enumMemberDeclaration.Initializer.AcceptVisitor(this);
                WritePropertyEnd();
            }

            WriteElementTail();
            NewLine();
            EndNode(enumMemberDeclaration);
        }

        public void VisitEventDeclaration(EventDeclaration eventDeclaration)
        {
            StartNode(eventDeclaration);
            WriteElementStart(EventDeclaration.EventKeywordRole.ToString());
            
            if (eventDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(eventDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (eventDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(eventDeclaration.ModifierTokens);
                WritePropertyEnd();
            }

            WritePropertyStart("ReturnType");
            eventDeclaration.ReturnType.AcceptVisitor(this);
            WritePropertyEnd();

            WriteTagEnd();
            WriteElementStart("Variable"); WriteTagEnd();
            eventDeclaration.Variables.AcceptVisitor(this);
            WriteElementEnd("Variable");
            //WriteCommaSeparatedList(eventDeclaration.Variables); // TODO

            WriteElementEnd(EventDeclaration.EventKeywordRole.ToString());
            EndNode(eventDeclaration);
        }

        public void VisitCustomEventDeclaration(CustomEventDeclaration customEventDeclaration)
        {
            StartNode(customEventDeclaration);
            WriteElementStart(CustomEventDeclaration.EventKeywordRole.ToString());
            //WriteAttributes(customEventDeclaration.Attributes);
            //WriteModifiers(customEventDeclaration.ModifierTokens);

            if (customEventDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(customEventDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (customEventDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(customEventDeclaration.ModifierTokens);
                WritePropertyEnd();
            }
            WritePropertyStart("ReturnType");
            customEventDeclaration.ReturnType.AcceptVisitor(this);
            WritePropertyEnd();
            //customEventDeclaration.ReturnType.AcceptVisitor(this);
            //Space();
            WritePropertyStart("PrivateImplementationType");
            customEventDeclaration.PrivateImplementationType.AcceptVisitor(this);
            WritePropertyEnd();
            //WritePrivateImplementationType(customEventDeclaration.PrivateImplementationType);
            WritePropertyStart("Name");
            customEventDeclaration.NameToken.AcceptVisitor(this);
            WritePropertyEnd();
            WriteTagEnd();
            //OpenBrace(policy.EventBraceStyle);
            // output add/remove in their original order
            foreach (AstNode node in customEventDeclaration.Children)
            {
                if (node.Role == CustomEventDeclaration.AddAccessorRole || node.Role == CustomEventDeclaration.RemoveAccessorRole)
                {
                    node.AcceptVisitor(this);
                }
            }
            //CloseBrace(policy.EventBraceStyle);
            WriteElementEnd(CustomEventDeclaration.EventKeywordRole.ToString());
            EndNode(customEventDeclaration);
        }

        public void VisitFieldDeclaration(FieldDeclaration fieldDeclaration)
        {
            StartNode(fieldDeclaration);
            WriteElementStart("field");

            if (fieldDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(fieldDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (fieldDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(fieldDeclaration.ModifierTokens);
                WritePropertyEnd();
            }
            WritePropertyStart("ReturnType");
            fieldDeclaration.ReturnType.AcceptVisitor(this);
            WritePropertyEnd();

            //WriteCommaSeparatedList(fieldDeclaration.Variables);    // TODO

            if (fieldDeclaration.Variables.Count() > 0)
            {
                Debug.Assert(fieldDeclaration.Variables.Count == 1);
                foreach (VariableInitializer node in fieldDeclaration.Variables)
                {
                    WritePropertyStart("Name");
                    WriteIdentifier(node.Name);
                    WritePropertyEnd();

                    if (!node.Initializer.IsNull)
                    {
                        WritePropertyStart("Initializer");
                        node.Initializer.AcceptVisitor(this);
                        WritePropertyEnd();
                    }
                }
            }
            
            WriteElementTail();
            NewLine();
            EndNode(fieldDeclaration);
        }

        public void VisitFixedFieldDeclaration(FixedFieldDeclaration fixedFieldDeclaration)
        {
            //UNUSED
            StartNode(fixedFieldDeclaration);
            WriteElementStart("fixedField");
            if (fixedFieldDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(fixedFieldDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (fixedFieldDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(fixedFieldDeclaration.ModifierTokens);
                WritePropertyEnd();
            }

            //WriteKeyword(FixedFieldDeclaration.FixedKeywordRole);
            //Space();
            WritePropertyStart("ReturnType");
            fixedFieldDeclaration.ReturnType.AcceptVisitor(this);
            WritePropertyEnd();

            WriteElementTail();
            NewLine();
            EndNode(fixedFieldDeclaration);
        }

        public void VisitFixedVariableInitializer(FixedVariableInitializer fixedVariableInitializer)
        {
            //UNUSED
            StartNode(fixedVariableInitializer);
            fixedVariableInitializer.NameToken.AcceptVisitor(this);
            if (!fixedVariableInitializer.CountExpression.IsNull)
            {
                WriteToken(Roles.LBracket);
                Space(policy.SpacesWithinBrackets);
                fixedVariableInitializer.CountExpression.AcceptVisitor(this);
                Space(policy.SpacesWithinBrackets);
                WriteToken(Roles.RBracket);
            }
            EndNode(fixedVariableInitializer);
        }

        public void VisitIndexerDeclaration(IndexerDeclaration indexerDeclaration)
        {
            StartNode(indexerDeclaration);
            WriteElementStart("indexer");

            if (indexerDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(indexerDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (indexerDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteModifiers(indexerDeclaration.ModifierTokens);
                WritePropertyEnd();
            }

            WritePropertyStart("ReturnType");
            indexerDeclaration.ReturnType.AcceptVisitor(this);
            WritePropertyEnd();

            //Space();
            WritePropertyStart("PrivateImplementationType");
            WritePrivateImplementationType(indexerDeclaration.PrivateImplementationType);
            WritePropertyEnd();
            WriteTagEnd();
            //WriteKeyword(IndexerDeclaration.ThisKeywordRole);
            //Space(policy.SpaceBeforeMethodDeclarationParentheses);
            WriteElementStart("Parameters"); WriteTagEnd();
            foreach(var param in indexerDeclaration.Parameters)
                VisitParameterDeclaration(param);
            WriteElementEnd("Parameters");
            
            NewLine();
            //WriteCommaSeparatedListInBrackets(indexerDeclaration.Parameters, policy.SpaceWithinMethodDeclarationParentheses);
            //OpenBrace(policy.PropertyBraceStyle);
            // output get/set in their original order
            foreach (AstNode node in indexerDeclaration.Children)
            {
                if (node.Role == IndexerDeclaration.GetterRole || node.Role == IndexerDeclaration.SetterRole)
                {
                    node.AcceptVisitor(this);
                }
            }
            //CloseBrace(policy.PropertyBraceStyle);
            //NewLine();
            WriteElementEnd("indexer");
            EndNode(indexerDeclaration);
        }

        public void VisitMethodDeclaration(MethodDeclaration methodDeclaration)
        {
            StartNode(methodDeclaration);
            WriteElementStart("method");
            if (methodDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(methodDeclaration.Attributes);
                WritePropertyEnd();
            }
            if (methodDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(methodDeclaration.ModifierTokens);
                WritePropertyEnd();
            }

            WritePropertyStart("ReturnType");
            methodDeclaration.ReturnType.AcceptVisitor(this);
            WritePropertyEnd();
            WritePropertyStart("PrivateImplementationType");
            WritePrivateImplementationType(methodDeclaration.PrivateImplementationType);
            WritePropertyEnd();
            WritePropertyStart("Name");
            methodDeclaration.NameToken.AcceptVisitor(this);
            WritePropertyEnd();

            if (methodDeclaration.TypeParameters.Count() > 0)
            {
                WritePropertyStart("TypeParameters");
                WriteTypeParameters(methodDeclaration.TypeParameters);
                WritePropertyEnd();
            }
            //Space(policy.SpaceBeforeMethodDeclarationParentheses);
            WritePropertyStart("Constraints");
            foreach (Constraint constraint in methodDeclaration.Constraints)
            {
                constraint.AcceptVisitor(this);
            }
            WritePropertyEnd();
            WriteTagEnd();
            WriteElementStart("Parameters");WriteTagEnd();
            if (methodDeclaration.Parameters.Count > 0)
            {
                foreach (ParameterDeclaration param in methodDeclaration.Parameters)
                {
                    VisitParameterDeclaration(param);
                }
            }
            WriteElementEnd("Parameters");
            //WriteCommaSeparatedListInParenthesis(methodDeclaration.Parameters, policy.SpaceWithinMethodDeclarationParentheses);
            WriteElementStart("Body"); WriteTagEnd();
            if (!methodDeclaration.Body.IsNull)
            {
                WriteCDataStart();
                WriteMethodBody(methodDeclaration.Body);
                WriteCDataEnd();
            }
            WriteElementEnd("Body");
            WriteElementEnd("method");
            EndNode(methodDeclaration);
        }

        public void VisitOperatorDeclaration(OperatorDeclaration operatorDeclaration)
        {
            StartNode(operatorDeclaration);
            WriteAttributes(operatorDeclaration.Attributes);
            WriteModifiers(operatorDeclaration.ModifierTokens);
            if (operatorDeclaration.OperatorType == OperatorType.Explicit)
            {
                WriteKeyword(OperatorDeclaration.ExplicitRole);
            }
            else if (operatorDeclaration.OperatorType == OperatorType.Implicit)
            {
                WriteKeyword(OperatorDeclaration.ImplicitRole);
            }
            else
            {
                operatorDeclaration.ReturnType.AcceptVisitor(this);
            }
            WriteKeyword(OperatorDeclaration.OperatorKeywordRole);
            Space();
            if (operatorDeclaration.OperatorType == OperatorType.Explicit
                || operatorDeclaration.OperatorType == OperatorType.Implicit)
            {
                operatorDeclaration.ReturnType.AcceptVisitor(this);
            }
            else
            {
                WriteToken(OperatorDeclaration.GetToken(operatorDeclaration.OperatorType), OperatorDeclaration.GetRole(operatorDeclaration.OperatorType));
            }
            Space(policy.SpaceBeforeMethodDeclarationParentheses);
            WriteCommaSeparatedListInParenthesis(operatorDeclaration.Parameters, policy.SpaceWithinMethodDeclarationParentheses);
            WriteMethodBody(operatorDeclaration.Body);
            EndNode(operatorDeclaration);
        }

        public void VisitParameterDeclaration(ParameterDeclaration parameterDeclaration)
        {
            StartNode(parameterDeclaration);
            WriteElementStart("Parameter");

            if (parameterDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(parameterDeclaration.Attributes);
                WritePropertyEnd();
            }
            WritePropertyStart("Modifier");
            switch (parameterDeclaration.ParameterModifier)
            {
                case ParameterModifier.Ref:
                    WriteKeyword(ParameterDeclaration.RefModifierRole);
                    break;
                case ParameterModifier.Out:
                    WriteKeyword(ParameterDeclaration.OutModifierRole);
                    break;
                case ParameterModifier.Params:
                    WriteKeyword(ParameterDeclaration.ParamsModifierRole);
                    break;
                case ParameterModifier.This:
                    WriteKeyword(ParameterDeclaration.ThisModifierRole);
                    break;
            }
            WritePropertyEnd();
            WritePropertyStart("Type");
            parameterDeclaration.Type.AcceptVisitor(this);
            WritePropertyEnd();
            /*if (!parameterDeclaration.Type.IsNull && !string.IsNullOrEmpty(parameterDeclaration.Name))
            {
                Space();
            }*/

            if (!string.IsNullOrEmpty(parameterDeclaration.Name))
            {
                WritePropertyStart("Name");
                parameterDeclaration.NameToken.AcceptVisitor(this);
                WritePropertyEnd();
            }
            if (!parameterDeclaration.DefaultExpression.IsNull)
            {
                WritePropertyStart("Default");
                //Space(policy.SpaceAroundAssignment);
                //WriteToken(Roles.Assign);
                //Space(policy.SpaceAroundAssignment);
                parameterDeclaration.DefaultExpression.AcceptVisitor(this);
                WritePropertyEnd();
            }
            WriteElementTail();
            EndNode(parameterDeclaration);
        }

        public void VisitPropertyDeclaration(PropertyDeclaration propertyDeclaration)
        {
            StartNode(propertyDeclaration);
            WriteElementStart("property");

            if (propertyDeclaration.Attributes.Count() > 0)
            {
                WritePropertyStart("Attributes");
                WriteAttributes(propertyDeclaration.Attributes);
                WritePropertyEnd();
            }

            if (propertyDeclaration.ModifierTokens.Count() > 0)
            {
                WritePropertyStart("Modifier");
                WriteModifiers(propertyDeclaration.ModifierTokens);
                WritePropertyEnd();
            }

            WritePropertyStart("ReturnType");
            propertyDeclaration.ReturnType.AcceptVisitor(this);
            WritePropertyEnd();
            //Space();
            WritePropertyStart("PrivateImplementationType");
            WritePrivateImplementationType(propertyDeclaration.PrivateImplementationType);
            WritePropertyEnd();
            
            WritePropertyStart("Name");
            propertyDeclaration.NameToken.AcceptVisitor(this);
            WritePropertyEnd();
            WriteTagEnd();
            //OpenBrace(policy.PropertyBraceStyle);
            // output get/set in their original order
            foreach (AstNode node in propertyDeclaration.Children)
            {
                if (node.Role == IndexerDeclaration.GetterRole || node.Role == IndexerDeclaration.SetterRole){
                    node.AcceptVisitor(this);
                }
            }
            
            WriteElementEnd("property");
            //CloseBrace(policy.PropertyBraceStyle);
            //NewLine();
            EndNode(propertyDeclaration);
        }

        #endregion

        #region Other nodes
        public void VisitVariableInitializer(VariableInitializer variableInitializer)
        {
            StartNode(variableInitializer);
            variableInitializer.NameToken.AcceptVisitor(this);
            if (!variableInitializer.Initializer.IsNull)
            {
                Space(policy.SpaceAroundAssignment);
                WriteToken(Roles.Assign);
                Space(policy.SpaceAroundAssignment);
                variableInitializer.Initializer.AcceptVisitor(this);
            }
            EndNode(variableInitializer);
        }

        public void VisitSyntaxTree(SyntaxTree syntaxTree)
        {
            //formatter.WriteComment(CommentType.InactiveCode, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            //NewLine();
            // don't do node tracking as we visit all children directly
            formatter.WriteComment(CommentType.SingleLine, syntaxTree.FileName);
            foreach (AstNode node in syntaxTree.Children)
            {
                node.AcceptVisitor(this);
            }
        }

        public void VisitSimpleType(SimpleType simpleType)
        {
            StartNode(simpleType);
            WriteIdentifier(simpleType.Identifier);
            WriteTypeArguments(simpleType.TypeArguments);
            EndNode(simpleType);
        }

        public void VisitMemberType(MemberType memberType)
        {
            StartNode(memberType);
            memberType.Target.AcceptVisitor(this);
            if (memberType.IsDoubleColon)
            {
                WriteToken(Roles.DoubleColon);
            }
            else
            {
                WriteToken(Roles.Dot);
            }
            WriteIdentifier(memberType.MemberName);
            WriteTypeArguments(memberType.TypeArguments);
            EndNode(memberType);
        }

        public void VisitComposedType(ComposedType composedType)
        {
            StartNode(composedType);
            composedType.BaseType.AcceptVisitor(this);
            if (composedType.HasNullableSpecifier)
            {
                WriteToken(ComposedType.NullableRole);
            }
            for (int i = 0; i < composedType.PointerRank; i++)
            {
                WriteToken(ComposedType.PointerRole);
            }
            foreach (var node in composedType.ArraySpecifiers)
            {
                node.AcceptVisitor(this);
            }
            EndNode(composedType);
        }

        public void VisitArraySpecifier(ArraySpecifier arraySpecifier)
        {
            StartNode(arraySpecifier);
            WriteToken(Roles.LBracket);
            foreach (var comma in arraySpecifier.GetChildrenByRole(Roles.Comma))
            {
                WriteSpecialsUpToNode(comma);
                formatter.WriteToken(",");
                lastWritten = LastWritten.Other;
            }
            WriteToken(Roles.RBracket);
            EndNode(arraySpecifier);
        }

        public void VisitPrimitiveType(PrimitiveType primitiveType)
        {
            StartNode(primitiveType);
            WriteKeyword(primitiveType.Keyword);
            if (primitiveType.Keyword == "new")
            {
                // new() constraint
                LPar();
                RPar();
            }
            EndNode(primitiveType);
        }

        public void VisitComment(Comment comment)
        {
            if (lastWritten == LastWritten.Division)
            {
                // When there's a comment starting after a division operator
                // "1.0 / /*comment*/a", then we need to insert a space in front of the comment.
                formatter.Space();
            }
            formatter.StartNode(comment);
            formatter.WriteComment(comment.CommentType, comment.Content);
            formatter.EndNode(comment);
            lastWritten = LastWritten.Whitespace;
        }

        public void VisitNewLine(NewLineNode newLineNode)
        {
            formatter.StartNode(newLineNode);
            formatter.NewLine();
            formatter.EndNode(newLineNode);
        }

        public void VisitWhitespace(WhitespaceNode whitespaceNode)
        {
            // unused
        }

        public void VisitText(TextNode textNode)
        {
            // unused
        }

        public void VisitPreProcessorDirective(PreProcessorDirective preProcessorDirective)
        {
            formatter.StartNode(preProcessorDirective);
            formatter.WritePreProcessorDirective(preProcessorDirective.Type, preProcessorDirective.Argument);
            formatter.EndNode(preProcessorDirective);
            lastWritten = LastWritten.Whitespace;
        }

        public void VisitTypeParameterDeclaration(TypeParameterDeclaration typeParameterDeclaration)
        {
            StartNode(typeParameterDeclaration);
            WriteAttributes(typeParameterDeclaration.Attributes);
            switch (typeParameterDeclaration.Variance)
            {
                case VarianceModifier.Invariant:
                    break;
                case VarianceModifier.Covariant:
                    WriteKeyword(TypeParameterDeclaration.OutVarianceKeywordRole);
                    break;
                case VarianceModifier.Contravariant:
                    WriteKeyword(TypeParameterDeclaration.InVarianceKeywordRole);
                    break;
                default:
                    throw new NotSupportedException("Invalid value for VarianceModifier");
            }
            typeParameterDeclaration.NameToken.AcceptVisitor(this);
            EndNode(typeParameterDeclaration);
        }

        public void VisitConstraint(Constraint constraint)
        {
            StartNode(constraint);
            Space();
            WriteKeyword(Roles.WhereKeyword);
            WriteIdentifier(constraint.TypeParameter.Identifier);
            Space();
            WriteToken(Roles.Colon);
            Space();
            WriteCommaSeparatedList(constraint.BaseTypes);
            EndNode(constraint);
        }

        /* RealName is VisitModifierToken */
        public void VisitCSharpTokenNode(CSharpTokenNode cSharpTokenNode)
        {
            CSharpModifierToken mod = cSharpTokenNode as CSharpModifierToken;
            if (mod != null)
            {
                StartNode(mod);
                WriteKeyword(CSharpModifierToken.GetModifierName(mod.Modifier));
                EndNode(mod);
            }
            else
            {
                throw new NotSupportedException("Should never visit individual tokens");
            }
        }

        public void VisitIdentifier(Identifier identifier)
        {
            StartNode(identifier);
            WriteIdentifier(identifier.Name);
            EndNode(identifier);
        }

        #endregion

        #region Pattern Nodes
        public void VisitPatternPlaceholder(AstNode placeholder, ICSharpCode.NRefactory.PatternMatching.Pattern pattern)
        {
            StartNode(placeholder);
            VisitNodeInPattern(pattern);
            EndNode(placeholder);
        }

        void VisitAnyNode(AnyNode anyNode)
        {
            if (!string.IsNullOrEmpty(anyNode.GroupName))
            {
                WriteIdentifier(anyNode.GroupName);
                WriteToken(Roles.Colon);
            }
        }

        void VisitBackreference(Backreference backreference)
        {
            WriteKeyword("backreference");
            LPar();
            WriteIdentifier(backreference.ReferencedGroupName);
            RPar();
        }

        void VisitIdentifierExpressionBackreference(IdentifierExpressionBackreference identifierExpressionBackreference)
        {
            WriteKeyword("identifierBackreference");
            LPar();
            WriteIdentifier(identifierExpressionBackreference.ReferencedGroupName);
            RPar();
        }

        void VisitChoice(Choice choice)
        {
            WriteKeyword("choice");
            Space();
            LPar();
            NewLine();
            formatter.Indent();
            foreach (INode alternative in choice)
            {
                VisitNodeInPattern(alternative);
                if (alternative != choice.Last())
                {
                    WriteToken(Roles.Comma);
                }
                NewLine();
            }
            formatter.Unindent();
            RPar();
        }

        void VisitNamedNode(NamedNode namedNode)
        {
            if (!string.IsNullOrEmpty(namedNode.GroupName))
            {
                WriteIdentifier(namedNode.GroupName);
                WriteToken(Roles.Colon);
            }
            VisitNodeInPattern(namedNode.ChildNode);
        }

        void VisitRepeat(Repeat repeat)
        {
            WriteKeyword("repeat");
            LPar();
            if (repeat.MinCount != 0 || repeat.MaxCount != int.MaxValue)
            {
                WriteIdentifier(repeat.MinCount.ToString());
                WriteToken(Roles.Comma);
                WriteIdentifier(repeat.MaxCount.ToString());
                WriteToken(Roles.Comma);
            }
            VisitNodeInPattern(repeat.ChildNode);
            RPar();
        }

        void VisitOptionalNode(OptionalNode optionalNode)
        {
            WriteKeyword("optional");
            LPar();
            VisitNodeInPattern(optionalNode.ChildNode);
            RPar();
        }

        void VisitNodeInPattern(INode childNode)
        {
            if (childNode is AstNode)
            {
                ((AstNode)childNode).AcceptVisitor(this);
            }
            else if (childNode is IdentifierExpressionBackreference)
            {
                VisitIdentifierExpressionBackreference((IdentifierExpressionBackreference)childNode);
            }
            else if (childNode is Choice)
            {
                VisitChoice((Choice)childNode);
            }
            else if (childNode is AnyNode)
            {
                VisitAnyNode((AnyNode)childNode);
            }
            else if (childNode is Backreference)
            {
                VisitBackreference((Backreference)childNode);
            }
            else if (childNode is NamedNode)
            {
                VisitNamedNode((NamedNode)childNode);
            }
            else if (childNode is OptionalNode)
            {
                VisitOptionalNode((OptionalNode)childNode);
            }
            else if (childNode is Repeat)
            {
                VisitRepeat((Repeat)childNode);
            }
            else
            {
                WritePrimitiveValue(childNode);
            }
        }
        #endregion

        #region Documentation Reference
        public void VisitDocumentationReference(DocumentationReference documentationReference)
        {
            StartNode(documentationReference);
            if (!documentationReference.DeclaringType.IsNull)
            {
                documentationReference.DeclaringType.AcceptVisitor(this);
                if (documentationReference.EntityType != EntityType.TypeDefinition)
                {
                    WriteToken(Roles.Dot);
                }
            }
            switch (documentationReference.EntityType)
            {
                case EntityType.TypeDefinition:
                    // we already printed the DeclaringType
                    break;
                case EntityType.Indexer:
                    WriteKeyword(IndexerDeclaration.ThisKeywordRole);
                    break;
                case EntityType.Operator:
                    var opType = documentationReference.OperatorType;
                    if (opType == OperatorType.Explicit)
                    {
                        WriteKeyword(OperatorDeclaration.ExplicitRole);
                    }
                    else if (opType == OperatorType.Implicit)
                    {
                        WriteKeyword(OperatorDeclaration.ImplicitRole);
                    }
                    WriteKeyword(OperatorDeclaration.OperatorKeywordRole);
                    Space();
                    if (opType == OperatorType.Explicit || opType == OperatorType.Implicit)
                    {
                        documentationReference.ConversionOperatorReturnType.AcceptVisitor(this);
                    }
                    else
                    {
                        WriteToken(OperatorDeclaration.GetToken(opType), OperatorDeclaration.GetRole(opType));
                    }
                    break;
                default:
                    WriteIdentifier(documentationReference.MemberName);
                    break;
            }
            WriteTypeArguments(documentationReference.TypeArguments);
            if (documentationReference.HasParameterList)
            {
                Space(policy.SpaceBeforeMethodDeclarationParentheses);
                if (documentationReference.EntityType == EntityType.Indexer)
                {
                    WriteCommaSeparatedListInBrackets(documentationReference.Parameters, policy.SpaceWithinMethodDeclarationParentheses);
                }
                else
                {
                    WriteCommaSeparatedListInParenthesis(documentationReference.Parameters, policy.SpaceWithinMethodDeclarationParentheses);
                }
            }
            EndNode(documentationReference);
        }
        #endregion
    }
}

