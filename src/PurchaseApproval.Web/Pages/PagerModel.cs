// View model for the shared _Pager partial.
namespace PurchaseApproval.Web.Pages;

public sealed record PagerModel(
    string PageName,
    int Page,
    int TotalPages,
    IReadOnlyDictionary<string, string> RouteValues)
{
    /// <summary>Builds the route dictionary from the current query string, dropping the page number.</summary>
    public static PagerModel FromQuery(string pageName, int page, int totalPages, IQueryCollection query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in query)
        {
            if (!string.Equals(key, "page", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                values[key] = value.ToString();
            }
        }

        return new PagerModel(pageName, page, totalPages, values);
    }
}
