namespace DtHub.Core.Hotkeys;

/// <summary>
/// The set of shortcuts, with the validation that goes with it.
/// Immutable: any change returns a new set, which prevents an invalid
/// shortcut from being half applied.
/// </summary>
public sealed class HotkeySet
{
    private readonly Dictionary<HotkeyAction, HotkeyBinding> _bindings;

    private HotkeySet(Dictionary<HotkeyAction, HotkeyBinding> bindings) => _bindings = bindings;

    /// <summary>Shortcuts shipped by default.</summary>
    public static HotkeySet Default => new(new Dictionary<HotkeyAction, HotkeyBinding>
    {
        // scrcpy reserves Alt for its own shortcuts: we stay on Ctrl.
        [HotkeyAction.ToggleConfigurator] = Bind(
            HotkeyAction.ToggleConfigurator, VirtualKeys.P, HotkeyModifiers.Control),
        [HotkeyAction.NextInstance] = Bind(
            HotkeyAction.NextInstance, VirtualKeys.Tab, HotkeyModifiers.Control),
        [HotkeyAction.PreviousInstance] = Bind(
            HotkeyAction.PreviousInstance, VirtualKeys.Tab, HotkeyModifiers.Control | HotkeyModifiers.Shift),
        [HotkeyAction.Rearrange] = Bind(HotkeyAction.Rearrange, VirtualKeys.R, HotkeyModifiers.Control),
        [HotkeyAction.Tile] = Bind(HotkeyAction.Tile, VirtualKeys.T, HotkeyModifiers.Control),
        [HotkeyAction.Quests] = Bind(HotkeyAction.Quests, VirtualKeys.Q, HotkeyModifiers.Control),

        // Ctrl+M and not Ctrl+A: these shortcuts are registered with
        // Windows, so they are taken away from every application. "A"
        // would have eaten the "select all" of the entire machine.
        // "M" is not reserved anywhere, and the shortcut is editable.
        [HotkeyAction.Almanax] = Bind(HotkeyAction.Almanax, VirtualKeys.M, HotkeyModifiers.Control),
        [HotkeyAction.Size1] = Bind(HotkeyAction.Size1, VirtualKeys.D1, HotkeyModifiers.Control),
        [HotkeyAction.Size2] = Bind(HotkeyAction.Size2, VirtualKeys.D2, HotkeyModifiers.Control),
        [HotkeyAction.Size3] = Bind(HotkeyAction.Size3, VirtualKeys.D3, HotkeyModifiers.Control),
        [HotkeyAction.Size4] = Bind(HotkeyAction.Size4, VirtualKeys.D4, HotkeyModifiers.Control),
        [HotkeyAction.Fullscreen] = Bind(HotkeyAction.Fullscreen, VirtualKeys.D5, HotkeyModifiers.Control),
        [HotkeyAction.Quit] = Bind(HotkeyAction.Quit, VirtualKeys.D0, HotkeyModifiers.Control),
    });

    /// <summary>
    /// All shortcuts, sorted in the editor's display order.
    /// </summary>
    public IReadOnlyList<HotkeyBinding> Bindings =>
        [.. Enum.GetValues<HotkeyAction>().Where(_bindings.ContainsKey).Select(a => _bindings[a])];

    /// <summary>
    /// Shortcut for an action, or <c>null</c> if it has none.
    /// </summary>
    public HotkeyBinding? For(HotkeyAction action) =>
        _bindings.TryGetValue(action, out var binding) ? binding : null;

    /// <summary>Action triggered by a combination, or <c>null</c>.</summary>
    public HotkeyAction? Resolve(int virtualKey, HotkeyModifiers modifiers) =>
        _bindings.Values
            .FirstOrDefault(b => b.IsAssigned && b.VirtualKey == virtualKey && b.Modifiers == modifiers)
            ?.Action;

    /// <summary>
    /// Checks a combination before accepting it. The targeted action
    /// is excluded from the duplicate search: reassigning the same
    /// shortcut to the same action is not a conflict.
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

        // Without a modifier, the key would be intercepted at every
        // keystroke. Function keys are an exception: they are not
        // used for typing.
        if (modifiers == HotkeyModifiers.None && !VirtualKeys.IsFunctionKey(virtualKey))
        {
            return HotkeyValidationResult.MissingModifier;
        }

        if (IsReserved(virtualKey, modifiers))
        {
            return HotkeyValidationResult.ReservedBySystem;
        }

        if (IsEditing(virtualKey, modifiers))
        {
            return HotkeyValidationResult.ReservedForEditing;
        }

        var conflicting = _bindings.Values.FirstOrDefault(
            b => b.Action != action && b.IsAssigned && b.VirtualKey == virtualKey && b.Modifiers == modifiers);

        return conflicting is null ? HotkeyValidationResult.Valid : HotkeyValidationResult.Duplicate;
    }

    /// <summary>Action that already holds this combination, if any.</summary>
    public HotkeyAction? FindConflict(HotkeyAction action, int virtualKey, HotkeyModifiers modifiers) =>
        _bindings.Values
            .FirstOrDefault(b => b.Action != action && b.IsAssigned
                                 && b.VirtualKey == virtualKey && b.Modifiers == modifiers)
            ?.Action;

    /// <summary>
    /// Returns a new set with the shortcut changed. A refused
    /// combination leaves the set unchanged.
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

    /// <summary>
    /// Removes an action's shortcut, which becomes inactive.
    /// </summary>
    public HotkeySet Without(HotkeyAction action)
    {
        var copy = new Dictionary<HotkeyAction, HotkeyBinding>(_bindings)
        {
            [action] = Bind(action, 0, HotkeyModifiers.None),
        };

        return new HotkeySet(copy);
    }

    /// <summary>
    /// Rebuilds a set from shortcuts read from disk. Invalid or
    /// duplicate entries are replaced with the default value: a file
    /// edited by hand must not make the application unusable.
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
                             && !IsEditing(binding.VirtualKey, binding.Modifiers)
                             && used.Add(binding.Combination);

            if (acceptable)
            {
                result[binding.Action] = binding;
            }
        }

        // Any action with no valid shortcut takes back its original
        // one, provided it is not already taken.
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
    /// Combinations that Windows intercepts itself: assigning them
    /// would give a shortcut that never triggers.
    /// </summary>
    private static bool IsReserved(int virtualKey, HotkeyModifiers modifiers)
    {
        // Ctrl+Alt+Del is never delivered to an application.
        if (virtualKey == VirtualKeys.Delete
            && modifiers.HasFlag(HotkeyModifiers.Control)
            && modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            return true;
        }

        // Win+L locks the session, Win+D shows the desktop.
        if (modifiers.HasFlag(HotkeyModifiers.Windows) && virtualKey is 0x4C or 0x44)
        {
            return true;
        }

        // Alt+Tab and Alt+Esc belong to the window switcher.
        return modifiers == HotkeyModifiers.Alt && virtualKey is VirtualKeys.Tab or VirtualKeys.Escape;
    }

    /// <summary>
    /// The editing keys, which we do not confiscate.
    ///
    /// Unlike the previous ones, these would intercept perfectly
    /// well, and that is the danger: <c>RegisterHotKey</c> applies to
    /// the whole desktop, so binding Ctrl+V to a DT Hub action would
    /// take pasting away from the text editor, the browser and the
    /// game itself, in both display modes, for as long as the
    /// application runs. Nothing forbade this move, and nothing
    /// would have explained it afterward.
    /// </summary>
    private static bool IsEditing(int virtualKey, HotkeyModifiers modifiers) =>
        modifiers == HotkeyModifiers.Control
        && virtualKey is VirtualKeys.A or VirtualKeys.C or VirtualKeys.V or VirtualKeys.X;

    private static HotkeyBinding Bind(HotkeyAction action, int virtualKey, HotkeyModifiers modifiers) =>
        new() { Action = action, VirtualKey = virtualKey, Modifiers = modifiers };
}
