using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Reflection;
using Microsoft.PowerShell.Commands;
using Microsoft.PowerShell.CrossCompatibility.Data.Modules;
using Microsoft.PowerShell.CrossCompatibility.Utility;
using SMA = System.Management.Automation;

namespace Microsoft.PowerShell.CrossCompatibility.Collection
{
    public class PowerShellDataCollector : IDisposable
    {
        private const string DEFAULT_DEFAULT_PARAMETER_SET = "__AllParameterSets";

        private const string CORE_MODULE_NAME = "Microsoft.PowerShell.Core";

        private static readonly CmdletInfo s_gmoInfo = new CmdletInfo("Get-Module", typeof(GetModuleCommand));

        private static readonly CmdletInfo s_ipmoInfo = new CmdletInfo("Import-Module", typeof(ImportModuleCommand));

        private static readonly CmdletInfo s_rmoInfo = new CmdletInfo("Remove-Module", typeof(RemoveModuleCommand));

        private static readonly CmdletInfo s_gcmInfo = new CmdletInfo("Get-Command", typeof(GetCommandCommand));

        internal static CmdletInfo GcmInfo => s_gcmInfo;

        private readonly PowerShellVersion _psVersion;

        private readonly Lazy<ReadOnlySet<string>> _lazyCommonParameters;

        private SMA.PowerShell _pwsh;

        public PowerShellDataCollector(SMA.PowerShell pwsh, PowerShellVersion psVersion)
        {
            _pwsh = pwsh;
            _psVersion = psVersion;
            _lazyCommonParameters = new Lazy<ReadOnlySet<string>>(GetPowerShellCommonParameterNames);
        }

        internal ReadOnlySet<string> CommonParameterNames => _lazyCommonParameters.Value;

        public JsonCaseInsensitiveStringDictionary<JsonDictionary<Version, ModuleData>> AssembleModulesData(
            IEnumerable<Tuple<string, Version, ModuleData>> modules)
        {
            var moduleDict = new JsonCaseInsensitiveStringDictionary<JsonDictionary<Version, ModuleData>>();
            foreach (Tuple<string, Version, ModuleData> module in modules)
            {
                if (moduleDict.TryGetValue(module.Item1, out JsonDictionary<Version, ModuleData> versionDict))
                {
                    versionDict.Add(module.Item2, module.Item3);
                    continue;
                }

                var newVersionDict = new JsonDictionary<Version, ModuleData>();
                newVersionDict.Add(module.Item2, module.Item3);
                moduleDict.Add(module.Item1, newVersionDict);
            }

            return moduleDict;
        }

        public IEnumerable<Tuple<string, Version, ModuleData>> GetModulesData(out IEnumerable<Exception> errors)
        {
            IEnumerable<PSModuleInfo> modules = _pwsh.AddCommand(s_gmoInfo)
                .AddParameter("ListAvailable")
                .InvokeAndClear<PSModuleInfo>();

            var moduleDatas = new List<Tuple<string, Version, ModuleData>>();

            // Add the core parts of the module
            moduleDatas.Add(GetCoreModuleData());

            var errs = new List<Exception>();
            foreach (PSModuleInfo module in modules)
            {
                try
                {
                    PSModuleInfo importedModule = _pwsh.AddCommand(s_ipmoInfo)
                        .AddParameter("ModuleInfo", module)
                        .AddParameter("PassThru")
                        .AddParameter("ErrorAction", "Stop")
                        .InvokeAndClear<PSModuleInfo>()
                        .FirstOrDefault();

                    moduleDatas.Add(GetSingleModuleData(importedModule));

                    _pwsh.AddCommand(s_rmoInfo)
                        .AddParameter("ModuleInfo", importedModule)
                        .InvokeAndClear();
                }
                catch (CmdletInvocationException)
                {
                    // Attempt to load the module in a new runspace instead
                    try
                    {
                        using (SMA.PowerShell fallbackPwsh = SMA.PowerShell.Create(RunspaceMode.NewRunspace))
                        {
                            PSModuleInfo importedModule = fallbackPwsh.AddCommand(s_ipmoInfo)
                                .AddParameter("Name", module.Path)
                                .AddParameter("PassThru")
                                .AddParameter("ErrorAction", "Stop")
                                .InvokeAndClear<PSModuleInfo>()
                                .FirstOrDefault();

                            moduleDatas.Add(GetSingleModuleData(importedModule));
                        }
                    }
                    catch (Exception fallbackException)
                    {
                        errs.Add(fallbackException);
                    }
                }
            }

            errors = errs;
            return moduleDatas;
        }

        public Tuple<string, Version, ModuleData> GetSingleModuleData(PSModuleInfo module)
        {
            var moduleData = new ModuleData()
            {
                Guid = module.Guid
            };

            if (module.ExportedAliases != null && module.ExportedAliases.Count > 0)
            {
                moduleData.Aliases = new JsonCaseInsensitiveStringDictionary<string>(module.ExportedAliases.Count);
                moduleData.Aliases.AddAll(GetAliasesData(module.ExportedAliases));
            }

            if (module.ExportedCmdlets != null && module.ExportedCmdlets.Count > 0)
            {
                moduleData.Cmdlets = new JsonCaseInsensitiveStringDictionary<CmdletData>(module.ExportedCmdlets.Count);
                moduleData.Cmdlets.AddAll(GetCmdletsData(module.ExportedCmdlets));
            }

            if (module.ExportedFunctions != null && module.ExportedFunctions.Count > 0)
            {
                moduleData.Functions = new JsonCaseInsensitiveStringDictionary<FunctionData>(module.ExportedCmdlets.Count);
                moduleData.Functions.AddAll(GetFunctionsData(module.ExportedFunctions));
            }

            if (module.ExportedVariables != null && module.ExportedVariables.Count > 0)
            {
                moduleData.Variables = GetVariablesData(module.ExportedVariables);
            }

            return new Tuple<string, Version, ModuleData>(module.Name, module.Version, moduleData);
        }

        public Tuple<string, Version, ModuleData> GetCoreModuleData()
        {
            var moduleData = new ModuleData();

            IEnumerable<CommandInfo> coreCommands = _pwsh.AddCommand(GcmInfo)
                .AddParameter("Module", CORE_MODULE_NAME)
                .InvokeAndClear<CommandInfo>();

            var cmdletData = new JsonCaseInsensitiveStringDictionary<CmdletData>();
            var functionData = new JsonCaseInsensitiveStringDictionary<FunctionData>();
            foreach (CommandInfo command in coreCommands)
            {
                switch (command)
                {
                    case CmdletInfo cmdlet:
                        cmdletData.Add(cmdlet.Name, GetSingleCmdletData(cmdlet));
                        continue;

                    case FunctionInfo function:
                        functionData.Add(function.Name, GetSingleFunctionData(function));
                        continue;

                    default:
                        throw new CompatibilityAnalysisException($"Command {command.Name} in core module is of unsupported type {command.CommandType}");
                }
            }

            moduleData.Cmdlets = cmdletData;
            moduleData.Functions = functionData;

            // Get default variables and core aliases out of a fresh runspace
            using (SMA.PowerShell freshPwsh = SMA.PowerShell.Create(RunspaceMode.NewRunspace))
            {
                Collection<PSVariable> defaultVariables = freshPwsh.AddCommand("Get-ChildItem")
                    .AddParameter("Path", "variable:")
                    .InvokeAndClear<PSVariable>();

                var variableArray = new string[defaultVariables.Count];
                for (int i = 0; i < moduleData.Variables.Length; i++)
                {
                    moduleData.Variables[i] = defaultVariables[i].Name;
                }
                moduleData.Variables = variableArray;

                IEnumerable<AliasInfo> coreAliases = freshPwsh.AddCommand("Get-ChildItem")
                    .AddParameter("Path", "alias:")
                    .InvokeAndClear<AliasInfo>();

                var aliases = new JsonCaseInsensitiveStringDictionary<string>();
                foreach (AliasInfo aliasInfo in coreAliases)
                {
                    aliases.Add(aliasInfo.Name, GetSingleAliasData(aliasInfo));
                }
                moduleData.Aliases = aliases;
            }

            Version psVersion = _psVersion.PreReleaseLabel != null
                ? new Version(_psVersion.Major, _psVersion.Minor, _psVersion.Build)
                : (Version)_psVersion;

            return new Tuple<string, Version, ModuleData>(CORE_MODULE_NAME, psVersion, moduleData);
        }

        public IEnumerable<KeyValuePair<string, string>> GetAliasesData(IReadOnlyDictionary<string, AliasInfo> aliases)
        {
            foreach (KeyValuePair<string, AliasInfo> alias in aliases)
            {
                yield return new KeyValuePair<string, string>(alias.Key, GetSingleAliasData(alias.Value));
            }
        }

        public string GetSingleAliasData(AliasInfo alias)
        {
            return alias.Definition;
        }

        public IEnumerable<KeyValuePair<string, CmdletData>> GetCmdletsData(IReadOnlyDictionary<string, CmdletInfo> cmdlets)
        {
            foreach (KeyValuePair<string, CmdletInfo> cmdlet in cmdlets)
            {
                yield return new KeyValuePair<string, CmdletData>(cmdlet.Key, GetSingleCmdletData(cmdlet.Value));
            }
        }

        public CmdletData GetSingleCmdletData(CmdletInfo cmdlet)
        {
            var cmdletData = new CmdletData();

            cmdletData.DefaultParameterSet = GetDefaultParameterSet(cmdlet.DefaultParameterSet);

            cmdletData.OutputType = GetOutputType(cmdlet.OutputType);

            cmdletData.ParameterSets = GetParameterSets(cmdlet.ParameterSets);

            AssembleParameters(
                cmdlet.Parameters,
                out JsonCaseInsensitiveStringDictionary<ParameterData> parameters,
                out JsonCaseInsensitiveStringDictionary<string> parameterAliases,
                isCmdletBinding: true);

            cmdletData.Parameters = parameters;
            cmdletData.ParameterAliases = parameterAliases;

            return cmdletData;
        }

        public ParameterData GetSingleParameterData(ParameterMetadata parameter)
        {
            var parameterData = new ParameterData()
            {
                Dynamic = parameter.IsDynamic
            };

            if (parameter.ParameterType != null)
            {
                parameterData.Type = TypeNaming.GetFullTypeName(parameter.ParameterType);
            }

            if (parameter.ParameterSets != null && parameter.ParameterSets.Count > 0)
            {
                parameterData.ParameterSets = new JsonCaseInsensitiveStringDictionary<ParameterSetData>();
                foreach (KeyValuePair<string, ParameterSetMetadata> parameterSet in parameter.ParameterSets)
                {
                    parameterData.ParameterSets[parameterSet.Key] = GetSingleParameterSetData(parameterSet.Value);
                }
            }

            return parameterData;
        }

        public ParameterSetData GetSingleParameterSetData(ParameterSetMetadata parameterSet)
        {
            var parameterSetData = new ParameterSetData()
            {
                Position = parameterSet.Position
            };

            var parameterSetFlags = new List<ParameterSetFlag>();

            if (parameterSet.IsMandatory)
            { 
                parameterSetFlags.Add(ParameterSetFlag.Mandatory);
            }

            if (parameterSet.ValueFromPipeline)
            {
                parameterSetFlags.Add(ParameterSetFlag.ValueFromPipeline);
            }

            if (parameterSet.ValueFromPipelineByPropertyName)
            {
                parameterSetFlags.Add(ParameterSetFlag.ValueFromPipelineByPropertyName);
            }

            if (parameterSet.ValueFromRemainingArguments)
            {
                parameterSetFlags.Add(ParameterSetFlag.ValueFromRemainingArguments);
            }

            if (parameterSetFlags.Count > 0)
            {
                parameterSetData.Flags = parameterSetFlags.ToArray();
            }

            return parameterSetData;
        }

        public IEnumerable<KeyValuePair<string, FunctionData>> GetFunctionsData(IReadOnlyDictionary<string, FunctionInfo> functions)
        {
            foreach (KeyValuePair<string, FunctionInfo> function in functions)
            {
                yield return new KeyValuePair<string, FunctionData>(function.Key, GetSingleFunctionData(function.Value));
            }
        }

        public FunctionData GetSingleFunctionData(FunctionInfo function)
        {
            var functionData = new FunctionData()
            {
                CmdletBinding = function.CmdletBinding
            };

            functionData.DefaultParameterSet = GetDefaultParameterSet(function.DefaultParameterSet);

            functionData.OutputType = GetOutputType(function.OutputType);

            functionData.ParameterSets = GetParameterSets(function.ParameterSets);

            AssembleParameters(
                function.Parameters,
                out JsonCaseInsensitiveStringDictionary<ParameterData> parameters,
                out JsonCaseInsensitiveStringDictionary<string> parameterAliases,
                isCmdletBinding: function.CmdletBinding);

            functionData.Parameters = parameters;
            functionData.ParameterAliases = parameterAliases;

            return functionData;
        }

        public string[] GetVariablesData(IReadOnlyDictionary<string, PSVariable> variables)
        {
            var variableData = new string[variables.Count];
            int i = 0;
            foreach (string variable in variables.Keys)
            {
                variableData[i] = variable;
                i++;
            }

            return variableData;
        }

        private string GetDefaultParameterSet(string defaultParameterSet)
        {
            if (defaultParameterSet == null
                || string.Equals(defaultParameterSet, DEFAULT_DEFAULT_PARAMETER_SET))
            {
                return null;
            }

            return defaultParameterSet;
        }

        private string[] GetOutputType(IReadOnlyList<PSTypeName> outputType)
        {
            if (outputType == null || outputType.Count <= 0)
            {
                return null;
            }

            var outputTypeData = new string[outputType.Count];
            for (int i = 0; i < outputTypeData.Length; i++)
            {
                outputTypeData[i] = outputType[i].Type != null
                    ? TypeNaming.GetFullTypeName(outputType[i].Type)
                    : outputType[i].Name;
            }

            return outputTypeData;
        }

        private string[] GetParameterSets(IReadOnlyList<CommandParameterSetInfo> parameterSets)
        {
            if (parameterSets == null || parameterSets.Count <= 0)
            {
                return null;
            }

            var parameterSetData = new string[parameterSets.Count];
            for (int i = 0; i < parameterSetData.Length; i++)
            {
                parameterSetData[i] = parameterSets[i].Name;
            }

            return parameterSetData;
        }

        private void AssembleParameters(
            IReadOnlyDictionary<string, ParameterMetadata> parameters,
            out JsonCaseInsensitiveStringDictionary<ParameterData> parameterData,
            out JsonCaseInsensitiveStringDictionary<string> parameterAliasData,
            bool isCmdletBinding)
        {
            if (parameters == null || parameters.Count == 0)
            {
                parameterData = null;
                parameterAliasData = null;
                return;
            }

            parameterData = new JsonCaseInsensitiveStringDictionary<ParameterData>();
            parameterAliasData = null;

            foreach (KeyValuePair<string, ParameterMetadata> parameter in parameters)
            {
                if (isCmdletBinding && CommonParameterNames.Contains(parameter.Key))
                {
                    continue;
                }

                parameterData[parameter.Key] = GetSingleParameterData(parameter.Value);

                if (parameter.Value.Aliases != null && parameter.Value.Aliases.Count > 0)
                {
                    if (parameterAliasData == null)
                    {
                        parameterAliasData = new JsonCaseInsensitiveStringDictionary<string>();
                    }

                    foreach (string alias in parameter.Value.Aliases)
                    {
                        parameterAliasData[alias] = parameter.Key;
                    }
                }
            }

            if (parameterData.Count == 0)
            {
                parameterData = null;
            }

            if (parameterAliasData != null && parameterAliasData.Count == 0)
            {
                parameterAliasData = null;
            }
        }

        private static ReadOnlySet<string> GetPowerShellCommonParameterNames()
        {
            var set = new List<string>();
            foreach (PropertyInfo property in typeof(CommonParameters).GetProperties())
            {
                set.Add(property.Name);
            }
            return new ReadOnlySet<string>(set, StringComparer.OrdinalIgnoreCase);
        }


        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _pwsh.Dispose();
                }

                _pwsh = null;
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}