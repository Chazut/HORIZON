namespace Horizon;

internal readonly struct BotLabelSample
{
    internal readonly (string personality, string action, bool ghost) Behavior;
    internal readonly bool FromHost;
    internal readonly (float current, float maximum)? Health;
    internal readonly double? Value;

    internal BotLabelSample((string, string, bool) behavior, bool fromHost = false,
        (float, float)? health = null, double? value = null)
    {
        Behavior = behavior;
        FromHost = fromHost;
        Health = health;
        Value = value;
    }
}
