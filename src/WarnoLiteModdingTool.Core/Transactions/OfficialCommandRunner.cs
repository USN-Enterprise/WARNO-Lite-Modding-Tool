using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WarnoLiteModdingTool.Core.Transactions;

public sealed record OfficialCommandResult(
    string CommandPath,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(item => item.Length > 0));
}

public sealed class OfficialCommandRunner
{
    public async Task<OfficialCommandResult> RunAsync(
        string projectRoot,
        string commandPath,
        string? simpleArgument = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(commandPath);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath) ||
            !string.Equals(Path.GetExtension(fullPath), ".bat", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("官方命令必须是目标 Mod 根目录内已探测到的 BAT。");
        }

        if (simpleArgument is not null && !Regex.IsMatch(simpleArgument, "^[A-Za-z0-9]+$"))
        {
            throw new InvalidOperationException("官方命令参数只能包含英文字母和数字。");
        }

        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(commandInterpreter) || !File.Exists(commandInterpreter))
        {
            commandInterpreter = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = commandInterpreter,
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var consoleEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        startInfo.StandardOutputEncoding = consoleEncoding;
        startInfo.StandardErrorEncoding = consoleEncoding;
        // cmd.exe consumes everything after /c as one native command line. ArgumentList would
        // escape the inner quotes with backslashes, which cmd treats as literal characters.
        startInfo.Arguments = simpleArgument is null
            ? $"/d /c call \"{fullPath}\""
            : $"/d /c call \"{fullPath}\" {simpleArgument}";

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动官方命令：{Path.GetFileName(fullPath)}");
        }
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new OfficialCommandResult(
            fullPath,
            process.ExitCode,
            await outputTask,
            await errorTask);
    }
}
