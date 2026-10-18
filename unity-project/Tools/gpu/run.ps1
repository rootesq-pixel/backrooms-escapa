param([string]$Name, [string]$Script, [string]$ArgsStr, [string]$EnvStr, [string]$Blend = "")
$W = "$env:USERPROFILE\Documents\gpubake"; $B = "C:\Program Files\Blender Foundation\Blender 4.2\blender.exe"
foreach ($kv in $EnvStr.Split(';')) { if ($kv) { $p = $kv.Split('=',2); [Environment]::SetEnvironmentVariable($p[0], $p[1].Replace('{W}', $W.Replace('\','/')), 'Process') } }
$env:CYCLES_DEVICE = 'GPU'
$log = "$W\logs\$Name.log"; New-Item -ItemType Directory -Force "$W\logs" | Out-Null; Remove-Item "$W\logs\$Name.done" -ErrorAction SilentlyContinue
$t0 = Get-Date
$a = @('-b','--factory-startup'); if ($Blend) { $a += $Blend.Replace('{W}', $W) }; $a += @('--python', "$W\scripts\$Script", '--') + $ArgsStr.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
& $B @a *> $log
$code = $LASTEXITCODE; $s = [int]((Get-Date) - $t0).TotalSeconds
"exit=$code seconds=$s" | Set-Content "$W\logs\$Name.done"
