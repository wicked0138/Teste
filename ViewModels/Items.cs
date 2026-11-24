using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using LumosBarbearia.Models;

namespace LumosBarbearia.ViewModels;

/// <summary>Itens de lista usados pelas telas de agendamento. Cada item guarda o comando de seleção do ViewModel.</summary>

public partial class DiaItem : ObservableObject
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public DiaItem(DateTime data, ICommand selecionar)
    {
        Data = data;
        Selecionar = selecionar;
    }

    public DateTime Data { get; }
    public ICommand Selecionar { get; }

    public string DiaSemana => Data.ToString("ddd", PtBr).TrimEnd('.').ToUpper(PtBr);
    public string DiaNumero => Data.ToString("dd", PtBr);
    public string Mes => Data.ToString("MMM", PtBr).TrimEnd('.').ToUpper(PtBr);

    [ObservableProperty] private bool _isSelected;
}

public partial class BarbeiroItem : ObservableObject
{
    public BarbeiroItem(Barbeiro barbeiro, ICommand selecionar)
    {
        Barbeiro = barbeiro;
        Selecionar = selecionar;
    }

    public Barbeiro Barbeiro { get; }
    public ICommand Selecionar { get; }
    public string Nome => Barbeiro.Nome;
    public string Inicial => string.IsNullOrWhiteSpace(Nome) ? "?" : Nome.Trim()[..1].ToUpperInvariant();

    [ObservableProperty] private bool _isSelected;
}

public partial class ServicoItem : ObservableObject
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public ServicoItem(Servico servico, ICommand selecionar)
    {
        Servico = servico;
        Selecionar = selecionar;
    }

    public Servico Servico { get; }
    public ICommand Selecionar { get; }
    public string Nome => Servico.Nome;
    public string PrecoTexto => Servico.Preco.ToString("C", PtBr);
    public string DuracaoTexto => Servico.Duracao >= 60 && Servico.Duracao % 60 == 0
        ? $"{Servico.Duracao / 60} h"
        : $"{Servico.Duracao} min";

    [ObservableProperty] private bool _isSelected;
}

public partial class HorarioItem : ObservableObject
{
    public HorarioItem(TimeSpan inicio, bool disponivel, ICommand selecionar)
    {
        Inicio = inicio;
        Disponivel = disponivel;
        Selecionar = selecionar;
    }

    public TimeSpan Inicio { get; }
    public bool Disponivel { get; }
    public ICommand Selecionar { get; }
    public string Texto => Inicio.ToString(@"hh\:mm");

    [ObservableProperty] private bool _isSelected;
}
