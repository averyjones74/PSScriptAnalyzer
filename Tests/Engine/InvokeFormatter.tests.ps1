$directory = Split-Path -Parent $MyInvocation.MyCommand.Path
$testRootDirectory = Split-Path -Parent $directory

Import-Module PSScriptAnalyzer
Import-Module (Join-Path $testRootDirectory "PSScriptAnalyzerTestHelper.psm1")

Describe "Invoke-Formatter Cmdlet" {
    Context "When positional parameters are given" {
        It "Should use the positional parameters" {
            $def = @"
function foo {
"abc"
}
"@

            $expected = @"
function foo {
    "abc"
}
"@

            $settings = @{
                IncludeRules = @('PSUseConsistentIndentation')
                Rules        = @{
                    PSUseConsistentIndentation = @{
                        Enable = $true
                    }
                }
            }

            Invoke-Formatter $def $settings | Should Be $expected
        }
    }
    Context "When no settings are given" {
        It "Should format using default settings" {
            $def = @'
function foo
{
get-childitem
$x=1+2
$hashtable = @{
property1 = "value"
    anotherProperty = "another value"
}
}
'@
            $expected = @'
function foo {
    get-childitem
    $x = 1 + 2
    $hashtable = @{
        property1       = "value"
        anotherProperty = "another value"
    }
}
'@

            Invoke-Formatter -ScriptDefinition $def | Should Be $expected
        }
    }

}
