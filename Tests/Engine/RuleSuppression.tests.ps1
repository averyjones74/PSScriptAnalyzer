﻿# Check if PSScriptAnalyzer is already loaded so we don't
# overwrite a test version of Invoke-ScriptAnalyzer by
# accident
if (!(Get-Module PSScriptAnalyzer) -and !$testingLibraryUsage)
{
	Import-Module -Verbose PSScriptAnalyzer
}

$directory = Split-Path -Parent $MyInvocation.MyCommand.Path
$violationsUsingScriptDefinition = Invoke-ScriptAnalyzer -ScriptDefinition (Get-Content -Raw "$directory\RuleSuppression.ps1")
$violations = Invoke-ScriptAnalyzer "$directory\RuleSuppression.ps1"

$ruleSuppressionBad = @'
Function do-something
{
    [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("PSAvoidUsingUserNameAndPassWordParams", "username")]
    Param(
    $username,
    $password
    )
}
'@

$ruleSuppressionInConfiguration = @'
Configuration xFileUpload
{
[Diagnostics.CodeAnalysis.SuppressMessageAttribute("PSAvoidUsingConvertToSecureStringWithPlainText", "")]
param ([string] $decryptedPassword)
$securePassword = ConvertTo-SecureString $decryptedPassword -AsPlainText -Force
}
'@

Describe "RuleSuppressionWithoutScope" {
    Context "Function" {
        It "Does not raise violations" {
            $suppression = $violations | Where-Object { $_.RuleName -eq "PSProvideCommentHelp" }
            $suppression.Count | Should Be 0
            $suppression = $violationsUsingScriptDefinition | Where-Object { $_.RuleName -eq "PSProvideCommentHelp" }
            $suppression.Count | Should Be 0
        }
    }

    Context "Script" {
        It "Does not raise violations" {
            $suppression = $violations | Where-Object {$_.RuleName -eq "PSProvideCommentHelp" }
            $suppression.Count | Should Be 0
            $suppression = $violationsUsingScriptDefinition | Where-Object {$_.RuleName -eq "PSProvideCommentHelp" }
            $suppression.Count | Should Be 0
        }
    }

    Context "RuleSuppressionID" {
        It "Only suppress violations for that ID" {
            $suppression = $violations | Where-Object {$_.RuleName -eq "PSAvoidDefaultValueForMandatoryParameter" }
            $suppression.Count | Should Be 1
            $suppression = $violationsUsingScriptDefinition | Where-Object {$_.RuleName -eq "PSAvoidDefaultValueForMandatoryParameter" }
            $suppression.Count | Should Be 1
        }
    }

    if (($PSVersionTable.PSVersion -ge [Version]'5.0'))
    {
        Context "Rule suppression within DSC Configuration definition" {
            It "Suppresses rule" {
                $suppressedRule = Invoke-ScriptAnalyzer -ScriptDefinition $ruleSuppressionInConfiguration -SuppressedOnly
                $suppressedRule.Count | Should Be 1
            }
        }
    }

    if (!$testingLibraryUsage)
    {
	Context "Bad Rule Suppression" {
    		It "Throws a non-terminating error" {
	   	   Invoke-ScriptAnalyzer -ScriptDefinition $ruleSuppressionBad -IncludeRule "PSAvoidUsingUserNameAndPassWordParams" -ErrorVariable errorRecord -ErrorAction SilentlyContinue
	   	   $errorRecord.Count | Should Be 1
	   	   $errorRecord.FullyQualifiedErrorId | Should match "suppression message attribute error"
	}
    }
    }
}

Describe "RuleSuppressionWithScope" {
    Context "FunctionScope" {
        It "Does not raise violations" {
            $suppression = $violations | Where-Object {$_.RuleName -eq "PSAvoidUsingPositionalParameters" }
            $suppression.Count | Should Be 0
            $suppression = $violationsUsingScriptDefinition | Where-Object {$_.RuleName -eq "PSAvoidUsingPositionalParameters" }
            $suppression.Count | Should Be 0
        }
    }
 }