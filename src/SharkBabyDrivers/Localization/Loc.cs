using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

namespace SharkBabyDrivers.Localization;

/// <summary>Runtime-switchable strings. Bind with {l:T key}; switching <see cref="Language"/> updates every binding.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    private string _language = "pt-BR";
    public event PropertyChangedEventHandler? PropertyChanged;

    public static readonly IReadOnlyList<(string Code, string Name)> Languages = [("pt-BR", "Português"), ("en", "English")];

    public string Language
    {
        get => _language;
        set
        {
            if (_language == value || !Strings.Table.ContainsKey(value)) return;
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        }
    }

    public string this[string key] =>
        Strings.Table[_language].TryGetValue(key, out var v) ? v
        : Strings.Table["pt-BR"].TryGetValue(key, out var pt) ? pt
        : key;

    public static string T(string key) => I[key];
    public static string F(string key, params object[] args) => string.Format(I[key], args);
}

/// <summary>XAML: Text="{l:T dpi_title}".</summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
