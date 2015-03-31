﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management.Automation.Language;

namespace Microsoft.Windows.Powershell.ScriptAnalyzer.Generic
{
    /// <summary>
    /// Represents an abstract class for rule that checks that
    /// a parameter of a cmdlet are used correctly.
    /// </summary>
    public abstract class AvoidParameterGeneric : IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: Analyzes the given Ast and returns DiagnosticRecords based on the anaylsis.
        /// </summary>
        /// <param name="ast">The script's ast</param>
        /// <param name="fileName">The name of the script file being analyzed</param>
        /// <returns>The results of the analysis</returns>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null) throw new ArgumentNullException("ast");

            // Finds all CommandAsts.
            IEnumerable<Ast> commandAsts = ast.FindAll(testAst => testAst is CommandAst, true);

            // Iterrates all CommandAsts and check the condition.
            foreach (CommandAst cmdAst in commandAsts)
            {
                if (CommandCondition(cmdAst) && cmdAst.CommandElements != null)
                {
                    foreach (CommandElementAst ceAst in cmdAst.CommandElements)
                    {
                        if (ParameterCondition(cmdAst, ceAst))
                        {
                            yield return new DiagnosticRecord(GetError(fileName, cmdAst), cmdAst.Extent, GetName(), DiagnosticSeverity.Warning, fileName);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// GetError: Retrieves the error message.
        /// </summary>
        /// <returns></returns>
        public abstract string GetError(string FileName, CommandAst CmdAst);

        /// <summary>
        /// Condition on the cmdlet that must be satisfied for the error to be raised
        /// </summary>
        /// <param name="CmdAst"></param>
        /// <returns></returns>
        public abstract bool CommandCondition(CommandAst CmdAst);

        /// <summary>
        /// Condition on the parameter that must be satisfied for the error to be raised.
        /// </summary>
        /// <param name="CmdAst"></param>
        /// <param name="CeAst"></param>
        /// <returns></returns>
        public abstract bool ParameterCondition(CommandAst CmdAst, CommandElementAst CeAst);

        /// <summary>
        /// GetName: Retrieves the name of the rule.
        /// </summary>
        /// <returns>The name of the rule.</returns>
        public abstract string GetName();

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public abstract string GetCommonName();

        /// <summary>
        /// GetDescription: Retrieves the description of the rule.
        /// </summary>
        /// <returns>The description of the rule.</returns>
        public abstract string GetDescription();

        /// <summary>
        /// GetSourceName: Retrieves the source name of the rule.
        /// </summary>
        /// <returns>The source name of the rule.</returns>
        public abstract string GetSourceName();

        /// <summary>
        /// GetSourceType: Retrieves the source type of the rule.
        /// </summary>
        /// <returns>The source type of the rule.</returns>
        public abstract  SourceType GetSourceType();
    }
}
