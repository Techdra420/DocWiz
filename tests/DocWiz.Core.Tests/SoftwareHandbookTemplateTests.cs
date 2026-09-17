using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Validation;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>The shipped file-based template (templates/projects/software-handbook) must
/// stay a valid starting point: zero validation errors and a user-training deck whose
/// every embed resolves.</summary>
public class SoftwareHandbookTemplateTests
{
    private static string TemplateDir =>
        TestRepo.At("templates", "projects", "software-handbook");

    [Fact]
    public void UserTrainingView_IsSlides_AndEveryEmbedResolves()
    {
        var project = ProjectLoader.Load(TemplateDir);
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline); // required for ResolveReference

        var view = project.Views.Single(v => v.Id == "user-training");
        Assert.Equal("slides", view.Mode);
        Assert.Equal("user", view.Audience);
        Assert.True(view.Slides.Count >= 5, $"expected the full deck, got {view.Slides.Count}");

        foreach (var slide in view.Slides)
            foreach (var md in slide.EffectiveRegions().Values)
                foreach (var reference in DocWizEmbed.FindRefs(md, pipeline))
                    _ = DocWizEmbed.ResolveReference(reference, project); // throws if dangling
    }

    [Fact]
    public void Template_ValidatesWithZeroIssues()
    {
        var project = ProjectLoader.Load(TemplateDir);
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
        Assert.Empty(ProjectValidator.Validate(project));
    }
}
