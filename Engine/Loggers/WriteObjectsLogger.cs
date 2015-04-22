﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Windows.Powershell.ScriptAnalyzer.Generic;
using Microsoft.Windows.Powershell.ScriptAnalyzer.Commands;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Threading;

namespace Microsoft.Windows.Powershell.ScriptAnalyzer.Loggers
{
    /// <summary>
    /// WriteObjectsLogger: Logs Diagnostics though WriteObject.
    /// </summary>
    [Export(typeof(ILogger))]
    public class WriteObjectsLogger : ILogger
    {
        #region Private members

        private CultureInfo cul = Thread.CurrentThread.CurrentCulture;
        private ResourceManager rm = new ResourceManager("Microsoft.Windows.Powershell.ScriptAnalyzer.Strings",
                                                                  Assembly.GetExecutingAssembly());

        #endregion

        #region Methods

        /// <summary>
        /// LogObject: Logs the given object though WriteObject.
        /// </summary>
        /// <param name="obj">The object to be logged</param>
        /// <param name="command">The Invoke-PSLint command this logger is running through</param>
        public void LogObject(Object obj, InvokeScriptAnalyzerCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException("command");
            }
            if (obj == null)
            {
                throw new ArgumentNullException("diagnostic");
            }
            command.WriteObject(obj);
        }

        /// <summary>
        /// GetName: Retrieves the name of this logger.
        /// </summary>
        /// <returns>The name of this logger</returns>
        public string GetName()
        {
            return rm.GetString("DefaultLoggerName", cul);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this logger.
        /// </summary>
        /// <returns>The description of this logger</returns>
        public string GetDescription()
        {
            return rm.GetString("DefaultLoggerDescription", cul);
        }

        #endregion
    }
}
