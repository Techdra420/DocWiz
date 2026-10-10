using DocWiz.Core.Loading;
using DocWiz.Core.Markdown;
using DocWiz.Core.Models;
using DocWiz.Core.Validation;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>Every template shipped beside the exe (templates/projects/*, offered on the welcome
/// screen) must be a clean starting point: zero validation issues — warnings included, so a
/// missing screenshot or attachment fails too — and every linked section in every output
/// resolves. Enumerates the folder, so a new template is covered without a new test.</summary>
public class ShippedTemplatesTests
{
    public static TheoryData<string> Templates()
    {
        var data = new TheoryData<string>();
        foreach (var dir in Directory.EnumerateDirectories(TestRepo.At("templates", "projects")))
            if (File.Exists(Path.Combine(dir, ProjectLayout.ManifestFileName)))
                data.Add(Path.GetFileName(dir));
        return data;
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Template_ValidatesWithZeroIssues(string template)
    {
        var project = ProjectLoader.Load(TestRepo.At("templates", "projects", template));
        DocWizMarkdown.ParseAll(project, DocWizMarkdown.CreatePipeline());
        Assert.Empty(ProjectValidator.Validate(project));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryLinkedSectionInEveryOutput_Resolves(string template)
    {
        var project = ProjectLoader.Load(TestRepo.At("templates", "projects", template));
        var pipeline = DocWizMarkdown.CreatePipeline();
        DocWizMarkdown.ParseAll(project, pipeline); // required for ResolveReference

        foreach (var view in project.Views)
            foreach (var slide in view.Slides)
                foreach (var md in slide.EffectiveRegions().Values)
                    foreach (var reference in DocWizEmbed.FindRefs(md, pipeline))
                        _ = DocWizEmbed.ResolveReference(reference, project); // throws if dangling
    }
}
