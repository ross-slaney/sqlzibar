using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlOS.Todo.E2eTests;

/// <summary>
/// Runs the real <c>SqlOS.Todo.Cli</c> executable (built next to the tests via a
/// project reference) as a child process. Playwright plays the human in the
/// browser; this class plays the terminal. Every run gets its own token home so
/// the developer's real <c>~/.sqlos/todo-cli/tokens.json</c> is never touched,
/// and the browser launch is disabled so CI runners stay headless.
/// </summary>
internal sealed class CliProcess : IAsyncDisposable
{
    private static readonly Regex UrlLine = new(@"^https?://\S+$", RegexOptions.Compiled);
    private static readonly string CliAssemblyPath = Path.Combine(AppContext.BaseDirectory, "SqlOS.Todo.Cli.dll");

    private readonly Process _process;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly TaskCompletionSource<string> _verificationUrl = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pumpStdout;
    private readonly Task _pumpStderr;

    private CliProcess(Process process)
    {
        _process = process;
        _pumpStdout = PumpAsync(process.StandardOutput, _stdout, onLine: line =>
        {
            if (UrlLine.IsMatch(line.Trim()))
            {
                _verificationUrl.TrySetResult(line.Trim());
            }
        });
        _pumpStderr = PumpAsync(process.StandardError, _stderr, onLine: null);
    }

    public static CliProcess Start(string tokenHome, params string[] args)
    {
        if (!File.Exists(CliAssemblyPath))
        {
            throw new FileNotFoundException(
                "SqlOS.Todo.Cli.dll was not built next to the e2e tests. Check the project reference in SqlOS.Todo.E2eTests.csproj.",
                CliAssemblyPath);
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(CliAssemblyPath);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Same environment contract documented in examples/SqlOS.Todo.Cli/README.md.
        startInfo.Environment["SQLOS_TODO_API_ORIGIN"] = TodoPostgresE2eTests.ApiOrigin;
        startInfo.Environment["SQLOS_TODO_CLI_HOME"] = tokenHome;
        startInfo.Environment["SQLOS_TODO_CLI_NO_BROWSER"] = "1";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start `dotnet {CliAssemblyPath} {string.Join(' ', args)}`.");
        return new CliProcess(process);
    }

    /// <summary>Runs a short-lived command to completion.</summary>
    public static async Task<CliResult> RunAsync(string tokenHome, params string[] args)
    {
        await using var process = Start(tokenHome, args);
        return await process.WaitForExitAsync(TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Waits for the URL line the CLI prints after "Open this URL to sign in:".
    /// Fails fast if the process exits first so a broken discovery step does not
    /// hang the test until the timeout.
    /// </summary>
    public async Task<string> WaitForVerificationUrlAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var exited = _process.WaitForExitAsync(cts.Token);
        var completed = await Task.WhenAny(_verificationUrl.Task, exited);

        if (completed == _verificationUrl.Task)
        {
            return await _verificationUrl.Task;
        }

        if (exited.IsCompletedSuccessfully)
        {
            await Task.WhenAll(_pumpStdout, _pumpStderr);
            throw new InvalidOperationException($"The CLI exited with code {_process.ExitCode} before printing a verification URL.\n{Describe()}");
        }

        throw new TimeoutException($"The CLI did not print a verification URL within {timeout}.\n{Describe()}");
    }

    public async Task<CliResult> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill();
            throw new TimeoutException($"The CLI did not exit within {timeout}.\n{Describe()}");
        }

        await Task.WhenAll(_pumpStdout, _pumpStderr);
        return new CliResult(_process.ExitCode, _stdout.ToString(), _stderr.ToString());
    }

    public ValueTask DisposeAsync()
    {
        TryKill();
        _process.Dispose();
        return ValueTask.CompletedTask;
    }

    private void TryKill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
    }

    private string Describe()
    {
        lock (_stdout)
        {
            lock (_stderr)
            {
                return $"--- stdout ---\n{_stdout}\n--- stderr ---\n{_stderr}";
            }
        }
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder sink, Action<string>? onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (sink)
            {
                sink.AppendLine(line);
            }

            onLine?.Invoke(line);
        }
    }
}

internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError)
{
    public override string ToString() => $"exit {ExitCode}\n--- stdout ---\n{StandardOutput}\n--- stderr ---\n{StandardError}";
}
