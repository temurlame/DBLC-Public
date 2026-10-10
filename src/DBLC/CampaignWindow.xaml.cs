using Microsoft.AspNetCore.Components.WebView.Wpf;
using System.Windows;

namespace DBLC;

public partial class CampaignWindow : Window
{
    public CampaignWindow(IServiceProvider services, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        BlazorHost.Services = services;
        BlazorHost.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(CampaignManager) });
    }
}
