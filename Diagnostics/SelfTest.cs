namespace Quick;

internal static class SelfTest
{
    public static void Run()
    {
        Assert(ExpressionEvaluator.TryEvaluate("1 + 2 * 3", out var precedence) && precedence == 7m);
        Assert(ExpressionEvaluator.TryEvaluate("(10 - 4) / 2", out var grouping) && grouping == 3m);
        Assert(ExpressionEvaluator.TryEvaluate("5603*2.7%", out var percentage) && percentage == 151.281m);
        Assert(ExpressionEvaluator.TryEvaluate("2(27000*1.4%)", out var implicitProduct) && implicitProduct == 756m);
        Assert(!ExpressionEvaluator.TryEvaluate("2(23*100", out _));
        Assert(ExpressionEvaluator.TryEvaluate("100-30%", out var subtractPercentage) && subtractPercentage == 70m);
        Assert(ExpressionEvaluator.TryEvaluateScript("a = 2\na+a*3", out var variables) && variables == 8m);
        Assert(ExpressionEvaluator.TryEvaluateScript("a=2,b=3\na*b", out var commaVariables) && commaVariables == 6m);
        Assert(ExpressionEvaluator.TryEvaluateScript("a=1\n1000*1.01^a", out var powerWithVariable) && powerWithVariable == 1010m);
        Assert(ExpressionEvaluator.TryEvaluate("2^3^2", out var rightAssociativePower) && rightAssociativePower == 512m);
        Assert(!ExpressionEvaluator.TryEvaluate("1 / 0", out _));
        Assert(ExchangeQuery.TryParse("10000myr", out var defaultQuery) && defaultQuery.To == "USD");
        Assert(ExchangeQuery.TryParse("10000myr sgd", out var query));
        Assert(query.Amount == 10000m && query.From == "MYR" && query.To == "SGD");
        Assert(LauncherForm.FormatShortcut(Keys.Alt, Keys.Space) == "Alt + Space");

        var rates = new Dictionary<string, decimal>
        {
            ["MYR"] = 1m,
            ["USD"] = 0.213m,
            ["SGD"] = 0.286m
        };
        Assert(ExchangeCalculator.TryConvert(new CurrencyQuery(10000m, "MYR", "USD"), rates, out var converted, out _));
        Assert(converted == 2130m);
        Assert(CurrencyExpressionEvaluator.TryEvaluate("100*200myr", rates, out var oneCurrency, out var oneCurrencyTarget) &&
               oneCurrencyTarget == "USD" && oneCurrency == 4260m);
        Assert(CurrencyExpressionEvaluator.TryEvaluate("100myr*200usd", rates, out var usdProduct, out var usdTarget) &&
               usdTarget == "USD" && usdProduct == 4260m);
        Assert(CurrencyExpressionEvaluator.TryEvaluate("100myr*200usd myr", rates, out var myrProduct, out var myrTarget) &&
               myrTarget == "MYR" && Math.Abs(myrProduct - (20000m / 0.213m)) < 0.0000001m);
        Assert(CurrencyExpressionEvaluator.TryEvaluate("200myr*200myr usd", rates, out var sameCurrencyProduct, out var sameCurrencyTarget) &&
               sameCurrencyTarget == "USD" && sameCurrencyProduct == 8520m);
        Assert(CurrencyExpressionEvaluator.TryEvaluate("(266+150)usd myr", rates, out var groupedCurrency, out var groupedTarget) &&
               groupedTarget == "MYR" && Math.Abs(groupedCurrency - (416m / 0.213m)) < 0.0000001m);
        Assert(ExpressionEvaluator.TryEvaluate("1.0119^12", out var growthFactor));
        Assert(CurrencyExpressionEvaluator.TryEvaluate(
                   "a=1.19\nb=7800\nc=12\n(b*(1+a/100)^c)usd sgd",
                   rates,
                   out var variableCurrency,
                   out var variableCurrencyTarget) &&
               variableCurrencyTarget == "SGD" &&
               Math.Abs(variableCurrency - (7800m * growthFactor * 0.286m / 0.213m)) < 0.0000001m);

        using var usageDocument = JsonDocument.Parse(
            "{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":34,\"resetsAt\":4102444800,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":12,\"windowDurationMins\":10080}}}}");
        Assert(CodexUsageReader.TryParseUsage(usageDocument.RootElement, out var usage) &&
               usage.Primary is { UsedPercent: 34 } &&
               usage.Secondary is { UsedPercent: 12 });
        Assert(LauncherForm.FormatCodexWindow(usage.Primary).StartsWith("5h 66% remaining", StringComparison.Ordinal));
        Assert(LauncherForm.FormatCodexWindow(usage.Secondary).StartsWith("weekly 88% remaining", StringComparison.Ordinal));
        var codexStartInfo = CodexUsageReader.CreateStartInfo("codex.exe");
        Assert(!codexStartInfo.Environment.ContainsKey("CODEX_CI") &&
               !codexStartInfo.Environment.ContainsKey("CODEX_SANDBOX_NETWORK_DISABLED"));
        Assert(codexStartInfo.ArgumentList.SequenceEqual(["app-server", "--stdio"]));
        using var logo = LauncherForm.LoadEmbeddedLogo();
        Assert(logo.Width > 0 && logo.Height > 0);

        Console.WriteLine("Self-check passed.");
    }

    private static void Assert(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Self-check failed.");
        }
    }
}
