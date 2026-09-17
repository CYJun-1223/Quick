using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Quick.Tests;

[TestClass]
public sealed class CalculationTests
{
    [TestMethod]
    public void CalculatorHonorsPrecedenceAndGrouping()
    {
        Assert.IsTrue(ExpressionEvaluator.TryEvaluate("1 + 2 * 3", out var precedence));
        Assert.AreEqual(7m, precedence);

        Assert.IsTrue(ExpressionEvaluator.TryEvaluate("(10 - 4) / 2", out var grouping));
        Assert.AreEqual(3m, grouping);
    }

    [TestMethod]
    public void CalculatorRejectsDivisionByZero()
    {
        Assert.IsFalse(ExpressionEvaluator.TryEvaluate("1 / 0", out _));
    }

    [TestMethod]
    public void CalculatorRejectsUnclosedParentheses()
    {
        Assert.IsFalse(ExpressionEvaluator.TryEvaluate("2(23 * 100", out _));
    }

    [TestMethod]
    public void CurrencyExpressionConvertsToRequestedTarget()
    {
        var rates = new Dictionary<string, decimal>
        {
            ["MYR"] = 1m,
            ["USD"] = 0.213m,
            ["SGD"] = 0.286m
        };

        Assert.IsTrue(CurrencyExpressionEvaluator.TryEvaluate(
            "100myr*200usd myr",
            rates,
            out var value,
            out var target));
        Assert.AreEqual("MYR", target);
        Assert.AreEqual(20000m / 0.213m, value, 0.0000001m);
    }

    [TestMethod]
    public void ShortcutFormattingIncludesModifierOrder()
    {
        Assert.AreEqual("Ctrl + Alt + K", LauncherForm.FormatShortcut(Keys.Control | Keys.Alt, Keys.K));
    }
}
