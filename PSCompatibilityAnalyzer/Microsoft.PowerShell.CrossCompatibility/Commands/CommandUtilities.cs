// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Management.Automation;

namespace Microsoft.PowerShell.CrossCompatibility.Commands
{
    internal static class CommandUtilities
    {
        private const string COMPATIBILITY_ERROR_ID = "CompatibilityAnalysisError";

        public const string MODULE_PREFIX = "PSCompatibility";

        public static void WriteExceptionAsWarning(
            this Cmdlet cmdlet,
            Exception exception,
            string errorId = COMPATIBILITY_ERROR_ID,
            ErrorCategory errorCategory = ErrorCategory.ReadError,
            object targetObject = null)
        {
            cmdlet.WriteWarning(exception.ToString());
        }

        public static string GetNormalizedAbsolutePath(this PSCmdlet cmdlet, string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            return cmdlet.SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
        }

        private static ErrorRecord CreateCompatibilityErrorRecord(
            Exception e,
            string errorId = COMPATIBILITY_ERROR_ID,
            ErrorCategory errorCategory = ErrorCategory.ReadError,
            object targetObject = null)
        {
            return new ErrorRecord(
                e,
                errorId,
                errorCategory,
                targetObject);
        }
    }
}