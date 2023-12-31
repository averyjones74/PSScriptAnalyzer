﻿# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

BeforeAll {
    $violationMessage = "Cmdlet 'Get-Command' has positional parameter. Please use named parameters instead of positional parameters when calling a command."
    $violationName = "PSAvoidUsingPositionalParameters"
    $violations = Invoke-ScriptAnalyzer $PSScriptRoot\AvoidPositionalParameters.ps1 | Where-Object {$_.RuleName -eq $violationName}
    $noViolations = Invoke-ScriptAnalyzer $PSScriptRoot\AvoidPositionalParametersNoViolations.ps1 | Where-Object {$_.RuleName -eq $violationName}
    $noViolationsDSC = Invoke-ScriptAnalyzer -ErrorAction SilentlyContinue $PSScriptRoot\serviceconfigdisabled.ps1 | Where-Object {$_.RuleName -eq $violationName}
}

Describe "AvoidPositionalParameters" {
    Context "When there are violations" {
        It "has 1 avoid positional parameters violation" {
            $violations.Count | Should -Be 1
        }

        It "has the correct description message" {
            $violations[0].Message | Should -Match $violationMessage
        }

        It "Triggers on alias" {
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition "gcm 'abc' 4 4.3"
            $violations.Count | Should -Be 2
            $violations.RuleName | Should -Contain $violationName
            $violations.RuleName | Should -Contain 'PSAvoidUsingCmdletAliases'
        }

        It "returns violations for command that is not in allow list of settings" {
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition 'Join-Path a b c d' -Settings @{
                IncludeRules = @('PSAvoidUsingPositionalParameters')
                Rules        = @{ PSAvoidUsingPositionalParameters = @{ CommandAllowList = 'Test-Path' } }
            }
            $violations.Count | Should -Be 1
            $violations.RuleName | Should -Be 'PSAvoidUsingPositionalParameters'
        }
    }

    Context "When there are no violations" {
        It "returns no violations" {
            $noViolations.Count | Should -Be 0
        }

        It "returns no violations for DSC configuration" {
            $noViolationsDSC.Count | Should -Be 0
        }

        It "returns no violations for AZ CLI by default" {
            Invoke-ScriptAnalyzer -ScriptDefinition 'az group deployment list' | Should -BeNullOrEmpty
        }

        It "returns no violations for command from allow list defined in settings and is case invariant" {
            Invoke-ScriptAnalyzer -ScriptDefinition 'join-patH a b c' -Settings @{
                IncludeRules = @('PSAvoidUsingPositionalParameters')
                Rules        = @{ PSAvoidUsingPositionalParameters = @{ CommandAllowList = 'az', 'Join-Path' } }
            } | Should -BeNullOrEmpty
        }
    }

    Context "Function defined and called in script, which has 3 or more positional parameters triggers rule." {
        It "returns avoid positional parameters violation" {
            $sb=
            {
                Function Foo {
                param(
                    [Parameter(Mandatory=$true,Position=1)] $A,
                    [Parameter(Position=2)]$B,
                    [Parameter(Position=3)]$C)
                }
                Foo "a" "b" "c"}
            $warnings = Invoke-ScriptAnalyzer -ScriptDefinition "$sb" -ExcludeRule PSReviewUnusedParameter
            $warnings.Count | Should -Be 1
            $warnings.RuleName | Should -BeExactly $violationName
        }
    }
}
