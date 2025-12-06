using System.Linq;
using System.Windows;
using System.Windows.Controls;
using X9AEditor.ViewModels;

namespace X9AEditor;

/// <summary>
/// Interaktionslogik für MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    readonly MainViewModel viewModel = new();

    public MainWindow(string? initialFilePath = null)
    {
        InitializeComponent();

        DataContext = viewModel;

        if (initialFilePath != null)
            viewModel.OpenCommand.Execute(initialFilePath);        
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) as string[] is [var path])
        {
            viewModel.OpenCommand.Execute(path);
            e.Handled = true;
        }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) as string[] is [var _])
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;

        e.Handled = true;
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        viewModel.SelectedVoices = dataGrid.SelectedItems.Cast<VoiceViewModel>().ToArray();
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        ((ContextMenu)sender).DataContext = DataContext;
    }
}
