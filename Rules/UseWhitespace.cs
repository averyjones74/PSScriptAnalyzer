﻿// Copyright (c) Microsoft Corporation.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
#if !CORECLR
using System.ComponentModel.Composition;
#endif
using System.Globalization;
using System.Linq;
using System.Management.Automation.Language;
using System.Text;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// A class to walk an AST to check for [violation]
    /// </summary>
#if !CORECLR
    [Export(typeof(IScriptRule))]
#endif
    public class UseWhitespace : ConfigurableRule
    {
        private enum ErrorKind { Brace, Paren, Operator };
        private const int whiteSpaceSize = 1;
        private const string whiteSpace = " ";

        private List<Func<TokenOperations, IEnumerable<DiagnosticRecord>>> violationFinders
                = new List<Func<TokenOperations, IEnumerable<DiagnosticRecord>>>();

        [ConfigurableRuleProperty(defaultValue: true)]
        public bool CheckOpenBrace { get; protected set; }

        [ConfigurableRuleProperty(defaultValue: true)]
        public bool CheckOpenParen { get; protected set; }

        [ConfigurableRuleProperty(defaultValue: true)]
        public bool CheckOperator { get; protected set; }

        public override void ConfigureRule(IDictionary<string, object> paramValueMap)
        {
            base.ConfigureRule(paramValueMap);
            if (CheckOpenBrace)
            {
                violationFinders.Add(FindOpenBraceViolations);
            }

            if (CheckOpenParen)
            {
                violationFinders.Add(FindOpenParenViolations);
            }

            if (CheckOperator)
            {
                violationFinders.Add(FindOperatorViolations);
            }
        }

        /// <summary>
        /// Analyzes the given ast to find the [violation]
        /// </summary>
        /// <param name="ast">AST to be analyzed. This should be non-null</param>
        /// <param name="fileName">Name of file that corresponds to the input AST.</param>
        /// <returns>A an enumerable type containing the violations</returns>
        public override IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null)
            {
                throw new ArgumentNullException("ast");
            }

            var tokenOperations = new TokenOperations(Helper.Instance.Tokens, ast);
            var diagnosticRecords = Enumerable.Empty<DiagnosticRecord>();
            foreach (var violationFinder in violationFinders)
            {
                diagnosticRecords = diagnosticRecords.Concat(violationFinder(tokenOperations));
            }

            return diagnosticRecords.ToArray(); // force evaluation here
        }

        private string GetError(ErrorKind kind)
        {
            switch (kind)
            {
                case ErrorKind.Brace:
                    return string.Format(CultureInfo.CurrentCulture, Strings.UseWhitespaceErrorBeforeBrace);
                case ErrorKind.Operator:
                    return string.Format(CultureInfo.CurrentCulture, Strings.UseWhitespaceErrorOperator);
                default:
                    return string.Format(CultureInfo.CurrentCulture, Strings.UseWhitespaceErrorBeforeParen);
            }
        }

        private IEnumerable<DiagnosticRecord> FindOpenBraceViolations(TokenOperations tokenOperations)
        {
            foreach (var lcurly in tokenOperations.GetTokenNodes(TokenKind.LCurly))
            {
                if (lcurly.Previous == null
                    || !IsPreviousTokenOnSameLine(lcurly)
                    || lcurly.Previous.Value.Kind == TokenKind.LCurly)
                {
                    continue;
                }

                if (!IsPreviousTokenApartByWhitespace(lcurly))
                {
                    yield return new DiagnosticRecord(
                        GetError(ErrorKind.Brace),
                        lcurly.Value.Extent,
                        GetName(),
                        GetDiagnosticSeverity(),
                        tokenOperations.Ast.Extent.File,
                        null,
                        GetOpenBracketCorrections(lcurly.Value).ToList());
                }
            }
        }

        private IEnumerable<DiagnosticRecord> FindOpenParenViolations(TokenOperations tokenOperations)
        {
            foreach (var lparen in tokenOperations.GetTokenNodes(TokenKind.LParen))
            {
                if (lparen.Previous == null
                    || !IsPreviousTokenOnSameLine(lparen)
                    || lparen.Previous.Value.Kind == TokenKind.LParen // if nested paren
                    || lparen.Previous.Value.Kind == TokenKind.Param  // if param block
                    || (lparen.Previous.Previous != null
                        && lparen.Previous.Previous.Value.Kind == TokenKind.Function)) //if function block

                {
                    continue;
                }

                if (!IsPreviousTokenApartByWhitespace(lparen))
                {
                    yield return new DiagnosticRecord(
                        GetError(ErrorKind.Paren),
                        lparen.Value.Extent,
                        GetName(),
                        GetDiagnosticSeverity(),
                        tokenOperations.Ast.Extent.File,
                        null,
                        GetOpenBracketCorrections(lparen.Value).ToList());
                }
            }
        }

        private IEnumerable<CorrectionExtent> GetOpenBracketCorrections(Token token)
        {
            yield return new CorrectionExtent(
                token.Extent,
                whiteSpace + token.Text,
                token.Extent.File,
                GetError(ErrorKind.Brace));
        }

        private bool IsPreviousTokenApartByWhitespace(LinkedListNode<Token> tokenNode)
        {
            return whiteSpaceSize ==
                (tokenNode.Value.Extent.StartColumnNumber - tokenNode.Previous.Value.Extent.EndColumnNumber);
        }

        private IEnumerable<DiagnosticRecord> FindOperatorViolations(TokenOperations tokenOperations)
        {
            Func<LinkedListNode<Token>, bool> predicate = tokenNode =>
            {
                return tokenNode.Previous != null
                    && IsPreviousTokenOnSameLine(tokenNode)
                    && IsPreviousTokenApartByWhitespace(tokenNode);
            };

            foreach (var tokenNode in tokenOperations.GetTokenNodes(IsOperator))
            {
                var hasWhitespaceBefore = false;
                var hasWhitespaceAfter = false;
                if (predicate(tokenNode))
                {
                    hasWhitespaceBefore = true;
                }

                if (predicate(tokenNode.Next))
                {
                    hasWhitespaceAfter = true;
                }

                if (!hasWhitespaceAfter || !hasWhitespaceBefore)
                {
                    yield return new DiagnosticRecord(
                        GetError(ErrorKind.Operator),
                        tokenNode.Value.Extent,
                        GetName(),
                        GetDiagnosticSeverity(),
                        tokenOperations.Ast.Extent.File,
                        null,
                        GetOperatorCorrections(tokenNode.Value, hasWhitespaceBefore, hasWhitespaceAfter).ToList());
                }
            }
        }

        private IEnumerable<CorrectionExtent> GetOperatorCorrections(
            Token token,
            bool hasWhitespaceBefore,
            bool hasWhitespaceAfter)
        {
            var sb = new StringBuilder();
            if (!hasWhitespaceBefore)
            {
                sb.Append(whiteSpace);
            }

            sb.Append(token.Text);
            if (!hasWhitespaceAfter)
            {
                sb.Append(whiteSpace);
            }

            yield return new CorrectionExtent(
                token.Extent,
                sb.ToString(),
                token.Extent.File,
                GetError(ErrorKind.Operator));
        }

        private bool IsOperator(Token token)
        {
            return TokenTraits.HasTrait(token.Kind, TokenFlags.AssignmentOperator)
                    || TokenTraits.HasTrait(token.Kind, TokenFlags.BinaryPrecedenceAdd)
                    || TokenTraits.HasTrait(token.Kind, TokenFlags.BinaryPrecedenceMultiply)
                    || token.Kind == TokenKind.AndAnd
                    || token.Kind == TokenKind.OrOr;
        }

        private bool IsPreviousTokenOnSameLine(LinkedListNode<Token> lparen)
        {
            return lparen.Previous.Value.Extent.StartLineNumber == lparen.Value.Extent.EndLineNumber;
        }

        /// <summary>
        /// Retrieves the common name of this rule.
        /// </summary>
        public override string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseWhitespaceCommonName);
        }

        /// <summary>
        /// Retrieves the description of this rule.
        /// </summary>
        public override string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseWhitespaceDescription);
        }

        /// <summary>
        /// Retrieves the name of this rule.
        /// </summary>
        public override string GetName()
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.NameSpaceFormat,
                GetSourceName(),
                Strings.UseWhitespaceName);
        }

        /// <summary>
        /// Retrieves the severity of the rule: error, warning or information.
        /// </summary>
        public override RuleSeverity GetSeverity()
        {
            return RuleSeverity.Warning;
        }

        /// <summary>
        /// Gets the severity of the returned diagnostic record: error, warning, or information.
        /// </summary>
        /// <returns></returns>
        public DiagnosticSeverity GetDiagnosticSeverity()
        {
            return DiagnosticSeverity.Warning;
        }

        /// <summary>
        /// Retrieves the name of the module/assembly the rule is from.
        /// </summary>
        public override string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }

        /// <summary>
        /// Retrieves the type of the rule, Builtin, Managed or Module.
        /// </summary>
        public override SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }
    }
}
