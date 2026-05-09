using Bridge.Converter;
using HtmlAgilityPack;
using Xunit;

namespace Bridge.Converter.Tests;

public sealed class SvgResolverTests
{
    private static readonly Dictionary<string, string> DefaultPathMappings = new()
    {
        ["M480.25 156.355"] = "♥",
        ["M458.915 307.705"] = "♠",
        ["M431.76 256"] = "♦",
        ["M477.443 295.143"] = "♣"
    };

    private static SvgResolver CreateResolver() =>
        new("arrow", DefaultPathMappings);

    private static HtmlNode ParseSvgNode(string svgHtml)
    {
        var htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(svgHtml);
        return htmlDocument.DocumentNode.SelectSingleNode("//svg")!;
    }

    [Fact]
    public void Resolve_ArrowSvg_ReturnsNull()
    {
        SvgResolver resolver = CreateResolver();
        HtmlNode svgNode = ParseSvgNode(
            """<svg class="arrow arrow--open" viewBox="0 0 512 512"><path d="M192 128l128 128-128 128z"></path></svg>""");

        string? result = resolver.Resolve(svgNode);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("M480.25 156.355c0 161.24-224.25 324.43-224.25 324.43S31.75", "♥")]
    [InlineData("M458.915 307.705c0 62.63-54 91.32-91.34 91.34", "♠")]
    [InlineData("M431.76 256c-69 42.24-137.27 126.89-175.76 224.78", "♦")]
    [InlineData("M477.443 295.143a104.45 104.45 0 0 1-202.26 36.67", "♣")]
    public void Resolve_KnownSuitPaths_ReturnsCorrectSymbol(string dPathPrefix, string expectedSymbol)
    {
        SvgResolver resolver = CreateResolver();
        HtmlNode svgNode = ParseSvgNode(
            $"""<svg viewBox="0 0 512 512"><path d="{dPathPrefix}"></path></svg>""");

        string? result = resolver.Resolve(svgNode);

        Assert.Equal(expectedSymbol, result);
    }

    [Fact]
    public void Resolve_UnknownPath_ReturnsQuestionMark()
    {
        SvgResolver resolver = CreateResolver();
        HtmlNode svgNode = ParseSvgNode(
            """<svg viewBox="0 0 512 512"><path d="M999 999 totally unknown path"></path></svg>""");

        string? result = resolver.Resolve(svgNode);

        Assert.Equal("?", result);
    }

    [Fact]
    public void Resolve_SvgWithNoPath_ReturnsQuestionMark()
    {
        SvgResolver resolver = CreateResolver();
        HtmlNode svgNode = ParseSvgNode("""<svg viewBox="0 0 512 512"></svg>""");

        string? result = resolver.Resolve(svgNode);

        Assert.Equal("?", result);
    }
}
