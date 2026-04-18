using System.Text;
using System.Text.RegularExpressions;

namespace OrderTextTrainer.Core.Services;

public sealed class TextNormalizer
{
    private static readonly Dictionary<char, string> CharMap = new()
    {
        ['，'] = ",", ['：'] = ":", ['；'] = ";", ['（'] = "(", ['）'] = ")", ['【'] = "[", ['】'] = "]",
        ['“'] = "\"", ['”'] = "\"", ['‘'] = "'", ['’'] = "'", ['　'] = " ", ['×'] = "x", ['✖'] = "x",
        ['✕'] = "x", ['✗'] = "x", ['✘'] = "x", ['＋'] = "+", ['➕'] = "+",
        ['①'] = "1", ['②'] = "2", ['③'] = "3", ['④'] = "4", ['⑤'] = "5", ['⑥'] = "6",
        ['❶'] = "1", ['❷'] = "2", ['❸'] = "3", ['❹'] = "4", ['❺'] = "5", ['❻'] = "6",
        ['❼'] = "7", ['❽'] = "8", ['❾'] = "9", ['❿'] = "10",
        ['盞'] = "盏", ['風'] = "风", ['鈴'] = "铃", ['雲'] = "云", ['樓'] = "楼", ['貨'] = "货",
        ['號'] = "号", ['區'] = "区", ['東'] = "东", ['廣'] = "广", ['鎮'] = "镇", ['側'] = "侧",
        ['拋'] = "抛", ['個'] = "个", ['禮'] = "礼", ['體'] = "体"
    };

    public string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (CharMap.TryGetValue(ch, out var mapped))
            {
                builder.Append(mapped);
                continue;
            }

            if (ch is '\u00A0' or '\u2002' or '\u2003' or '\u2009')
            {
                builder.Append(' ');
                continue;
            }

            if (ch >= '０' && ch <= '９')
            {
                builder.Append((char)('0' + ch - '０'));
                continue;
            }

            // Convert the common full-width ASCII block to half-width before parsing.
            if (ch >= '\uFF01' && ch <= '\uFF5E')
            {
                builder.Append((char)(ch - 0xFEE0));
                continue;
            }

            builder.Append(ch);
        }

        var normalized = builder.ToString()
            .Normalize(NormalizationForm.FormKC)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("·", " ")
            .Replace("🎁", string.Empty)
            .Replace("\\", "/");

        normalized = Regex.Replace(normalized, @"\s*<br\s*/?>\s*", "\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s*</p\s*>\s*", "\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"<[^>]+>", " ", RegexOptions.IgnoreCase);
        normalized = normalized
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);

        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        normalized = Regex.Replace(
            normalized,
            @"(?<![A-Za-z])(?:\+?86[- ]?)?[0-9OoＯ〇\- ]{7,}(?![A-Za-z])",
            match => match.Value
                .Replace('O', '0')
                .Replace('o', '0')
                .Replace('Ｏ', '0')
                .Replace('〇', '0'));
        return normalized.Trim();
    }
}
