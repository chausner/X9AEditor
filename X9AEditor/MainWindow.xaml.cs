using Microsoft.Win32;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using X9AEditor.ViewModels;

namespace X9AEditor
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        MainViewModel viewModel = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                viewModel.OpenCommand.Execute(files[0]);
            }
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
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
}
