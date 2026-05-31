sealed class AppHostSettings
{
    private readonly Dictionary<string, string> _defaults;
    private readonly CliArguments _arguments;

    private AppHostSettings(Dictionary<string, string> defaults, CliArguments arguments)
    {
        _defaults = defaults;
        _arguments = arguments;
    }

    public static AppHostSettings Load(CliArguments arguments)
    {
        var path = arguments.Last("defaults-file")
            ?? Environment.GetEnvironmentVariable("RESEARCH_ASPIRE_DEFAULTS_FILE")
            ?? FindDefaultsFile();

        return new AppHostSettings(ReadDefaults(path), arguments);
    }

    public string Get(string environmentKey, string argumentKey)
    {
        if (_arguments.Last(argumentKey) is { Length: > 0 } argumentValue)
        {
            return argumentValue;
        }

        if (Environment.GetEnvironmentVariable(environmentKey) is { Length: > 0 } environmentValue)
        {
            return environmentValue;
        }

        if (_defaults.TryGetValue(environmentKey, out var defaultValue) && defaultValue.Length > 0)
        {
            return defaultValue;
        }

        throw new InvalidOperationException($"Missing AppHost setting '{environmentKey}'. Add it to apphost.env, an environment variable, or --{argumentKey}.");
    }

    public string GetOptional(string environmentKey, string argumentKey)
    {
        if (_arguments.Contains(argumentKey))
        {
            return _arguments.Last(argumentKey) ?? string.Empty;
        }

        if (Environment.GetEnvironmentVariable(environmentKey) is { } environmentValue)
        {
            return environmentValue;
        }

        return _defaults.GetValueOrDefault(environmentKey, string.Empty);
    }

    public ContainerImageReference Image(string environmentKey, string argumentKey)
        => ContainerImageReference.Parse(Get(environmentKey, argumentKey));

    public int Int(string environmentKey, string argumentKey)
    {
        var value = Get(environmentKey, argumentKey);
        if (int.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"AppHost setting '{environmentKey}' must be an integer, but was '{value}'.");
    }

    public bool Bool(string environmentKey, string argumentKey)
    {
        if (_arguments.Contains(argumentKey) && _arguments.Last(argumentKey) is null)
        {
            return true;
        }

        var value = Get(environmentKey, argumentKey);
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        var normalized = value.ToLowerInvariant();
        if (normalized is "1" or "yes" or "on")
        {
            return true;
        }

        if (normalized is "0" or "no" or "off")
        {
            return false;
        }

        throw new InvalidOperationException($"AppHost setting '{environmentKey}' must be a boolean, but was '{value}'.");
    }

    public TEnum Enum<TEnum>(string environmentKey, string argumentKey)
        where TEnum : struct, Enum
    {
        var value = Get(environmentKey, argumentKey);
        if (System.Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"AppHost setting '{environmentKey}' must be one of: {string.Join(", ", System.Enum.GetNames<TEnum>())}.");
    }

    private static string FindDefaultsFile()
    {
        foreach (var basePath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(basePath);
            while (directory is not null)
            {
                foreach (var candidate in new[]
                {
                    Path.Combine(directory.FullName, "apphost.env"),
                    Path.Combine(directory.FullName, "ResearchEngine.AppHost", "apphost.env")
                })
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Could not find ResearchEngine.AppHost/apphost.env. Pass --defaults-file <path> or set RESEARCH_ASPIRE_DEFAULTS_FILE.");
    }

    private static Dictionary<string, string> ReadDefaults(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            values[key] = Unquote(value);
        }

        return values;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
