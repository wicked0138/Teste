using LumosBarbearia.Models;
using Supabase.Postgrest.Exceptions;

namespace LumosBarbearia.Services;

/// <summary>Cadastro do cliente (tabela clientes) + registro do consentimento LGPD.</summary>
public class ClienteService
{
    /// <summary>Aumente quando o texto da LGPD (CadastroViewModel.TextoLgpd) mudar.</summary>
    public const string VersaoTermo = "1.0";

    private readonly SupabaseService _supabase;

    public ClienteService(SupabaseService supabase) => _supabase = supabase;

    private Supabase.Client Db => _supabase.Client;

    public async Task<ResultadoOperacao> CadastrarAsync(string nome, string email, string telefone)
    {
        await _supabase.InicializarAsync();

        var usuario = _supabase.UsuarioId;
        if (usuario is null) return ResultadoOperacao.Falha("Você precisa estar logado.");

        try
        {
            var existente = await _supabase.ObterClienteAsync();

            if (existente is null)
            {
                // aceito_lgpd fica de fora do INSERT (ignoreOnInsert): o banco o bloqueia na escrita direta
                await Db.From<Cliente>().Insert(new Cliente
                {
                    AuthUserId = usuario.Value,
                    Nome = nome,
                    Email = email,
                    Telefone = telefone
                });
            }
            else
            {
                await Db.From<Cliente>()
                    .Where(c => c.Id == existente.Id)
                    .Set(c => c.Nome, nome)
                    .Set(c => c.Telefone, telefone)
                    .Update();
            }

            // Grava o aceite e o histórico do consentimento
            await Db.Rpc(DbContract.FnConsentimento, new Dictionary<string, object>
            {
                [DbContract.PFinalidade] = "agendamento", // é a finalidade que marca clientes.aceito_lgpd
                [DbContract.PVersaoTermo] = VersaoTermo,
                [DbContract.PConsentiu] = true,
                [DbContract.POrigem] = "aplicativo"
            });

            return ResultadoOperacao.Ok();
        }
        catch (PostgrestException ex)
        {
            var t = ((ex.Message ?? "") + " " + (ex.Content ?? "")).ToLowerInvariant();
            if (t.Contains("duplicate") || t.Contains("unique") || t.Contains("23505"))
                return ResultadoOperacao.Falha("Já existe um cadastro com este e-mail ou telefone.");
            if (t.Contains("ck_cli_telefone"))
                return ResultadoOperacao.Falha("Telefone inválido. Use DDD + número.");
            if (t.Contains("ck_cli_email"))
                return ResultadoOperacao.Falha("E-mail inválido.");

            System.Diagnostics.Debug.WriteLine($"[Cadastro] {ex.Message} | {ex.Content}");
            return ResultadoOperacao.Falha("Não foi possível salvar seu cadastro. Tente novamente.");
        }
    }
}
