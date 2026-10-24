using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;

/// <summary>Reports missing UI translations as compiler errors on every app build.</summary>
public sealed class GameFlowLocalizationCheck : Microsoft.Build.Utilities.Task
{
    [Required] public string RepositoryRoot { get; set; }

    private sealed class Entry
    {
        public string Value;
        public int Line;
        public bool Invariant;
    }

    private readonly Regex entry = new Regex(
        @"^msgid ""((?:[^""\\]|\\.)*)""\s*\r?\nmsgstr ""((?:[^""\\]|\\.)*)""", RegexOptions.Multiline);
    private readonly Regex placeholder = new Regex(@"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})");
    private readonly Regex markup = new Regex(
        @"\b(?:Text|Content|Header|Title|Watermark|PlaceholderText|ToolTip\.Tip)\s*=\s*""([^""]+)""");
    private readonly Regex keyUse = new Regex(
        @"(?:\b(?:localization\w*|localizer|loc)\s*\[|\b(?:L|Loc|T|Label|Localized|Translate|TranslateOrDefault|GetText|phoneText)\s*\(|UiTextResources\.(?:Get|Format)\s*\()\s*""([^""]+)""|\{DynamicResource Ui\.([^}\s]+)\}|data-i18n(?:-aria)?=""([^""]+)""",
        RegexOptions.IgnoreCase);

    public override bool Execute()
    {
        var root = Path.GetFullPath(RepositoryRoot);
        var catalogueRoot = Path.Combine(root, "src", "GameFlow.App", "Localization");
        var catalogues = new Dictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(catalogueRoot, "*.po"))
        {
            var text = File.ReadAllText(path);
            foreach (var block in Regex.Split(text.Trim(), @"\r?\n[ \t]*\r?\n"))
            {
                var content = Regex.Replace(block, @"^#[^\r\n]*\r?\n?", "", RegexOptions.Multiline).Trim();
                if (content.Length == 0) { continue; }
                var header = content.StartsWith("msgid \"\"\n") || content.StartsWith("msgid \"\"\r\n");
                var grammar = header
                    ? "^msgid \"\"\\r?\\nmsgstr \"\"(?:\\r?\\n\"(?:[^\"\\\\]|\\\\.)*\")*$"
                    : "^msgid \"(?:[^\"\\\\]|\\\\.)+\"\\r?\\nmsgstr \"(?:[^\"\\\\]|\\\\.)*\"$";
                if (!Regex.IsMatch(content, grammar))
                {
                    Error("GFLOC001", path, 1, "Malformed PO entry. Use one quoted msgid and one quoted msgstr per entry; only the header may continue on additional lines.");
                }
            }
            var items = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (Match match in entry.Matches(text))
            {
                var key = Regex.Unescape(match.Groups[1].Value);
                if (key.Length == 0) { continue; }
                var line = 1 + text.Substring(0, match.Index).Count(c => c == '\n');
                if (items.ContainsKey(key))
                {
                    Error("GFLOC001", path, line, "Duplicate translation key: " + key);
                    continue;
                }
                var start = text.LastIndexOf("\n\n", match.Index, StringComparison.Ordinal);
                var comments = text.Substring(Math.Max(0, start), match.Index - Math.Max(0, start));
                items[key] = new Entry { Value = Regex.Unescape(match.Groups[2].Value), Line = line,
                    Invariant = comments.Contains("#. invariant") };
            }
            catalogues[Path.GetFileNameWithoutExtension(path)] = items;
        }
        Dictionary<string, Entry> english;
        if (!catalogues.TryGetValue("en", out english) || english.Count < 100)
        {
            Error("GFLOC001", Path.Combine(catalogueRoot, "en.po"), 1, "English translation catalogue is missing or unreadable.");
            return false;
        }
        foreach (var language in new[] { "en", "cs", "de", "es", "fr", "it", "pl", "ru" })
        {
            var path = Path.Combine(catalogueRoot, language + ".po");
            Dictionary<string, Entry> translated;
            if (!catalogues.TryGetValue(language, out translated))
            {
                Error("GFLOC002", path, 1, "Required translation catalogue is missing.");
                continue;
            }
            foreach (var pair in english)
            {
                Entry value;
                if (!translated.TryGetValue(pair.Key, out value) || string.IsNullOrWhiteSpace(value.Value))
                {
                    Error("GFLOC002", path, value == null ? 1 : value.Line, "Missing translation: " + pair.Key);
                    continue;
                }
                var expected = placeholder.Matches(pair.Value.Value).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
                var actual = placeholder.Matches(value.Value).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
                if (!expected.SequenceEqual(actual))
                {
                    Error("GFLOC003", path, value.Line, "Translation format arguments differ from English: " + pair.Key);
                }
                if (language != "en" && value.Value == pair.Value.Value && value.Value.Length > 18 && !value.Invariant)
                {
                    Error("GFLOC004", path, value.Line, "English text was copied without translation: " + pair.Key);
                }
            }
            foreach (var stale in translated.Keys.Except(english.Keys))
            {
                Error("GFLOC002", path, translated[stale].Line, "Translation key does not exist in English: " + stale);
            }
        }
        foreach (var path in Directory.GetFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Where(p => (p.EndsWith(".cs") || p.EndsWith(".axaml")) && !p.Split(Path.DirectorySeparatorChar).Any(s => s == "obj" || s == "bin")))
        {
            var text = File.ReadAllText(path);
            // Retain string literals and line numbers while ignoring examples in comments.
            text = Regex.Replace(text, path.EndsWith(".axaml")
                ? @"<!--[\s\S]*?-->"
                : "@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"|//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/", match =>
                match.Value.StartsWith("//") || match.Value.StartsWith("/*") || match.Value.StartsWith("<!--")
                    ? Regex.Replace(match.Value, @"[^\r\n]", " ") : match.Value);
            ValidateEnumLabels(path, text, english);
            if (path.EndsWith(".cs"))
            {
                foreach (Match status in Regex.Matches(text,
                    @"\b(?:StatusText|StatusMessage|SummaryMessage|NetworkSharingStatus)\s*=\s*\$?""([^""\r\n]+)"""))
                {
                    Error("GFLOC008", path, 1 + text.Substring(0, status.Index).Count(c => c == '\n'),
                        "UI status text must use a translation resource: " + status.Groups[1].Value);
                }
            }
            foreach (Match match in keyUse.Matches(text))
            {
                var key = match.Groups[1].Success ? match.Groups[1].Value
                    : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                if (!english.ContainsKey(key))
                {
                    Error("GFLOC005", path, 1 + text.Substring(0, match.Index).Count(c => c == '\n'), "UI translation key is not in the catalogue: " + key);
                }
            }
            if (!path.EndsWith(".axaml")) { continue; }
            foreach (Match match in markup.Matches(text))
            {
                var value = match.Groups[1].Value;
                if (value.StartsWith("{") || !Regex.IsMatch(value, "[A-Za-z]{2}") ||
                    value == "GameFlow" || value == "GAME" || value == "FLOW") { continue; }
                Error("GFLOC006", path, 1 + text.Substring(0, match.Index).Count(c => c == '\n'),
                    "UI text must use a translation resource: " + value);
            }
        }
        return !Log.HasLoggedErrors;
    }

    private void ValidateEnumLabels(string path, string text, Dictionary<string, Entry> english)
    {
        // These enums are exposed by UI pickers or the live input readout.
        // Adding a value must add its label before the app can compile.
        var exposed = new HashSet<string>(new[] { "ButtonId", "StickId", "RuleMode", "StickBlendMode", "GamepadAction", "MouseInput", "RuleKind" });
        foreach (Match declaration in Regex.Matches(text, @"\benum\s+(\w+)\s*\{([^{}]*)\}"))
        {
            var name = declaration.Groups[1].Value;
            if (!exposed.Contains(name) && !english.Keys.Any(key => key.StartsWith(name + ".", StringComparison.Ordinal))) { continue; }
            foreach (var part in declaration.Groups[2].Value.Split(','))
            {
                var member = Regex.Match(part, @"^\s*(\w+)(?:\s*=.*)?\s*$");
                if (!member.Success) { continue; }
                var value = member.Groups[1].Value;
                var keys = name == "RuleKind"
                    ? value == "Preserved" ? new[] { "MappedControlGenericTitle" }
                        : new[] { "RuleKind" + value + "Label", "RuleKind" + value + "Description" }
                    : new[] { name + "." + value };
                foreach (var key in keys)
                {
                    if (!english.ContainsKey(key))
                    {
                        Error("GFLOC007", path, 1, "UI enum value has no translation key: " + key);
                    }
                }
            }
        }
    }

    private void Error(string code, string file, int line, string message)
    {
        Log.LogError("Localization", code, null, file, line, 1, 0, 0, message);
    }
}
