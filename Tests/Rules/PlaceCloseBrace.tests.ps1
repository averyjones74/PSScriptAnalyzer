﻿Import-Module PSScriptAnalyzer
$ruleName = "PSPlaceCloseBrace"

Describe "PlaceCloseBrace" {
    Context "When a close brace is not on a new line" {
        BeforeAll {
            $def = @'
function foo {
    Write-Output "close brace not on a new line"}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -IncludeRule $ruleName
        }

        It "Should find a violation" {
            $violations.Count | Should Be 1
        }

        It "Should mark the right extent" {
            $violations[0].Extent.Text | Should Be "}"
        }
    }

    Context "When there is an extra new line before a close brace" {
        BeforeAll {
            $def = @'
function foo {
    Write-Output "close brace not on a new line"

}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -IncludeRule $ruleName
        }

        It "Should find a violation" {
            $violations.Count | Should Be 1
        }

        It "Should mark the right extent" {
            $violations[0].Extent.Text | Should Be "}"
        }
    }
}
