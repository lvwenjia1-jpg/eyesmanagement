using System.Text.Encodings.Web;
using System.Text.Json;
using OrderTextTrainer.Core.Models;

namespace OrderTextTrainer.Core.Services;

public sealed class RuleRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public string GetDefaultRulePath()
    {
        return RuntimeDataPathHelper.GetDataFilePath("parser-rules.json");
    }

    public string GetDefaultSamplePath()
    {
        return RuntimeDataPathHelper.GetDataFilePath("training-samples.jsonl");
    }

    public ParserRuleSet LoadOrCreate(string? path = null)
    {
        var fullPath = path ?? GetDefaultRulePath();
        if (!File.Exists(fullPath))
        {
            var defaultRules = SanitizeRuleSet(ParserRuleSet.CreateDefault());
            Save(defaultRules, fullPath);
            return defaultRules;
        }

        var json = File.ReadAllText(fullPath);
        return SanitizeRuleSet(JsonSerializer.Deserialize<ParserRuleSet>(json, JsonOptions) ?? ParserRuleSet.CreateDefault());
    }

    public void Save(ParserRuleSet ruleSet, string? path = null)
    {
        var fullPath = path ?? GetDefaultRulePath();
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        BackupExistingFile(fullPath);
        var json = JsonSerializer.Serialize(SanitizeRuleSet(ruleSet), JsonOptions);
        File.WriteAllText(fullPath, json);
    }

    public void AppendSample(TrainingSample sample, string? path = null)
    {
        var fullPath = path ?? GetDefaultSamplePath();
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var line = JsonSerializer.Serialize(sample, JsonOptions);
        File.AppendAllText(fullPath, line + Environment.NewLine);
    }

    private static ParserRuleSet SanitizeRuleSet(ParserRuleSet ruleSet)
    {
        ruleSet.WearTypeAliases ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (ruleSet.WearTypeAliases.TryGetValue("半年抛", out var aliases) && aliases is not null)
        {
            ruleSet.WearTypeAliases["半年抛"] = aliases
                .Where(alias => !string.Equals(alias?.Trim(), "LENSPOP", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return ruleSet;
    }
    private static void BackupExistingFile(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return;
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length == 0)
        {
            return;
        }

        var backupPath = $"{fullPath}.bak-{DateTime.Now:yyyyMMddHHmmss}";
        File.Copy(fullPath, backupPath, overwrite: false);

        var backupDirectory = fileInfo.Directory;
        if (backupDirectory is null)
        {
            return;
        }

        foreach (var staleBackup in backupDirectory
                     .GetFiles($"{fileInfo.Name}.bak-*")
                     .OrderByDescending(item => item.LastWriteTimeUtc)
                     .Skip(10))
        {
            try
            {
                staleBackup.Delete();
            }
            catch
            {
            }
        }
    }
}
