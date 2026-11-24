using Newtonsoft.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace LumosBarbearia.Services;

/// <summary>
/// Guarda a sessão (tokens) no aparelho para o usuário continuar logado ao reabrir o app.
/// Usa Preferences (área privada do app; funciona em Android e Windows).
/// </summary>
public class SupabaseSessionHandler : IGotrueSessionPersistence<Session>
{
    private const string Chave = "lumos.supabase.session";

    public void SaveSession(Session session)
    {
        try { Preferences.Default.Set(Chave, JsonConvert.SerializeObject(session)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Sessão] Falha ao salvar: {ex.Message}"); }
    }

    public void DestroySession()
    {
        try { Preferences.Default.Remove(Chave); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Sessão] Falha ao apagar: {ex.Message}"); }
    }

    public Session? LoadSession()
    {
        try
        {
            var json = Preferences.Default.Get<string?>(Chave, null);
            return string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<Session>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Sessão] Falha ao ler: {ex.Message}");
            return null;
        }
    }
}
