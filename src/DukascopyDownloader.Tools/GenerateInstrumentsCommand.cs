using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DukascopyDownloader.Tools;

internal static class GenerateInstrumentsCommand
{
    internal const string DefaultUrl = "https://freeserv.dukascopy.com/2.0/index.php?path=common%2Finstruments";
    internal const string DefaultReferrer = "https://freeserv.dukascopy.com/";
    internal const string HistoryStartUrl = "https://datafeed.dukascopy.com/datafeed/metadata/HistoryStart.bi5";

    public static async Task<int> RunAsync(
        string? root,
        string? url,
        string? outPath)
    {
        var rootDir = GetPathOrDefault(root, Directory.GetCurrentDirectory());
        var docsDir = Path.Combine(rootDir, "docs");
        var outMarkdownPath = GetPathOrDefault(outPath, Path.Combine(docsDir, "instruments.md"));
        var effectiveUrl = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url;

        JsonObject dukascopyData;
        try
        {
            dukascopyData = await FetchInstrumentsAsync(effectiveUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error fetching instruments: {ex.Message}");
            return 1;
        }

        Dictionary<string, HistoryEntry> history;
        try
        {
            history = await FetchHistoryStartAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error parsing HistoryStart.bi5: {ex.Message}");
            return 1;
        }

        var content = RenderMarkdown(dukascopyData, history, effectiveUrl);

        Directory.CreateDirectory(Path.GetDirectoryName(outMarkdownPath)!);
        File.WriteAllText(outMarkdownPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Wrote {outMarkdownPath}");
        return 0;
    }

    private static string GetPathOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(fallback)
            : Path.GetFullPath(value);
    }

    private static async Task<JsonObject> FetchInstrumentsAsync(string url)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Referrer = new Uri(DefaultReferrer);
        var raw = await client.GetStringAsync(url);
        var payload = UnwrapJsonp(raw);
        return JsonNode.Parse(payload)?.AsObject() ?? throw new InvalidOperationException("Unexpected JSON payload.");
    }

    private static string UnwrapJsonp(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("jsonp(", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            return trimmed["jsonp(".Length..^1];
        }

        throw new InvalidOperationException("Unexpected JSONP wrapper.");
    }

    private static async Task<Dictionary<string, HistoryEntry>> FetchHistoryStartAsync()
    {
        using var client = new HttpClient();
        var payload = await client.GetByteArrayAsync(HistoryStartUrl);
        await using var stream = new MemoryStream(payload, writable: false);
        var history = new Dictionary<string, HistoryEntry>(StringComparer.OrdinalIgnoreCase);

        while (TryReadUInt16BE(stream, out var nameLen))
        {
            if (nameLen == 0 || nameLen > 256)
            {
                throw new InvalidDataException($"Invalid instrument name length: {nameLen}.");
            }

            var nameBytes = ReadBytes(stream, nameLen, "instrument name");
            var symbol = Encoding.ASCII.GetString(nameBytes).Trim();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new InvalidDataException("Instrument name was empty or whitespace.");
            }

            var count = ReadUInt64BE(stream, "period count");
            if (count == 0 || count > 16)
            {
                throw new InvalidDataException($"Invalid period count {count} for instrument {symbol}.");
            }

            var periods = new Dictionary<ulong, ulong>();
            for (var i = 0; i < (long)count; i++)
            {
                var periodMs = ReadUInt64BE(stream, "periodMs");
                var startMs = ReadUInt64BE(stream, "startTimeMs");
                periods[periodMs] = startMs;
            }

            history[symbol] = new HistoryEntry(symbol, periods);
        }

        return history;
    }

    private static bool TryReadUInt16BE(Stream stream, out ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        var read = stream.Read(buffer);
        if (read == 0)
        {
            value = 0;
            return false; // clean EOF
        }

        if (read != 2)
        {
            throw new EndOfStreamException("Unexpected EOF reading instrument name length.");
        }

        value = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        return true;
    }

    private static ulong ReadUInt64BE(Stream stream, string context)
    {
        Span<byte> buffer = stackalloc byte[8];
        var read = stream.Read(buffer);
        if (read != 8)
        {
            throw new EndOfStreamException($"Unexpected EOF reading {context}.");
        }

        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private static byte[] ReadBytes(Stream stream, int length, string context)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(buffer, offset, length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException($"Unexpected EOF reading {context} (needed {length} bytes, got {offset}).");
            }
            offset += read;
        }

        return buffer;
    }

    private static string RenderMarkdown(JsonObject dukascopyData, Dictionary<string, HistoryEntry> history, string url)
    {
        var instruments = dukascopyData["instruments"] as JsonObject ?? new JsonObject();
        var groups = dukascopyData["groups"] as JsonObject ?? new JsonObject();

        var sections = new List<string>();

        foreach (var group in groups.OrderBy(kv => kv.Value?["title"]?.ToString() ?? string.Empty))
        {
            var groupObj = group.Value as JsonObject;
            if (groupObj is null)
            {
                continue;
            }

            var instrumentIds = groupObj["instruments"] as JsonArray;
            if (instrumentIds is null || instrumentIds.Count == 0)
            {
                continue;
            }

            var items = new List<Dictionary<string, string>>();

            foreach (var idNode in instrumentIds)
            {
                var instId = idNode?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(instId))
                {
                    continue;
                }

                var inst = instruments[instId] as JsonObject;
                if (inst is null)
                {
                    continue;
                }

                var desc = inst["description"]?.ToString()
                           ?? inst["title"]?.ToString()
                           ?? inst["name"]?.ToString()
                           ?? string.Empty;

                var instrumentCode = inst["historical_filename"]?.ToString()
                                     ?? inst["title"]?.ToString()
                                     ?? inst["name"]?.ToString()
                                     ?? instId;

                var resolved = ResolveMetadata(instrumentCode, inst, history);

                items.Add(new Dictionary<string, string>
                {
                    ["desc"] = desc.Replace("|", "\\|"),
                    ["instrument"] = $"`{instrumentCode}`",
                    ["tick"] = resolved.Tick,
                    ["m1"] = resolved.M1,
                    ["h1"] = resolved.H1,
                    ["d1"] = resolved.D1
                });
            }

            if (items.Count == 0)
            {
                continue;
            }

            items.Sort((a, b) => string.Compare(a["desc"], b["desc"], StringComparison.Ordinal));
            sections.Add(BuildTable(groupObj["title"]?.ToString() ?? "Unknown", items));
        }

        var lines = new List<string>
        {
            "# Dukascopy Instruments",
            string.Empty,
            "This catalog lists Dukascopy instruments grouped by category, with the earliest available history per timeframe. Dates are UTC (`YYYY-MM-DD`). `undefined` means no history is advertised for that timeframe.",
            string.Empty,
            $"_Last updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC_",
            string.Empty
        };

        lines.AddRange(sections);
        lines.Add(string.Empty);
        lines.Add("Sources:");
        lines.Add("- Dukascopy public instruments API");

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    private static string BuildTable(string groupTitle, List<Dictionary<string, string>> items)
    {
        var header = $"### {groupTitle}{Environment.NewLine}{Environment.NewLine}"
                     + "| Description | Instrument | Tick/S1 since | M1–M30 since | H1–H4 since | D1/MN1 since |" + Environment.NewLine
                     + "| --- | --- | --- | --- | --- | --- |" + Environment.NewLine;

        var rows = items
            .Select(item => $"| {item["desc"]} | {item["instrument"]} | {item["tick"]} | {item["m1"]} | {item["h1"]} | {item["d1"]} |");

        return header + string.Join(Environment.NewLine, rows) + Environment.NewLine;
    }

    private static ResolvedMetadata ResolveMetadata(string instrumentCode, JsonObject inst, Dictionary<string, HistoryEntry> history)
    {
        var lookupKey = Canonicalize(instrumentCode);
        if (history.TryGetValue(lookupKey, out var entry))
        {
            return new ResolvedMetadata(
                GetDate(entry, 0),
                GetDate(entry, 60_000),
                GetDate(entry, 3_600_000),
                GetDate(entry, 86_400_000));
        }

        // Fallback to API data if HistoryStart is missing for this instrument.
        var tick = PickFieldFromApi(inst, "history_start_tick");
        var m1 = PickFieldFromApi(inst, "history_start_60sec");
        var h1 = PickFieldFromApi(inst, "history_start_60min");
        var d1 = PickFieldFromApi(inst, "history_start_day");

        return new ResolvedMetadata(tick, m1, h1, d1);
    }

    private static string Canonicalize(string instrumentCode)
    {
        return instrumentCode.Replace(".", string.Empty)
            .Replace("/", string.Empty)
            .Replace("-", string.Empty)
            .ToUpperInvariant();
    }

    private static string GetDate(HistoryEntry entry, ulong periodMs)
    {
        if (entry.Periods.TryGetValue(periodMs, out var ms))
        {
            return MsToDate(ms);
        }

        return "undefined";
    }

    private static string PickFieldFromApi(JsonObject inst, string field)
    {
        if (TryGetValidMilliseconds(inst[field], out var value))
        {
            return MsToDate(value);
        }

        return "undefined";
    }

    private static bool TryGetValidMilliseconds(JsonNode? node, out long value)
    {
        value = 0;
        if (!TryReadLong(node, out var parsed))
        {
            return false;
        }

        value = parsed;
        return value > 0;
    }

    private static bool TryReadLong(JsonNode? node, out long value)
    {
        value = 0;
        if (node is null)
        {
            return false;
        }

        if (node is JsonValue v && v.TryGetValue<long>(out var direct))
        {
            value = direct;
            return true;
        }

        return long.TryParse(node.ToString(), out value);
    }

    private static string MsToDate(long value)
    {
        if (value < 10_000_000_000)
        {
            value *= 1000;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(value)
            .ToUniversalTime()
            .ToString("yyyy-MM-dd");
    }

    private static string MsToDate(ulong value) => MsToDate(unchecked((long)value));

    private sealed record HistoryEntry(string Symbol, Dictionary<ulong, ulong> Periods);

    private sealed record ResolvedMetadata(string Tick, string M1, string H1, string D1);
}
