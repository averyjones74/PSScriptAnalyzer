﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management.Automation.Language;
using System.Management.Automation;
using Microsoft.Windows.Powershell.ScriptAnalyzer.Generic;
using System.ComponentModel.Composition;
using System.Resources;
using System.Globalization;
using System.Threading;
using System.Reflection;
using System.IO;

namespace Microsoft.Windows.Powershell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// MissingModuleManifestField: Run Test Module Manifest to check that none of the required fields are missing.
    /// </summary>
    [Export(typeof (IScriptRule))]
    public class MissingModuleManifestField : IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: Run Test Module Manifest to check that none of the required fields are missing. From the ILintScriptRule interface.
        /// </summary>
        /// <param name="ast">The script's ast</param>
        /// <param name="fileName">The script's file name</param>
        /// <returns>A List of diagnostic results of this rule</returns>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            if (String.Equals(System.IO.Path.GetExtension(fileName), ".psd1", StringComparison.OrdinalIgnoreCase))
            {
                var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace);

                try
                {
                    ps.AddCommand("Test-ModuleManifest");
                    ps.AddParameter("Path", fileName);
                    ps.Invoke();

                } catch { }

                if (ps != null && ps.HadErrors && ps.Streams != null && ps.Streams.Error != null)
                {
                    foreach (var errorRecord in ps.Streams.Error)
                    {
                        if (errorRecord.CategoryInfo != null && errorRecord.CategoryInfo.Category == System.Management.Automation.ErrorCategory.ResourceUnavailable
                            && String.Equals("MissingMemberException", errorRecord.CategoryInfo.Reason, StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Debug.Assert(errorRecord.Exception != null && !String.IsNullOrWhiteSpace(errorRecord.Exception.Message), Strings.NullErrorMessage);

                            yield return
                                new DiagnosticRecord(errorRecord.Exception.Message, ast.Extent, GetName(), DiagnosticSeverity.Warning, fileName);
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
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.MissingModuleManifestFieldName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return String.Format(CultureInfo.CurrentCulture, Strings.MissingModuleManifestFieldCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return String.Format(CultureInfo.CurrentCulture, Strings.MissingModuleManifestFieldDescription);
        }

        /// <summary>
        /// Method: Retrieves the type of the rule: builtin, managed or module.
        /// </summary>
        public SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }

        /// <summary>
        /// Method: Retrieves the module/assembly name the rule is from.
        /// </summary>
        public string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }
    }
}
