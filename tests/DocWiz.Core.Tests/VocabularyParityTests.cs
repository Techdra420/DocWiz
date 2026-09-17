using System.Text.RegularExpressions;
using DocWiz.Core.Markdown;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (audit F8): the `:::` element vocabulary must agree across the client
/// (schema.js <c>CALLOUTS</c>/<c>DEFAULT_LABEL</c>, views.js <c>CONTAINER_LABEL</c>) and the
/// server (<see cref="DocWizContainerRenderer.KnownContainers"/>). A new element type can't be
/// added on one side only, and every container the editor can select must have a label that
/// comes from one source (the F8 drift was callouts falling back to the raw type, e.g. "tip").
/// </summary>
public class VocabularyParityTests
{
    [Fact]
    public void SchemaCallouts_AreAllKnownContainers()
    {
        var callouts = JsStringArray(SchemaJs(), "CALLOUTS");
        Assert.NotEmpty(callouts);
        foreach (var c in callouts)
            Assert.Contains(c, DocWizContainerRenderer.KnownContainers);
    }

    [Fact]
    public void DefaultLabel_KeysExactlyMatchCallouts()
    {
        var schema = SchemaJs();
        var callouts = JsStringArray(schema, "CALLOUTS").OrderBy(x => x, StringComparer.Ordinal);
        var labelKeys = JsObjectKeys(schema, "DEFAULT_LABEL").OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(callouts, labelKeys);
    }

    [Fact]
    public void ContainerLabel_TakesCalloutLabelsFromDefaultLabel()
    {
        // The fix for F8: callout pill labels must come from DEFAULT_LABEL (one source), not be
        // re-listed or omitted. Pinning the `...DEFAULT_LABEL` spread keeps the context-bar pill
        // in lockstep with the callout chrome tag.
        var views = TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", "views.js");
        var def = Regex.Match(views, @"const\s+CONTAINER_LABEL\s*=\s*\{(?<body>.*?)\}", RegexOptions.Singleline);
        Assert.True(def.Success, "Could not find CONTAINER_LABEL in views.js");
        Assert.Contains("...DEFAULT_LABEL", def.Groups["body"].Value);
    }

    private static string SchemaJs() => TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", "schema.js");

    private static List<string> JsStringArray(string source, string name)
    {
        var m = Regex.Match(source, name + @"\s*=\s*\[(?<items>[^\]]*)\]");
        Assert.True(m.Success, $"Could not find `{name} = [...]`");
        return m.Groups["items"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim('\'', '"')).Where(s => s.Length > 0).ToList();
    }

    private static List<string> JsObjectKeys(string source, string name)
    {
        var m = Regex.Match(source, name + @"\s*=\s*\{(?<body>[^}]*)\}");
        Assert.True(m.Success, $"Could not find `{name} = {{...}}`");
        return Regex.Matches(m.Groups["body"].Value, @"(\w+)\s*:")
            .Select(x => x.Groups[1].Value).ToList();
    }
}
