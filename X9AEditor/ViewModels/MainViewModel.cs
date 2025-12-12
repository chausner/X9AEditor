using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace X9AEditor.ViewModels;

class MainViewModel : ViewModel
{
    MainWindow mainWindow;
    bool isFileLoaded;
    string? loadedFilePath;
    X9aFile? x9aFile;

    string? lastSearchTerm;
    int? lastMatchIndex;

    public ObservableCollection<VoiceViewModel> Voices { get; }

    public IList<VoiceViewModel> SelectedVoices { get; set; }

    public RelayCommand<string?> OpenCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand CloseCommand { get; }

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    public RelayCommand CopyCommand { get; }
    public RelayCommand PasteCommand { get; }

    public RelayCommand UndoChangesCommand { get; }
    public RelayCommand ResetToFactorySettingCommand { get; }
    public RelayCommand InitializeCommand { get; }

    public RelayCommand<string> SearchCommand { get; }

    public RelayCommand GitHubCommand { get; }
    public RelayCommand AboutCommand { get; }

    public bool IsFileLoaded
    {
        get => isFileLoaded;
        set => SetProperty(ref isFileLoaded, value);
    }

    public string? LoadedFilePath
    {
        get => loadedFilePath;
        set => SetProperty(ref loadedFilePath, value);
    }

    public X9aFile? X9aFile => x9aFile;

    public MainViewModel(MainWindow mainWindow)
    {
        this.mainWindow = mainWindow;
        Voices = new ObservableCollection<VoiceViewModel>();
        SelectedVoices = Array.Empty<VoiceViewModel>();

        OpenCommand = new RelayCommand<string?>(ExecuteOpenCommand);
        SaveCommand = new RelayCommand(ExecuteSaveCommand, () => IsFileLoaded);
        SaveAsCommand = new RelayCommand(ExecuteSaveAsCommand, () => IsFileLoaded);
        CloseCommand = new RelayCommand(ExecuteCloseCommand);

        MoveUpCommand = new RelayCommand(ExecuteMoveUpCommand, CanExecuteMoveUpCommand);
        MoveDownCommand = new RelayCommand(ExecuteMoveDownCommand, CanExecuteMoveDownCommand);

        CopyCommand = new RelayCommand(ExecuteCopyCommand, () => SelectedVoices.Count > 0);
        PasteCommand = new RelayCommand(ExecutePasteCommand, () => SelectedVoices.Count > 0 && Clipboard.ContainsData("X9AVoice"));

        UndoChangesCommand = new RelayCommand(ExecuteUndoChangesCommand, () => SelectedVoices.Count > 0);
        ResetToFactorySettingCommand = new RelayCommand(ExecuteResetToFactorySettingCommand, () => SelectedVoices.Count > 0);
        InitializeCommand = new RelayCommand(ExecuteInitializeCommand, () => SelectedVoices.Count > 0);

        SearchCommand = new RelayCommand<string>(ExecuteSearchCommand!, _ => IsFileLoaded);

        GitHubCommand = new RelayCommand(ExecuteGitHubCommand);
        AboutCommand = new RelayCommand(ExecuteAboutCommand);
    }

    private void ExecuteOpenCommand(string? path)
    {
        if (path == null)
        {        
            OpenFileDialog openFileDialog = new();

            openFileDialog.Filter = "Yamaha CP88/CP73 X9A files (*.x9a)|*.x9a";

            if (openFileDialog.ShowDialog() != true)
                return;

            path = openFileDialog.FileName;
        }

        LoadFile(path);
    }

    private void LoadFile(string path)
    {
        try
        {
            x9aFile = ParseX9A(path);
        }
        catch (Exception ex)
        {
            TaskDialog taskDialog = new();

            taskDialog.MainIcon = TaskDialogIcon.Error;
            taskDialog.Content = "X9A Editor cannot read this file, most likely because it was created using an unsupported CP88/CP73 firmware version.\r\n\r\nPlease report this issue on <a href=\"https://github.com/chausner/X9AEditor\">GitHub</a>.";
            taskDialog.ExpandedInformation = ex.ToString();
            taskDialog.EnableHyperlinks = true;
            taskDialog.AllowDialogCancellation = true;
            taskDialog.WindowTitle = "Error opening file";
            taskDialog.Buttons.Add(new TaskDialogButton(ButtonType.Ok));
            taskDialog.HyperlinkClicked += (sender, e) => Process.Start(e.Href);

            taskDialog.ShowDialog();
            return;
        }

        IsFileLoaded = true;
        LoadedFilePath = path;

        lastSearchTerm = null;
        lastMatchIndex = null;

        Voices.Clear();
        for (int i = 0; i < x9aFile.Voices.Length; i++)
            Voices.Add(new VoiceViewModel(x9aFile, (X9aFile.Voice)x9aFile.Voices[i].Clone(), (i / 8) + 1, (i % 8) + 1, this));
    }

    private X9aFile ParseX9A(string path)
    {
        X9aFile x9aFile;

        byte[] data = File.ReadAllBytes(path);
        using (MemoryStream memoryStream = new(data, false))
            x9aFile = X9aFile.Parse(path);

        // as a sanity check, we re-encode the parsed file and check that we end up with exactly the same bytes
        // this should give us confidence that the file is in a supported format
        byte[] data2;
        using (MemoryStream memoryStream = new(data.Length))
        {
            x9aFile.Save(memoryStream);
            data2 = memoryStream.ToArray();
        }

        if (!data2.SequenceEqual(data))
            throw new InvalidDataException("Re-encoded X9A is different from input");

        return x9aFile;
    }

    private void ExecuteSaveCommand()
    {
        SaveFile(LoadedFilePath);
    }

    private void ExecuteSaveAsCommand()
    {
        SaveFileDialog saveFileDialog = new();

        saveFileDialog.Filter = "Yamaha CP88/CP73 X9A files (*.x9a)|*.x9a";

        if (saveFileDialog.ShowDialog() != true)
            return;

        SaveFile(saveFileDialog.FileName);
    }

    private void SaveFile(string path)
    {
        for (int i = 0; i < x9aFile.Voices.Length; i++)
            x9aFile.Voices[i] = Voices[i].Voice;

        try
        {
            x9aFile.Save(path);
        }
        catch
        {
            MessageBox.Show("The file could not be saved.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        LoadedFilePath = path;
    }

    private void ExecuteCloseCommand()
    {
        Application.Current.Shutdown();
    }

    private void ExecuteMoveUpCommand()
    {
        VoiceViewModel[] sortedSelectedVoices = SelectedVoices.OrderBy(voice => voice.Index).ToArray();

        foreach (VoiceViewModel voice in sortedSelectedVoices)
        {
            Voices.Move(voice.Index, voice.Index - 1);

            VoiceViewModel voiceAbove = Voices.Single(v => v.Index == voice.Index - 1);

            if (voiceAbove.LiveSetIndex < 8)
                voiceAbove.LiveSetIndex++;
            else
            {
                voiceAbove.LiveSetIndex = 1;
                voiceAbove.LiveSetPage++;
            }

            if (voice.LiveSetIndex > 1)
                voice.LiveSetIndex--;
            else
            {
                voice.LiveSetIndex = 8;
                voice.LiveSetPage--;
            }
        }
    }

    private bool CanExecuteMoveUpCommand()
    {
        return SelectedVoices.Count > 0 && !SelectedVoices.Any(v => v.LiveSetPage == 1 && v.LiveSetIndex == 1);
    }

    private void ExecuteMoveDownCommand()
    {
        VoiceViewModel[] sortedSelectedVoices = SelectedVoices.OrderByDescending(voice => voice.Index).ToArray();

        foreach (VoiceViewModel voice in sortedSelectedVoices)
        {
            Voices.Move(voice.Index, voice.Index + 1);

            VoiceViewModel voiceBelow = Voices.Single(v => v.Index == voice.Index + 1);

            if (voice.LiveSetIndex < 8)
                voice.LiveSetIndex++;
            else
            {
                voice.LiveSetIndex = 1;
                voice.LiveSetPage++;
            }

            if (voiceBelow.LiveSetIndex > 1)
                voiceBelow.LiveSetIndex--;
            else
            {
                voiceBelow.LiveSetIndex = 8;
                voiceBelow.LiveSetPage--;
            }
        }
    }

    private bool CanExecuteMoveDownCommand()
    {
        int lastLiveSetPage = (Voices.Count + 7) / 8;

        return SelectedVoices.Count > 0 && !SelectedVoices.Any(v => v.LiveSetPage == lastLiveSetPage && v.LiveSetIndex == 8);
    }

    private void ExecuteCopyCommand()
    {
        X9aFile.Voice[] voices = SelectedVoices.OrderBy(voice => voice.Index).Select(v => v.Voice).ToArray();

        string serializedVoices = JsonSerializer.Serialize(voices, new JsonSerializerOptions() { IncludeFields = true });

        Clipboard.SetData("X9AVoice", serializedVoices);
    }

    private void ExecutePasteCommand()
    {
        if (!Clipboard.TryGetData<string>("X9AVoice", out var serializedVoices))
            return;

        X9aFile.Voice[] voices = JsonSerializer.Deserialize<X9aFile.Voice[]>(serializedVoices, new JsonSerializerOptions() { IncludeFields = true })!;

        VoiceViewModel? firstSelectedVoice = SelectedVoices.OrderBy(voice => voice.Index).FirstOrDefault();

        if (firstSelectedVoice == null)
            return;

        for (int i = 0; i < voices.Length; i++)
        {
            if (firstSelectedVoice.Index + i >= Voices.Count)
                break;

            Voices[firstSelectedVoice.Index + i].Voice = voices[i];
        }
    }

    private void ExecuteUndoChangesCommand()
    {
        foreach (VoiceViewModel voice in SelectedVoices)
            voice.UndoChangesCommand.Execute(null);
    }

    private void ExecuteResetToFactorySettingCommand()
    {
        foreach (VoiceViewModel voice in SelectedVoices)
            voice.ResetToFactorySettingCommand.Execute(null);
    }

    private void ExecuteInitializeCommand()
    {
        foreach (VoiceViewModel voice in SelectedVoices)
            voice.InitializeCommand.Execute(null);
    }

    private void ExecuteGitHubCommand()
    {
        Process.Start(new ProcessStartInfo("https://github.com/chausner/X9AEditor") { UseShellExecute = true });
    }

    private void ExecuteAboutCommand()
    {
        TaskDialog taskDialog = new();

        taskDialog.MainIcon = TaskDialogIcon.Information;
        taskDialog.MainInstruction = "X9A Editor";
        taskDialog.Content =
            "Version " + Assembly.GetExecutingAssembly().GetName().Version + "\r\n\r\n" +
            "Copyright © Christoph Hausner 2020, 2022, 2024-2025\r\n\r\n" +
            "<a href=\"https://github.com/chausner/X9AEditor\">https://github.com/chausner/X9AEditor</a>";
        taskDialog.EnableHyperlinks = true;
        taskDialog.AllowDialogCancellation = true;
        taskDialog.WindowTitle = "About X9A Editor";
        taskDialog.Buttons.Add(new TaskDialogButton(ButtonType.Ok));
        taskDialog.HyperlinkClicked += (sender, e) => Process.Start(new ProcessStartInfo(e.Href) { UseShellExecute = true });

        taskDialog.ShowDialog();
    }

    private void ExecuteSearchCommand(string searchTerm)
    {
        searchTerm = searchTerm.Trim();

        if (searchTerm.Length == 0)
            return;

        if (!string.Equals(searchTerm, lastSearchTerm, StringComparison.OrdinalIgnoreCase))
            lastMatchIndex = null;

        lastSearchTerm = searchTerm;

        int? matchIndex = FindNextVoiceByName(searchTerm, ((lastMatchIndex ?? -1) + 1) % Voices.Count);

        lastMatchIndex = matchIndex;

        if (matchIndex != null)
        {
            VoiceViewModel match = Voices[matchIndex.Value];

            mainWindow.dataGrid.SelectedItem = match;
            mainWindow.dataGrid.ScrollIntoView(match);
        }
        else
        {
            MessageBox.Show($"No voice matches \"{searchTerm}\".", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        int? FindNextVoiceByName(string searchTerm, int startIndex)
        {
            for (int offset = 0; offset < Voices.Count; offset++)
            {
                int index = (startIndex + offset) % Voices.Count;

                if (Voices[index].Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return index;
            }

            return null;
        }
    }
}
