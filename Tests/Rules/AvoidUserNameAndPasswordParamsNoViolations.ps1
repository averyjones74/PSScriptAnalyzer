﻿function MyFunction ($username, $param2)
{
    
}

function MyFunction2 ($param1, $passwords)
{
    
}

function MyFunction3
{
    [CmdletBinding()]
    [Alias()]
    [OutputType([int])]
    Param
    (
        # Param1 help description
        [Parameter(Mandatory=$true,
                   ValueFromPipelineByPropertyName=$true,
                   Position=0)]
        [System.Management.Automation.CredentialAttribute()]
        [pscredential]
        $UserName,

        # Param2 help description
        [pscredential]
        [System.Management.Automation.CredentialAttribute()]
        $Password
    )
}
