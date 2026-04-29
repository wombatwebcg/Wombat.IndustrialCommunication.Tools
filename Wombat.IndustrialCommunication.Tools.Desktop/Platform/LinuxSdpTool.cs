using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Wombat.IndustrialCommunication;
using Wombat.IndustrialCommunication.Tools.Models;

namespace Wombat.IndustrialCommunication.Tools.Desktop.Platform;

internal static class LinuxSdpTool
{
    private static readonly Regex ServiceNameRegex = BuildRegex(@"Service Name:\s*(.+)");
    private static readonly Regex ServiceIdRegex = BuildRegex(@"Service Class ID List:\s*""(.+?)""\s*\(([^)]+)\)");
    private static readonly Regex ChannelRegex = BuildRegex(@"Channel:\s*(\d+)");

    public static async Task<IReadOnlyList<BluetoothServiceOption>> QueryServicesAsync(string deviceId, CancellationToken cancellationToken)
    {
        var queryResult = await RunAsync($"sdptool browse {deviceId}", cancellationToken).ConfigureAwait(false);
        if (!queryResult.IsSuccess || string.IsNullOrWhiteSpace(queryResult.ResultValue))
        {
            return Array.Empty<BluetoothServiceOption>();
        }

        return ParseServices(queryResult.ResultValue);
    }

    public static OperationResult<int> TryResolveChannel(string deviceId, string serviceId)
    {
        try
        {
            var browse = RunSync($"sdptool browse {deviceId}");
            if (!browse.IsSuccess || string.IsNullOrWhiteSpace(browse.ResultValue))
            {
                return OperationResult.CreateFailedResult<int>(browse.Message);
            }

            var services = ParseServices(browse.ResultValue);
            var match = services.FirstOrDefault(service => string.Equals(service.ServiceId, serviceId, StringComparison.OrdinalIgnoreCase));
            if (match == null || !TryParseChannel(match.Description, out var channel))
            {
                return OperationResult.CreateFailedResult<int>("未找到匹配的 RFCOMM 通道。");
            }

            return OperationResult.CreateSuccessResult(channel);
        }
        catch (Exception ex)
        {
            return OperationResult.CreateFailedResult<int>(ex);
        }
    }

    private static IReadOnlyList<BluetoothServiceOption> ParseServices(string content)
    {
        var blocks = content
            .Split("Service Name:", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(block => "Service Name: " + block)
            .ToArray();

        var services = new List<BluetoothServiceOption>();
        foreach (var block in blocks)
        {
            var name = MatchValue(ServiceNameRegex, block);
            var idMatch = ServiceIdRegex.Match(block);
            var channel = MatchValue(ChannelRegex, block);

            if (!idMatch.Success)
            {
                continue;
            }

            var uuid = idMatch.Groups[2].Value.Trim();
            var serviceId = NormalizeUuid(uuid);
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                continue;
            }

            services.Add(new BluetoothServiceOption
            {
                ServiceId = serviceId,
                DisplayName = string.IsNullOrWhiteSpace(name) ? idMatch.Groups[1].Value.Trim() : name,
                Description = string.IsNullOrWhiteSpace(channel) ? "Linux sdptool 查询结果" : $"Linux sdptool 查询结果，RFCOMM 通道 {channel}"
            });
        }

        return services;
    }

    private static string NormalizeUuid(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            return guid.ToString();
        }

        if (ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var shortUuid))
        {
            return $"0000{shortUuid:x4}-0000-1000-8000-00805f9b34fb";
        }

        return string.Empty;
    }

    private static bool TryParseChannel(string description, out int channel)
    {
        channel = 0;
        var match = ChannelRegex.Match(description);
        return match.Success && int.TryParse(match.Groups[1].Value, out channel);
    }

    private static string MatchValue(Regex regex, string input)
    {
        var match = regex.Match(input);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static async Task<OperationResult<string>> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-lc \"{arguments}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return process.ExitCode == 0
                ? OperationResult.CreateSuccessResult<string>(stdout)
                : CreateFailedResult(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }
        catch (Exception ex)
        {
            return CreateFailedResult(ex);
        }
    }

    private static OperationResult<string> RunSync(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-lc \"{arguments}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return OperationResult.CreateFailedResult<string>("无法启动 Linux SDP 查询进程。");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0
                ? OperationResult.CreateSuccessResult<string>(stdout)
                : CreateFailedResult(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }
        catch (Exception ex)
        {
            return CreateFailedResult(ex);
        }
    }

    private static Regex BuildRegex(string pattern)
    {
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    }

    private static OperationResult<string> CreateFailedResult(string message)
    {
        return new OperationResult<string>().SetInfo(OperationResult.CreateFailedResult(message));
    }

    private static OperationResult<string> CreateFailedResult(Exception ex)
    {
        return new OperationResult<string>().SetInfo(OperationResult.CreateFailedResult(ex));
    }
}
