using System;
using System.Text.RegularExpressions;

namespace X9AEditor.ViewModels;

class VoiceViewModel : ViewModel
{
    readonly X9aFile x9aFile;

    X9aFile.Voice voice;

    public X9aFile.Voice Voice
    {
        get => voice;
        set
        {
            SetProperty(ref voice, value);
            InvokePropertyChanged(nameof(Name));
            InvokePropertyChanged(nameof(IsInitSound));
            InvokePropertyChanged(nameof(IsChanged));
            InvokePropertyChanged(nameof(IsFactorySetting));
        }
    }

    int liveSetPage;
    public int LiveSetPage
    {
        get => liveSetPage;
        set
        {
            SetProperty(ref liveSetPage, value);
            InvokePropertyChanged(nameof(IsChanged));
            InvokePropertyChanged(nameof(IsFactorySetting));
        }
    }

    int liveSetIndex;
    public int LiveSetIndex
    {
        get => liveSetIndex;
        set
        {
            SetProperty(ref liveSetIndex, value);
            InvokePropertyChanged(nameof(IsChanged));
            InvokePropertyChanged(nameof(IsFactorySetting));
        }
    }

    readonly MainViewModel mainViewModel;

    public MainViewModel MainViewModel => mainViewModel;

    public RelayCommand UndoChangesCommand { get; }
    public RelayCommand ResetToFactorySettingCommand { get; }
    public RelayCommand InitializeCommand { get; }        

    public VoiceViewModel(X9aFile x9aFile, X9aFile.Voice voice, int liveSetPage, int liveSetIndex, MainViewModel mainViewModel)
    {
        this.x9aFile = x9aFile;
        this.voice = voice;
        LiveSetPage = liveSetPage;
        LiveSetIndex = liveSetIndex;
        this.mainViewModel = mainViewModel;

        UndoChangesCommand = new RelayCommand(ExecuteUndoChangesCommand);
        ResetToFactorySettingCommand = new RelayCommand(ExecuteResetToFactorySettingCommand);
        InitializeCommand = new RelayCommand(ExecuteInitializeCommand);
    }

    public string Name
    {
        get => Voice.Name;
        set
        {
            if (value != Voice.Name)
            {
                if (value.Length > 15)
                    throw new ArgumentException("Name cannot have more than 15 characters");
                if (!Regex.IsMatch(value, @"^[a-zA-Z 0-9!""#$%&'()*+,\-./;<=>?@\[¥\]^_`{|}~]*$")) // ASCII characters 21..7E without : and replacing \ with ¥
                    throw new ArgumentException(@"Name must consist of the following characters: a-z A-Z <space> 0-9 !""#$%&'()*+,\-./;<=>?@\[¥\]^_`{|}~");

                Voice.Name = value;
                InvokePropertyChanged(nameof(Name));
                InvokePropertyChanged(nameof(IsInitSound));
                InvokePropertyChanged(nameof(IsChanged));
                InvokePropertyChanged(nameof(IsFactorySetting));
            }
        }
    }

    public bool IsInitSound => Voice.Equals(FactorySetting.InitSound);

    public bool IsChanged => !Voice.Equals(x9aFile.Voices[Index]);

    public bool IsFactorySetting => Voice.Equals(FactorySetting.Instance.Voices[Index]);

    public int Index => (LiveSetPage - 1) * 8 + (LiveSetIndex - 1);

    private void ExecuteUndoChangesCommand()
    {
        Voice = (X9aFile.Voice)x9aFile.Voices[Index].Clone();
    }

    private void ExecuteResetToFactorySettingCommand()
    {
        Voice = (X9aFile.Voice)FactorySetting.Instance.Voices[Index].Clone();
    }

    private void ExecuteInitializeCommand()
    {
        Voice = (X9aFile.Voice)FactorySetting.InitSound.Clone();
    }
}
