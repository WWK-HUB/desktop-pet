$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'prepare-fixtures.ps1')
$petRoot = Split-Path -Parent $PSScriptRoot
$petExe = Join-Path $petRoot 'DesktopPet.exe'
$petRunning = Get-Process DesktopPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $petExe }
if ($petRunning) { throw 'Close the running desktop pet before this process test.' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PetSmokeNative {
    public delegate bool EnumCallback(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCallback callback, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr p, uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumCallback callback, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder text, int length);
    public static IntPtr FindPet(int pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint id; GetWindowThreadProcessId(h, out id);
            if (id == pid && IsWindowVisible(h) && (GetWindowLong(h, -20) & 0x80000) != 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static IntPtr FindStartButton(IntPtr window) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(window, delegate(IntPtr h, IntPtr l) {
            var text = new StringBuilder(256); GetWindowText(h, text, text.Capacity);
            if (text.ToString().StartsWith("\u542f\u52a8\u684c\u5ba0")) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static IntPtr Find(int pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint id; GetWindowThreadProcessId(h, out id);
            if (id == pid && IsWindowVisible(h)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
$petLines = [System.Collections.Generic.List[string]]::new()
# This is the interactive launcher under UI test; SW_HIDE would suppress its startup window.
$petFixturePath = Join-Path $PSScriptRoot 'fixtures\demo'
$petProcess = Start-Process -FilePath $petExe -ArgumentList ('--no-auto-phrases --character "' + $petFixturePath + '"') -PassThru -WindowStyle Normal
try {
    $petWindow = [IntPtr]::Zero
    for ($petAttempt = 0; $petAttempt -lt 30; $petAttempt++) {
        Start-Sleep -Milliseconds 100
        $petWindow = [PetSmokeNative]::Find($petProcess.Id)
        if ($petWindow -ne [IntPtr]::Zero) { break }
    }
    if ($petWindow -eq [IntPtr]::Zero) { throw 'Normal startup did not create a visible window.' }
    $petLauncher = $petWindow
    if ([PetSmokeNative]::FindPet($petProcess.Id) -ne [IntPtr]::Zero) { throw 'Startup must not create a desktop pet before Start is clicked.' }
    $petLines.Add('PASS: Normal executable startup opens the launcher with no layered pet window')
    $petSecond = Start-Process -FilePath $petExe -PassThru -WindowStyle Hidden
    if (-not $petSecond.WaitForExit(5000) -or $petSecond.ExitCode -ne 0) { throw 'Second launch did not exit cleanly.' }
    $petProcess.Refresh()
    if ($petProcess.HasExited) { throw 'First instance exited unexpectedly.' }
    $petLines.Add('PASS: Duplicate launch exits successfully while the first instance stays alive')
    $petStart = [PetSmokeNative]::FindStartButton($petLauncher)
    if ($petStart -eq [IntPtr]::Zero) { throw 'Launcher Start button was not found.' }
    [PetSmokeNative]::PostMessage($petStart, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $petWindow = [IntPtr]::Zero
    for ($petAttempt = 0; $petAttempt -lt 50; $petAttempt++) {
        Start-Sleep -Milliseconds 100
        $petWindow = [PetSmokeNative]::FindPet($petProcess.Id)
        if ($petWindow -ne [IntPtr]::Zero) { break }
    }
    if ($petWindow -eq [IntPtr]::Zero) { throw 'Clicking Start did not create a pet.' }
    $petLines.Add('PASS: Clicking the launcher Start button creates the layered desktop pet')
    $petCpuBefore = $petProcess.TotalProcessorTime.TotalMilliseconds
    $petGdiBefore = [PetSmokeNative]::GetGuiResources($petProcess.Handle, 0)
    Start-Sleep -Seconds 8
    $petProcess.Refresh()
    if ($petProcess.HasExited) { throw 'Pet crashed during timer animation.' }
    $petGdiAfter = [PetSmokeNative]::GetGuiResources($petProcess.Handle, 0)
    $petCpuUsed = [Math]::Round($petProcess.TotalProcessorTime.TotalMilliseconds - $petCpuBefore)
    $petMemory = [Math]::Round($petProcess.WorkingSet64 / 1MB, 1)
    if ($petGdiAfter -gt $petGdiBefore + 8) { throw 'GDI resource count unexpectedly increased.' }
    $petLines.Add("PASS: Real animation timer runs for 8 seconds; GDI handles $petGdiBefore -> $petGdiAfter")
    $petLines.Add("MEASURED: CPU ${petCpuUsed}ms over 8 seconds; working set ${petMemory}MB")
    $petThird = Start-Process -FilePath $petExe -PassThru -WindowStyle Hidden
    if (-not $petThird.WaitForExit(5000) -or $petThird.ExitCode -ne 0) { throw 'Relaunch while pet is running failed.' }
    Start-Sleep -Milliseconds 250
    if (-not [PetSmokeNative]::IsWindowVisible($petLauncher) -or [PetSmokeNative]::FindPet($petProcess.Id) -ne $petWindow) { throw 'Relaunch did not reopen the launcher with the same pet.' }
    $petLines.Add('PASS: Relaunch while pet runs reopens the hidden launcher and keeps the same pet')
    [PetSmokeNative]::PostMessage($petWindow, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 500
    if (-not [PetSmokeNative]::IsWindowVisible($petLauncher) -or [PetSmokeNative]::FindPet($petProcess.Id) -ne [IntPtr]::Zero) { throw 'Closing the pet did not return to the launcher.' }
    $petLines.Add('PASS: Closing the pet returns to the original launcher')
    [PetSmokeNative]::PostMessage($petLauncher, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    if (-not $petProcess.WaitForExit(5000) -or $petProcess.ExitCode -ne 0) { throw 'Window close did not exit normally.' }
    $petLines.Add('PASS: Native window close exits with code 0')
    $petLines.Add('Completed: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    $petLines | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'results\smoke-results.txt') -Encoding UTF8
    $petLines
} finally {
    if (-not $petProcess.HasExited) { $petProcess.Kill() }
    $petProcess.Dispose()
}
