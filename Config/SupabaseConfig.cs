namespace LumosBarbearia.Config;

/// <summary>
/// ÚNICO lugar para configurar o projeto Supabase.
/// Painel Supabase > Project Settings > API:
///   - Url: "Project URL"
///   - ChavePublica: chave "anon" ou "sb_publishable_..." (pode ficar no app, desde que o RLS esteja ativo).
/// NUNCA coloque a chave "service_role" / "secret" aqui.
/// </summary>
public static class SupabaseConfig
{
    public const string Url = "https://botpxbkfstpziloequf.supabase.co";            // ex.: https://abcdefgh.supabase.co
    public const string ChavePublica = "sb_publishable_rkcv2xeIU1jyiXhPkpy7Hw_CHvAK4k3";    // ex.: sb_publishable_...

    public static bool EstaConfigurado =>
        !Url.StartsWith("COLE_AQUI", StringComparison.Ordinal) &&
        !ChavePublica.StartsWith("COLE_AQUI", StringComparison.Ordinal);
}
