using Android.App;
using Android.Content;
using Android.Content.PM;

namespace LumosBarbearia;

/// <summary>
/// Recebe o retorno do login Google em lumosbarbearia://auth-callback
/// (mesmo valor de SupabaseService.RedirectUri e das Redirect URLs no painel do Supabase).
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "lumosbarbearia",
    DataHost = "auth-callback")]
public class WebAuthenticatorActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
