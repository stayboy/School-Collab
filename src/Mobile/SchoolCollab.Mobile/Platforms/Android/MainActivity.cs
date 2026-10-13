using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace SchoolCollab.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// The OIDC redirect. Wired as an ATTRIBUTE, not as a hand-written <intent-filter> in
// AndroidManifest.xml: that manifest has no <activity> element at all (the activity is generated
// from the [Activity] attribute above), so an orphan filter there would be invalid XML for this
// project and would fail the android TFM build. MAUI's manifest generation merges this into the
// generated <activity> block.
//
// DataScheme + DataHost must EQUAL the CallbackUrl the head hands to WebAuthenticator
// (MobileAuth:RedirectScheme + MobileAuth:RedirectHost, i.e. schoolcollab://signin-oidc), which
// in turn must equal the realm's redirectUris entry: WebAuthenticator only resolves its pending
// task when an intent carrying that scheme AND authority re-launches the app. The scheme alone
// would accept every schoolcollab:// URI any app sends, hence the host pin.
//
// Security: the schoolcollab scheme is unverified and not owned, so any app may register the same
// filter. PKCE + state are the deliberate mitigations (a hijacked code is useless without the
// in-memory code_verifier) - see MobileAuthOptions' remarks.
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "schoolcollab",
    DataHost = "signin-oidc")]
public class MainActivity : MauiAppCompatActivity
{
}
