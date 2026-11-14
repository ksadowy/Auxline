using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Auxline.Diagnostics;
using Auxline.Models;
using Auxline.Services;

namespace Auxline.ViewModels;

/// <summary>A dropdown entry: the setting's value with a readable label.</summary>
public sealed record AnchorOption(WidgetAnchor Value, string Label);

/// <param name="Device">Monitor device name, or <see cref="PrimaryDevice"/> for "follow the primary monitor".</param>
public sealed record MonitorOption(string Device, string Label)
{
    /// <summary>WPF dropdowns can't select a null value, so "primary" is an empty string here and null in the settings.</summary>
    public const string PrimaryDevice = "";
}

/// <summary>
/// Settings window state. Every change is applied live and saved by <see cref="SettingsService"/>.
/// Dispose when the window closes: it listens to the long-lived settings service.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    /// <summary>Shown in the colour box when no custom colour has been chosen yet.</summary>
    private const string DefaultCustomTextColor = "#FFFFFF";

    private readonly SettingsService _settings;

    /// <summary>True while values are copied in from the settings, so that doesn't echo back as an update.</summary>
    private bool _isLoading;

    [ObservableProperty]
    private double _leftOffset;

    [ObservableProperty]
    private double _maxWidgetWidth;

    [ObservableProperty]
    private double _coverSize;

    [ObservableProperty]
    private WidgetAnchor _anchor;

    [ObservableProperty]
    private string _monitorDevice = MonitorOption.PrimaryDevice;

    [ObservableProperty]
    private ThemePreference _theme;

    [ObservableProperty]
    private string _fontFamily = LookSettings.DefaultFontFamily;

    [ObservableProperty]
    private string _fontSearch = "";

    [ObservableProperty]
    private IReadOnlyList<string> _filteredFonts = [];

    /// <summary>
    /// The list's selection, kept apart from <see cref="FontFamily"/>: when a search hides the current font the
    /// list deselects (null), which must not reset the font.
    /// </summary>
    [ObservableProperty]
    private string? _selectedFontInList;

    [ObservableProperty]
    private double _titleFontSize;

    [ObservableProperty]
    private double _artistFontSize;

    [ObservableProperty]
    private bool _useCustomTextColor;

    /// <summary>Free text while typing; only applied once it's a valid #RRGGBB / #AARRGGBB value.</summary>
    [ObservableProperty]
    private string _textColor = DefaultCustomTextColor;

    /// <summary>Colour wheel side of <see cref="TextColor"/>; the two stay in sync.</summary>
    [ObservableProperty]
    private Color _pickerColor = Colors.White;

    [ObservableProperty]
    private CornerStyle _flyoutCorners;

    [ObservableProperty]
    private bool _useAcrylic;

    public IReadOnlyList<AnchorOption> AnchorOptions { get; } =
    [
        new(WidgetAnchor.LeftOfIcons, "Left of the taskbar icons"),
        new(WidgetAnchor.RightOfIcons, "Right of the icons, next to the tray"),
    ];

    public IReadOnlyList<ThemePreference> Themes { get; } = Enum.GetValues<ThemePreference>();

    public IReadOnlyList<CornerStyle> CornerStyles { get; } = Enum.GetValues<CornerStyle>();

    public IReadOnlyList<string> AvailableFonts { get; }

    /// <summary>Priority order: first = preferred.</summary>
    public ObservableCollection<SourceItemViewModel> Sources { get; } = [];

    public IReadOnlyList<MonitorOption> MonitorOptions { get; }

    public SettingsViewModel(SettingsService settings, IReadOnlyList<MonitorOption> monitorOptions)
    {
        _settings = settings;
        MonitorOptions = monitorOptions;
        AvailableFonts = ListInstalledFonts(settings.Current.Look.FontFamily);
        FilteredFonts = AvailableFonts;
        Load(settings.Current);
        _settings.Changed += OnSettingsChanged;
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    /// <summary>Shows apps discovered while the window is open; our own edits keep the same set, so no rebuild.</summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var isSameSet = settings.Sources.Count == Sources.Count
            && settings.Sources.All(source => Sources.Any(item => string.Equals(item.AppId, source.AppId, StringComparison.OrdinalIgnoreCase)));
        if (!isSameSet) LoadSources(settings.Sources);
    }

    [RelayCommand]
    private void MoveSourceUp(SourceItemViewModel item) => MoveSource(item, -1);

    [RelayCommand]
    private void MoveSourceDown(SourceItemViewModel item) => MoveSource(item, +1);

    /// <summary>Forgets the source; it's added again (enabled, last) the next time it plays.</summary>
    [RelayCommand]
    private void RemoveSource(SourceItemViewModel item)
    {
        Sources.Remove(item);
        SaveSources();
    }

    private void MoveSource(SourceItemViewModel item, int offset)
    {
        var index = Sources.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Sources.Count) return;

        Sources.Move(index, target);
        SaveSources();
    }

    private void SaveSources()
    {
        if (_isLoading) return;
        _settings.Update(settings => settings with { Sources = Sources.Select(item => item.ToEntry()).ToList() });
    }

    private void LoadSources(IReadOnlyList<SourceEntry> sources)
    {
        Sources.Clear();
        foreach (var source in sources) Sources.Add(new SourceItemViewModel(source, SaveSources));
    }

    partial void OnLeftOffsetChanged(double value) => UpdateLayout(layout => layout with { LeftOffset = value });

    partial void OnMaxWidgetWidthChanged(double value) => UpdateLayout(layout => layout with { MaxWidgetWidth = value });

    partial void OnCoverSizeChanged(double value) => UpdateLayout(layout => layout with { CoverSize = value });

    partial void OnAnchorChanged(WidgetAnchor value) => UpdateLayout(layout => layout with { Anchor = value });

    partial void OnMonitorDeviceChanged(string value) =>
        UpdateLayout(layout => layout with { MonitorDevice = value == MonitorOption.PrimaryDevice ? null : value });

    partial void OnThemeChanged(ThemePreference value) => UpdateLook(look => look with { Theme = value });

    partial void OnFontFamilyChanged(string value) => UpdateLook(look => look with { FontFamily = value });

    partial void OnFontSearchChanged(string value) =>
        FilteredFonts = AvailableFonts.Where(font => font.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    partial void OnSelectedFontInListChanged(string? value)
    {
        if (value is not null) FontFamily = value;
    }

    partial void OnTitleFontSizeChanged(double value) => UpdateLook(look => look with { TitleFontSize = value });

    partial void OnArtistFontSizeChanged(double value) => UpdateLook(look => look with { ArtistFontSize = value });

    partial void OnUseCustomTextColorChanged(bool value) => ApplyTextColor();

    partial void OnTextColorChanged(string value)
    {
        // Typing a valid code moves the wheel; equal colours stop the wheel ↔ text round trip.
        if (LookSettings.IsHexColor(value)) PickerColor = (Color)ColorConverter.ConvertFromString(value);
        ApplyTextColor();
    }

    partial void OnPickerColorChanged(Color value) => TextColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";

    partial void OnFlyoutCornersChanged(CornerStyle value) => UpdateLook(look => look with { FlyoutCorners = value });

    partial void OnUseAcrylicChanged(bool value) => UpdateLook(look => look with { UseAcrylic = value });

    [RelayCommand]
    private void ResetToDefaults()
    {
        _settings.Update(_ => new AppSettings());
        Load(_settings.Current);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppDiagnostics.LogDirectory);
            Process.Start(new ProcessStartInfo { FileName = AppDiagnostics.LogDirectory, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not open the log folder: {ex.Message}");
        }
    }

    /// <summary>Half-typed values (e.g. "#FF8") are ignored; the widget keeps its last valid colour.</summary>
    private void ApplyTextColor()
    {
        if (UseCustomTextColor && !LookSettings.IsHexColor(TextColor)) return;
        UpdateLook(look => look with { TextColor = UseCustomTextColor ? TextColor : null });
    }

    private void UpdateLayout(Func<LayoutSettings, LayoutSettings> change)
    {
        if (_isLoading) return;
        _settings.Update(settings => settings with { Layout = change(settings.Layout) });
    }

    private void UpdateLook(Func<LookSettings, LookSettings> change)
    {
        if (_isLoading) return;
        _settings.Update(settings => settings with { Look = change(settings.Look) });
    }

    private void Load(AppSettings settings)
    {
        _isLoading = true;
        LeftOffset = settings.Layout.LeftOffset;
        MaxWidgetWidth = settings.Layout.MaxWidgetWidth;
        CoverSize = settings.Layout.CoverSize;
        Anchor = settings.Layout.Anchor;
        MonitorDevice = settings.Layout.MonitorDevice ?? MonitorOption.PrimaryDevice;
        Theme = settings.Look.Theme;
        FontFamily = settings.Look.FontFamily;
        SelectedFontInList = settings.Look.FontFamily;
        TitleFontSize = settings.Look.TitleFontSize;
        ArtistFontSize = settings.Look.ArtistFontSize;
        UseCustomTextColor = settings.Look.TextColor is not null;
        TextColor = settings.Look.TextColor ?? DefaultCustomTextColor;
        FlyoutCorners = settings.Look.FlyoutCorners;
        UseAcrylic = settings.Look.UseAcrylic;
        LoadSources(settings.Sources);
        _isLoading = false;
    }

    /// <summary>Sorted family names; the current one is kept even if it's no longer installed, so it stays selectable.</summary>
    private static IReadOnlyList<string> ListInstalledFonts(string currentFont) =>
        Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Append(currentFont)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
