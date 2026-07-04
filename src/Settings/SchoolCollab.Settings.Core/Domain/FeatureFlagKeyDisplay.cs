using Humanizer;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Display formatters for <see cref="FeatureFlag.Key"/>. The canonical storage
/// form is the upper-cased, colon-delimited identifier produced by
/// <see cref="FeatureFlag.NormalizeKey"/> (e.g. <c>FEATURE:ENABLECODEDVALUESAICHAT</c>),
/// which is hostile to humans in lists. UI surfaces (admin grid, audit log) show
/// the canonical key in two more readable shapes so operators can scan the list
/// quickly (Title Case) and still copy the value used in code (PascalCase).
/// </summary>
public static class FeatureFlagKeyDisplay
{
    /// <summary>
    /// Humanised Title Case form, e.g. <c>FEATURE:ENABLECODEDVALUESAICHAT</c> →
    /// <c>Feature: Enable Coded Values Ai Chat</c>. Splits on the namespace
    /// separator (<c>:</c>) and on word boundaries inside each segment.
    /// All-caps segments are split using a small English vocabulary so the
    /// humanised form contains real words instead of a single capitalised blob.
    /// </summary>
    public static string ToTitleCase(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var parts = key.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return key;
        }

        // The first segment is the namespace/category prefix (e.g. "FEATURE",
        // "BETA"). It is TitleCased along with the rest — "FEATURE" becomes
        // "Feature" — so the display is consistent across all segments.
        return string.Join(": ", parts.Select(PartToTitleCase));
    }

    /// <summary>
    /// PascalCase form preserving the namespace separator, e.g.
    /// <c>FEATURE:ENABLECODEDVALUESAICHAT</c> →
    /// <c>FEATURE:EnableCodedValuesAiChat</c>. The first segment (the
    /// namespace prefix) is preserved in its original casing so it reads as a
    /// screaming-case category marker; subsequent segments are word-split and
    /// PascalCased to match the C# identifier the user actually wrote (just
    /// without the running-together all-caps form).
    /// </summary>
    public static string ToPascalCase(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var parts = key.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return key;
        }

        // Segment 0 is the namespace prefix — preserve its casing (e.g.
        // "FEATURE" stays "FEATURE"). Segments 1+ are the area/identifier and
        // get the full PascalCase treatment.
        return parts[0] + (parts.Length > 1
            ? ":" + string.Join(':', parts.Skip(1).Select(PartToPascalCase))
            : string.Empty);
    }

    // ── per-segment transformation ─────────────────────────────────────────

    private static string PartToTitleCase(string part)
    {
        // If the segment already has mixed case (e.g. "EnableCodedValues"),
        // Humanizer.Humanize splits it on camelCase boundaries correctly.
        if (HasMixedCase(part))
        {
            return part.Humanize().Titleize();
        }

        // All-caps (or all-lowercase) segments are split using a vocabulary
        // table, then title-cased. We lower-case first so the vocabulary lookup
        // is case-insensitive and Humanizer.Titleize only has to do the casing.
        var lower = part.ToLowerInvariant();
        var words = SplitAllCapsIntoWords(lower);
        return string.Join(' ', words.Select(w => w.Titleize()));
    }

    private static string PartToPascalCase(string part)
    {
        // Word-split and PascalCase so all-caps identifiers render with the
        // word boundaries the author intended ("ENABLECODEDVALUESAICHAT" →
        // "EnableCodedValuesAiChat", not "Enablecodedvaluesaichat"). Mixed-case
        // input is normalised via the same round-trip so the casing is
        // consistent.
        var titleCased = PartToTitleCase(part);
        return string.Concat(titleCased.Split(' ').Select(w => w.Titleize()));
    }

    private static bool HasMixedCase(string s)
    {
        var hasUpper = false;
        var hasLower = false;
        foreach (var c in s)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            if (hasUpper && hasLower) return true;
        }
        return false;
    }

    // ── vocabulary-based word splitter for ALL-CAPS / all-lower segments ───

    // A small set of common English words that appear in feature-flag names
    // ("Enable", "Disable", "New", "Old", "Ai", "Chat", "Dashboard", "Report",
    // "Beta", "Pilot", "Rollout", "Migration", "Cache", "Queue", etc.). The
    // splitter greedily matches the longest word at each position, falling
    // back to single characters for letters that don't form a known word —
    // which means unknown substrings still render, just without the spaces
    // a human would have inserted.
    private static readonly string[] Vocabulary =
    {
        "ability", "able", "about", "above", "access", "account", "action", "active",
        "activity", "admin", "after", "agent", "ai", "alert", "all", "analytics",
        "api", "app", "application", "approval", "archive", "archived", "asset",
        "assistant", "async", "audit", "auth", "auto", "automation", "available",

        "backend", "backup", "beta", "billing", "block", "bot", "build", "bulk",
        "button",

        "cache", "calendar", "call", "campaign", "card", "category", "change",
        "channel", "chat", "check", "checkout", "child", "claim", "class", "clear",
        "click", "client", "cloud", "code", "coded", "column", "comment",
        "commerce", "company", "component", "config", "connect", "contact",
        "content", "context", "control", "conversation", "copy", "create",
        "creation", "criteria", "current", "custom", "customer",

        "dashboard", "data", "database", "date", "debug", "default", "delete",
        "delivery", "demo", "design", "detail", "dev", "device", "dialog",
        "disable", "disabled", "display", "doc", "document", "domain", "draft",
        "drawer",

        "edit", "editor", "email", "embed", "enable", "enabled", "end", "engine",
        "entry", "error", "event", "export",

        "feature", "feed", "field", "file", "filter", "flag", "flow", "folder",
        "form", "format", "framework", "full", "function",

        "gateway", "general", "generate", "global", "grade", "graph", "group",
        "guest",

        "handler", "header", "health", "help", "hidden", "history", "home", "host",

        "id", "image", "import", "in", "index", "info", "inherit", "input",
        "integration", "internal", "invite", "item",

        "job", "join", "json", "jump",

        "key",

        "label", "land", "language", "launch", "layer", "layout", "level", "library",
        "license", "link", "list", "live", "load", "local", "lock", "log", "login",
        "logout",

        "main", "manage", "manager", "map", "mapping", "market", "master", "media",
        "member", "menu", "message", "metadata", "method", "metric", "middleware",
        "migration", "mobile", "mock", "mode", "model", "module", "monitor",

        "name", "navigation", "new", "next", "node", "note", "notification",
        "number",

        "object", "off", "offer", "office", "old", "on", "online", "open", "option",
        "order", "org", "organization", "origin", "out", "output", "override",

        "package", "page", "panel", "parent", "parse", "partner", "password",
        "path", "patient", "payment", "pending", "permission", "person", "phase",
        "phone", "pilot", "plan", "platform", "plugin", "policy", "poll", "popup",
        "portal", "post", "preview", "pricing", "primary", "print", "priority",
        "private", "process", "product", "profile", "project", "prompt",
        "property", "public", "push",

        "query", "queue", "quick",

        "rate", "read", "ready", "reason", "receive", "record", "redirect",
        "reference", "region", "register", "release", "reload", "remove", "render",
        "report", "request", "reset", "resolve", "resource", "response", "restore",
        "retry", "return", "review", "role", "rollout", "root", "route", "row",

        "safe", "save", "schedule", "schema", "scope", "screen", "search", "secret",
        "section", "secure", "security", "seed", "select", "send", "server",
        "service", "session", "setting", "share", "ship", "shop", "short", "show",
        "sidebar", "sign", "site", "smoke", "soft", "sort", "source", "space",
        "stage", "start", "state", "static", "stats", "status", "step", "stop",
        "store", "stream", "student", "subject", "submit", "summary", "support",
        "sync", "system",

        "table", "tag", "task", "team", "template", "tenant", "test", "text",
        "theme", "thread", "ticket", "time", "timeout", "title", "token", "tool",
        "top", "track", "transaction", "translate", "trial", "trigger", "type",

        "ui", "undo", "unit", "unread", "update", "upgrade", "upload", "url",
        "usage", "user", "username",

        "validate", "value", "values", "vendor", "verify", "version", "video",
        "view", "virtual", "voice",

        "wait", "warning", "watch", "web", "webhook", "widget", "wizard", "work",
        "workflow", "write",

        "xml", "yaml", "year",

        "zip", "zone",
    };

    private static readonly HashSet<string> _vocabularySet = new(Vocabulary, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Greedy left-to-right word split of an all-lowercase string. At each
    /// position, take the longest prefix that is a known word; if no word
    /// matches, take a single letter and continue. The result is always
    /// non-empty (a single character is at minimum taken) so the caller never
    /// sees a phantom empty word.
    /// </summary>
    private static IEnumerable<string> SplitAllCapsIntoWords(string lower)
    {
        if (lower.Length == 0)
        {
            yield break;
        }

        var i = 0;
        while (i < lower.Length)
        {
            var matched = false;

            // Try the longest possible match first so "enable" wins over "in" + "able".
            for (var len = Math.Min(lower.Length - i, 12); len >= 2; len--)
            {
                if (_vocabularySet.Contains(lower.Substring(i, len)))
                {
                    yield return lower.Substring(i, len);
                    i += len;
                    matched = true;
                    break;
                }
            }

            if (matched)
            {
                continue;
            }

            // No word matched — emit a single letter and advance. This keeps
            // unfamiliar substrings visible (e.g. brand names) without losing
            // characters.
            yield return lower[i].ToString();
            i++;
        }
    }
}
