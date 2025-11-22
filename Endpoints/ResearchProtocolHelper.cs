using System.Text;
using System.Text.RegularExpressions;
using ResearchApi.Domain;
using ResearchApi.Endpoints;
using ResearchApi.Endpoints.DTOs;

public static class ResearchProtocolHelper
{
    private const string ClarificationsBeginMarker = "[LOCAL_DEEP_RESEARCH_CLARIFICATIONS_BEGIN]";
    private const string ClarificationsEndMarker   = "[LOCAL_DEEP_RESEARCH_CLARIFICATIONS_END]";

    /// <summary>
    /// Читает [DR_BREADTH=3][DR_DEPTH=2] из любого сообщения.
    /// Возвращает (null, null), если тегов нет.
    /// </summary>
    public static (int? breadth, int? depth) ExtractBreadthDepthFromMessages(
        IReadOnlyList<OpenAiChatMessageDto> messages)
    {
        int? breadth = null;
        int? depth   = null;

        var breadthRegex = new Regex(@"\[DR_BREADTH=(\d+)\]", RegexOptions.IgnoreCase);
        var depthRegex   = new Regex(@"\[DR_DEPTH=(\d+)\]", RegexOptions.IgnoreCase);

        foreach (var msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg.Content))
                continue;

            var content = msg.Content;

            var bMatch = breadthRegex.Match(content);
            if (bMatch.Success && int.TryParse(bMatch.Groups[1].Value, out var bVal))
            {
                breadth = bVal;
            }

            var dMatch = depthRegex.Match(content);
            if (dMatch.Success && int.TryParse(dMatch.Groups[1].Value, out var dVal))
            {
                depth = dVal;
            }
        }

        return (breadth, depth);
    }

    /// <summary>
    /// Парсинг вопросов из содержимого с блоком кларификаций
    /// </summary>
    public static List<string> ExtractQuestionsFromContent(string content)
    {
        var result = new List<string>();

        var beginIdx = content.IndexOf(ClarificationsBeginMarker, StringComparison.Ordinal);
        var endIdx   = content.IndexOf(ClarificationsEndMarker,   StringComparison.Ordinal);

        if (beginIdx < 0 || endIdx < 0 || endIdx <= beginIdx)
            return result;

        var start = beginIdx + ClarificationsBeginMarker.Length;
        var len   = endIdx - start;
        var block = content.Substring(start, len);

        var lines = block.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        foreach (var line in lines)
        {
            var stripped = Regex.Replace(line, @"^\d+[\.\)]\s*", "");
            stripped = stripped.Trim();
            if (!string.IsNullOrWhiteSpace(stripped))
                result.Add(stripped);
        }

        return result;
    }

    /// <summary>
    /// Парсинг ответов пользователя из текста
    /// </summary>
    public static List<string> ParseAnswersFromUserText(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var current = new StringBuilder();
        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^\d+[\.\)\-]\s*"))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                var stripped = Regex.Replace(line, @"^\d+[\.\)\-]\s*", "");
                current.Append(stripped);
            }
            else
            {
                if (current.Length > 0)
                {
                    current.Append(' ');
                    current.Append(line);
                }
                else
                {
                    current.Append(line);
                }
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString().Trim());

        return result;
    }
}
