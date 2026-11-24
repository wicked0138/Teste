using LumosBarbearia.ViewModels;

namespace LumosBarbearia.Views;

public partial class AgendarPage : ContentPage
{
    private readonly AgendarViewModel _vm;

    public AgendarPage(AgendarViewModel vm)
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
