#UseDeclaredVarsMoreThanAssignments 
**Severity Level: Warning**

##Description
Generally variables that are not used more than their assignments are considered wasteful and not needed.

##How to Fix
Remove the variables that are declared but not used outside of the function.

##Example
###Wrong： 
``` PowerShell
function Test
{
    $declaredVar = "Declared just for fun"
    $declaredVar2 = "Not used"
    Write-Output $declaredVar
}
```

###Correct: 
``` PowerShell
function Test
{
    $declaredVar = "Declared just for fun"
    Write-Output $declaredVar
}
```
