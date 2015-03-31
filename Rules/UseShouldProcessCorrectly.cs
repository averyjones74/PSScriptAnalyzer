﻿using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management.Automation;
using System.Management.Automation.Language;
using Microsoft.Windows.Powershell.ScriptAnalyzer.Generic;
using System.ComponentModel.Composition;
using System.Resources;
using System.Globalization;
using System.Threading;
using System.Reflection;

namespace Microsoft.Windows.Powershell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// UseShouldProcessCorrectly: Analyzes the ast to check that if the ShouldProcess attribute is present, the function calls ShouldProcess and vice versa.
    /// </summary>
    [Export(typeof(IScriptRule))]
    public class UseShouldProcessCorrectly : IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: Analyzes the ast to check that if the ShouldProcess attribute is present, the function calls ShouldProcess and vice versa.
        /// </summary>
        /// <param name="ast">The script's ast</param>
        /// <param name="fileName">The script's file name</param>
        /// <returns>A List of diagnostic results of this rule</returns>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            IEnumerable<Ast> funcDefAsts = ast.FindAll(testAst => testAst is FunctionDefinitionAst, true);
            IEnumerable<Ast> attributeAsts;
            IEnumerable<Ast> memberAsts;
            IScriptExtent extent;
            string funcName;
            string supportsShouldProcess = "SupportsShouldProcess";
            string trueString = "$true";
            bool hasShouldProcessAttribute;
            bool callsShouldProcess;

            foreach (FunctionDefinitionAst funcDefAst in funcDefAsts) {
                extent = funcDefAst.Extent;
                funcName = funcDefAst.Name;

                hasShouldProcessAttribute = false;
                callsShouldProcess = false;

                attributeAsts = funcDefAst.FindAll(testAst => testAst is NamedAttributeArgumentAst, true);
                foreach (NamedAttributeArgumentAst attributeAst in attributeAsts) {
                    hasShouldProcessAttribute |= attributeAst.ArgumentName.Equals(supportsShouldProcess, StringComparison.OrdinalIgnoreCase) && attributeAst.Argument.Extent.Text.Equals(trueString, StringComparison.OrdinalIgnoreCase);
                }

                memberAsts = funcDefAst.FindAll(testAst => testAst is MemberExpressionAst, true);
                foreach (MemberExpressionAst memberAst in memberAsts) {
                    callsShouldProcess |= memberAst.Member.Extent.Text.Equals("ShouldProcess", StringComparison.OrdinalIgnoreCase) || memberAst.Member.Extent.Text.Equals("ShouldContinue", StringComparison.OrdinalIgnoreCase);
                }

                if (hasShouldProcessAttribute && !callsShouldProcess) {
                    yield return new DiagnosticRecord(string.Format(CultureInfo.CurrentCulture, Strings.ShouldProcessErrorHasAttribute, funcName), extent, GetName(), DiagnosticSeverity.Warning, fileName);
                }
                else if (!hasShouldProcessAttribute && callsShouldProcess) {
                     yield return new DiagnosticRecord(string.Format(CultureInfo.CurrentCulture, Strings.ShouldProcessErrorHasCmdlet, funcName), extent, GetName(), DiagnosticSeverity.Warning, fileName);
                }
            }
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.ShouldProcessName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the Common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.ShouldProcessCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture,Strings.ShouldProcessDescription);
        }

        /// <summary>
        /// GetSourceType: Retrieves the type of the rule: builtin, managed or module.
        /// </summary>
        public SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }

        /// <summary>
        /// GetSourceName: Retrieves the module/assembly name the rule is from.
        /// </summary>
        public string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture,Strings.SourceName);
        }
    }

}
