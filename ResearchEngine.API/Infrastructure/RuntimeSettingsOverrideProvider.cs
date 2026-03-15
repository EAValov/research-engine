using Microsoft.Extensions.Configuration;

namespace ResearchEngine.Infrastructure;

public sealed class RuntimeSettingsOverrideSource : IConfigurationSource
{
    public RuntimeSettingsOverrideProvider Provider { get; } = new();

    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}

public sealed class RuntimeSettingsOverrideProvider : ConfigurationProvider
{
    private readonly object _sync = new();

    public void SetValues(IEnumerable<KeyValuePair<string, string?>> values)
    {
        lock (_sync)
        {
            foreach (var pair in values)
            {
                if (pair.Value is null)
                {
                    Data.Remove(pair.Key);
                }
                else
                {
                    Data[pair.Key] = pair.Value;
                }
            }

            OnReload();
        }
    }
}
