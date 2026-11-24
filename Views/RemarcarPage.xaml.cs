using LumosBarbearia.ViewModels;

namespace LumosBarbearia.Views;

public partial class RemarcarPage : ContentPage
{
    private readonly RemarcarViewModel _vm;

    public RemarcarPage(RemarcarViewModel vm)
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
