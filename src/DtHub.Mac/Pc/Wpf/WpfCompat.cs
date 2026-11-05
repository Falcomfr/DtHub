// The handful of WPF types the PC's view models and services name, so that
// they compile and run on the Mac unchanged. Each one does on Avalonia what
// the PC code expects of it, and no more: this is not a WPF, only the part
// of it the shared code touches. A WPF type the PC starts using shows up
// here as a build error of the Mac project, on the Windows CI too.
#pragma warning disable CA1822 // Instance members, as in WPF, for the call sites.
#pragma warning disable CA1010, CA1710 // ICollectionView: WPF's name and shape, which the shared code uses.
#pragma warning disable CA2201 // ExternalException: the one the PC code catches around the clipboard.

using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace System.Windows
{
    /// <summary>WPF's application, reduced to what the shared code asks of it.</summary>
    public sealed class Application
    {
        private static readonly Application Instance = new();

        private Application()
        {
        }

        /// <summary>Null until Avalonia runs, as WPF's is before startup.</summary>
        public static Application? Current => Avalonia.Application.Current is null ? null : Instance;

        public Threading.Dispatcher Dispatcher => Threading.Dispatcher.UiThread;

        /// <summary>The main window, for its scale only.</summary>
        public object? MainWindow =>
            (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        /// <summary>
        /// The PC's resources are WPF objects; none exists here, and every
        /// caller has a fallback for a missing one.
        /// </summary>
        public object? TryFindResource(object key) => null;
    }

    public static class Clipboard
    {
        /// <summary>
        /// Through the main window's clipboard. A failure comes out as the
        /// ExternalException the PC code catches.
        /// </summary>
        public static void SetText(string text)
        {
            var window = (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            if (window?.Clipboard is not { } clipboard)
            {
                throw new Runtime.InteropServices.ExternalException("No clipboard.");
            }

            // Waited for, as WPF's is synchronous: a failure must reach the
            // caller, which warns the player.
            try
            {
                _ = DtHub.Mac.Pc.Modal.Wait(async () =>
                {
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text).ConfigureAwait(true);
                    return true;
                });
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not Runtime.InteropServices.ExternalException)
            {
                throw new Runtime.InteropServices.ExternalException(exception.Message, exception);
            }
        }
    }

    public enum FlowDirection
    {
        LeftToRight,
        RightToLeft,
    }

    public static class FontStyles
    {
        public static object Normal => "Normal";
    }

    public static class FontWeights
    {
        public static object Normal => "Normal";
    }

    public static class FontStretches
    {
        public static object Normal => "Normal";
    }
}

namespace System.Windows.Threading
{
    public enum DispatcherPriority
    {
        Background,
        ContextIdle,
        Loaded,
        Render,
        Normal,
        Send,
    }

    /// <summary>WPF's dispatcher over Avalonia's UI thread.</summary>
    public sealed class Dispatcher
    {
        internal static readonly Dispatcher UiThread = new();

        private Dispatcher()
        {
        }

        public bool CheckAccess() => Avalonia.Threading.Dispatcher.UIThread.CheckAccess();

        public DispatcherOperation BeginInvoke(Action action) => InvokeAsync(action);

        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action action) =>
            new(Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action, Map(priority)).GetTask());

        public DispatcherOperation InvokeAsync(Action action) =>
            new(Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action).GetTask());

        private static Avalonia.Threading.DispatcherPriority Map(DispatcherPriority priority) => priority switch
        {
            DispatcherPriority.Background => Avalonia.Threading.DispatcherPriority.Background,
            DispatcherPriority.ContextIdle => Avalonia.Threading.DispatcherPriority.ContextIdle,
            DispatcherPriority.Loaded => Avalonia.Threading.DispatcherPriority.Loaded,
            DispatcherPriority.Render => Avalonia.Threading.DispatcherPriority.Render,
            DispatcherPriority.Send => Avalonia.Threading.DispatcherPriority.Send,
            _ => Avalonia.Threading.DispatcherPriority.Normal,
        };
    }

    public sealed class DispatcherOperation(Task task)
    {
        public Task Task { get; } = task;
    }
}

namespace System.Windows.Media
{
    /// <summary>
    /// Never produced here (no WPF resource exists), so the quest list's
    /// text measure always takes its estimate.
    /// </summary>
    public sealed class FontFamily;

    public sealed class Typeface(FontFamily face, object style, object weight, object stretch)
    {
        public FontFamily Face { get; } = face;

        public object Style { get; } = style;

        public object Weight { get; } = weight;

        public object Stretch { get; } = stretch;
    }

    /// <summary>Never found among resources here, like FontFamily.</summary>
    public sealed class SolidColorBrush
    {
        public Color Color { get; init; }
    }

    public readonly record struct Color(byte R, byte G, byte B);

    public static class Brushes
    {
        public static object Black => "Black";
    }

    public enum TextFormattingMode
    {
        Ideal,
        Display,
    }

    public readonly record struct DpiScale(double PixelsPerDip);

    public static class VisualTreeHelper
    {
        public static DpiScale GetDpi(object visual) =>
            new(visual is Avalonia.Controls.TopLevel level ? level.RenderScaling : 1.0);
    }

    /// <summary>Only reached with a font face, which never happens here.</summary>
    public sealed class FormattedText
    {
        public FormattedText(
            string text,
            Globalization.CultureInfo culture,
            FlowDirection flow,
            Typeface typeface,
            double size,
            object brush,
            object? substitution,
            TextFormattingMode mode,
            double pixelsPerDip)
        {
            WidthIncludingTrailingWhitespace = text.Length * size * 0.6;
            _ = (culture, flow, typeface, brush, substitution, mode, pixelsPerDip);
        }

        public double WidthIncludingTrailingWhitespace { get; }
    }
}

namespace System.Windows.Data
{
    public sealed class PropertyGroupDescription(string propertyName) : GroupDescription
    {
        public string PropertyName { get; } = propertyName;
    }

    /// <summary>One group of a view: its name, the grouped value, and its items.</summary>
    public sealed class CollectionViewGroup(object? name, IReadOnlyList<object> items)
    {
        public object? Name { get; } = name;

        public IReadOnlyList<object> Items { get; } = items;
    }

    public static class CollectionViewSource
    {
        public static ICollectionView GetDefaultView(IEnumerable source) => new FilteredView(source);
    }

    /// <summary>
    /// The filtered list WPF puts between a collection and its list box:
    /// what passes <see cref="Filter"/>, refreshed when asked or when the
    /// source changes.
    /// </summary>
    internal sealed class FilteredView : ICollectionView
    {
        private readonly IEnumerable _source;
        private Predicate<object>? _filter;

        public FilteredView(IEnumerable source)
        {
            _source = source;

            if (source is INotifyCollectionChanged changing)
            {
                changing.CollectionChanged += (_, _) => Refresh();
            }
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        /// <summary>Applied by <see cref="Groups"/> only: the list itself stays flat.</summary>
        public Collections.ObjectModel.ObservableCollection<GroupDescription> GroupDescriptions { get; } = [];

        /// <summary>
        /// What passes the filter, by the value of the first group
        /// description's property, in order of first appearance, as WPF
        /// groups it. A single group of everything without a description.
        /// </summary>
        public IReadOnlyList<CollectionViewGroup> Groups
        {
            get
            {
                var property = GroupDescriptions.OfType<PropertyGroupDescription>().FirstOrDefault()?.PropertyName;

                return
                [
                    .. this.Cast<object>()
                        .GroupBy(item => property is null ? null : item.GetType().GetProperty(property)?.GetValue(item))
                        .Select(group => new CollectionViewGroup(group.Key, [.. group])),
                ];
            }
        }

        public bool IsEmpty => !GetEnumerator().MoveNext();

        public Predicate<object>? Filter
        {
            get => _filter;
            set
            {
                _filter = value;
                Refresh();
            }
        }

        public void Refresh() =>
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

        public IEnumerator GetEnumerator() =>
            _source.Cast<object>().Where(item => _filter?.Invoke(item) ?? true).GetEnumerator();
    }
}

namespace System.ComponentModel
{
    public interface ICollectionView : IEnumerable, INotifyCollectionChanged
    {
        Collections.ObjectModel.ObservableCollection<GroupDescription> GroupDescriptions { get; }

        /// <summary>
        /// WPF's groups, as a snapshot taken when read: the Mac's lists ask
        /// for them again on each change of the view.
        /// </summary>
        IReadOnlyList<Windows.Data.CollectionViewGroup> Groups { get; }

        bool IsEmpty { get; }

        Predicate<object>? Filter { get; set; }

        void Refresh();
    }

    public abstract class GroupDescription;
}
