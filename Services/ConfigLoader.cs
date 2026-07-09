using System.IO;
using BiliCommunityGuard.App.Models;

namespace BiliCommunityGuard.App.Services;

public sealed class ConfigLoader
{
    private static readonly Dictionary<string, string> TopLevelKeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scan_interval_seconds"] = "scan_interval_seconds",
        ["扫描间隔秒数"] = "scan_interval_seconds",
        ["video_window_size"] = "video_window_size",
        ["最新视频保护数量"] = "video_window_size",
        ["dynamic_window_size"] = "dynamic_window_size",
        ["最新动态保护数量"] = "dynamic_window_size",
        ["scan_batch_size_per_cycle"] = "scan_batch_size_per_cycle",
        ["每轮扫描内容数"] = "scan_batch_size_per_cycle",
        ["comment_page_size"] = "comment_page_size",
        ["评论页大小"] = "comment_page_size",
        ["include_sub_replies"] = "include_sub_replies",
        ["是否扫描二级评论"] = "include_sub_replies",
        ["account_strategy"] = "account_strategy",
        ["账号轮转策略"] = "account_strategy",
        ["dry_run"] = "dry_run",
        ["仅调试不真实举报"] = "dry_run",
        ["min_re_report_interval_seconds"] = "min_re_report_interval_seconds",
        ["再次举报间隔秒数"] = "min_re_report_interval_seconds",
        ["account_cooldown_seconds"] = "account_cooldown_seconds",
        ["账号冷却秒数"] = "account_cooldown_seconds"
    };

    private static readonly Dictionary<string, string> SectionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["protect_ups"] = "protect_ups",
        ["保护UP主UID列表"] = "protect_ups",
        ["report"] = "report",
        ["举报设置"] = "report",
        ["request_interval_ms"] = "request_interval_ms",
        ["请求间隔毫秒"] = "request_interval_ms"
    };

    private static readonly Dictionary<string, string> ReportKeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reason"] = "reason",
        ["原因代码"] = "reason",
        ["content"] = "content",
        ["补充说明"] = "content"
    };

    private static readonly Dictionary<string, string> RequestIntervalKeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["min"] = "min",
        ["最小值"] = "min",
        ["max"] = "max",
        ["最大值"] = "max"
    };

    public AppConfig LoadFromFile(string path)
    {
        var content = File.ReadAllText(path);
        return ParseYaml(content);
    }

    public AppConfig ParseYaml(string content)
    {
        var config = new AppConfig();
        string? currentSection = null;

        foreach (var rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var lineWithoutComment = RemoveComment(rawLine);
            if (string.IsNullOrWhiteSpace(lineWithoutComment))
            {
                continue;
            }

            var indent = rawLine.TakeWhile(char.IsWhiteSpace).Count();
            var trimmed = lineWithoutComment.Trim();

            if (indent == 0)
            {
                if (trimmed.EndsWith(':'))
                {
                    currentSection = NormalizeSectionName(trimmed[..^1].Trim());
                    continue;
                }

                currentSection = null;
                ParseTopLevel(config, trimmed);
                continue;
            }

            ParseNested(config, currentSection, trimmed);
        }

        return config;
    }

    public string CreateSampleConfig()
    {
        return """
# 配置文件优先使用中文键名。
# 程序也兼容旧版英文键名，已有配置可以继续使用。

# 扫描间隔，单位秒。
扫描间隔秒数: 60

# 需要保护的 UP 主 UID 列表。每行一个数字。
保护UP主UID列表:
  - 12345678
  - 87654321

# 每个 UP 纳入扫描范围的最新视频数量。
最新视频保护数量: 10

# 每个 UP 纳入扫描范围的最新动态数量。
最新动态保护数量: 10

# 每轮实际扫描多少个内容。
# 为了降低风控，建议先保持 1。
每轮扫描内容数: 1

# 每次从评论区读取多少条一级评论。
# B 站接口单页通常最多 20。
评论页大小: 20

# 是否扫描二级评论。
# 当前程序只实现一级评论，所以保持 false。
是否扫描二级评论: false

举报设置:
  # 举报原因代码，可选值如下：
  # 0: 其他（仅此项会使用下面的“补充说明”）
  # 1: 垃圾广告
  # 2: 色情
  # 3: 刷屏
  # 4: 引战
  # 5: 剧透
  # 6: 政治
  # 7: 人身攻击
  # 8: 内容不相关
  # 9: 违法违规
  # 10: 低俗
  # 11: 非法网站
  # 12: 赌博诈骗
  # 13: 传播不实信息
  # 14: 怂恿教唆信息
  # 15: 侵犯隐私
  原因代码: 1

  # 举报补充说明，可为空字符串。
  # 仅当“原因代码”=0 时会生效。
  补充说明: ""

# 多账号轮转策略。当前仅支持 round_robin。
账号轮转策略: round_robin

# 调试模式。true 时只扫描和记录，不真实举报。
仅调试不真实举报: false

请求间隔毫秒:
  # 每次请求前的最小随机等待时间，单位毫秒。
  最小值: 1500

  # 每次请求前的最大随机等待时间，单位毫秒。
  最大值: 4000

# 同一账号对同一评论再次举报前的最小间隔，单位秒。
再次举报间隔秒数: 600

# 账号触发频控后的冷却时间，单位秒。
账号冷却秒数: 300
""";
    }

    private static string RemoveComment(string line)
    {
        var commentIndex = line.IndexOf('#');
        return commentIndex >= 0 ? line[..commentIndex] : line;
    }

    private static void ParseTopLevel(AppConfig config, string line)
    {
        var (rawKey, value) = SplitKeyValue(line);
        var key = NormalizeTopLevelKey(rawKey);

        switch (key)
        {
            case "scan_interval_seconds":
                config.ScanIntervalSeconds = ParseInt(value, key);
                break;
            case "video_window_size":
                config.VideoWindowSize = ParseInt(value, key);
                break;
            case "dynamic_window_size":
                config.DynamicWindowSize = ParseInt(value, key);
                break;
            case "scan_batch_size_per_cycle":
                config.ScanBatchSizePerCycle = ParseInt(value, key);
                break;
            case "comment_page_size":
                config.CommentPageSize = ParseInt(value, key);
                break;
            case "include_sub_replies":
                config.IncludeSubReplies = ParseBool(value, key);
                break;
            case "account_strategy":
                config.AccountStrategy = ParseString(value);
                break;
            case "dry_run":
                config.DryRun = ParseBool(value, key);
                break;
            case "min_re_report_interval_seconds":
                config.MinReReportIntervalSeconds = ParseInt(value, key);
                break;
            case "account_cooldown_seconds":
                config.AccountCooldownSeconds = ParseInt(value, key);
                break;
        }
    }

    private static void ParseNested(AppConfig config, string? section, string line)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return;
        }

        if (section == "protect_ups")
        {
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                return;
            }

            var value = line[2..].Trim();
            config.ProtectUps.Add(ParseLong(value, "protect_ups"));
            return;
        }

        var (rawKey, valuePart) = SplitKeyValue(line);
        var key = NormalizeNestedKey(section, rawKey);

        switch (section)
        {
            case "report":
                if (key == "reason")
                {
                    config.Report.Reason = ParseInt(valuePart, key);
                }
                else if (key == "content")
                {
                    config.Report.Content = ParseString(valuePart);
                }
                break;
            case "request_interval_ms":
                if (key == "min")
                {
                    config.RequestIntervalMs.Min = ParseInt(valuePart, key);
                }
                else if (key == "max")
                {
                    config.RequestIntervalMs.Max = ParseInt(valuePart, key);
                }
                break;
        }
    }

    private static (string Key, string Value) SplitKeyValue(string line)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0)
        {
            throw new InvalidDataException($"无法解析配置行: {line}");
        }

        return (line[..separatorIndex].Trim(), line[(separatorIndex + 1)..].Trim());
    }

    private static int ParseInt(string value, string key)
    {
        if (!int.TryParse(ParseString(value), out var parsed))
        {
            throw new InvalidDataException($"配置项 {key} 不是合法整数。");
        }

        return parsed;
    }

    private static long ParseLong(string value, string key)
    {
        if (!long.TryParse(ParseString(value), out var parsed))
        {
            throw new InvalidDataException($"配置项 {key} 不是合法长整数。");
        }

        return parsed;
    }

    private static bool ParseBool(string value, string key)
    {
        if (!bool.TryParse(ParseString(value), out var parsed))
        {
            throw new InvalidDataException($"配置项 {key} 不是合法布尔值。");
        }

        return parsed;
    }

    private static string ParseString(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) ||
             (trimmed.StartsWith('\'') && trimmed.EndsWith('\''))))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string NormalizeTopLevelKey(string key)
    {
        return TopLevelKeyAliases.TryGetValue(key, out var normalized)
            ? normalized
            : key;
    }

    private static string NormalizeSectionName(string section)
    {
        return SectionAliases.TryGetValue(section, out var normalized)
            ? normalized
            : section;
    }

    private static string NormalizeNestedKey(string section, string key)
    {
        return section switch
        {
            "report" when ReportKeyAliases.TryGetValue(key, out var reportKey) => reportKey,
            "request_interval_ms" when RequestIntervalKeyAliases.TryGetValue(key, out var requestKey) => requestKey,
            _ => key
        };
    }
}

