using System.Text.Json;

namespace MainApi.Domain;

public static class PriceRuleTypes
{
    public const string Base = "base";
    public const string Bulk = "bulk";
    public const string Clearance = "clearance";
}

public sealed class PriceRuleRecord
{
    public long Id { get; set; }

    public string RuleType { get; set; } = PriceRuleTypes.Base;

    public string PriceName { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string? ClearanceSelectionsJson { get; set; }

    public int RequiredQuantity { get; set; }

    public int PriceValue { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class PriceRuleUpsertItem
{
    public string RuleType { get; set; } = PriceRuleTypes.Base;

    public string PriceName { get; set; } = string.Empty;

    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;

    public string? ClearanceSelectionsJson { get; set; }

    public int RequiredQuantity { get; set; }

    public int PriceValue { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class PriceRuleUpsertResult
{
    public int TotalCount { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }
}

public sealed class ClearanceRuleSelection
{
    public string SpecificationToken { get; set; } = string.Empty;

    public string ModelToken { get; set; } = string.Empty;
}

public static class ClearanceRuleSelectionSerializer
{
    private const string LegacyPrefix = "clearance-selections:";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(IReadOnlyList<ClearanceRuleSelection> selections)
    {
        return JsonSerializer.Serialize(new ClearanceRuleSelectionsDocument
        {
            Version = 1,
            Selections = selections.ToList()
        }, SerializerOptions);
    }

    public static bool TryDeserialize(string? value, out List<ClearanceRuleSelection> selections)
    {
        selections = new List<ClearanceRuleSelection>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            if (value.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            {
                selections = JsonSerializer.Deserialize<List<ClearanceRuleSelection>>(value[LegacyPrefix.Length..], SerializerOptions)
                    ?? new List<ClearanceRuleSelection>();
                return true;
            }

            var document = JsonSerializer.Deserialize<ClearanceRuleSelectionsDocument>(value, SerializerOptions);
            if (document?.Selections is null)
            {
                return false;
            }

            selections = document.Selections;
            return document.Version == 1;
        }
        catch (JsonException)
        {
            selections = new List<ClearanceRuleSelection>();
            return false;
        }
    }
}

public sealed class ClearanceRuleSelectionsDocument
{
    public int Version { get; set; } = 1;

    public List<ClearanceRuleSelection> Selections { get; set; } = new();
}
