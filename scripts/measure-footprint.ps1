<#
.SYNOPSIS
  Measures the CPU and memory used by the Lock Monitor provider and the widget host processes.

.DESCRIPTION
  Samples processor time and memory of LockMonitor.Widget.exe, WidgetService.exe and
  WidgetBoard.exe at the start and end of the window and prints the average CPU use.
  Keep the Widgets board open (or the lock screen showing) for the whole window when measuring
  the active cost; close it to measure the idle cost.

.PARAMETER Seconds  Length of the measurement window (default 60).
#>
[CmdletBinding()]
param([int] $Seconds = 60)

$names = 'LockMonitor.Widget', 'WidgetService', 'WidgetBoard'

function Read-Counters {
    $result = @{}
    foreach ($p in Get-Process -Name $names -ErrorAction SilentlyContinue) {
        $result[$p.Id] = [pscustomobject]@{
            Name    = $p.ProcessName
            Id      = $p.Id
            Cpu     = $p.TotalProcessorTime
            Private = $p.PrivateMemorySize64
            WorkSet = $p.WorkingSet64
        }
    }
    $result
}

$cores = [Environment]::ProcessorCount
$before = Read-Counters
$clock = [Diagnostics.Stopwatch]::StartNew()
Write-Host "Measuring for $Seconds s..."
Start-Sleep -Seconds $Seconds
$after = Read-Counters
$wallMs = $clock.Elapsed.TotalMilliseconds

$rows = foreach ($id in $after.Keys) {
    $a = $after[$id]
    $b = $before[$id]
    $cpuMs = if ($b) { ($a.Cpu - $b.Cpu).TotalMilliseconds } else { $a.Cpu.TotalMilliseconds }
    [pscustomobject]@{
        Process         = $a.Name
        Pid             = $id
        'CPU ms'        = [math]::Round($cpuMs)
        'CPU % (1 core)'  = [math]::Round(100 * $cpuMs / $wallMs, 3)
        'CPU % (all)'   = [math]::Round(100 * $cpuMs / $wallMs / $cores, 3)
        'Private MB'    = [math]::Round($a.Private / 1MB, 1)
        'Working set MB' = [math]::Round($a.WorkSet / 1MB, 1)
        Note            = if ($b) { '' } else { 'started during window' }
    }
}
$rows | Sort-Object Process | Format-Table -AutoSize
