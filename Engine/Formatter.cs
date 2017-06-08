using System.Collections;
using System.Management.Automation;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer
{
    /// <summary>
    /// A class to provide code formatting capability.
    /// </summary>
    public class Formatter
    {
        /// <summary>
        /// Format a powershell script.
        /// </summary>
        /// <param name="scriptDefinition">A string representing a powershell script.</param>
        /// <param name="settings">Settings to be used for formatting</param>
        /// <param name="range">The range in which formatting should take place.</param>
        /// <param name="cmdlet">The cmdlet object that calls this method.</param>
        /// <returns></returns>
        public static string Format<TCmdlet>(
            string scriptDefinition,
            Settings settings,
            Range range,
            TCmdlet cmdlet) where TCmdlet : PSCmdlet, IOutputWriter
        {
            // todo add argument check
            Helper.Instance = new Helper(cmdlet.SessionState.InvokeCommand, cmdlet);
            Helper.Instance.Initialize();

            var ruleOrder = new string[]
            {
                "PSPlaceCloseBrace",
                "PSPlaceOpenBrace",
                "PSUseConsistentWhitespace",
                "PSUseConsistentIndentation",
                "PSAlignAssignmentStatement"
            };

            var text = new EditableText(scriptDefinition);
            foreach (var rule in ruleOrder)
            {
                if (!settings.RuleArguments.ContainsKey(rule))
                {
                    continue;
                }

                cmdlet.WriteVerbose("Running " + rule);
                var currentSettings = GetCurrentSettings(settings, rule);
                ScriptAnalyzer.Instance.UpdateSettings(currentSettings);
                ScriptAnalyzer.Instance.Initialize(cmdlet, null, null, null, null, true, false);

                Range updatedRange;
                text = ScriptAnalyzer.Instance.Fix(text, range, out updatedRange);
                range = updatedRange;
            }

            return text.ToString();
        }

        private static Settings GetCurrentSettings(Settings settings, string rule)
        {
            return new Settings(new Hashtable()
            {
                {"IncludeRules", new string[] {rule}},
                {"Rules", new Hashtable() { { rule, new Hashtable(settings.RuleArguments[rule]) } } }
            });
        }
    }
}
