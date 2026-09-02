using DtHub.Core.Adb;

namespace DtHub.Core.Android;

/// <summary>
/// Coupe les animations d'un téléphone, et les lui rend.
///
/// C'est le seul réglage de l'application qui modifie l'appareil plutôt que la
/// session. Les trois échelles sont globales et survivent à la fermeture de
/// DT Hub : les couper sans les rendre laisserait le téléphone dans un état que
/// son propriétaire n'a pas choisi et ne saurait pas expliquer.
///
/// D'où la règle tenue ici : <b>on relit avant d'écrire</b>, on garde ce qu'on a
/// trouvé, et on le rend. Supposer que tout valait 1 remettrait à 1 le téléphone
/// de quelqu'un qui les avait lui-même réglées autrement.
///
/// Aucune garantie en cas de fin brutale de l'application : ce qui est écrit sur
/// le téléphone y reste. L'interface le dit plutôt que de le taire.
/// </summary>
public sealed class DeviceAnimationService
{
    private readonly Dictionary<string, AnimationScales> _saved = new(StringComparer.Ordinal);
    private readonly IAdbClient _adb;

    public DeviceAnimationService(IAdbClient adb) => _adb = adb;

    /// <summary>Les téléphones dont les animations sont coupées de notre fait.</summary>
    public IReadOnlyCollection<string> Touched
    {
        get
        {
            lock (_saved)
            {
                return [.. _saved.Keys];
            }
        }
    }

    /// <summary>
    /// Coupe les animations, après avoir mémorisé ce qui s'y trouvait.
    ///
    /// Sans effet si l'appareil est déjà pris en charge : deux sessions sur le
    /// même téléphone ne doivent pas faire mémoriser des zéros comme état
    /// d'origine, ce qui rendrait la restauration inopérante.
    /// </summary>
    public async Task<bool> DisableAsync(string serial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        lock (_saved)
        {
            if (_saved.ContainsKey(serial))
            {
                return true;
            }
        }

        AnimationScales before;

        try
        {
            before = await ReadAsync(serial, cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException)
        {
            // Faute d'avoir pu lire, on n'écrit pas : on ne saurait pas rendre.
            return false;
        }

        // Déjà coupées sans nous : on n'y touche pas, et il n'y aura rien à
        // rendre.
        if (before.AllOff)
        {
            return true;
        }

        if (!await WriteAsync(serial, AnimationScales.Off, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        lock (_saved)
        {
            _saved[serial] = before;
        }

        return true;
    }

    /// <summary>Rend au téléphone les valeurs trouvées avant la coupure.</summary>
    public async Task RestoreAsync(string serial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        AnimationScales before;

        lock (_saved)
        {
            if (!_saved.Remove(serial, out before))
            {
                return;
            }
        }

        if (!await WriteAsync(serial, before, cancellationToken).ConfigureAwait(false))
        {
            // Remis en liste : une tentative plus tard, à l'arrêt, vaut mieux
            // que de laisser le téléphone ainsi.
            lock (_saved)
            {
                _saved[serial] = before;
            }
        }
    }

    /// <summary>
    /// Rend leurs animations à tous les téléphones touchés. Appelé à l'arrêt de
    /// l'application, où c'est la dernière occasion.
    /// </summary>
    public async Task RestoreAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var serial in Touched)
        {
            await RestoreAsync(serial, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AnimationScales> ReadAsync(string serial, CancellationToken cancellationToken)
    {
        var values = new string[AnimationScales.Keys.Length];

        for (var i = 0; i < AnimationScales.Keys.Length; i++)
        {
            values[i] = await _adb
                .ShellAsync(serial, ["settings", "get", "global", AnimationScales.Keys[i]], null, cancellationToken)
                .ConfigureAwait(false);
        }

        return AnimationScales.Parse(values[0], values[1], values[2]);
    }

    private async Task<bool> WriteAsync(
        string serial,
        AnimationScales scales,
        CancellationToken cancellationToken)
    {
        try
        {
            for (var i = 0; i < AnimationScales.Keys.Length; i++)
            {
                await _adb.ShellAsync(
                    serial,
                    [
                        "settings",
                        "put",
                        "global",
                        AnimationScales.Keys[i],
                        AnimationScales.Text(scales.Values[i]),
                    ],
                    null,
                    cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (AdbException)
        {
            return false;
        }
    }
}
