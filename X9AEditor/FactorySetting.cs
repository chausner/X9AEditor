using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace X9AEditor;

static class FactorySetting
{
    public static X9aFile Instance;

    public static X9aFile.Voice InitSound;

    static FactorySetting()
    {
        using (Stream stream = Application.GetResourceStream(new Uri(@"Assets\FactorySetting-1.60.X9A", UriKind.Relative)).Stream)
            Instance = X9aFile.Parse(stream);

        InitSound = Instance.Voices.Last();
    }
}
