using System.Reflection;
using System.Text.Json;
using DocWiz.App;
using DocWiz.Core.Models;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DocWiz.Core.Tests;

/// <summary>
/// The output editor's load → save round trip (GET /api/view → working model → PUT
/// /api/view) must keep every slide key. It once dropped `- chapter: &lt;id&gt;` (a
/// whole-chapter entry): the GET listed its fields by hand, the view YAML is read with
/// IgnoreUnmatchedProperties, and a plain Save rewrote the entry as an empty
/// `- layout: title-content` slide.
/// </summary>
public class ViewRoundTripTests
{
    // Reads view YAML the way the editor and the loader do (underscored keys, unknown keys ignored).
    private static readonly IDeserializer YamlIn = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // Minimal APIs (Results.Json / ReadFromJsonAsync) use the web defaults: camelCase names.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string DeckYaml =
        "id: deck\n" +
        "title: Deck\n" +
        "audience: user\n" +
        "mode: slides\n" +
        "slides:\n" +
        "- layout: title-content\n" +
        "  title: Intro\n" +
        "  regions:\n" +
        "    body: Some authored intro text.\n" +
        "- chapter: daily-operations\n" +
        "- include: daily-operations#daily-startup\n";

    [Fact]
    public void EverySlideKey_SurvivesTheEditorLoadSaveRoundTrip()
    {
        var original = YamlIn.Deserialize<ViewDefinition>(DeckYaml);

        // GET → the client → PUT, with the client handing the slides back as it got them.
        var wire = JsonSerializer.Serialize(original.Slides.Select(EditorServer.ToSlideDto), Web);
        Assert.Contains("\"chapter\":\"daily-operations\"", wire);
        var slides = JsonSerializer.Deserialize<List<ViewSlideDto>>(wire, Web)!;
        var saved = EditorServer.ToView(new ViewDto(original.Id, original.Title, original.Audience, original.Mode, slides));
        var yaml = ViewYaml.Serializer.Serialize(saved);

        Assert.Contains("- chapter: daily-operations", yaml);
        var reloaded = YamlIn.Deserialize<ViewDefinition>(yaml);
        Assert.Equal(original.Slides.Count, reloaded.Slides.Count);
        for (var i = 0; i < original.Slides.Count; i++)
            foreach (var p in SlideKeys())
                Assert.Equal(JsonSerializer.Serialize(p.GetValue(original.Slides[i])),
                             JsonSerializer.Serialize(p.GetValue(reloaded.Slides[i])));
    }

    [Fact]
    public void ViewSlideDto_CarriesEverySlideDefYamlKey()
    {
        // The wire slide is the ONE shape for GET and PUT; a SlideDef key it lacks is dropped
        // on the next save. Add the key to ViewSlideDto (and ToSlideDto / ToView) with the model.
        var wire = typeof(ViewSlideDto).GetProperties().Select(p => p.Name).ToHashSet();
        foreach (var p in SlideKeys())
            Assert.True(wire.Contains(p.Name), $"ViewSlideDto has no '{p.Name}' — the editor would drop it on save.");
    }

    [Fact]
    public void Client_KeepsTheChapterKeyThroughItsWorkingModel()
    {
        // The browser half of the round trip (app.js): the working model keeps a whole-chapter
        // entry's key and the save payload sends it back; the zone editors never mount on its
        // slides (their content is the chapter's — an edit there could not be saved).
        var app = TestRepo.Read("src", "DocWiz.App", "wwwroot", "app.js");
        Assert.Contains("if (s.chapter) return { chapter: s.chapter,", app);       // normalizeSlide
        Assert.Contains("if (slide.chapter) return { chapter: slide.chapter };", app); // slidePayload
        var slideEdit = TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", "slideedit.js");
        Assert.Contains("if (!slide || slide.chapter) return;", slideEdit);
    }

    // The YAML-serialized keys of a slide.
    private static IEnumerable<PropertyInfo> SlideKeys() =>
        typeof(SlideDef).GetProperties()
            .Where(p => p.CanWrite && p.GetCustomAttribute<YamlIgnoreAttribute>() is null);
}
