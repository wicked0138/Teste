namespace LumosBarbearia.Services;

public interface IDialogService
{
    Task AlertAsync(string titulo, string mensagem);
    Task<bool> ConfirmAsync(string titulo, string mensagem, string aceitar = "Sim", string cancelar = "Não");
}

public interface INavigationService
{
    Task GoToAsync(string rota);
    Task GoToAsync(string rota, IDictionary<string, object> parametros);
}

public class DialogService : IDialogService
{
    public Task AlertAsync(string titulo, string mensagem) =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlert(titulo, mensagem, "OK"));

    public Task<bool> ConfirmAsync(string titulo, string mensagem, string aceitar = "Sim", string cancelar = "Não") =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlert(titulo, mensagem, aceitar, cancelar));
}

public class NavigationService : INavigationService
{
    public Task GoToAsync(string rota) =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(rota));

    public Task GoToAsync(string rota, IDictionary<string, object> parametros) =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(rota, parametros));
}
