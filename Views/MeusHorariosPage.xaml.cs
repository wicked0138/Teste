using LumosBarbearia.ViewModels;

namespace LumosBarbearia.Views;

public partial class MeusHorariosPage : ContentPage
{
    private readonly MeusHorariosViewModel _vm;

    public MeusHorariosPage(MeusHorariosViewModel vm)
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
