using Microsoft.AspNetCore.Components.WebView.Wpf;
using System.Windows;

namespace DBLC;

public partial class PaperPopoutWindow : Window
{
    public PaperPopoutWindow(string viewName, IServiceProvider services, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        Title = viewName == "Player" ? "DBLC · Player" : "DBLC · Workspace";
        BlazorHost.Services = services;
        BlazorHost.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(PaperPopout),
            Parameters = new Dictionary<string, object?> { [nameof(PaperPopout.ViewName)] = viewName }
        });
    }
}
