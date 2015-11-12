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

Describe "RuleSuppressionWithoutScope" {
    Context "Function" {
        It "Does not raise violations" {
            $suppression = $violations | Where-Object { $_.RuleName -eq "PSProvideCommentHelp" }
            $suppression = $violationsUsingScriptDefinition | Where-Object { $_.RuleName -eq "PSProvideCommentHelp" }
            $suppression.Count | Should Be 0
        }
    }

    Context "Class" {
        It "Does not raise violations" {
            $suppression = $violations | Where-Object {$_.RuleName -eq "PSAvoidUsingInvokeExpression" }
            $suppression = $violationsUsingScriptDefinition | Where-Object {$_.RuleName -eq "PSAvoidUsingInvokeExpression" }
            $suppression.Count | Should Be 0
        }
    }

    Context "FunctionInClass" {
        It "Does not raise violations" {
            $suppression = $violations | Where-Object {$_.RuleName -eq "PSAvoidUsingCmdletAliases" }
            $suppression = $violationsUsingScriptDefinition | Where-Object {$_.RuleName -eq "PSAvoidUsingCmdletAliases" }
            $suppression.Count | Should Be 0
        }        
    }

    Context "Script" {
        It "Does not raise violations" {
            $suppression = $violations | Where-Object {$_.RuleName -eq "PSProvideCommentHelp" }
            $suppression = $violationsUsingScriptDefinition | Where-Object {$_.RuleName -eq "PSProvideCommentHelp" }
            $suppression.Count | Should Be 0
        }
    }

    Context "RuleSuppressionID" {
        It "Only suppress violations for that ID" {
            $suppression = $violations | Where-Object {$_.RuleName -eq "PSProvideDefaultParameterValue" }
            $suppression = $violationsUsingScriptDefinition | Where-Object {$_.RuleName -eq "PSProvideDefaultParameterValue" }
            $suppression.Count | Should Be 1
        }
    }
}

Describe "RuleSuppressionWithScope" {
    Context "FunctionScope" {
        It "Does not raise violations" {
            $suppression = $violations | Where-Object {$_.RuleName -eq "PSAvoidUsingPositionalParameters" }
            $suppression = $violationsUsingScriptDefinition | Where-Object {$_.RuleName -eq "PSAvoidUsingPositionalParameters" }
            $suppression.Count | Should Be 0
        }
    }

    Context "ClassScope" {
        It "Does not raise violations" {
            $suppression = $violations | Where-Object {$_.RuleName -eq "PSAvoidUsingConvertToSecureStringWithPlainText" }
            $suppression = $violationsUsingScriptDefinition | Where-Object {$_.RuleName -eq "PSAvoidUsingConvertToSecureStringWithPlainText" }
            $suppression.Count | Should Be 0
        }
    }
}