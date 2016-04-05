﻿Import-Module PSScriptAnalyzer

$violationMessage = [regex]::Escape("Parameter '`$password' should use SecureString, otherwise this will expose sensitive information. See ConvertTo-SecureString for more information.")
$violationName = "PSAvoidUsingPlainTextForPassword"
$directory = Split-Path -Parent $MyInvocation.MyCommand.Path
$violationFilepath = Join-Path $directory 'AvoidUsingPlainTextForPassword.ps1'
$violations = Invoke-ScriptAnalyzer $violationFilepath | Where-Object {$_.RuleName -eq $violationName}
$noViolations = Invoke-ScriptAnalyzer $directory\AvoidUsingPlainTextForPasswordNoViolations.ps1 | Where-Object {$_.RuleName -eq $violationName}

Describe "AvoidUsingPlainTextForPassword" {
    Context "When there are violations" {
        It "has 3 avoid using plain text for password violations" {
            $violations.Count | Should Be 4
        }

	It "suggests corrections" {
            Import-Module .\PSScriptAnalyzerTestHelper.psm1
	    Test-CorrectionExtent $violationFilepath $violations[0] 1 '$passphrases' '[SecureString] $passphrases'
	    $violations[0].SuggestedCorrections[0].Description | Should Be 'Set $passphrases type to SecureString'

	    Test-CorrectionExtent $violationFilepath $violations[1] 1 '$passwordparam' '[SecureString] $passwordparam'
	    Test-CorrectionExtent $violationFilepath $violations[2] 1 '$credential' '[SecureString] $credential'
	    Test-CorrectionExtent $violationFilepath $violations[3] 1 '$password' '[SecureString] $password'
	}

        It "has the correct violation message" {
            $violations[3].Message | Should Match $violationMessage
        }
    }

    Context "When there are no violations" {
        It "returns no violations" {
            $noViolations.Count | Should Be 0
        }
    }
}