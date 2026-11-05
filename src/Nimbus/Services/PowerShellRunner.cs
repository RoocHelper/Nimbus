using System.Diagnostics;
using System.IO;
using System.Text;

namespace Nimbus.Services;

public sealed record ScriptResult(int ExitCode, int WarningCount, int ErrorCount, IReadOnlyList<string> Lines);

/// <summary>
/// Runs Windows PowerShell 5.1 with the bundled WinUtil functions loaded (Scripts\NimbusShim.ps1).
/// Output lines are streamed back; lines starting with "##NIMBUS:" are protocol messages.
/// </summary>
public static class PowerShellRunner
{
    public const string ProtocolPrefix = "##NIMBUS:";

    public static string PowerShellPath =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>Quotes a value as a PowerShell single-quoted string literal.</summary>
    public static string Q(string? value) => "'" + (value ?? string.Empty).Replace("'", "''") + "'";

    public static async Task<ScriptResult> RunAsync(string body, Action<string>? onLine, CancellationToken token = default)
    {
        var workDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Nimbus")).FullName;
        var scriptPath = Path.Combine(workDir, $"run-{Guid.NewGuid():N}.ps1");

        var script = new StringBuilder()
            .AppendLine("$ErrorActionPreference = 'Continue'")
            .AppendLine("$ProgressPreference = 'SilentlyContinue'")
            .AppendLine("try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}")
            .AppendLine($"$NimbusScripts = {Q(AppPaths.BundledScripts)}")
            .AppendLine($"$NimbusData = {Q(AppPaths.BundledData)}")
            .AppendLine($"$NimbusWork = {Q(AppPaths.Work)}")
            .AppendLine(". (Join-Path $NimbusScripts 'NimbusShim.ps1')")
            .AppendLine("try {")
            .AppendLine(body)
            .AppendLine("} catch {")
            .AppendLine("  Write-Host \"##NIMBUS:ERROR|$($_.Exception.Message)\"")
            .AppendLine("  exit 1")
            .AppendLine("}")
            .ToString();

        // UTF-8 with BOM so Windows PowerShell 5.1 reads non-ASCII text correctly.
        await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(true), token);

        var lines = new List<string>();
        var warnings = 0;
        var errors = 0;
        var gate = new object();

        void Handle(string? line, bool isError)
        {
            if (line is null)
            {
                return;
            }

            lock (gate)
            {
                lines.Add(line);
                if (isError && !string.IsNullOrWhiteSpace(line))
                {
                    errors++;
                }
                else if (line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase)
                         || line.StartsWith(ProtocolPrefix + "LOG|WARN", StringComparison.Ordinal))
                {
                    warnings++;
                }
                else if (line.StartsWith(ProtocolPrefix + "LOG|ERROR", StringComparison.Ordinal)
                         || line.StartsWith(ProtocolPrefix + "ERROR|", StringComparison.Ordinal))
                {
                    errors++;
                }
            }

            onLine?.Invoke(line);
        }

        var psi = new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workDir,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Handle(e.Data, false);
        process.ErrorDataReceived += (_, e) => Handle(e.Data, true);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (token.Register(() =>
                   {
                       try
                       {
                           process.Kill(entireProcessTree: true);
                       }
                       catch
                       {
                           // Already exited.
                       }
                   }))
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }

            // Make sure the async readers have flushed.
            process.WaitForExit();
            List<string> snapshot;
            lock (gate)
            {
                snapshot = lines.ToList();
            }

            return new ScriptResult(process.ExitCode, warnings, errors, snapshot);
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch
            {
                // Temp file cleanup is best effort.
            }
        }
    }
}
