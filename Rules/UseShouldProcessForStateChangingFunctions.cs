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
using System.ComponentModel.Composition;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Globalization;
using System.Linq;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{   
    /// <summary>
    /// UseShouldProcessForStateChangingFunctions: Analyzes the ast to check if ShouldProcess is included in Advanced functions if the Verb of the function could change system state.
    /// </summary>
    [Export(typeof(IScriptRule))]
    public class UseShouldProcessForStateChangingFunctions : IScriptRule
    {
        /// <summary>
        /// Array of verbs that can potentially change the state of a system
        /// </summary>
        private string[] stateChangingVerbs =
        {
            "Restart-",
            "Stop-",
            "New-",
            "Set-",
            "Update-",
            "Reset-",
            "Remove-"
        };

        /// <summary>
        /// AnalyzeScript: Analyzes the ast to check if ShouldProcess is included in Advanced functions if the Verb of the function could change system state.
        /// </summary>
        /// <param name="ast">The script's ast</param>
        /// <param name="fileName">The script's file name</param>
        /// <returns>A List of diagnostic results of this rule</returns>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null)
            {
                throw new ArgumentNullException(Strings.NullAstErrorMessage);
            }
            IEnumerable<Ast> funcDefWithNoShouldProcessAttrAsts = ast.FindAll(IsStateChangingFunctionWithNoShouldProcessAttribute, true);            
            foreach (FunctionDefinitionAst funcDefAst in funcDefWithNoShouldProcessAttrAsts)
            {
                yield return new DiagnosticRecord(
                    string.Format(CultureInfo.CurrentCulture, Strings.UseShouldProcessForStateChangingFunctionsError, funcDefAst.Name), 
                    Helper.Instance.GetScriptExtentForFunctionName(funcDefAst),                    
                    this.GetName(), 
                    DiagnosticSeverity.Warning, 
                    fileName);
            }
                            
        }
        /// <summary>
        /// Checks if the ast defines a state changing function
        /// </summary>
        /// <param name="ast"></param>
        /// <returns>Returns true or false</returns>
        private bool IsStateChangingFunctionWithNoShouldProcessAttribute(Ast ast)
        {
            var funcDefAst = ast as FunctionDefinitionAst;
            if (funcDefAst == null)
            {
                return false;
            }
            string funcName = funcDefAst.Name;
            var targetFuncName = this.stateChangingVerbs.Where(
                elem => funcName.StartsWith(
                    elem,
                    StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (targetFuncName == null                
                || funcDefAst.Body.ParamBlock == null
                || funcDefAst.Body.ParamBlock.Attributes == null)
            {
                return false;
            }
            return !HasShouldProcessTrue(funcDefAst.Body.ParamBlock.Attributes);
        }
        
        /// <summary>
        /// Checks if an attribute has SupportShouldProcess set to $true
        /// </summary>
        /// <param name="attributeAsts"></param>
        /// <returns>Returns true or false</returns>
        private bool HasShouldProcessTrue(IEnumerable<AttributeAst> attributeAsts)
        {
            foreach (var attributeAst in attributeAsts)
            {                
                if (attributeAst == null || attributeAst.NamedArguments == null)
                {
                    continue;
                }
                if (attributeAst.TypeName.GetReflectionAttributeType() 
                    == typeof(CmdletBindingAttribute))
                {
                    foreach (var namedAttributeAst in attributeAst.NamedArguments)
                    {
                        if (namedAttributeAst == null
                            || !namedAttributeAst.ArgumentName.Equals(
                                "SupportsShouldProcess", 
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        return IsShouldProcessTrue(namedAttributeAst);                        
                    }
                }
            }
            // Cannot find any SupportShouldProcess attribute   
            return false;
        }

        /// <summary>
        /// Checks if the SupportShouldProcess attribute is true
        /// </summary>
        /// <param name="namedAttributeArgumentAst"></param>
        /// <returns>Returns true or false</returns>
        private bool IsShouldProcessTrue(NamedAttributeArgumentAst namedAttributeArgumentAst)
        {
            if (namedAttributeArgumentAst.ExpressionOmitted)
            {
                return true;
            }
            else
            {
                var varExpAst = namedAttributeArgumentAst.Argument as VariableExpressionAst;
                if (varExpAst == null)
                {
                    var constExpAst = namedAttributeArgumentAst.Argument as ConstantExpressionAst;
                    if (constExpAst == null)
                    {
                        return false;
                    }
                    bool constExpVal;
                    if (LanguagePrimitives.TryConvertTo<bool>(constExpAst.Value, out constExpVal))
                    {
                        return constExpVal;
                    }                    
                }
                else if (varExpAst.VariablePath.UserPath.Equals(
                    bool.TrueString, 
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }                
            }
            return false;
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, this.GetSourceName(), Strings.UseShouldProcessForStateChangingFunctionsName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the Common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseShouldProcessForStateChangingFunctionsCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseShouldProcessForStateChangingFunctionsDescrption);
        }

        /// <summary>
        /// GetSourceType: Retrieves the type of the rule: built-in, managed or module.
        /// </summary>
        /// <returns>Source type {PS, PSDSC}</returns>
        public SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }

        /// <summary>
        /// GetSeverity: Retrieves the severity of the rule: error, warning of information.
        /// </summary>
        /// <returns>Rule severity {Information, Warning, Error}</returns>
        public RuleSeverity GetSeverity()
        {
            return RuleSeverity.Warning;
        }

        /// <summary>
        /// GetSourceName: Retrieves the module/assembly name the rule is from.
        /// </summary>
        /// <returns>Source name</returns>
        public string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }
    }
}
