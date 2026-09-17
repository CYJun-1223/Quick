namespace Quick;

internal readonly record struct CurrencyQuery(decimal Amount, string From, string To);

internal static class ExchangeQuery
{
    private static readonly Regex Pattern = new(
        @"^\s*(?<amount>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*(?<from>[A-Za-z]{3})(?:\s*(?:(?:to|in|->)\s*)?(?<to>[A-Za-z]{3}))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParse(string input, out CurrencyQuery query)
    {
        query = default;
        var match = Pattern.Match(input.Replace(",", string.Empty, StringComparison.Ordinal));
        if (!match.Success ||
            !decimal.TryParse(match.Groups["amount"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            return false;
        }

        var from = match.Groups["from"].Value.ToUpperInvariant();
        var to = match.Groups["to"].Success
            ? match.Groups["to"].Value.ToUpperInvariant()
            : "USD";

        query = new CurrencyQuery(amount, from, to);
        return true;
    }
}

internal static class ExchangeCalculator
{
    public static bool TryConvert(
        CurrencyQuery query,
        IReadOnlyDictionary<string, decimal> ratesFromMyr,
        out decimal converted,
        out decimal rate)
    {
        converted = 0;
        rate = 0;

        if (!ratesFromMyr.TryGetValue(query.From, out var fromRate) ||
            !ratesFromMyr.TryGetValue(query.To, out var toRate) ||
            fromRate <= 0 ||
            toRate <= 0)
        {
            return false;
        }

        rate = toRate / fromRate;

        try
        {
            converted = query.Amount * rate;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

internal static class CurrencyExpressionEvaluator
{
    private static readonly Regex CurrencyTokenPattern = new(
        @"(?<![A-Za-z0-9_])(?<amount>(?:\d+(?:\.\d*)?|\.\d+))\s*(?<currency>[A-Za-z]{3})(?![A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TargetPattern = new(
        @"^(?<expression>.+?)\s+(?<target>[A-Za-z]{3})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex GroupCurrencyPattern = new(
        @"^\((?<expression>.*)\)(?<currency>[A-Za-z]{3})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public static bool TryEvaluate(
        string input,
        IReadOnlyDictionary<string, decimal> ratesFromMyr,
        out decimal value,
        out string target)
    {
        value = 0;
        target = string.Empty;

        var expression = input.Trim().Replace('×', '*').Replace('÷', '/');
        string? explicitTarget = null;
        var targetMatch = TargetPattern.Match(expression);
        if (targetMatch.Success)
        {
            expression = targetMatch.Groups["expression"].Value.TrimEnd();
            explicitTarget = targetMatch.Groups["target"].Value.ToUpperInvariant();
        }

        var matches = CurrencyTokenPattern.Matches(expression);
        if (matches.Count == 0)
        {
            var groupExpression = expression;
            var scriptPrefix = string.Empty;
            var separator = expression.LastIndexOfAny(['\r', '\n', ',']);
            if (separator >= 0)
            {
                scriptPrefix = expression[..(separator + 1)];
                groupExpression = expression[(separator + 1)..].Trim();
            }

            var groupMatch = GroupCurrencyPattern.Match(groupExpression);
            if (!groupMatch.Success)
            {
                return false;
            }

            var source = groupMatch.Groups["currency"].Value.ToUpperInvariant();
            target = explicitTarget ?? "USD";
            if (!ratesFromMyr.TryGetValue(source, out var sourceRate) ||
                !ratesFromMyr.TryGetValue(target, out var groupTargetRate) ||
                sourceRate <= 0 ||
                groupTargetRate <= 0 ||
                !ExpressionEvaluator.TryEvaluateScript(
                    scriptPrefix + groupMatch.Groups["expression"].Value,
                    out var groupValue))
            {
                return false;
            }

            try
            {
                value = groupValue * groupTargetRate / sourceRate;
                return true;
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }

        var currencies = matches
            .Cast<Match>()
            .Select(match => match.Groups["currency"].Value.ToUpperInvariant())
            .ToArray();
        var distinctCurrencies = currencies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        target = explicitTarget ??
                 (distinctCurrencies.Length > 1
                     ? currencies[^1]
                     : "USD");

        if (!ratesFromMyr.TryGetValue(target, out var targetRate) || targetRate <= 0)
        {
            return false;
        }

        var calculationTarget = distinctCurrencies.Length == 1 ? distinctCurrencies[0] : target;
        if (!ratesFromMyr.TryGetValue(calculationTarget, out var calculationRate) || calculationRate <= 0)
        {
            return false;
        }

        foreach (var currency in currencies)
        {
            if (!ratesFromMyr.TryGetValue(currency, out var sourceRate) || sourceRate <= 0)
            {
                return false;
            }
        }

        try
        {
            var convertedExpression = CurrencyTokenPattern.Replace(expression, match =>
            {
                var amount = match.Groups["amount"].Value;
                var currency = match.Groups["currency"].Value.ToUpperInvariant();
                var factor = calculationRate / ratesFromMyr[currency];
                return $"({amount}*{factor.ToString("0.#############################", CultureInfo.InvariantCulture)})";
            });

            if (!ExpressionEvaluator.TryEvaluateScript(convertedExpression, out value))
            {
                return false;
            }

            if (!string.Equals(calculationTarget, target, StringComparison.OrdinalIgnoreCase))
            {
                value *= targetRate / calculationRate;
            }

            return true;
        }
        catch (OverflowException)
        {
            value = 0;
            return false;
        }
    }
}

internal static class ExpressionEvaluator
{
    public static bool TryEvaluate(string input, out decimal value) =>
        TryEvaluateExpression(input, null, out value);

    public static bool TryEvaluateScript(string input, out decimal value)
    {
        value = 0;
        var variables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var statements = input.Split(
            [',', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (statements.Length == 0)
        {
            return false;
        }

        foreach (var statement in statements)
        {
            if (TrySplitAssignment(statement, out var name, out var expression))
            {
                if (!TryEvaluateExpression(expression, variables, out var assigned))
                {
                    return false;
                }

                variables[name] = assigned;
                value = assigned;
                continue;
            }

            if (!TryEvaluateExpression(statement, variables, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryEvaluateExpression(
        string input,
        IReadOnlyDictionary<string, decimal>? variables,
        out decimal value)
    {
        value = 0;

        try
        {
            var parser = new Parser(input.Replace('×', '*').Replace('÷', '/'), variables);
            if (!parser.TryParseExpression(out value))
            {
                return false;
            }

            parser.SkipWhitespace();
            return parser.IsAtEnd;
        }
        catch (OverflowException)
        {
            value = 0;
            return false;
        }
    }

    private static bool TrySplitAssignment(string statement, out string name, out string expression)
    {
        name = string.Empty;
        expression = string.Empty;

        var equals = statement.IndexOf('=');
        if (equals <= 0 || equals == statement.Length - 1)
        {
            return false;
        }

        name = statement[..equals].Trim();
        expression = statement[(equals + 1)..].Trim();
        return IsIdentifier(name) && expression.Length > 0;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, decimal>? _variables;
        private int _index;

        public Parser(string text, IReadOnlyDictionary<string, decimal>? variables)
        {
            _text = text;
            _variables = variables;
        }

        public bool IsAtEnd => _index >= _text.Length;

        public bool TryParseExpression(out decimal value)
        {
            return TryParseExpression(out value, out _);
        }

        private bool TryParseExpression(out decimal value, out bool isPercentage)
        {
            if (!TryParseTerm(out value, out isPercentage))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (Match('+'))
                {
                    if (!TryParseTerm(out var right, out var rightIsPercentage))
                    {
                        return false;
                    }

                    value += rightIsPercentage ? value * right : right;
                    isPercentage = false;
                }
                else if (Match('-'))
                {
                    if (!TryParseTerm(out var right, out var rightIsPercentage))
                    {
                        return false;
                    }

                    value -= rightIsPercentage ? value * right : right;
                    isPercentage = false;
                }
                else
                {
                    return true;
                }
            }
        }

        private bool TryParseTerm(out decimal value, out bool isPercentage)
        {
            if (!TryParsePower(out value, out isPercentage))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                {
                    if (!TryParsePower(out var right, out _))
                    {
                        return false;
                    }

                    value *= right;
                    isPercentage = false;
                }
                else if (Match('/'))
                {
                    if (!TryParsePower(out var right, out _) || right == 0)
                    {
                        return false;
                    }

                    value /= right;
                    isPercentage = false;
                }
                else if (IsNext('('))
                {
                    if (!TryParsePower(out var right, out _))
                    {
                        return false;
                    }

                    value *= right;
                    isPercentage = false;
                }
                else
                {
                    return true;
                }
            }
        }

        private bool TryParsePower(out decimal value, out bool isPercentage)
        {
            if (!TryParseFactor(out value, out isPercentage))
            {
                return false;
            }

            SkipWhitespace();
            if (!Match('^'))
            {
                return true;
            }

            if (!TryParsePower(out var exponent, out _))
            {
                return false;
            }

            if (!TryPower(value, exponent, out value))
            {
                return false;
            }

            isPercentage = false;
            return true;
        }

        private static bool TryPower(decimal baseValue, decimal exponent, out decimal value)
        {
            value = 0m;
            if (exponent == decimal.Truncate(exponent) &&
                exponent >= int.MinValue &&
                exponent <= int.MaxValue)
            {
                var remaining = (long)exponent;
                var negative = remaining < 0;
                if (negative)
                {
                    remaining = -remaining;
                }

                var result = 1m;
                var factor = baseValue;
                try
                {
                    while (remaining > 0)
                    {
                        if ((remaining & 1) != 0)
                        {
                            result *= factor;
                        }

                        remaining >>= 1;
                        if (remaining > 0)
                        {
                            factor *= factor;
                        }
                    }

                    if (negative)
                    {
                        if (result == 0m)
                        {
                            return false;
                        }

                        result = 1m / result;
                    }

                    value = result;
                    return true;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            try
            {
                var result = Math.Pow((double)baseValue, (double)exponent);
                if (!double.IsFinite(result))
                {
                    return false;
                }

                value = (decimal)result;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private bool TryParseFactor(out decimal value, out bool isPercentage)
        {
            isPercentage = false;
            SkipWhitespace();

            if (Match('+'))
            {
                return TryParseFactor(out value, out isPercentage);
            }

            if (Match('-'))
            {
                if (!TryParseFactor(out value, out isPercentage))
                {
                    return false;
                }

                value = -value;
                return true;
            }

            if (Match('('))
            {
                if (!TryParseExpression(out value, out isPercentage))
                {
                    return false;
                }

                if (!Match(')'))
                {
                    return false;
                }
            }
            else if (!TryParseNumber(out value) &&
                     (!TryParseIdentifier(out var name) ||
                      _variables is null ||
                      !_variables.TryGetValue(name, out value)))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (!Match('%'))
                {
                    return true;
                }

                value /= 100m;
                isPercentage = true;
            }
        }

        private bool TryParseNumber(out decimal value)
        {
            SkipWhitespace();
            var start = _index;
            var hasDigits = false;
            var hasDecimalPoint = false;

            while (_index < _text.Length)
            {
                var character = _text[_index];
                if (char.IsDigit(character))
                {
                    hasDigits = true;
                    _index++;
                    continue;
                }

                if (character == '.' && !hasDecimalPoint)
                {
                    hasDecimalPoint = true;
                    _index++;
                    continue;
                }

                break;
            }

            value = 0;
            return hasDigits && decimal.TryParse(
                _text[start.._index],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value);
        }

        private bool TryParseIdentifier(out string name)
        {
            SkipWhitespace();
            if (_index >= _text.Length ||
                !(char.IsLetter(_text[_index]) || _text[_index] == '_'))
            {
                name = string.Empty;
                return false;
            }

            var start = _index++;
            while (_index < _text.Length &&
                   (char.IsLetterOrDigit(_text[_index]) || _text[_index] == '_'))
            {
                _index++;
            }

            name = _text[start.._index];
            return true;
        }

        public void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }
        }

        private bool Match(char expected)
        {
            if (_index >= _text.Length || _text[_index] != expected)
            {
                return false;
            }

            _index++;
            return true;
        }

        private bool IsNext(char expected) =>
            _index < _text.Length && _text[_index] == expected;
    }
}
