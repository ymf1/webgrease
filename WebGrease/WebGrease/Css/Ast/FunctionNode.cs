// --------------------------------------------------------------------------------------------------------------------
// <copyright file="FunctionNode.cs" company="Microsoft">
//   Copyright Microsoft Corporation, all rights reserved
// </copyright>
// <summary>
//   function
//   FUNCTION S* expr ')' S*
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace WebGrease.Css.Ast
{
    using System.Diagnostics.Contracts;
    using Visitor;

    /// <summary>function
    /// FUNCTION S* expr ')' S*</summary>
    public sealed class FunctionNode : AstNode
    {
        /// <summary>Initializes a new instance of the FunctionNode class</summary>
        /// <param name="functionName">FunctionNode name</param>
        /// <param name="exprNode">Expression object</param>
        public FunctionNode(string functionName, ExprNode exprNode)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(functionName));

            this.FunctionName = functionName;
            this.ExprNode = exprNode;
        }

        /// <summary>
        /// Gets the function name
        /// </summary>
        /// <value>FunctionNode name</value>
        public string FunctionName { get; private set; }

        /// <summary>
        /// Gets the expression value
        /// </summary>
        /// <value>Expression value</value>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Expr")]
        public ExprNode ExprNode { get; private set; }

        /// <summary>Defines an accept operation</summary>
        /// <param name="nodeVisitor">The visitor to invoke</param>
        /// <returns>The modified AST node if modified otherwise the original node</returns>
        public override AstNode Accept(NodeVisitor nodeVisitor)
        {
            return nodeVisitor.VisitFunctionNode(this);
        }
    }
}
