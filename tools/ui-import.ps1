Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$h = (Get-Process Fulbr).MainWindowHandle
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

function Find-ByTypeAndName($type, $name) {
    $c1 = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
    $c2 = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $and = New-Object System.Windows.Automation.AndCondition($c1, $c2)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)
}

$nav = Find-ByTypeAndName ([System.Windows.Automation.ControlType]::RadioButton) 'Подписки'
if ($null -eq $nav) { Write-Error 'Subscriptions nav not found'; exit 1 }
$nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 800

$button = Find-ByTypeAndName ([System.Windows.Automation.ControlType]::Button) 'Добавить рекомендуемый источник'
if ($null -eq $button) { Write-Error 'Recommended source button not found'; exit 2 }
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

Write-Host 'Invoked recommended source import. Waiting for network + parse...'
Start-Sleep -Seconds 15
Write-Host 'Done.'
