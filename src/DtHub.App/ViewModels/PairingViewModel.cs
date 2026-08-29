using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Adb;
using DtHub.Core.Devices;

namespace DtHub.App.ViewModels;

/// <summary>
/// Association d'un téléphone en Wi-Fi. Le panneau surveille le réseau tout
/// seul et pré-remplit ce qu'il peut : l'utilisateur ne tape que le code à six
/// chiffres affiché sur le téléphone.
/// </summary>
public sealed partial class PairingViewModel : ObservableObject
{
    private readonly DevicePairingService _pairing;

    public PairingViewModel(DevicePairingService pairing) => _pairing = pairing;

    /// <summary>Téléphones qui annoncent un code d'association sur le réseau.</summary>
    public ObservableCollection<MdnsService> Candidates { get; } = [];

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _port = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Vrai quand un téléphone en attente a été repéré sur le réseau.</summary>
    public bool HasCandidate => Candidates.Count > 0;

    /// <summary>Déclenché après une association réussie.</summary>
    public event EventHandler? Paired;

    /// <summary>
    /// Cherche les téléphones affichant un code. Appelé en boucle par la
    /// fenêtre : c'est ce qui rend l'association quasi automatique.
    /// </summary>
    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var found = await _pairing.FindPairingCandidatesAsync(cancellationToken).ConfigureAwait(true);

            Candidates.Clear();
            foreach (var candidate in found)
            {
                Candidates.Add(candidate);
            }

            OnPropertyChanged(nameof(HasCandidate));

            // Un seul téléphone en attente : on remplit pour lui.
            if (Candidates.Count == 1 && !IsBusy)
            {
                Address = Candidates[0].Host;
                Port = Candidates[0].Port.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (AdbException)
        {
            // La découverte est un confort : son échec laisse la saisie
            // manuelle possible.
        }
    }

    [RelayCommand]
    private async Task PairAsync(CancellationToken cancellationToken)
    {
        var address = Address.Trim();
        var code = Code.Trim();

        if (address.Length == 0 || code.Length == 0
            || !int.TryParse(Port.Trim(), out var port) || port is < 1 or > 65535)
        {
            Status = "Recopiez l'adresse IP, le port et le code affichés sur le téléphone.";
            return;
        }

        IsBusy = true;
        Status = "Association en cours…";

        try
        {
            var result = await _pairing.PairAndConnectAsync(address, port, code, cancellationToken)
                .ConfigureAwait(true);

            // Le code ne sert qu'une fois : il est effacé aussitôt.
            Code = string.Empty;
            Status = result.UserMessage;

            if (result.Connected)
            {
                Paired?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (AdbException exception)
        {
            Status = exception.UserMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Connexion directe, pour le cas où le réseau bloque la découverte et où
    /// l'utilisateur saisit lui-même le port affiché sous « Débogage sans fil ».
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var address = Address.Trim();

        if (address.Length == 0 || !int.TryParse(Port.Trim(), out var port))
        {
            Status = "Recopiez l'adresse IP et le port affichés sur le téléphone.";
            return;
        }

        IsBusy = true;

        try
        {
            var result = await _pairing.ConnectAsync(address, port, cancellationToken).ConfigureAwait(true);
            Status = result.UserMessage;

            if (result.Connected)
            {
                Paired?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (AdbException exception)
        {
            Status = exception.UserMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
