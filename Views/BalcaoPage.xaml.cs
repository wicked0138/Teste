using LumosBarbearia.ViewModels;

namespace LumosBarbearia.Views;

public partial class BalcaoPage : ContentPage
{
    private readonly BalcaoViewModel _vm;

    public BalcaoPage(BalcaoViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.AtivarAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Desativar();
    }
}
