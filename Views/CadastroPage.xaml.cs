using LumosBarbearia.ViewModels;

namespace LumosBarbearia.Views;

public partial class CadastroPage : ContentPage
{
    private readonly CadastroViewModel _vm;

    public CadastroPage(CadastroViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.CarregarCommand.ExecuteAsync(null);
    }
}
