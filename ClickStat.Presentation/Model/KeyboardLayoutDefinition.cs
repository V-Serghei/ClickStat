namespace ClickStat.Presentation.Model;

public static class KeyboardLayoutDefinition
{
    public static readonly HashSet<string> StandardKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Escape",
        "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
        "Oemtilde","D1","D2","D3","D4","D5","D6","D7","D8","D9","D0",
        "OemMinus","Oemplus","Back",
        "Tab","Q","W","E","R","T","Y","U","I","O","P","Oem4","Oem6","OemPipe",
        "Capital","A","S","D","F","G","H","J","K","L","OemSemicolon","OemQuotes","Enter",
        "LShiftKey","Z","X","C","V","B","N","M","Oemcomma","OemPeriod","Oem2","RShiftKey",
        "LControlKey","LWin","LMenu","Space","RMenu","RControlKey",
        "PrintScreen","LaunchApplication2","MediaStop",
        "Insert","Home","PageUp","Delete","End","Next",
        "Up","Left","Down","Right",
        "MediaPreviousTrack","MediaPlayPause","MediaNextTrack",
        "VolumeMute","VolumeDown","VolumeUp",
        "Clear","NumLock","Divide","Multiply","Subtract",
        "NumPad7","NumPad8","NumPad9",
        "NumPad4","NumPad5","NumPad6",
        "NumPad1","NumPad2","NumPad3",
        "NumPad0","Decimal","Add"
    };
}
