// Renders a server timestamp so the browser can show it in the viewer's own timezone.
//
// Usage in a view:  <timestamp utc="@row.SubmittedAtUtc" />              -> compact viewer-local time
//                   <timestamp utc="@audit.RecordedAtUtc" mode="utc" /> -> compact UTC time
//
// The server always writes the UTC value into the datetime attribute, a compact visible fallback,
// and a labelled UTC tooltip. wwwroot/js/local-time.js then replaces every element marked
// data-local-time with a compact device-local rendering. Full timezone details remain available
// in the title/accessible label. A page viewed without JavaScript still shows correct UTC. Date-only values
// (cheque dates) must not use this helper; they are rendered as plain text so they never shift.
using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PurchaseApproval.Web.TagHelpers;

[HtmlTargetElement("timestamp", TagStructure = TagStructure.WithoutEndTag)]
public sealed class TimestampTagHelper : TagHelper
{
    public DateTimeOffset? Utc { get; set; }

    /// <summary>"local" (default) converts in the browser; "utc" keeps the UTC rendering.</summary>
    public string Mode { get; set; } = "local";

    /// <summary>Text shown when the value is null, already HTML-escaped by Razor when supplied.</summary>
    public string Empty { get; set; } = "—";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Utc is not { } value)
        {
            output.TagName = "span";
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Content.SetContent(Empty);
            return;
        }

        var utc = value.ToUniversalTime();
        output.TagName = "time";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("datetime", utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        output.Attributes.SetAttribute("class", "compact-time text-nowrap");
        if (string.Equals(Mode, "local", StringComparison.OrdinalIgnoreCase))
        {
            output.Attributes.SetAttribute("data-local-time", "true");
        }

        var utcLabel = utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        output.Attributes.SetAttribute("title", utcLabel);
        output.Attributes.SetAttribute("aria-label", utcLabel);
        output.Content.SetContent(utc.ToString("yyyy-MM-dd · HH:mm", CultureInfo.InvariantCulture));
    }
}
