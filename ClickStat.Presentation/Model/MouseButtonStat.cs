using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClickStat.Presentation.Model;

public class MouseButtonStat : INotifyPropertyChanged
{
    public int ButtonCode { get; }
    public string ButtonName { get; set; }

    private long _count;
    public long Count
    {
        get => _count;
        set
        {
            _count = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedCount));
        }
    }

    public string FormattedCount => _count switch
    {
        >= 1_000_000 => $"{_count / 1_000_000.0:F1}М",
        >= 1_000     => $"{_count / 1_000.0:F1}к",
        _            => _count.ToString()
    };

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); }
    }

    public void DoActive() => IsActive = true;

    public MouseButtonStat(int buttonCode, string buttonName)
    {
        ButtonCode = buttonCode;
        ButtonName = buttonName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
