param([string]$names="sofa,boat")
$g="$env:USERPROFILE\Documents\gpubake"; $bl="C:\Program Files\Blender Foundation\Blender 4.2\blender.exe"; $d="$g\props"
Add-Type -Namespace W -Name U9 -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, System.IntPtr l);
public delegate bool EnumProc(System.IntPtr h, System.IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr h);
public static int VisibleFor(uint target){ int n=0; EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==target && IsWindowVisible(h)) n++; return true; }, System.IntPtr.Zero); return n; }
"@
$t0=Get-Date; $v=0; $res=@()
$list=$names.Split(","); foreach($n in $list){
  Remove-Item -Recurse -Force "$d\out_$n" -ErrorAction SilentlyContinue
  $p = Start-Process $bl -ArgumentList "-b","--factory-startup","-P","`"$d\$($n)_build.py`"","--","`"$d\out_$n`"" -WindowStyle Hidden -PassThru -RedirectStandardOutput "$g\logs\props_$n.out" -RedirectStandardError "$g\logs\props_$n.err"
  while(-not $p.HasExited){ Start-Sleep -Milliseconds 300; if([W.U9]::VisibleFor([uint32]$p.Id) -gt 0){$v++} }
  $res += "$n=$($p.ExitCode)"
}
Compress-Archive -Force -Path ($list | ForEach-Object { "$d\out_$_" }) -DestinationPath "$g\props_out_$($list -join '_').zip"
"blender $($res -join ' ') secs=$([int]((Get-Date)-$t0).TotalSeconds) visibleWindowSamples=$v" | Set-Content "$g\logs\props_$($list -join '_').done"
