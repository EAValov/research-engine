using System.Text;

sealed class CliArguments
{
    private readonly Dictionary<string, List<string?>> _values;

    private CliArguments(Dictionary<string, List<string?>> values)
    {
        _values = values;
    }

    public static CliArguments Parse(string[] args)
    {
        var parsed = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var keyValue = arg[2..];
            var equalsIndex = keyValue.IndexOf('=');
            string key;
            string? value = null;

            if (equalsIndex >= 0)
            {
                key = keyValue[..equalsIndex];
                value = keyValue[(equalsIndex + 1)..];
            }
            else
            {
                key = keyValue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
            }

            if (!parsed.TryGetValue(key, out var values))
            {
                values = [];
                parsed[key] = values;
            }

            values.Add(value);
        }

        return new CliArguments(parsed);
    }

    public bool Contains(string key)
        => _values.ContainsKey(key);

    public string? Last(string key)
        => _values.TryGetValue(key, out var matches)
            ? matches.LastOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            : null;
}

static class CommandLine
{
    public static string[] Split(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var args = new List<string>();
        var current = new StringBuilder();
        char? quote = null;

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (quote is null && char.IsWhiteSpace(ch))
            {
                AddCurrent();
                continue;
            }

            if ((ch == '"' || ch == '\'') && (quote is null || quote == ch))
            {
                quote = quote is null ? ch : null;
                continue;
            }

            if (ch == '\\' && i + 1 < value.Length && quote == '"')
            {
                current.Append(value[++i]);
                continue;
            }

            current.Append(ch);
        }

        AddCurrent();
        return [.. args];

        void AddCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            args.Add(current.ToString());
            current.Clear();
        }
    }
}
