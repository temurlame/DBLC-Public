using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Markdig;

namespace DBLC;

public static class PlayerMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    private static readonly HtmlSanitizer Sanitizer = new();
    private static readonly Regex RenderedImage = new("<img\\s+src=\"(?<src>assets/[^\"]+)\"\\s+alt=\"(?<alt>[^\"]*)\"\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Render(string markdown, Guid campaignId, PlayerImageAssetStore images, bool interactive = true)
    {
        var safeHtml = Sanitizer.Sanitize(Markdown.ToHtml(markdown, Pipeline));
        return RenderedImage.Replace(safeHtml, match =>
        {
            var path = match.Groups["src"].Value;
            var asset = images.Find(campaignId, path);
            if (asset is null) return match.Value;
            var alt = HtmlEncoder.Default.Encode(match.Groups["alt"].Value);
            var encodedPath = HtmlEncoder.Default.Encode(path);
            var style = FormattableString.Invariant($"--frame-x:{asset.Frame.X}%;--frame-y:{asset.Frame.Y}%;--frame-zoom:{asset.Frame.Zoom}");
            var image = $"<span class=\"player-markdown-image-frame\" style=\"{style}\"><img src=\"{asset.DataUrl}\" alt=\"{alt}\" /></span>";
            return interactive
                ? $"<button type=\"button\" class=\"player-markdown-image\" data-image-path=\"{encodedPath}\" aria-label=\"View full-size {alt}\">{image}</button>"
                : $"<div class=\"player-markdown-image player-review-image-static\">{image}</div>";
        });
    }
}
