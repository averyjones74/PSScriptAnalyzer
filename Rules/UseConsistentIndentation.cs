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
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// A class to walk an AST to check for [violation]
    /// </summary>
#if !CORECLR
    [Export(typeof(IScriptRule))]
#endif
    class UseConsistentIndentation : IScriptRule
    {
        private readonly int unitsPerIndentationLevel;

        UseConsistentIndentation()
        {
            // TODO Add a parameter for indentation kind {Tab, Space}
            // TODO make this configurable
            unitsPerIndentationLevel = 4;
        }

        /// <summary>
        /// Analyzes the given ast to find the [violation]
        /// </summary>
        /// <param name="ast">AST to be analyzed. This should be non-null</param>
        /// <param name="fileName">Name of file that corresponds to the input AST.</param>
        /// <returns>A an enumerable type containing the violations</returns>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null)
            {
                throw new ArgumentNullException("ast");
            }

            var tokens = Helper.Instance.Tokens;
            var diagnosticRecords = new List<DiagnosticRecord>();
            var openBracePosStack = new Stack<int>();
            bool onNewLine = true;
            for (int k = 0; k < tokens.Length; k++)
            {
                var token = tokens[k];
                var curIndentationLevel = GetIndentationLevel(openBracePosStack);
                var curIndentation = GetIndentation(curIndentationLevel);
                switch (token.Kind)
                {
                    case TokenKind.LCurly:
                        AddViolation(token, curIndentationLevel, diagnosticRecords, ref onNewLine);
                        openBracePosStack.Push(k);
                        break;

                    case TokenKind.RCurly:
                        if (openBracePosStack.Count > 0)
                        {
                            openBracePosStack.Pop();
                        }
                        AddViolation(token, GetIndentationLevel(openBracePosStack), diagnosticRecords, ref onNewLine);
                        break;

                    case TokenKind.NewLine:
                        onNewLine = true;
                        break;

                    default:
                        // we do not want to make a call for every token, hence
                        // we add this redundant check
                        if (onNewLine)
                        {
                            AddViolation(token, curIndentationLevel, diagnosticRecords, ref onNewLine);
                        }
                        break;
                }
            }

            return diagnosticRecords;
        }

        private void AddViolation(
            Token token,
            int curIndentationLevel,
            List<DiagnosticRecord> diagnosticRecords,
            ref bool onNewLine)
        {
            if (onNewLine)
            {
                onNewLine = false;
                if (token.Extent.StartColumnNumber - 1 != GetIndentation(curIndentationLevel))
                {
                    var fileName = token.Extent.File;
                    var extent = token.Extent;
                    var violationExtent = extent = new ScriptExtent(
                        new ScriptPosition(
                            fileName,
                            extent.StartLineNumber,
                            1, // first column in the line
                            extent.StartScriptPosition.Line),
                        new ScriptPosition(
                            fileName,
                            extent.StartLineNumber,
                            extent.StartColumnNumber,
                            extent.StartScriptPosition.Line));
                    diagnosticRecords.Add(
                        new DiagnosticRecord(
                            "not correct indenation", // TODO replace with localized string
                            violationExtent,
                            GetName(),
                            GetDiagnosticSeverity(),
                            fileName,
                            null,
                            null));
                }
            }
        }

        private static int ClipNegative(int x)
        {
            return x > 0 ? x : 0;
        }

        private int GetIndentationColumnNumber(int indentationLevel)
        {
            return GetIndentation(indentationLevel) + 1;
        }

        private int GetIndentation(int indentationLevel)
        {
            return indentationLevel * this.unitsPerIndentationLevel;
        }

        private int GetIndentationLevel(Stack<int> openBracePosStack)
        {
            return openBracePosStack.Count;
        }

        /// <summary>
        /// Retrieves the common name of this rule.
        /// </summary>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseConsistentIndentationCommonName);
        }

        /// <summary>
        /// Retrieves the description of this rule.
        /// </summary>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseConsistentIndentationDescription);
        }

        /// <summary>
        /// Retrieves the name of this rule.
        /// </summary>
        public string GetName()
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.NameSpaceFormat,
                GetSourceName(),
                Strings.UseConsistentIndentationName);
        }

        /// <summary>
        /// Retrieves the severity of the rule: error, warning or information.
        /// </summary>
        public RuleSeverity GetSeverity()
        {
            return RuleSeverity.Information;
        }

        /// <summary>
        /// Gets the severity of the returned diagnostic record: error, warning, or information.
        /// </summary>
        /// <returns></returns>
        public DiagnosticSeverity GetDiagnosticSeverity()
        {
            return DiagnosticSeverity.Information;
        }

        /// <summary>
        /// Retrieves the name of the module/assembly the rule is from.
        /// </summary>
        public string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }

        /// <summary>
        /// Retrieves the type of the rule, Builtin, Managed or Module.
        /// </summary>
        public SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }
    }
}
