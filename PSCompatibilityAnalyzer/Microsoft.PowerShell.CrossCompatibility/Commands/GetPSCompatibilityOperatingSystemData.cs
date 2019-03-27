using System.Management.Automation;
using Microsoft.PowerShell.CrossCompatibility.Collection;

namespace Microsoft.PowerShell.CrossCompatibility.Commands
{
    [Cmdlet(VerbsCommon.Get, CommandUtilities.MODULE_PREFIX + "OperatingSystemData")]
    public class GetPSCompatibilityOperatingSystemData : Cmdlet
    {
        protected override void EndProcessing()
        {
            using (var pwsh = System.Management.Automation.PowerShell.Create())
            using (var platformInfoCollector = new PlatformInformationCollector(pwsh))
            {
                WriteObject(platformInfoCollector.GetOperatingSystemData());
            }
        }
    }
}