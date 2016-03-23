﻿Import-Module PSScriptAnalyzer
$violationMessage = "'cls' is an alias of 'Clear-Host'. Alias can introduce possible problems and make scripts hard to maintain. Please consider changing alias to its full content."
$violationName = "PSAvoidUsingCmdletAliases"
$directory = Split-Path -Parent $MyInvocation.MyCommand.Path
$violations = Invoke-ScriptAnalyzer $directory\AvoidUsingAlias.ps1 | Where-Object {$_.RuleName -eq $violationName}
$noViolations = Invoke-ScriptAnalyzer $directory\AvoidUsingAliasNoViolations.ps1 | Where-Object {$_.RuleName -eq $violationName}

Describe "AvoidUsingAlias" {
    Context "When there are violations" {
        It "has 2 Avoid Using Alias Cmdlets violations" {
            $violations.Count | Should Be 2
        }

        It "has the correct description message" {
            $violations[1].Message | Should Match $violationMessage
        }

		It "suggests correction" {
			$violations[0].SuggestedCorrection | Should Be 'Invoke-Expression'
			$violations[1].SuggestedCorrection | Should Be 'Clear-Host'
		}
    }

    Context "When there are no violations" {
        It "returns no violations" {
            $noViolations.Count | Should Be 0
        }
    }
}