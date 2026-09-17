using System.IO;
using System.Linq;
using Xunit;

namespace DocWiz.Core.Tests;

public class PipelineSourceParityTests
{
    [Fact]
    public void OnlyDocWizMarkdownConstructsThePipeline()
    {
        var offenders = Directory.EnumerateFiles(TestRepo.At("src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\','/').Contains("/obj/") && !f.Replace('\\','/').Contains("/bin/"))
            .Where(f => File.ReadAllText(f).Contains("new MarkdownPipelineBuilder("))
            .Select(Path.GetFileName).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "DocWizMarkdown.cs" }, offenders);
    }
}
