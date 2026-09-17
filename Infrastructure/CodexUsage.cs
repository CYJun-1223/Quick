namespace Quick;

internal readonly record struct CodexUsageWindow(int UsedPercent, long? ResetsAt, long? WindowDurationMins);

internal readonly record struct CodexUsageSnapshot(
    CodexUsageWindow? Primary,
    CodexUsageWindow? Secondary);

internal readonly record struct CodexUsageReadResult(CodexUsageSnapshot? Snapshot, string Error);

internal static class CodexUsageReader
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(12);

    public static async Task<CodexUsageReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var executable = FindCodexExecutable();
        if (executable is null)
        {
            return new(null, "Codex CLI was not found");
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(executable)
        };

        try
        {
            if (!process.Start())
            {
                return new(null, "Could not start Codex CLI");
            }

            process.StandardInput.WriteLine(
                "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"quick\",\"title\":\"Quick\",\"version\":\"1.0.0\"},\"capabilities\":{\"experimentalApi\":false}}}");
            process.StandardInput.Flush();
            var initialize = await ReadResponseAsync(process, 1, cancellationToken);
            if (initialize is null)
            {
                return new(null, "Codex did not respond");
            }

            if (GetError(initialize.Value) is { } initializeError)
            {
                return new(null, FormatError(initializeError));
            }

            process.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
            process.StandardInput.WriteLine(
                "{\"id\":2,\"method\":\"account/read\",\"params\":{\"refreshToken\":true}}");
            process.StandardInput.Flush();
            var account = await ReadResponseAsync(process, 2, cancellationToken);
            if (account is null)
            {
                return new(null, "Codex did not respond");
            }

            if (GetError(account.Value) is { } accountError)
            {
                return new(null, FormatError(accountError));
            }

            process.StandardInput.WriteLine(
                "{\"id\":3,\"method\":\"account/rateLimits/read\",\"params\":{\"excludeResetCreditDetails\":true}}");
            process.StandardInput.Flush();
            var response = await ReadResponseAsync(process, 3, cancellationToken);
            if (response is null)
            {
                return new(null, "Codex did not respond");
            }

            if (GetError(response.Value) is { } error)
            {
                return new(null, FormatError(error));
            }

            return TryParseUsage(response.Value, out var snapshot)
                ? new(snapshot, string.Empty)
                : new(null, "Codex returned no usage data");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new(null, "Codex usage unavailable");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");
        startInfo.Environment.Remove("CODEX_CI");
        startInfo.Environment.Remove("CODEX_SANDBOX_NETWORK_DISABLED");

        var configuredHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var userHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        var configuredAuth = !string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(configuredHome, "auth.json")
            : string.Empty;
        if (File.Exists(configuredAuth))
        {
            startInfo.Environment["CODEX_HOME"] = configuredHome!;
        }
        else if (File.Exists(Path.Combine(userHome, "auth.json")))
        {
            startInfo.Environment["CODEX_HOME"] = userHome;
        }

        return startInfo;
    }

    internal static bool TryParseUsage(JsonElement response, out CodexUsageSnapshot snapshot)
    {
        snapshot = default;
        if (!response.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object ||
            !TryGetRateLimitSnapshot(result, out var limits))
        {
            return false;
        }

        snapshot = new(
            ParseWindow(limits, "primary"),
            ParseWindow(limits, "secondary"));
        return true;
    }

    private static async Task<JsonElement?> ReadResponseAsync(
        Process process,
        int expectedId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line;
            try
            {
                line = await process.StandardOutput.ReadLineAsync(cancellationToken)
                    .AsTask()
                    .WaitAsync(ResponseTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return null;
            }

            if (line is null)
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) &&
                    id.ValueKind == JsonValueKind.Number &&
                    id.TryGetInt32(out var actualId) &&
                    actualId == expectedId)
                {
                    return root.Clone();
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    private static bool TryGetRateLimitSnapshot(JsonElement result, out JsonElement limits)
    {
        if (result.TryGetProperty("rateLimits", out limits) && limits.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        if (result.TryGetProperty("rateLimitsByLimitId", out var byLimitId) &&
            byLimitId.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in byLimitId.EnumerateObject())
            {
                if (string.Equals(property.Name, "codex", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    limits = property.Value;
                    return true;
                }
            }
        }

        limits = default;
        return false;
    }

    private static CodexUsageWindow? ParseWindow(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !TryGetInt(window, "usedPercent", "used_percent", out var usedPercent))
        {
            return null;
        }

        return new(
            Math.Clamp(usedPercent, 0, 100),
            GetOptionalLong(window, "resetsAt", "resets_at"),
            GetOptionalLong(window, "windowDurationMins", "window_minutes"));
    }

    private static string? GetError(JsonElement response)
    {
        if (!response.TryGetProperty("error", out var error) ||
            error.ValueKind != JsonValueKind.Object ||
            !error.TryGetProperty("message", out var message))
        {
            return null;
        }

        return message.GetString() ?? "Codex returned an error";
    }

    private static string FormatError(string error) =>
        error.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("sign in", StringComparison.OrdinalIgnoreCase)
            ? "Sign in to Codex CLI to read usage"
            : "Codex usage unavailable";

    private static bool TryGetInt(JsonElement value, string firstName, string secondName, out int result)
    {
        result = 0;
        return TryGetProperty(value, firstName, secondName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out result);
    }

    private static long? GetOptionalLong(JsonElement value, string firstName, string secondName)
    {
        if (!TryGetProperty(value, firstName, secondName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static bool TryGetProperty(
        JsonElement value,
        string firstName,
        string secondName,
        out JsonElement property)
    {
        if (value.TryGetProperty(firstName, out property) || value.TryGetProperty(secondName, out property))
        {
            return true;
        }

        property = default;
        return false;
    }

    private static string? FindCodexExecutable()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var relativeBinary = Path.Combine(
            "node_modules",
            "@openai",
            "codex-win32-x64",
            "vendor",
            "x86_64-pc-windows-msvc",
            "bin",
            "codex.exe");
        var candidates = new[]
        {
            Path.Combine(appData, "npm", "node_modules", "@openai", "codex", relativeBinary),
            Path.Combine(appData, "npm", "node_modules", "@openai", relativeBinary)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("Path");
        if (path is not null)
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidatesFromPath = new[]
                {
                    Path.Combine(directory, "codex.exe"),
                    Path.Combine(directory, "node_modules", "@openai", "codex", relativeBinary),
                    Path.Combine(directory, "node_modules", "@openai", relativeBinary)
                };
                foreach (var candidate in candidatesFromPath)
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }
}
