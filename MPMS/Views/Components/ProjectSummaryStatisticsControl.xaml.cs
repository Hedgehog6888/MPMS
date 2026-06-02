using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class ProjectSummaryStatisticsControl
{
    public ProjectSummaryStatisticsControl()
    {
        InitializeComponent();
    }

}
