﻿/********************************************************
*                                                        *
*   © Copyright (C) Microsoft. All rights reserved.      *
*                                                        *
*********************************************************/

namespace Microsoft.VisualStudio.Threading.Analyzers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>
    /// Analyzes the usages on AsyncEventHandler delegates and reports warning if
    /// they are invoked NOT using the extension method TplExtensions.InvokeAsync()
    /// in Microsoft.VisualStudio.Threading assembly.
    /// </summary>
    /// <remarks>
    /// [Background] AsyncEventHandler returns a Task and the default invocation mechanism
    /// does not handle the faults thrown from the Tasks. That is why TplExtensions.InvokeAsync()
    /// was invented to solve that problem. TplExtensions.InvokeAsync() will ensure all the delegates
    /// are executed, aggregate the thrown exceptions, and re-throw the aggregated exception.
    /// It is always better to use TplExtensions.InvokeAsync() for AsyncEventHandler delegates.
    ///
    /// i.e.
    /// <![CDATA[
    ///   void Test(AsyncEventHandler handler) {
    ///       handler(sender, args); /* This analyzer will report warning on this invocation. */
    ///   }
    /// ]]>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AsyncEventHandlerAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(Rules.AsyncEventHandlerShouldBeCalledByInvokeAsync);
            }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCodeBlockStartAction<SyntaxKind>(ctxt =>
            {
                // This is a very specical case to check if this method is TplExtensions.InvokeAsync().
                // If it is, then do not run the ananlyzer inside that method.
                if (!(Utils.GetFullName(ctxt.OwningSymbol.ContainingType) == TypeIdentifiers.TplExtensions.FullName
                      && ctxt.OwningSymbol.Name == TypeIdentifiers.TplExtensions.InvokeAsyncName))
                {
                    ctxt.RegisterSyntaxNodeAction(this.AnalyzeInvocation, SyntaxKind.InvocationExpression);
                }
            });
        }

        /// <summary>
        /// Analyze each invocation syntax node.
        /// </summary>
        private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            var symbol = context.SemanticModel.GetSymbolInfo(invocation.Expression).Symbol;
            if (symbol != null)
            {
                ISymbol type = null;
                if (symbol.Kind == SymbolKind.Method)
                {
                    // Handle the case when call into AsyncEventHandler via Invoke() method.
                    // i.e.
                    // AsyncEventHandler handler;
                    // handler.Invoke(null, null);
                    type = symbol.ContainingType;
                }
                else
                {
                    type = Utils.ResolveTypeFromSymbol(symbol);
                }

                if (type != null)
                {
                    var fullName = Utils.GetFullName(type);
                    if (fullName == TypeIdentifiers.AsyncEventHandler.FullName)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rules.AsyncEventHandlerShouldBeCalledByInvokeAsync, context.Node.GetLocation()));
                    }
                }
            }
        }
    }
}