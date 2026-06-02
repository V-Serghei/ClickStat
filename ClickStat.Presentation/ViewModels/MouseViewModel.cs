using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using ClickStat.Core.Interfaces;
using ClickStat.Presentation.Model;
using ClickStat.Presentation.Views;

namespace ClickStat.Presentation.ViewModels;

public class MouseViewModel : INotifyPropertyChanged
{
    private readonly IMouseStatisticsService _statisticsService;

    // Standard buttons (always visible)
    public MouseButtonStat LeftButton    { get; } = new((int)MouseButtons.Left,     "Левая кнопка");
    public MouseButtonStat RightButton   { get; } = new((int)MouseButtons.Right,    "Правая кнопка");
    public MouseButtonStat MiddleButton  { get; } = new((int)MouseButtons.Middle,   "Колесо (клик)");
    public MouseButtonStat BackButton    { get; } = new((int)MouseButtons.XButton1, "Кнопка назад");
    public MouseButtonStat ForwardButton { get; } = new((int)MouseButtons.XButton2, "Кнопка вперёд");

    // User-registered custom buttons
    public ObservableCollection<MouseButtonStat> CustomButtons { get; } = new();

    // Scroll stats
    private long _scrollUpNotches;
    private long _scrollDownNotches;

    public long ScrollUpNotches
    {
        get => _scrollUpNotches;
        private set
        {
            _scrollUpNotches = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScrollUpRotations));
            OnPropertyChanged(nameof(ScrollUpMeters));
        }
    }

    public long ScrollDownNotches
    {
        get => _scrollDownNotches;
        private set
        {
            _scrollDownNotches = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScrollDownRotations));
            OnPropertyChanged(nameof(ScrollDownMeters));
        }
    }

    // 24 notches per full wheel rotation; ~2.1 mm rim travel per notch
    public double ScrollUpRotations   => Math.Round(_scrollUpNotches   / 24.0, 1);
    public double ScrollDownRotations => Math.Round(_scrollDownNotches / 24.0, 1);
    public double ScrollUpMeters      => Math.Round(_scrollUpNotches   * 0.0021, 2);
    public double ScrollDownMeters    => Math.Round(_scrollDownNotches * 0.0021, 2);

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public ICommand AddButtonCommand { get; }
    public ICommand RefreshCommand   { get; }

    public MouseViewModel(IMouseStatisticsService statisticsService)
    {
        _statisticsService = statisticsService;
        AddButtonCommand = new RelayCommand(_ => OpenAddButtonDialog());
        RefreshCommand   = new RelayCommand(async _ => await LoadDataAsync());
        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var buttons = await _statisticsService.GetButtonStatistics();
            var scroll  = await _statisticsService.GetScrollStatistics();

            var standardCodes = new HashSet<int>
            {
                (int)MouseButtons.Left, (int)MouseButtons.Right, (int)MouseButtons.Middle,
                (int)MouseButtons.XButton1, (int)MouseButtons.XButton2
            };

            foreach (var btn in buttons)
            {
                if      (btn.ButtonCode == (int)MouseButtons.Left)     LeftButton.Count    = btn.Count;
                else if (btn.ButtonCode == (int)MouseButtons.Right)    RightButton.Count   = btn.Count;
                else if (btn.ButtonCode == (int)MouseButtons.Middle)   MiddleButton.Count  = btn.Count;
                else if (btn.ButtonCode == (int)MouseButtons.XButton1) BackButton.Count    = btn.Count;
                else if (btn.ButtonCode == (int)MouseButtons.XButton2) ForwardButton.Count = btn.Count;
            }

            CustomButtons.Clear();
            foreach (var btn in buttons)
            {
                if (!standardCodes.Contains(btn.ButtonCode) && btn.IsRegistered)
                    CustomButtons.Add(new MouseButtonStat(btn.ButtonCode, btn.ButtonName) { Count = btn.Count });
            }

            if (scroll != null)
            {
                ScrollUpNotches   = scroll.ScrollUpNotches;
                ScrollDownNotches = scroll.ScrollDownNotches;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void OpenAddButtonDialog()
    {
        var dialog = new AddMouseButtonDialog { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && dialog.RegisteredButton.HasValue)
        {
            var (code, name) = dialog.RegisteredButton.Value;
            await _statisticsService.RegisterCustomButton(code, name);
            await LoadDataAsync();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
