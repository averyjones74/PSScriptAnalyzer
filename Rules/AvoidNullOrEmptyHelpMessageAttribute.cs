﻿//
// Copyright (c) Microsoft Corporation.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;
using System.ComponentModel.Composition;
using System.Globalization;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// AvoidUsingNullOrEmptyHelpMessageParameter: Check if the HelpMessage parameter is set to a non-empty string.
    /// </summary>
    [Export(typeof(IScriptRule))]
    public class AvoidNullOrEmptyHelpMessageAttribute : IScriptRule
    {               
        /// <summary>
        /// AvoidUsingNullOrEmptyHelpMessageParameter: Check if the HelpMessage parameter is set to a non-empty string.
        /// </summary>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            // Finds all functionAst
            IEnumerable<Ast> functionAsts = ast.FindAll(testAst => testAst is FunctionDefinitionAst, true);

            foreach (FunctionDefinitionAst funcAst in functionAsts)
            {
                if (funcAst.Body == null || funcAst.Body.ParamBlock == null
                        || funcAst.Body.ParamBlock.Attributes == null || funcAst.Body.ParamBlock.Parameters == null)               
                    continue;
                                           
                foreach (var paramAst in funcAst.Body.ParamBlock.Parameters)
                {                        
                    foreach (var paramAstAttribute in paramAst.Attributes)
                    {
                        if (!(paramAstAttribute is AttributeAst))
                            continue;

                        var namedArguments = (paramAstAttribute as AttributeAst).NamedArguments;

                        if (namedArguments == null)
                            continue;

                        foreach (NamedAttributeArgumentAst namedArgument in namedArguments)
                        {
                            if (!(String.Equals(namedArgument.ArgumentName, "HelpMessage", StringComparison.OrdinalIgnoreCase))
                                 || namedArgument.ExpressionOmitted)
                                continue;
                            
                            string errCondition;
                            if (namedArgument.Argument.Extent.Text.Equals("\"\""))
                            {
                                errCondition = "empty";
                            }
                            else if (namedArgument.Argument.Extent.Text.Equals("$null", StringComparison.OrdinalIgnoreCase))
                            {
                                errCondition = "null";
                            }
                            else
                            {
                                errCondition = null;
                            }

                            if (!String.IsNullOrEmpty(errCondition))
                            {
                                string message = string.Format(CultureInfo.CurrentCulture,
                                                                Strings.AvoidNullOrEmptyHelpMessageAttributeError,
                                                                paramAst.Name.VariablePath.UserPath);
                                yield return new DiagnosticRecord(message,
                                                                    paramAst.Extent, 
                                                                    GetName(), 
                                                                    DiagnosticSeverity.Error, 
                                                                    fileName, 
                                                                    paramAst.Name.VariablePath.UserPath);
                            }                            
                        }                                                
                    }
                }
                
            }
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.AvoidNullOrEmptyHelpMessageAttributeName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidNullOrEmptyHelpMessageAttributeCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidNullOrEmptyHelpMessageAttributeDescription);
        }

        /// <summary>
        /// GetSourceType: Retrieves the type of the rule, builtin, managed or module.
        /// </summary>
        public SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }

        /// <summary>
        /// GetSeverity: Retrieves the severity of the rule: error, warning of information.
        /// </summary>
        /// <returns></returns>
        public RuleSeverity GetSeverity()
        {
            return RuleSeverity.Error;
        }

        /// <summary>
        /// GetSourceName: Retrieves the module/assembly name the rule is from.
        /// </summary>
        public string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }
    }
}
