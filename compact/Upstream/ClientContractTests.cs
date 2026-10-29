using System;
using System.IO;
using System.Text.Json;

namespace CodexEdgeWidget;

// Quota-only JSON contracts. These fixtures do not contain account or login data.
public static class ClientContractTests
{
    public static void Run(Action<string, Action> check)
    {
        check("quota parser reads five-hour and weekly windows", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":32.5,"windowDurationMins":300,"resetsAt":1800000000},"secondary":{"usedPercent":58,"windowDurationMins":10080,"resetsAt":1800500000}}}""");
            Equal(32.5, data.Primary!.UsedPercent);
            Equal(300, data.Primary.WindowMinutes);
            Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000), data.Primary.ResetsAt);
            Equal(58.0, data.Secondary!.UsedPercent);
            Equal(10080, data.Secondary.WindowMinutes);
        });

        check("quota parser preserves swapped server slots", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":73,"windowDurationMins":10080},"secondary":{"usedPercent":14,"windowDurationMins":300}}}""");
            Equal(10080, data.Primary!.WindowMinutes);
            Equal(300, data.Secondary!.WindowMinutes);
            Equal(73.0, data.Primary.UsedPercent);
            Equal(14.0, data.Secondary.UsedPercent);
        });

        check("missing used percent never becomes a full remaining quota", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"windowDurationMins":300},"secondary":{"usedPercent":42,"windowDurationMins":10080}}}""");
            Equal<QuotaPeriod?>(null, data.Primary);
            Equal(42.0, data.Secondary!.UsedPercent);
        });

        check("null used percent is unavailable", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":null,"windowDurationMins":300}}}""");
            Equal<QuotaPeriod?>(null, data.Primary);
        });

        check("actual zero usage remains a valid zero", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":0,"windowDurationMins":300}}}""");
            Equal(0.0, data.Primary!.UsedPercent);
        });

        check("quota percent clamps finite server overshoot", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":-1,"windowDurationMins":300},"secondary":{"usedPercent":101,"windowDurationMins":10080}}}""");
            Equal(0.0, data.Primary!.UsedPercent);
            Equal(100.0, data.Secondary!.UsedPercent);
        });

        check("nonfinite or nonnumeric usage is unavailable", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":1e400,"windowDurationMins":300},"secondary":{"usedPercent":"unknown","windowDurationMins":10080}}}""");
            Equal<QuotaPeriod?>(null, data.Primary);
            Equal<QuotaPeriod?>(null, data.Secondary);
        });

        check("missing and nonpositive window durations are unavailable", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":10},"secondary":{"usedPercent":20,"windowDurationMins":0}}}""");
            Equal<QuotaPeriod?>(null, data.Primary);
            Equal<QuotaPeriod?>(null, data.Secondary);
        });

        check("unknown reset preserves quota without inventing a countdown", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":12,"windowDurationMins":300},"secondary":{"usedPercent":34,"windowDurationMins":10080,"resetsAt":null}}}""");
            Equal<DateTimeOffset?>(null, data.Primary!.ResetsAt);
            Equal<DateTimeOffset?>(null, data.Secondary!.ResetsAt);
        });

        check("out-of-range reset cannot crash the panel", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":12,"windowDurationMins":300,"resetsAt":9223372036854775807},"secondary":{"usedPercent":34,"windowDurationMins":10080,"resetsAt":-1}}}""");
            Equal<DateTimeOffset?>(null, data.Primary!.ResetsAt);
            Equal<DateTimeOffset?>(null, data.Secondary!.ResetsAt);
        });

        check("expired reset is preserved without resetting quota", () =>
        {
            var data = Read("""{"rateLimits":{"primary":{"usedPercent":85,"windowDurationMins":300,"resetsAt":1600000000}}}""");
            Equal(85.0, data.Primary!.UsedPercent);
            Equal(DateTimeOffset.FromUnixTimeSeconds(1600000000), data.Primary.ResetsAt);
        });

        check("explicitly unavailable windows remain null", () =>
        {
            var data = Read("""{"rateLimits":{"primary":null,"secondary":null}}""");
            Equal<QuotaPeriod?>(null, data.Primary);
            Equal<QuotaPeriod?>(null, data.Secondary);
        });

        check("missing rateLimits fails instead of inventing data", () =>
        {
            try { Read("{}"); }
            catch (InvalidDataException) { return; }
            throw new Exception("Expected InvalidDataException for missing rateLimits.");
        });
    }

    private static QuotaData Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return QuotaClient.Parse(document.RootElement);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual))
            throw new Exception($"Expected {(expected is null ? "<null>" : expected.ToString())}, actual {(actual is null ? "<null>" : actual.ToString())}.");
    }
}
