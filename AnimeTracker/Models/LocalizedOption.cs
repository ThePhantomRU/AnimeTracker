namespace AnimeTracker.Models
{
    // Элемент локализованного выпадающего списка.
    //
    // Key - канонический ключ значения (мы используем русское название
    // жанра/статуса как канонический ключ - см. MainViewModel.GetRussianGenre/
    // GetRussianStatus, они уже используются по всему проекту как единая
    // точка нормализации). Key НИКОГДА не меняется при переключении языка
    // интерфейса.
    //
    // Display - то, что реально показывается в ComboBox для текущего языка
    // интерфейса (например Key="Фэнтези", Display="Fantasy" на английском).
    //
    // Идея: ComboBox привязывается через SelectedValuePath="Key" и
    // SelectedValue="{Binding ...Key}", а не через SelectedItem. WPF ищет
    // элемент в ItemsSource, чьё свойство Key совпадает со значением
    // SelectedValue - по значению, а не по ссылке на объект. Поэтому даже
    // ПОЛНАЯ замена коллекции ItemsSource (при пересчёте списка на новом
    // языке) не сбрасывает выбор: пока в новом списке есть элемент с тем же
    // Key, WPF сам находит его и корректно подсвечивает. А поскольку Key -
    // это фиксированное каноническое (русское) значение, которое не
    // переводится и не меняется при смене языка, его вообще не нужно
    // "конвертировать" при переключении - меняется только Display.
    public class LocalizedOption
    {
        public string Key { get; set; } = "";
        public string Display { get; set; } = "";

        public override bool Equals(object? obj) => obj is LocalizedOption other && other.Key == Key;
        public override int GetHashCode() => Key.GetHashCode();

        // ВАЖНО: без этого переопределения Object.ToString() возвращает
        // полное имя типа с namespace ("AnimeTracker.Models.LocalizedOption").
        // Если кастомный стиль ComboBox (RoundedComboBox) отображает выбранное
        // значение через привязку напрямую к объекту (SelectedItem/SelectedValue)
        // вместо TemplateBinding к SelectionBoxItem с учётом DisplayMemberPath,
        // именно ToString() и попадает на экран. Возвращая здесь Display,
        // мы гарантируем правильный текст независимо от того, как именно
        // устроен шаблон стиля.
        public override string ToString() => Display;
    }
}