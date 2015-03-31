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
    /// AvoidUserNameAndPasswordParams: Check that a function does not use both username and password
    /// parameters.
    /// </summary>
    [Export(typeof(IScriptRule))]
    public class AvoidUserNameAndPasswordParams : IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: Check that a function does not use both username
        /// and password parameters.
        /// </summary>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            // Finds all functionAst
            IEnumerable<Ast> functionAsts = ast.FindAll(testAst => testAst is FunctionDefinitionAst, true);

            List<String> passwords = new List<String>() {"Password", "Passphrase"};
            List<String> usernames = new List<String>() { "Username", "User", "ID", "APIKey", "Key",
                                                        "Account", "Name" };

            foreach (FunctionDefinitionAst funcAst in functionAsts)
            {
                bool hasPwd = false;
                bool hasUserName = false;

                // Finds all ParamAsts.
                IEnumerable<Ast> paramAsts = funcAst.FindAll(testAst => testAst is ParameterAst, true);
                // Iterrates all ParamAsts and check if their names are on the list.
                foreach (ParameterAst paramAst in paramAsts)
                {
                    TypeInfo paramType = (TypeInfo)paramAst.StaticType;
                    String paramName = paramAst.Name.VariablePath.ToString();

                    if (paramType == typeof(PSCredential) || (paramType.IsArray && paramType.GetElementType() == typeof (PSCredential)))
                    {
                        continue;
                    }

                    foreach (String password in passwords)
                    {
                        if (paramName.IndexOf(password, StringComparison.OrdinalIgnoreCase) != -1)
                        {
                            hasPwd = true;
                            break;
                        }
                    }

                    foreach (String username in usernames)
                    {
                        if (paramName.IndexOf(username, StringComparison.OrdinalIgnoreCase) != -1)
                        {
                            hasUserName = true;
                            break;
                        }
                    }
                }

                if (hasUserName && hasPwd)
                {
                    yield return new DiagnosticRecord(
                        String.Format(CultureInfo.CurrentCulture, Strings.AvoidUserNameAndPasswordParamsError, funcAst.Name),
                        funcAst.Extent, GetName(), DiagnosticSeverity.Warning, fileName);
                }
            }
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.AvoidUserNameAndPasswordParamsName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidUserNameAndPasswordParamsCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidUserNameAndPasswordParamsDescription);
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
