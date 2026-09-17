using DocWiz.Core.Markdown;
using Xunit;

namespace DocWiz.Core.Tests;

/// <summary>
/// ADR-008 parity gate (audit F6): the <c>::: embed dw:&lt;ref&gt; :::</c> directive wire form is
/// produced in three places that cannot share one function — <see cref="DocWizEmbed.DirectiveFor"/>
/// (C#, canonical), <c>serialize.js</c> (editor ESM serializer), and <c>app.js</c> (classic-script
/// slide-region normalizer). This gate pins the canonical output (golden) and pins the wire-form
/// delimiters across all three sources, so a directive-syntax change in one is caught. (Behavioural
/// correctness of the canonical producer's width/align handling is covered by EmbedTests.)
/// </summary>
public class WireFormParityTests
{
    [Fact]
    public void EmbedDirective_CanonicalProducer_EmitsTheExpectedWireForm()
    {
        Assert.Equal("::: embed dw:ch#sec\n:::", DocWizEmbed.DirectiveFor("ch#sec"));
        Assert.Equal("::: embed dw:ch#sec width=60%\n:::", DocWizEmbed.DirectiveFor("ch#sec", "60%"));
        Assert.Equal("::: embed dw:ch#sec width=60% align=center\n:::",
            DocWizEmbed.DirectiveFor("ch#sec", "60%", "center"));
    }

    [Fact]
    public void EmbedDirective_AllThreeProducers_ShareTheWireFormDelimiters()
    {
        foreach (var src in new[]
        {
            TestRepo.Read("src", "DocWiz.Core", "Markdown", "DocWizEmbed.cs"),
            TestRepo.Read("src", "DocWiz.App", "wwwroot", "editor2", "serialize.js"),
            TestRepo.Read("src", "DocWiz.App", "wwwroot", "app.js"),
        })
        {
            Assert.Contains("::: embed dw:", src);   // the directive prefix
            Assert.Contains("\\n:::", src);          // the two-line close (literal \n::: in source)
        }
    }
}
