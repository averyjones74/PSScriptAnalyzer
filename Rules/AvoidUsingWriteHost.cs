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
    /// AvoidUsingWriteHost: Check that Write-Host or Console.Write are not used
    /// </summary>
    [Export(typeof(IScriptRule))]
    public class AvoidUsingWriteHost : IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: check that Write-Host or Console.Write are not used.
        /// </summary>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            // Finds all CommandAsts.
            IEnumerable<Ast> commandAsts = ast.FindAll(testAst => testAst is CommandAst, true);

            // Iterrates all CommandAsts and check the command name.
            foreach (CommandAst cmdAst in commandAsts)
            {
                if (cmdAst.GetCommandName() != null && String.Equals(cmdAst.GetCommandName(), "write-host", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new DiagnosticRecord(String.Format(CultureInfo.CurrentCulture, Strings.AvoidUsingWriteHostError, System.IO.Path.GetFileName(fileName)),
                        cmdAst.Extent, GetName(), DiagnosticSeverity.Warning, fileName);
                }
            }

            // Finds all InvokeMemberExpressionAst
            IEnumerable<Ast> invokeAsts = ast.FindAll(testAst => testAst is InvokeMemberExpressionAst, true);

            foreach (InvokeMemberExpressionAst invokeAst in invokeAsts)
            {
                TypeExpressionAst typeAst = invokeAst.Expression as TypeExpressionAst;
                if (typeAst == null || typeAst.TypeName == null || typeAst.TypeName.FullName == null) continue;

                if (typeAst.TypeName.FullName.EndsWith("console", StringComparison.OrdinalIgnoreCase)
                    && !String.IsNullOrWhiteSpace(invokeAst.Member.Extent.Text) && invokeAst.Member.Extent.Text.StartsWith("Write", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new DiagnosticRecord(String.Format(CultureInfo.CurrentCulture, Strings.AvoidUsingConsoleWriteError, System.IO.Path.GetFileName(fileName), invokeAst.Member.Extent.Text),
                        invokeAst.Extent, GetName(), DiagnosticSeverity.Warning, fileName);
                }
            }
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.AvoidUsingWriteHostName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidUsingWriteHostCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidUsingWriteHostDescription);
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
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }
    }
}
