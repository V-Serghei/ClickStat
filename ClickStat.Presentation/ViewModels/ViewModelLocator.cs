namespace ClickStat.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
public class ViewModelLocator
{
    public static ViewModelLocator Current { get; } = new ViewModelLocator();
        
    public IServiceProvider ServiceProvider { get; set; }
        
    public MainViewModel Main => 
        ServiceProvider?.GetService<MainViewModel>() ?? new MainViewModel();
}