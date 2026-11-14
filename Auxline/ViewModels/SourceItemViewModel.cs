using CommunityToolkit.Mvvm.ComponentModel;
using Auxline.Models;
using Auxline.Services;

namespace Auxline.ViewModels;

/// <summary>One row in the Sources tab.</summary>
public sealed partial class SourceItemViewModel : ObservableObject
{
    private readonly Action _onChanged;

    [ObservableProperty]
    private bool _isEnabled;

    public SourceItemViewModel(SourceEntry entry, Action onChanged)
    {
        AppId = entry.AppId;
        DisplayName = AppIdentity.ToDisplayName(entry.AppId);
        _isEnabled = entry.IsEnabled;
        _onChanged = onChanged;
    }

    public string AppId { get; }

    public string DisplayName { get; }

    public SourceEntry ToEntry() => new(AppId, IsEnabled);

    partial void OnIsEnabledChanged(bool value) => _onChanged();
}
