using HtmlAgilityPack;

namespace Bridge.Converter;

/// <summary>
/// Resolves an HTML &lt;svg&gt; node to a Unicode suit symbol, or null if the SVG should be skipped.
/// </summary>
public sealed class SvgResolver
{
    private readonly string _svgClassToSkip;
    private readonly Dictionary<string, string> _pathMappings;

    public SvgResolver(string svgClassToSkip, Dictionary<string, string> pathMappings)
    {
        _svgClassToSkip = svgClassToSkip;
        _pathMappings = pathMappings;
    }

    /// <summary>
    /// Returns the suit symbol for the given SVG node, or null if the SVG is an arrow (to be skipped).
    /// Returns "?" if the path is unknown.
    /// </summary>
    public string? Resolve(HtmlNode svgNode)
    {
        string svgClass = svgNode.GetAttributeValue("class", string.Empty);
        if (svgClass.Contains(_svgClassToSkip, StringComparison.Ordinal))
            return null;

        HtmlNode? pathNode = svgNode.SelectSingleNode(".//path");
        if (pathNode is null)
            return "?";

        string dAttribute = pathNode.GetAttributeValue("d", string.Empty);

        foreach (KeyValuePair<string, string> mapping in _pathMappings)
        {
            if (dAttribute.StartsWith(mapping.Key, StringComparison.Ordinal))
                return mapping.Value;
        }

        return "?";
    }
}
