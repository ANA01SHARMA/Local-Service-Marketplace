using System.Text.Encodings.Web;
using LocalServicesMarketplace.Models;
using LocalServicesMarketplace.Services.Interfaces;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.WebUtilities;

namespace LocalServicesMarketplace.Infrastructure;

/// <summary>&lt;avatar name="Rohit Verma" src="/uploads/..." size="sm" /&gt; - photo or deterministic initials.</summary>
[HtmlTargetElement("avatar", TagStructure = TagStructure.WithoutEndTag)]
public class AvatarTagHelper : TagHelper
{
    public string? Name { get; set; }
    public string? Src { get; set; }
    public string? Size { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        var size = string.IsNullOrEmpty(Size) ? "" : $" avatar-{Size}";
        output.Attributes.SetAttribute("class", $"avatar tone-{DisplayExtensions.AvatarTone(Name)}{size}");
        if (!string.IsNullOrEmpty(Src))
        {
            output.Content.SetHtmlContent($"<img src=\"{HtmlEncoder.Default.Encode(Src)}\" alt=\"\" loading=\"lazy\">");
            output.Attributes.SetAttribute("aria-hidden", "true");
        }
        else
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
            output.Content.SetContent(DisplayExtensions.Initials(Name));
        }
    }
}

/// <summary>&lt;status-badge status="@b.Status" /&gt;</summary>
[HtmlTargetElement("status-badge", TagStructure = TagStructure.WithoutEndTag)]
public class StatusBadgeTagHelper : TagHelper
{
    public BookingStatus Status { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"badge-status {Status.BadgeClass()}");
        output.Content.SetHtmlContent($"<i class=\"bi {Status.Icon()}\" aria-hidden=\"true\"></i>{Status.Label()}");
    }
}

/// <summary>&lt;stars value="4.5" /&gt; - five stars, accessible label.</summary>
[HtmlTargetElement("stars", TagStructure = TagStructure.WithoutEndTag)]
public class StarsTagHelper : TagHelper
{
    public decimal Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "stars");
        output.Attributes.SetAttribute("role", "img");
        output.Attributes.SetAttribute("aria-label", $"{Value:0.#} out of 5 stars");
        var full = (int)Math.Round(Value, MidpointRounding.AwayFromZero);
        output.Content.SetHtmlContent(new string('★', full) + $"<span class=\"off\">{new string('★', 5 - full)}</span>");
    }
}

/// <summary>&lt;pager page="1" total-pages="5" /&gt; - keeps the current query string, only swaps ?page=.</summary>
[HtmlTargetElement("pager", TagStructure = TagStructure.WithoutEndTag)]
public class PagerTagHelper : TagHelper
{
    public int Page { get; set; }
    public int TotalPages { get; set; }

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (TotalPages <= 1)
        {
            output.SuppressOutput();
            return;
        }
        var request = ViewContext.HttpContext.Request;
        string Href(int page)
        {
            var query = request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
            query["page"] = page.ToString();
            return QueryHelpers.AddQueryString(request.Path, query!);
        }

        output.TagName = "nav";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("aria-label", "Pages");
        var html = new HtmlContentBuilder();
        html.AppendHtml("<ul class=\"pagination pagination-sm mb-0\">");
        void Item(int page, string label, bool disabled = false, bool active = false)
        {
            var cls = "page-item" + (disabled ? " disabled" : "") + (active ? " active" : "");
            var aria = active ? " aria-current=\"page\"" : "";
            html.AppendHtml(disabled
                ? $"<li class=\"{cls}\"><span class=\"page-link\">{label}</span></li>"
                : $"<li class=\"{cls}\"><a class=\"page-link\" href=\"{HtmlEncoder.Default.Encode(Href(page))}\"{aria}>{label}</a></li>");
        }
        Item(Page - 1, "Previous", disabled: Page <= 1);
        var from = Math.Max(1, Page - 2);
        var to = Math.Min(TotalPages, Page + 2);
        for (var p = from; p <= to; p++) Item(p, p.ToString(), active: p == Page);
        Item(Page + 1, "Next", disabled: Page >= TotalPages);
        html.AppendHtml("</ul>");
        output.Content.SetHtmlContent(html);
    }
}

/// <summary>Notification bell with unread count and the latest five notifications (View Component).</summary>
public class NotificationBellViewComponent(INotificationService notifications) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = UserClaimsPrincipal.TryGetUserId();
        if (userId is null) return Content(string.Empty);
        ViewData["Unread"] = await notifications.CountUnreadAsync(userId.Value);
        return View(await notifications.GetLatestAsync(userId.Value, 5));
    }
}
