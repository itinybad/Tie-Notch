param(
    [string]$Filter = '',
    [string]$Configuration = 'Release',
    [string]$LogPath = '',
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Tie-Notch: 在独立的 Windows 桌面上运行测试，测试窗口既不会出现在你的桌面上，也不会抢前台。
$repository = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($LogPath)) { $LogPath = Join-Path $repository 'artifacts\test-background.log' }
$artifactDirectory = Split-Path $LogPath -Parent
if (-not (Test-Path -LiteralPath $artifactDirectory)) { New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null }

$project = Join-Path $repository 'Tests\VNotch.Tests.csproj'
$testArguments = 'dotnet test ''' + $project + ''' --configuration ' + $Configuration + ' --no-build --no-restore'
if (-not [string]::IsNullOrWhiteSpace($Filter)) { $testArguments = $testArguments + ' --filter ''' + $Filter + '''' }

$innerPath = Join-Path $artifactDirectory 'test-background-inner.ps1'
Set-Content -LiteralPath $innerPath -Value '$ErrorActionPreference = ''Continue''' -Encoding utf8
Add-Content -LiteralPath $innerPath -Value '[Console]::OutputEncoding = [Text.Encoding]::UTF8' -Encoding utf8
Add-Content -LiteralPath $innerPath -Value ('Set-Location -LiteralPath ''' + $repository + '''') -Encoding utf8
Add-Content -LiteralPath $innerPath -Value ($testArguments + ' *> ''' + $LogPath + '''') -Encoding utf8
Add-Content -LiteralPath $innerPath -Value 'exit $LASTEXITCODE' -Encoding utf8

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class HiddenDesktopRunner
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string lpszDesktop, string lpszDevice, IntPtr pDevmode, uint dwFlags, uint dwDesiredAccess, IntPtr lpsa);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint CreateNoWindow = 0x08000000;

    public static string Run(string commandLine, string workingDirectory, int timeoutMs)
    {
        IntPtr desktop = CreateDesktop("VNotchTestsDesktop", null, IntPtr.Zero, 0, 0x000F01FF, IntPtr.Zero);
        if (desktop == IntPtr.Zero) return "CreateDesktop failed err=" + Marshal.GetLastWin32Error();
        try
        {
            var startup = new STARTUPINFO();
            startup.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            startup.lpDesktop = @"winsta0\VNotchTestsDesktop";
            PROCESS_INFORMATION process;
            if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateNoWindow, IntPtr.Zero, workingDirectory, ref startup, out process))
                return "CreateProcess failed err=" + Marshal.GetLastWin32Error();
            uint wait = WaitForSingleObject(process.hProcess, (uint)timeoutMs);
            uint code = 0;
            GetExitCodeProcess(process.hProcess, out code);
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
            return wait == 0 ? "exit=" + code : "timeout after " + timeoutMs + " ms";
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }
}
'@

$pwshPath = (Get-Process -Id $PID).Path
$commandLine = '"' + $pwshPath + '" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $innerPath + '"'
Write-Host ('[*] 在隐藏桌面 VNotchTestsDesktop 上运行测试')
Write-Host ('[*] 日志文件: ' + $LogPath)
$result = [HiddenDesktopRunner]::Run($commandLine, $repository, $TimeoutMinutes * 60 * 1000)
Write-Host ('[*] 运行结果: ' + $result)
Get-Content -LiteralPath $LogPath -Tail 25
