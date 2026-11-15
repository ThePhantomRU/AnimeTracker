using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace AnimeTracker.Models
{
    public class AdditionalGenreItem : INotifyPropertyChanged
    {
        private int _index;
        public int Index
        {
            get => _index;
            set { _index = value; OnPropertyChanged(); }
        }

        // Канонический (русский) ключ выбранного доп. жанра - см. подробный
        // комментарий в LocalizedOption.cs. Он НЕ переводится и не меняется
        // при переключении языка интерфейса, поэтому ComboBox, привязанный
        // через SelectedValue="{Binding SelectedGenreKey}" +
        // SelectedValuePath="Key", не теряет выбор при смене языка - меняется
        // только отображаемый текст в AvailableGenreOptions.
        private string? _selectedGenreKey;
        public string? SelectedGenreKey
        {
            get => _selectedGenreKey;
            set { _selectedGenreKey = value; OnPropertyChanged(); }
        }

        // Обычный List<LocalizedOption>, заменяемый целиком (один
        // PropertyChanged на обновление), а не ObservableCollection с
        // точечными Clear()/Add(). SelectedValue/SelectedValuePath по
        // задумке WPF переживает полную замену ItemsSource - при каждом
        // обновлении списка WPF заново ищет элемент с Key, равным текущему
        // SelectedGenreKey, и сам восстанавливает выделение. Это надёжнее,
        // чем Clear()+Add() по одному элементу, который на время Clear()
        // оставляет коллекцию пустой.
        private List<LocalizedOption> _availableGenreOptions = new();
        public List<LocalizedOption> AvailableGenreOptions
        {
            get => _availableGenreOptions;
            set { _availableGenreOptions = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}