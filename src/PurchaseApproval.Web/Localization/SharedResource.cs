// Marker type for the application's single shared string table.
//
// How localization works here: every user-facing English string is its own key
// (L["Approve request"]). The Persian table Resources/SharedResource.fa.resx maps those keys to
// Persian text; a missing key falls back to the English key itself, so English never needs a
// resource file. The class must live in the project's root namespace for ResourceManager to find
// "Resources/SharedResource.fa.resx". Formatting (numbers, dates) is always invariant/English:
// only the UI culture changes with the language switch (see LanguageCookieRequestCultureProvider).
namespace PurchaseApproval.Web;

public sealed class SharedResource
{
}
