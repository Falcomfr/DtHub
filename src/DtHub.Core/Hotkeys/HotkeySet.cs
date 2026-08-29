namespace DtHub.Core.Hotkeys;

/// <summary>
/// L'ensemble des raccourcis, avec la validation qui va avec. Immuable : toute
/// modification rend un nouvel ensemble, ce qui évite qu'un raccourci invalide
/// soit à moitié appliqué.
/// </summary>
public sealed class HotkeySet
{
    private readonly Dictionary<HotkeyAction, HotkeyBinding> _bindings;

    private HotkeySet(Dictionary<HotkeyAction, HotkeyBinding> bindings) => _bindings = bindings;

    /// <summary>Raccourcis livrés par défaut.</summary>
    public static HotkeySet Default => new(new Dictionary<HotkeyAction, HotkeyBinding>
    {
        // scrcpy réserve Alt pour ses propres raccourcis : on reste sur Ctrl.
        [HotkeyAction.ToggleConfigurator] = Bind(
            HotkeyAction.ToggleConfigurator, VirtualKeys.P, HotkeyModifiers.Control),
        [HotkeyAction.NextInstance] = Bind(
            HotkeyAction.NextInstance, VirtualKeys.Tab, HotkeyModifiers.Control),
        [HotkeyAction.PreviousInstance] = Bind(
            HotkeyAction.PreviousInstance, VirtualKeys.Tab, HotkeyModifiers.Control | HotkeyModifiers.Shift),
        [HotkeyAction.Rearrange] = Bind(HotkeyAction.Rearrange, VirtualKeys.R, HotkeyModifiers.Control),
        [HotkeyAction.CloseAll] = Bind(HotkeyAction.CloseAll, VirtualKeys.D0, HotkeyModifiers.Control),
    });

    /// <summary>Tous les raccourcis, triés dans l'ordre d'affichage de l'éditeur.</summary>
    public IReadOnlyList<HotkeyBinding> Bindings =>
        [.. Enum.GetValues<HotkeyAction>().Where(_bindings.ContainsKey).Select(a => _bindings[a])];

    /// <summary>Raccourci d'une action, ou <c>null</c> si elle n'en a pas.</summary>
    public HotkeyBinding? For(HotkeyAction action) =>
        _bindings.TryGetValue(action, out var binding) ? binding : null;

    /// <summary>Action déclenchée par une combinaison, ou <c>null</c>.</summary>
    public HotkeyAction? Resolve(int virtualKey, HotkeyModifiers modifiers) =>
        _bindings.Values
            .FirstOrDefault(b => b.IsAssigned && b.VirtualKey == virtualKey && b.Modifiers == modifiers)
            ?.Action;

    /// <summary>
    /// Vérifie une combinaison avant de l'accepter. L'action visée est exclue
    /// de la recherche de doublon : réattribuer le même raccourci à la même
    /// action n'est pas un conflit.
    /// </summary>
    public HotkeyValidationResult Validate(HotkeyAction action, int virtualKey, HotkeyModifiers modifiers)
    {
        if (virtualKey == 0)
        {
            return HotkeyValidationResult.NoKey;
        }

        if (VirtualKeys.IsModifierKey(virtualKey))
        {
            return HotkeyValidationResult.ModifierOnly;
        }

        // Sans modificateur, la touche serait interceptée à chaque frappe. Les
        // touches de fonction font exception : elles ne servent pas à écrire.
        if (modifiers == HotkeyModifiers.None && !VirtualKeys.IsFunctionKey(virtualKey))
        {
            return HotkeyValidationResult.MissingModifier;
        }

        if (IsReserved(virtualKey, modifiers))
        {
            return HotkeyValidationResult.ReservedBySystem;
        }

        var conflicting = _bindings.Values.FirstOrDefault(
            b => b.Action != action && b.IsAssigned && b.VirtualKey == virtualKey && b.Modifiers == modifiers);

        return conflicting is null ? HotkeyValidationResult.Valid : HotkeyValidationResult.Duplicate;
    }

    /// <summary>Action qui détient déjà cette combinaison, le cas échéant.</summary>
    public HotkeyAction? FindConflict(HotkeyAction action, int virtualKey, HotkeyModifiers modifiers) =>
        _bindings.Values
            .FirstOrDefault(b => b.Action != action && b.IsAssigned
                                 && b.VirtualKey == virtualKey && b.Modifiers == modifiers)
            ?.Action;

    /// <summary>
    /// Rend un nouvel ensemble avec le raccourci modifié. Une combinaison
    /// refusée laisse l'ensemble inchangé.
    /// </summary>
    public HotkeySet With(HotkeyAction action, int virtualKey, HotkeyModifiers modifiers)
    {
        if (Validate(action, virtualKey, modifiers) != HotkeyValidationResult.Valid)
        {
            return this;
        }

        var copy = new Dictionary<HotkeyAction, HotkeyBinding>(_bindings)
        {
            [action] = Bind(action, virtualKey, modifiers),
        };

        return new HotkeySet(copy);
    }

    /// <summary>Retire le raccourci d'une action, qui devient inactive.</summary>
    public HotkeySet Without(HotkeyAction action)
    {
        var copy = new Dictionary<HotkeyAction, HotkeyBinding>(_bindings)
        {
            [action] = Bind(action, 0, HotkeyModifiers.None),
        };

        return new HotkeySet(copy);
    }

    /// <summary>
    /// Reconstruit un ensemble à partir de raccourcis lus sur disque. Les
    /// entrées invalides ou en doublon sont remplacées par la valeur par
    /// défaut : un fichier modifié à la main ne doit pas rendre l'application
    /// inutilisable.
    /// </summary>
    public static HotkeySet FromBindings(IEnumerable<HotkeyBinding>? bindings)
    {
        var result = new Dictionary<HotkeyAction, HotkeyBinding>();
        var used = new HashSet<(int, HotkeyModifiers)>();

        foreach (var binding in bindings ?? [])
        {
            if (result.ContainsKey(binding.Action))
            {
                continue;
            }

            if (!binding.IsAssigned)
            {
                result[binding.Action] = binding;
                continue;
            }

            var acceptable = !VirtualKeys.IsModifierKey(binding.VirtualKey)
                             && (binding.Modifiers != HotkeyModifiers.None
                                 || VirtualKeys.IsFunctionKey(binding.VirtualKey))
                             && !IsReserved(binding.VirtualKey, binding.Modifiers)
                             && used.Add(binding.Combination);

            if (acceptable)
            {
                result[binding.Action] = binding;
            }
        }

        // Toute action sans raccourci valide reprend celui d'origine, à
        // condition qu'il ne soit pas déjà pris.
        foreach (var fallback in Default.Bindings)
        {
            if (result.ContainsKey(fallback.Action))
            {
                continue;
            }

            result[fallback.Action] = used.Add(fallback.Combination)
                ? fallback
                : Bind(fallback.Action, 0, HotkeyModifiers.None);
        }

        return new HotkeySet(result);
    }

    /// <summary>
    /// Combinaisons que Windows intercepte lui-même : les attribuer donnerait
    /// un raccourci qui ne se déclenche jamais.
    /// </summary>
    private static bool IsReserved(int virtualKey, HotkeyModifiers modifiers)
    {
        // Ctrl+Alt+Suppr n'est jamais délivrée à une application.
        if (virtualKey == VirtualKeys.Delete
            && modifiers.HasFlag(HotkeyModifiers.Control)
            && modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            return true;
        }

        // Win+L verrouille la session, Win+D affiche le bureau.
        if (modifiers.HasFlag(HotkeyModifiers.Windows) && virtualKey is 0x4C or 0x44)
        {
            return true;
        }

        // Alt+Tab et Alt+Échap appartiennent au sélecteur de fenêtres.
        return modifiers == HotkeyModifiers.Alt && virtualKey is VirtualKeys.Tab or VirtualKeys.Escape;
    }

    private static HotkeyBinding Bind(HotkeyAction action, int virtualKey, HotkeyModifiers modifiers) =>
        new() { Action = action, VirtualKey = virtualKey, Modifiers = modifiers };
}
