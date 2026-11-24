using LumosBarbearia.Models;
using Supabase.Gotrue;

namespace LumosBarbearia.Services;

/// <summary>
/// Ponto único de acesso ao Supabase (registrado como Singleton no MauiProgram).
/// Responsável por: inicializar o cliente, restaurar a sessão salva e autenticar (Google OAuth).
/// </summary>
public class SupabaseService
{
    /// <summary>
    /// Deep link que o Google/Supabase usa para devolver o usuário ao app.
    /// Precisa estar em: Supabase > Authentication > URL Configuration > Redirect URLs
    /// e no intent-filter Android (Platforms/Android/WebAuthenticatorCallbackActivity.cs).
    /// </summary>
    public const string RedirectUri = "lumosbarbearia://auth-callback";

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _inicializado;

    public SupabaseService(Supabase.Client client)
    {
        Client = client;
    }

    /// <summary>Cliente Supabase já configurado (Auth, Postgrest, Realtime).</summary>
    public Supabase.Client Client { get; }

    /// <summary>Disparado após login/logout, para telas atualizarem (ex.: mostrar aba Balcão).</summary>
    public event EventHandler? AutenticacaoAlterada;

    public bool EstaLogado => Client.Auth.CurrentUser is not null;

    public Guid? UsuarioId =>
        Guid.TryParse(Client.Auth.CurrentUser?.Id, out var id) ? id : null;

    /// <summary>E-mail da conta logada (vem do Google/Auth) — usado para pré-preencher o cadastro.</summary>
    public string? EmailAtual => Client.Auth.CurrentUser?.Email;

    /// <summary>Nome vindo do provedor (Google envia "full_name"/"name"); null se não houver.</summary>
    public string? NomeAtual
    {
        get
        {
            var meta = Client.Auth.CurrentUser?.UserMetadata;
            if (meta is null) return null;
            foreach (var chave in new[] { "full_name", "name" })
                if (meta.TryGetValue(chave, out var valor) && !string.IsNullOrWhiteSpace(valor?.ToString()))
                    return valor!.ToString();
            return null;
        }
    }

    /// <summary>
    /// Inicializa o cliente e restaura a sessão persistida (SessionHandler). Pode ser chamado
    /// várias vezes; só executa uma. Chame no início do app / antes de qualquer consulta.
    /// </summary>
    public async Task InicializarAsync()
    {
        if (_inicializado) return;

        await _initLock.WaitAsync();
        try
        {
            if (_inicializado) return;
            await Client.InitializeAsync();
            _inicializado = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Login com Google via OAuth + PKCE:
    /// 1) pede ao Supabase a URL de autorização do Google;
    /// 2) abre o navegador do sistema (WebAuthenticator) e aguarda o retorno ao deep link;
    /// 3) troca o "code" recebido por uma sessão.
    /// Retorna true se o usuário ficou autenticado; false se cancelou.
    /// </summary>
    /// <remarks>WebAuthenticator não é suportado no Windows: teste em Android/iOS.</remarks>
    public async Task<bool> EntrarComGoogleAsync()
    {
        await InicializarAsync();

        try
        {
            var authState = await Client.Auth.SignIn(
                Supabase.Gotrue.Constants.Provider.Google,
                new SignInOptions
                {
                    RedirectTo = RedirectUri,
                    FlowType = Supabase.Gotrue.Constants.OAuthFlowType.PKCE
                });

            var resultado = await WebAuthenticator.Default.AuthenticateAsync(
                authState.Uri,
                new Uri(RedirectUri));

            if (!resultado.Properties.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
                return false;

            var sessao = await Client.Auth.ExchangeCodeForSession(authState.PKCEVerifier, code);
            if (sessao is null) return false;

            AutenticacaoAlterada?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (TaskCanceledException)
        {
            // Usuário fechou a janela do Google
            return false;
        }
    }

    /// <summary>Login por e-mail e senha (funciona também no Windows). Retorna null se OK, ou a mensagem de erro.</summary>
    public async Task<string?> EntrarComEmailAsync(string email, string senha)
    {
        await InicializarAsync();
        try
        {
            var sessao = await Client.Auth.SignIn(email, senha);
            if (sessao?.User is null) return "E-mail ou senha incorretos.";

            AutenticacaoAlterada?.Invoke(this, EventArgs.Empty);
            return null;
        }
        catch (Exception ex) when (ex is Supabase.Gotrue.Exceptions.GotrueException)
        {
            var t = ex.Message.ToLowerInvariant();
            if (t.Contains("confirm")) return "Confirme seu e-mail antes de entrar (veja sua caixa de entrada).";
            return "E-mail ou senha incorretos.";
        }
    }

    /// <summary>
    /// Cria a conta por e-mail e senha. Retorna (ok, mensagem). Se o projeto exigir confirmação de e-mail,
    /// ok = true mas a pessoa só consegue entrar depois de confirmar.
    /// </summary>
    public async Task<(bool Ok, string Mensagem)> CriarContaAsync(string email, string senha)
    {
        await InicializarAsync();
        try
        {
            var sessao = await Client.Auth.SignUp(email, senha);
            if (sessao?.AccessToken is { Length: > 0 })
            {
                AutenticacaoAlterada?.Invoke(this, EventArgs.Empty);
                return (true, string.Empty);
            }
            return (true, "Enviamos um e-mail de confirmação. Confirme e depois entre na sua conta.");
        }
        catch (Exception ex) when (ex is Supabase.Gotrue.Exceptions.GotrueException)
        {
            var t = ex.Message.ToLowerInvariant();
            if (t.Contains("already") || t.Contains("registered")) return (false, "Este e-mail já tem conta. Use \"Entrar\".");
            if (t.Contains("password")) return (false, "A senha precisa ter pelo menos 6 caracteres.");
            return (false, "Não foi possível criar a conta. Verifique os dados.");
        }
    }

    /// <summary>Encerra a sessão local e no servidor.</summary>
    public async Task SairAsync()
    {
        await InicializarAsync();
        await Client.Auth.SignOut();
        AutenticacaoAlterada?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Perfil (tabela clientes) do usuário logado. Retorna null se deslogado OU se o login existe
    /// mas o cadastro (telefone + aceite LGPD) ainda não foi concluído — o app deve levar à tela "cadastro".
    /// </summary>
    public async Task<Cliente?> ObterClienteAsync()
    {
        await InicializarAsync();

        var id = UsuarioId;
        if (id is null) return null;

        // Single() devolve null quando não há linha
        return await Client.From<Cliente>()
            .Where(c => c.AuthUserId == id.Value)
            .Single();
    }

    /// <summary>
    /// True se o usuário logado é barbeiro (existe em "barbeiros" com o seu auth_user_id).
    /// Controla a visibilidade da aba "Balcão". Requer o patch_app.sql.
    /// </summary>
    public async Task<bool> EhBarbeiroAsync()
    {
        await InicializarAsync();

        var id = UsuarioId;
        if (id is null) return false;

        var barbeiro = await Client.From<Barbeiro>()
            .Where(b => b.AuthUserId == id.Value)
            .Single();

        return barbeiro is not null;
    }
}
