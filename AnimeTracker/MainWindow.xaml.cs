using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;
using AnimeTracker.Views;
using LiveCharts.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AnimeTracker
{
    /// <summary>
    /// Главное окно приложения: боковая панель навигации, страницы-вкладки (План, Оценка, Топ, Статистика,
    /// Инструкция, Настройки, О программе), экспорт/импорт данных, «Поделиться» и оформление окна.
    /// Бизнес-логика находится в <see cref="MainViewModel"/>; здесь — только код, зависящий от визуального дерева.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>Единственный экземпляр главного окна (используется моделью представления).</summary>
        public static MainWindow? Instance { get; private set; }

        private Rect _previousWindowRect;
        private Views.CriteriaHelpWindow? _criteriaHelpWindow;
        private bool _isSidebarExpanded = true;
        private bool _updatingTabFromViewModel = false;

        private readonly Dictionary<string, string> _tabEmoji = new()
        {
            { "Plan", "📋" },
            { "Rating", "⭐" },
            { "Top", "🏆" },
            { "Stats", "📊" },
            { "Instructions", "📘" },
            { "Settings", "⚙" },
            { "About", "ℹ" }
        };

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;

            RatingTab.SetBinding(RadioButton.IsEnabledProperty, new Binding("HasAnimeForRating"));
            RatingTab.UpdateLayout();

            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                this.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath));
            }

            StateChanged += MainWindow_StateChanged;
            Loaded += OnMainWindowLoaded;
            Closed += MainWindow_Closed;
            LocalizationManager.LanguageChanged += OnLanguageChanged;

            // Обновление Clip для скругления при изменении размера
            MainBorder.SizeChanged += (s, e) => UpdateMainBorderClip();
        }

        /// <summary>Обновляет всё, что не обновляется само при смене языка: список тем, кнопки меню, таблицы и заголовки колонок.</summary>
        private void OnLanguageChanged()
        {
            PopulateThemeComboBox();
            UpdateNavigationButtons(_isSidebarExpanded);
            RefreshPlanGrid();
            RefreshTopGrid();
            UpdateDataGridHeaders();
            if (DataContext is MainViewModel vm && vm.SelectedForRating != null)
                PopulateRatingTabControl(vm.SelectedForRating);
        }

        private void RefreshTopGrid() => RefreshDataGrid(TopDataGrid);

        /// <summary>
        /// Принудительно пересоздаёт строки таблицы: переустанавливает источник данных и обновляет представление.
        /// Нужно после смены языка, когда тексты в ячейках зависят от конвертеров, а не от изменения свойств.
        /// </summary>
        private void RefreshDataGrid(DataGrid? grid)
        {
            if (grid == null) return;

            Dispatcher.Invoke(() =>
            {
                var source = grid.ItemsSource;
                grid.ItemsSource = null;
                grid.ItemsSource = source;
                grid.UpdateLayout();
                grid.InvalidateVisual();

                if (source != null)
                {
                    var view = CollectionViewSource.GetDefaultView(source);
                    view?.Refresh();
                }
            }, DispatcherPriority.Background);
        }

        private void UpdateDataGridHeaders()
        {
            UpdateDataGridHeaders(PlanDataGrid);
            UpdateDataGridHeaders(TopDataGrid);
        }

        private void UpdateDataGridHeaders(DataGrid grid)
        {
            if (grid == null) return;
            foreach (var column in grid.Columns)
            {
                string key = ColumnExtensions.GetResourceKey(column);
                if (!string.IsNullOrEmpty(key))
                {
                    var resource = Application.Current.FindResource(key);
                    if (resource != null)
                        column.Header = resource;
                }
            }
        }

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            var vm = (MainViewModel)DataContext!;

            // Смена статуса аниме прямо в таблице: плавно показываем, куда оно переместилось
            vm.PlanAnimeMoved += OnPlanAnimeMoved;

            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedTabIndex))
                {
                    Dispatcher.Invoke(() => SetActiveTabByIndex(vm.SelectedTabIndex));
                }
                if (args.PropertyName == nameof(MainViewModel.SelectedForRating))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (vm.SelectedForRating != null)
                        {
                            RefreshRatingPageDataContext();
                            UpdateRatingPageBindings();
                        }
                    });
                }
                if (args.PropertyName is nameof(MainViewModel.StatusSeries)
                    or nameof(MainViewModel.YearTrendSeries))
                {
                    ReapplyRoundedCornersToAllCharts();
                }
            };

            Keyboard.ClearFocus();

            try
            {
                PopulateThemeComboBox();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки тем: {ex.Message}");
            }

            SyncLanguageComboBox();

            if (vm.HasAnimeForRating)
                PopulateRatingTabControl(vm.SelectedForRating!);

            // Разворачиваем окно после полной инициализации
            Dispatcher.BeginInvoke(new Action(() =>
            {
                WindowState = WindowState.Maximized;
            }), DispatcherPriority.ApplicationIdle);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RefreshPlanGrid();
            }), DispatcherPriority.Background);

            // Важно: применяем Clip сразу после загрузки
            UpdateMainBorderClip();
        }

        private void PopulateThemeComboBox()
        {
            if (ThemeComboBox == null) return;

            ThemeComboBox.Items.Clear();
            foreach (var key in App.ThemeKeys)
            {
                string resourceKey = $"Theme_{key}";
                string displayName = Application.Current.FindResource(resourceKey) as string ?? key;
                var item = new ComboBoxItem
                {
                    Content = displayName,
                    Tag = key
                };
                ThemeComboBox.Items.Add(item);
            }
            SyncThemeComboBox();
        }

        public void SyncThemeComboBox()
        {
            if (ThemeComboBox == null) return;
            string currentKey = App.CurrentThemeKey;
            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag as string == currentKey)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        /// <summary>Принудительно обновляет таблицу вкладки «План» (см. <see cref="RefreshDataGrid"/>).</summary>
        public void RefreshPlanGrid() => RefreshDataGrid(PlanDataGrid);

        /// <summary>Переключает вкладку по индексу (вызывается из модели представления). Защита от повторного входа не даёт сработать циклу уведомлений.</summary>
        public void SetActiveTabByIndex(int index)
        {
            if (_updatingTabFromViewModel) return;
            _updatingTabFromViewModel = true;
            RadioButton? target = index switch
            {
                0 => PlanTab,
                1 => RatingTab,
                2 => TopTab,
                3 => StatsTab,
                4 => InstructionsTab,
                5 => SettingsTab,
                6 => AboutTab,
                _ => null
            };
            if (target != null && !target.IsChecked.GetValueOrDefault())
            {
                target.IsChecked = true;
            }
            _updatingTabFromViewModel = false;

            if (index == 1)
            {
                var vm = DataContext as MainViewModel;
                if (vm?.SelectedForRating != null)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        PopulateRatingTabControl(vm.SelectedForRating!);
                        RefreshRatingPageDataContext();
                        UpdateRatingPageBindings();
                    }, DispatcherPriority.Background);
                }
            }
        }

        /// <summary>
        /// Строит вкладки оценивания: по одной вкладке на каждый сезон и фильм выбранного аниме.
        /// OVA и ONA не оцениваются (они только указываются в составе тайтла); исключение — тайтл без
        /// сезонов и фильмов, у которого оценивать больше нечего (см. <see cref="Anime.OvaAndOnaAreRated"/>).
        /// </summary>
        public void PopulateRatingTabControl(Anime? anime)
        {
            RatingPartsTabControl.Items.Clear();
            if (anime == null) return;

            var parts = new List<PartViewModel>();
            int idx = 1;
            string seasonLabel = MainViewModel.GetLocalizedString("Part_Season");
            string movieLabel = MainViewModel.GetLocalizedString("Part_Movie");
            string ovaLabel = MainViewModel.GetLocalizedString("Part_OVA");
            string onaLabel = MainViewModel.GetLocalizedString("Part_ONA");

            foreach (var s in anime.Seasons)
                parts.Add(new PartViewModel { Part = s, DisplayName = $"{seasonLabel} {idx++}" });
            idx = 1;
            foreach (var m in anime.Movies)
                parts.Add(new PartViewModel { Part = m, DisplayName = $"{movieLabel} {idx++}" });
            if (anime.OvaAndOnaAreRated)
            {
                idx = 1;
                foreach (var o in anime.Ovas)
                    parts.Add(new PartViewModel { Part = o, DisplayName = $"{ovaLabel} {idx++}" });
                idx = 1;
                foreach (var o in anime.Onas)
                    parts.Add(new PartViewModel { Part = o, DisplayName = $"{onaLabel} {idx++}" });
            }

            if (parts.Count == 0)
            {
                var dummyPanel = new TextBlock
                {
                    Text = MainViewModel.GetLocalizedString("Rating_NoParts"),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var tabItem = new TabItem { Header = "Нет данных", Content = dummyPanel };
                RatingPartsTabControl.Items.Add(tabItem);
                return;
            }

            foreach (var p in parts)
            {
                var panel = new PartRatingPanel { DataContext = p.Part };
                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Content = panel
                };
                var tabItem = new TabItem { Header = p.DisplayName, Content = scrollViewer };
                RatingPartsTabControl.Items.Add(tabItem);
            }

            if (RatingPartsTabControl.Items.Count > 0)
                RatingPartsTabControl.SelectedIndex = 0;

            RatingPartsTabControl.UpdateLayout();
        }

        public void PopulateRatingTabControl()
        {
            var vm = DataContext as MainViewModel;
            PopulateRatingTabControl(vm?.SelectedForRating);
        }

        public void RefreshRatingPageDataContext()
        {
            var currentContext = RatingPage.DataContext;
            RatingPage.DataContext = null;
            RatingPage.DataContext = currentContext;
            RatingPage.UpdateLayout();
        }

        private void UpdateRatingPageBindings()
        {
            foreach (var tb in FindVisualChildren<TextBlock>(RatingPage))
            {
                var expr = BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty);
                expr?.UpdateTarget();
            }
            RatingPage.UpdateLayout();
        }

        public void ShowRatingForAnime(Anime anime)
        {
            if (anime == null) return;

            this.Activate();
            this.Focus();

            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            vm.SelectedForRating = anime;
            vm.CurrentRatingAnime = anime;
            vm.OnPropertyChanged(nameof(vm.SelectedForRating));
            vm.OnPropertyChanged(nameof(vm.CurrentRatingAnime));
            vm.OnPropertyChanged(nameof(vm.HasAnimeForRating));

            vm.SelectedTabIndex = 1;
            RatingTab.IsChecked = true;

            PopulateRatingTabControl(anime);
            RefreshRatingPageDataContext();
            UpdateRatingPageBindings();

            RatingPage.IsEnabled = true;
            RatingTab.IsEnabled = true;
            RatingPage.UpdateLayout();

            CommandManager.InvalidateRequerySuggested();
        }

        public void ClearRatingTabControl()
        {
            RatingPartsTabControl.Items.Clear();
        }

        // ===================== ОСТАЛЬНЫЕ МЕТОДЫ =====================

        private void TableCard_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Border card)
                ApplyCardClip(card, e.NewSize.Width, e.NewSize.Height);
        }

        private void TableCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border card)
                ApplyCardClip(card, card.ActualWidth, card.ActualHeight);
        }

        private void ApplyCardClip(Border card, double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                card.Clip = null;
                return;
            }

            double radius = card.CornerRadius.TopLeft > 0 ? card.CornerRadius.TopLeft : 0;
            card.Clip = new RectangleGeometry(new Rect(0, 0, width, height))
            {
                RadiusX = radius,
                RadiusY = radius
            };
        }

        /// <summary>Прокрутка колесом мыши для таблиц. Не вмешивается, если событие пришло из открытого выпадающего списка.</summary>
        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Если колесо крутят над открытым выпадающим списком (ComboBox) — не перехватываем событие,
            // чтобы список прокручивался сам, а не таблица под ним.
            if (e.OriginalSource is DependencyObject source && IsInsideOpenComboBox(source))
                return;

            if (sender is FrameworkElement fe)
            {
                var sv = fe as ScrollViewer ?? FindVisualChild<ScrollViewer>(fe);
                if (sv != null)
                {
                    // Плавная прокрутка с накоплением: быстрые щелчки колеса складываются в одно плавное движение
                    SmoothScroller.ScrollBy(sv, -e.Delta * 0.6);
                    e.Handled = true;
                }
            }
        }

        private static bool IsInsideOpenComboBox(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is ComboBox cb)
                    return cb.IsDropDownOpen;

                // Содержимое Popup живёт в отдельном визуальном дереве, поэтому при необходимости
                // поднимаемся по логическому дереву (Popup -> шаблон -> ComboBox).
                DependencyObject? parent = null;
                if (element is System.Windows.Media.Visual || element is System.Windows.Media.Media3D.Visual3D)
                    parent = VisualTreeHelper.GetParent(element);
                parent ??= LogicalTreeHelper.GetParent(element);
                if (parent == null && element is FrameworkElement fe)
                    parent = fe.TemplatedParent;
                element = parent;
            }
            return false;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) yield return t;
                foreach (var nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }

        #region Share
        private bool _isSharing;

        private static string ShareText(string key, string fallback)
            => Application.Current.TryFindResource(key) as string ?? fallback;

        private async void SharePlan_Click(object sender, RoutedEventArgs e) => await ShareListAsync(PlanDataGrid, isTop: false);

        private async void ShareTop_Click(object sender, RoutedEventArgs e) => await ShareListAsync(TopDataGrid, isTop: true);

        /// <summary>
        /// Генерирует изображение(я) из ТЕКУЩЕГО содержимого таблицы (с учётом поиска и сортировки) и сохраняет на диск.
        /// Очень длинный список автоматически делится на несколько картинок.
        /// </summary>
        private async Task ShareListAsync(DataGrid grid, bool isTop)
        {
            if (_isSharing) return;

            var items = grid.Items.OfType<Anime>().ToList();
            if (items.Count == 0)
            {
                MessageBox.Show(ShareText("Msg_ShareEmpty", "Список пуст — делиться нечем."), "Anime Tracker",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = ShareText("Share_SaveDialogTitle", "Сохранить изображение"),
                FileName = $"AnimeTracker_{(isTop ? "Top" : "Plan")}_{DateTime.Now:yyyy-MM-dd}",
                DefaultExt = ".png",
                AddExtension = true,
                OverwritePrompt = true,
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg"
            };
            if (dialog.ShowDialog(this) != true) return;

            _isSharing = true;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                // Даём интерфейсу отрисовать курсор ожидания перед тяжёлой работой
                await Dispatcher.Yield(DispatcherPriority.Background);

                var job = ShareImageService.Create(items, isTop, FontFamily);
                for (int i = 0; i < job.PageCount; i++)
                {
                    // Рендер — в UI-потоке, кодирование и запись на диск — в фоне; страницы идут по одной (экономия памяти)
                    var bitmap = job.RenderPage(i);
                    string path = ShareImageService.BuildPartPath(dialog.FileName, i, job.PageCount);
                    await Task.Run(() => ShareImageService.SaveBitmap(bitmap, path));
                }
                // Успешное сохранение никак не комментируется: пользователь сам выбрал, куда сохранять.
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show(string.Format(ShareText("Msg_ShareError", "Не удалось создать изображение: {0}"), ex.Message),
                    "Anime Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isSharing = false;
            }
        }
        #endregion

        #region Sidebar
        private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e)
        {
            _isSidebarExpanded = !_isSidebarExpanded;
            ApplySidebarState(_isSidebarExpanded);
        }

        private void SaveRatingButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (TabItem tab in RatingPartsTabControl.Items)
            {
                if (tab.Content is ScrollViewer sv && sv.Content is PartRatingPanel panel)
                {
                    panel.UpdateAllBindings();
                }
            }

            RatingPage.UpdateLayout();
            RatingPartsTabControl.UpdateLayout();

            var vm = (MainViewModel)DataContext;
            if (vm.AddToTopCommand.CanExecute(null))
                vm.AddToTopCommand.Execute(null);
        }

        // Номер последней анимации боковой панели — позволяет корректно обработать быстрые повторные нажатия.
        private int _sidebarAnimationVersion;

        /// <summary>
        /// Разворачивает/сворачивает боковую панель плавной анимацией ширины.
        /// <para>
        /// ОПТИМИЗАЦИЯ: анимация ширины панели меняет раскладку на КАЖДОМ кадре, и вместе с ней на каждом
        /// кадре пересчитывалась раскладка всей контентной области (таблицы с сотнями строк, диаграммы).
        /// Поэтому на время анимации ширина области страниц фиксируется на итоговом значении:
        /// тяжёлая раскладка выполняется один раз, а панель скользит плавно.
        /// </para>
        /// </summary>
        private void ApplySidebarState(bool expanded)
        {
            double targetWidth = expanded ? 280 : 60;
            var anim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            // Обновляем стили и контент заранее, а не во время анимации
            UpdateNavigationButtons(expanded);
            UpdateSearchVisibility();
            ToggleSidebarButton.Content = expanded ? "☰" : "✕";

            // Фиксируем итоговую ширину области страниц на время анимации
            int version = ++_sidebarAnimationVersion;
            if (PageContainer.Parent is FrameworkElement contentHost)
            {
                double totalWidth = SidebarColumn.ActualWidth + contentHost.ActualWidth;
                double finalContentWidth = totalWidth - targetWidth;
                if (finalContentWidth > 0)
                {
                    PageContainer.HorizontalAlignment = HorizontalAlignment.Left;
                    PageContainer.Width = finalContentWidth;
                }
            }

            anim.Completed += (s, e) =>
            {
                // Если за это время запущена новая анимация — она сама снимет фиксацию по окончании
                if (version != _sidebarAnimationVersion) return;
                PageContainer.ClearValue(FrameworkElement.WidthProperty);
                PageContainer.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
            };

            SidebarBorder.BeginAnimation(Border.WidthProperty, anim);
        }

        // ОПТИМИЗИРОВАННЫЙ МЕТОД: Стиль берется только один раз и применяется, только если он изменился.
        // Это убирает фризы при сворачивании панели.
        // Соответствие «ключ вкладки → ключ строки локализации» (создаётся один раз, а не при каждом вызове)
        private static readonly Dictionary<string, string> NavResourceMap = new()
        {
            { "Plan", "Tab_Plan" },
            { "Rating", "Tab_Rating" },
            { "Top", "Tab_Top" },
            { "Stats", "Tab_Statistics" },
            { "Instructions", "Tab_Instructions" },
            { "Settings", "Tab_Settings" },
            { "About", "Tab_About" }
        };

        private void UpdateNavigationButtons(bool expanded)
        {
            Style targetStyle = (Style)FindResource(expanded ? "NavRadioButton" : "NavRadioButtonCollapsed");
            var resourceMap = NavResourceMap;

            foreach (var child in NavigationStack.Children)
            {
                if (child is RadioButton radio)
                {
                    string? key = null;
                    if (radio == PlanTab) key = "Plan";
                    else if (radio == RatingTab) key = "Rating";
                    else if (radio == TopTab) key = "Top";
                    else if (radio == StatsTab) key = "Stats";
                    else if (radio == InstructionsTab) key = "Instructions";
                    else if (radio == SettingsTab) key = "Settings";
                    else if (radio == AboutTab) key = "About";

                    if (key != null)
                    {
                        // Меняем стиль только если он отличается, чтобы не пересоздавать шаблоны
                        if (radio.Style != targetStyle)
                            radio.Style = targetStyle;

                        if (expanded)
                        {
                            string resourceKey = resourceMap[key];
                            string localizedText = Application.Current.FindResource(resourceKey) as string ?? key;
                            radio.Content = localizedText;
                        }
                        else
                        {
                            radio.Content = _tabEmoji[key];
                        }
                    }
                }
            }
        }

        private void UpdateSearchVisibility()
        {
            bool isPlan = PlanTab.IsChecked == true;
            bool isTop = TopTab.IsChecked == true;
            bool isPlanOrTop = isPlan || isTop;

            if (_isSidebarExpanded)
            {
                SearchPanel.Visibility = Visibility.Visible;
                SearchIconButton.Visibility = Visibility.Collapsed;

                if (isPlan)
                {
                    SearchTextBoxPlan.Visibility = Visibility.Visible;
                    if (SearchTextBoxTop != null) SearchTextBoxTop.Visibility = Visibility.Collapsed;
                }
                else if (isTop)
                {
                    SearchTextBoxPlan.Visibility = Visibility.Collapsed;
                    if (SearchTextBoxTop != null) SearchTextBoxTop.Visibility = Visibility.Visible;
                }
                else
                {
                    SearchTextBoxPlan.Visibility = Visibility.Collapsed;
                    if (SearchTextBoxTop != null) SearchTextBoxTop.Visibility = Visibility.Collapsed;
                }

                SearchPanel.IsEnabled = isPlanOrTop;
                SearchPanel.Opacity = isPlanOrTop ? 1.0 : 0.5;
            }
            else
            {
                SearchPanel.Visibility = Visibility.Collapsed;
                SearchIconButton.Visibility = Visibility.Visible;
                SearchIconButton.IsEnabled = isPlanOrTop;
                SearchIconButton.Opacity = isPlanOrTop ? 1.0 : 0.4;
            }
        }

        private void SearchIconButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSidebarExpanded)
            {
                _isSidebarExpanded = true;
                ApplySidebarState(true);
                if (PlanTab.IsChecked == true)
                    SearchTextBoxPlan.Focus();
                else if (TopTab.IsChecked == true && SearchTextBoxTop != null)
                    SearchTextBoxTop.Focus();
            }
        }
        #endregion

        #region Navigation
        /// <summary>Переключение страниц: плавно скрывает текущую (fade + сдвиг) и показывает выбранную.</summary>
        private void NavRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (PlanPage == null) return;

            int index = 0;
            if (sender == PlanTab) index = 0;
            else if (sender == RatingTab) index = 1;
            else if (sender == TopTab) index = 2;
            else if (sender == StatsTab) index = 3;
            else if (sender == InstructionsTab) index = 4;
            else if (sender == SettingsTab) index = 5;
            else if (sender == AboutTab) index = 6;

            var vm = (MainViewModel)DataContext;
            if (vm != null) vm.SelectedTabIndex = index;

            if (sender == StatsTab && vm != null)
            {
                vm.UpdateCharts();
            }

            FrameworkElement? currentPage = null;
            if (PlanPage.Visibility == Visibility.Visible) currentPage = PlanPage;
            else if (RatingPage.Visibility == Visibility.Visible) currentPage = RatingPage;
            else if (TopPage.Visibility == Visibility.Visible) currentPage = TopPage;
            else if (StatsPage.Visibility == Visibility.Visible) currentPage = StatsPage;
            else if (InstructionsPage.Visibility == Visibility.Visible) currentPage = InstructionsPage;
            else if (SettingsPage.Visibility == Visibility.Visible) currentPage = SettingsPage;
            else if (AboutPage.Visibility == Visibility.Visible) currentPage = AboutPage;

            if (currentPage != null)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
                var slideOut = new DoubleAnimation(0, 10, TimeSpan.FromMilliseconds(100));
                Storyboard.SetTarget(slideOut, currentPage);
                Storyboard.SetTargetProperty(slideOut, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
                fadeOut.Completed += (s, ev) =>
                {
                    // Collapsed (а не Hidden): скрытая страница полностью исключается из раскладки.
                    // Hidden оставлял все страницы (таблицы, диаграммы) в layout, и они пересчитывались при каждом ресайзе.
                    currentPage.Visibility = Visibility.Collapsed;
                    if (currentPage.RenderTransform is TranslateTransform tt) tt.Y = 0;
                };
                currentPage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                if (currentPage.RenderTransform is TranslateTransform ct) ct.BeginAnimation(TranslateTransform.YProperty, slideOut);
            }

            FrameworkElement? newPage = null;
            if (sender == PlanTab) newPage = PlanPage;
            else if (sender == RatingTab) newPage = RatingPage;
            else if (sender == TopTab) newPage = TopPage;
            else if (sender == StatsTab) newPage = StatsPage;
            else if (sender == InstructionsTab) newPage = InstructionsPage;
            else if (sender == SettingsTab) newPage = SettingsPage;
            else if (sender == AboutTab) newPage = AboutPage;

            if (newPage != null)
            {
                newPage.Visibility = Visibility.Visible;
                newPage.Opacity = 0;
                if (newPage.RenderTransform is TranslateTransform nt) nt.Y = -10;

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                var slideIn = new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(200));
                newPage.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                if (newPage.RenderTransform is TranslateTransform nt2) nt2.BeginAnimation(TranslateTransform.YProperty, slideIn);

                ResetScrollPosition(newPage);
            }

            UpdateSearchVisibility();
        }
        #endregion

        #region Search
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchPlaceholder != null)
                SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchTextBoxPlan.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
        }
        #endregion

        #region Window chrome
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) MaximizeRestore_Click(sender, e);
            else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            var vm = (MainViewModel)DataContext;
            if (vm != null)
            {
                vm.HandlePendingRating();
                vm.SaveDataToFile();
            }
            Application.Current.Shutdown();
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                if (_previousWindowRect.Width == 0 || _previousWindowRect.Height == 0)
                {
                    _previousWindowRect = new Rect(Left, Top, 1350, 850);
                }
                WindowState = WindowState.Normal;
                Left = _previousWindowRect.Left;
                Top = _previousWindowRect.Top;
                Width = _previousWindowRect.Width;
                Height = _previousWindowRect.Height;
            }
            else
            {
                _previousWindowRect = new Rect(Left, Top, Width, Height);
                WindowState = WindowState.Maximized;
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Left;
                Top = workArea.Top;
                Width = workArea.Width;
                Height = workArea.Height;

                MainBorder.CornerRadius = new CornerRadius(0);
                MainBorder.Effect = null;
            }
            else
            {
                MainBorder.CornerRadius = new CornerRadius(20);
                MainBorder.Effect = (DropShadowEffect)FindResource("WindowShadow");
            }
            UpdateMainBorderClip();
        }

        // Метод для обрезки содержимого по скруглению (исправляет углы)
        private void UpdateMainBorderClip()
        {
            double radius = MainBorder.CornerRadius.TopLeft;
            if (radius > 0 && MainBorder.ActualWidth > 0 && MainBorder.ActualHeight > 0)
            {
                MainBorder.Clip = new RectangleGeometry(new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight), radius, radius);
            }
            else
            {
                MainBorder.Clip = null;
            }
        }
        #endregion

        #region Event Handlers
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var vm = (MainViewModel)DataContext!;
            if (vm != null)
            {
                vm.HandlePendingRating();
                vm.SaveDataToFile();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var vm = (MainViewModel)DataContext;
                if (vm.SelectedTabIndex == 2)
                {
                    if (!_isSidebarExpanded)
                    {
                        _isSidebarExpanded = true;
                        ApplySidebarState(true);
                    }
                    if (SearchTextBoxTop != null)
                    {
                        SearchTextBoxTop.Visibility = Visibility.Visible;
                        SearchTextBoxTop.Focus();
                    }
                }
                else
                {
                    vm.SelectedTabIndex = 0;
                    if (!_isSidebarExpanded)
                    {
                        _isSidebarExpanded = true;
                        ApplySidebarState(true);
                    }
                    SearchTextBoxPlan.Focus();
                }
                e.Handled = true;
            }
        }

        private async void AddAnime_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = new Views.AddAnimeWindow { Owner = this };
            if (addWindow.ShowDialog() == true)
            {
                var anime = addWindow.AddedAnime;
                if (anime != null)
                {
                    var vm = (MainViewModel)DataContext;
                    vm.RefreshPlanView();
                    vm.UpdateAllStatistics();
                    vm.SaveDataToFile();
                    RefreshPlanGrid();

                    if (vm.PlanList.Contains(anime))
                    {
                        await HighlightAnime(anime);
                    }
                }
            }
        }

        // Номер последнего запроса подсветки: если пользователь успел запустить новую, старая прерывается
        private int _highlightVersion;

        /// <summary>
        /// Реакция на смену статуса или редактирование аниме в таблице «План»: после перестроения списка аниме
        /// оказывается в другом месте (например, «Смотрю» поднимается вверх, а переименованное — по алфавиту),
        /// поэтому плавно прокручиваем к нему и подсвечиваем.
        /// </summary>
        private async void OnPlanAnimeMoved(Anime anime)
        {
            try
            {
                if (!PlanDataGrid.IsVisible) return; // пользователь не на вкладке «План»

                // Ждём, пока завершится редактирование ячейки и таблица перестроится
                await Dispatcher.Yield(DispatcherPriority.Background);
                PlanDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

                var vm = (MainViewModel)DataContext;
                if (vm.PlanList.Contains(anime))
                    await HighlightAnime(anime);
            }
            catch (Exception ex)
            {
                Logger.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Измеряет, до какой позиции нужно прокрутить таблицу, чтобы строка аниме оказалась по центру окна просмотра.
        /// Если строка ещё не создана (виртуализация), при <paramref name="bringIntoView"/> её сначала подводят в видимую область.
        /// Возвращает null, если строку получить не удалось.
        /// </summary>
        private double? MeasureCenteredOffset(Anime anime, ScrollViewer scrollViewer, bool bringIntoView)
        {
            var row = PlanDataGrid.ItemContainerGenerator.ContainerFromItem(anime) as DataGridRow;
            if (row == null || bringIntoView)
            {
                PlanDataGrid.ScrollIntoView(anime);
                PlanDataGrid.UpdateLayout();
                row = PlanDataGrid.ItemContainerGenerator.ContainerFromItem(anime) as DataGridRow;
                if (row == null) return null;
            }

            double rowTop = row.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
            double target = scrollViewer.VerticalOffset + rowTop
                            - (scrollViewer.ViewportHeight / 2) + (row.ActualHeight / 2);
            return Math.Max(0, Math.Min(target, scrollViewer.ScrollableHeight));
        }

        /// <summary>
        /// Выделяет аниме в таблице «План», плавно прокручивает к нему (по центру окна просмотра)
        /// и коротко подсвечивает строку.
        /// </summary>
        private async Task HighlightAnime(Anime anime)
        {
            int version = ++_highlightVersion;

            await Task.Delay(50); // даём таблице построить строки после обновления списка
            if (version != _highlightVersion) return;

            var scrollViewer = FindVisualChild<ScrollViewer>(PlanDataGrid);
            if (scrollViewer == null) return;

            PlanDataGrid.SelectedItem = anime;
            double startOffset = scrollViewer.VerticalOffset;

            // 1) Временно «прыгаем» к строке, чтобы измерить её положение. Всё происходит синхронно,
            //    без отрисовки между шагами, поэтому на экране прыжка не видно.
            double? target = MeasureCenteredOffset(anime, scrollViewer, bringIntoView: true);
            if (target == null) return;

            // 2) Возвращаемся на прежнее место и плавно едем к цели
            scrollViewer.ScrollToVerticalOffset(startOffset);
            scrollViewer.UpdateLayout();
            if (!await SmoothScroller.AnimateToAsync(scrollViewer, target.Value) || version != _highlightVersion)
                return; // пользователь начал прокручивать сам — не мешаем

            // 3) Уточнение. Из-за виртуализации высота прокручиваемой области лишь оценивается и меняется,
            //    пока показываются новые строки: поэтому для аниме в самом низу списка первая цель могла
            //    оказаться «недолётом». Пересчитываем цель и при необходимости доезжаем (до 4 раз).
            for (int attempt = 0; attempt < 4; attempt++)
            {
                PlanDataGrid.UpdateLayout();
                double? corrected = MeasureCenteredOffset(anime, scrollViewer, bringIntoView: false)
                                    ?? MeasureCenteredOffset(anime, scrollViewer, bringIntoView: true);
                if (corrected == null) return;
                if (Math.Abs(corrected.Value - scrollViewer.VerticalOffset) < 2) break;

                if (!await SmoothScroller.AnimateToAsync(scrollViewer, corrected.Value) || version != _highlightVersion)
                    return;
            }

            // 4) Строка на месте — подсвечиваем её
            PlanDataGrid.UpdateLayout();
            if (PlanDataGrid.ItemContainerGenerator.ContainerFromItem(anime) is DataGridRow row)
                await FlashRowWhite(row);
        }

        private async Task FlashRowWhite(DataGridRow row)
        {
            if (row == null) return;
            var originalBackground = row.Background;
            var whiteBrush = new SolidColorBrush(Colors.White);
            whiteBrush.Opacity = 1.0;
            row.Background = whiteBrush;

            var opacityAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var tcs = new TaskCompletionSource<bool>();
            EventHandler? handler = null;
            handler = (s, e) =>
            {
                tcs.SetResult(true);
                opacityAnimation.Completed -= handler;
            };
            opacityAnimation.Completed += handler;
            whiteBrush.BeginAnimation(SolidColorBrush.OpacityProperty, opacityAnimation);
            await tcs.Task;
            row.Background = originalBackground;
        }

        private bool _gridRelayoutScheduled;

        /// <summary>
        /// При изменении размера окна заставляет таблицы пересчитать перенос текста в строках.
        /// События ресайза приходят пачками, поэтому пересчёт планируется один раз на пачку,
        /// и только для таблицы, которая сейчас видна.
        /// </summary>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_gridRelayoutScheduled) return;
            _gridRelayoutScheduled = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _gridRelayoutScheduled = false;
                RelayoutGridIfVisible(PlanDataGrid);
                RelayoutGridIfVisible(TopDataGrid);
            }), DispatcherPriority.Background);
        }

        private static void RelayoutGridIfVisible(DataGrid? grid)
        {
            if (grid == null || !grid.IsVisible) return;
            grid.InvalidateMeasure();
            grid.InvalidateArrange();
            grid.UpdateLayout();
        }

        private void TempRating_Click(object sender, RoutedEventArgs e)
        {
            var vm = (MainViewModel)DataContext;
            if (vm.SelectedAnime != null)
            {
                var window = new Views.TempRatingWindow(vm.SelectedAnime) { Owner = this };
                window.ShowDialog();
                vm.SaveDataToFile();
            }
        }

        private void ShowCriteriaHelp_Click(object sender, RoutedEventArgs e)
        {
            if (_criteriaHelpWindow != null && _criteriaHelpWindow.IsLoaded)
            {
                _criteriaHelpWindow.Activate();
                return;
            }
            _criteriaHelpWindow = new Views.CriteriaHelpWindow();
            _criteriaHelpWindow.Owner = this;
            _criteriaHelpWindow.Closed += (s, args) => _criteriaHelpWindow = null;
            _criteriaHelpWindow.Show();
        }

        private void OpenBackupManager_Click(object sender, RoutedEventArgs e)
        {
            var window = new Views.BackupManagerWindow { Owner = this };
            window.ShowDialog();
        }

        private void ExportData_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                FileName = "animeData",
                DefaultExt = ".json",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var vm = (MainViewModel)DataContext;
                    vm.SaveDataToFile();
                    string sourcePath = MainViewModel.DataFilePath;
                    if (System.IO.File.Exists(sourcePath))
                    {
                        System.IO.File.Copy(sourcePath, dialog.FileName, overwrite: true);
                        string message = Services.LocalizationManager.CurrentLanguage == "English"
                            ? (string)FindResource("Msg_ExportSuccess")
                            : (string)FindResource("Msg_ExportSuccess");
                        MessageBox.Show(message, "Anime Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else MessageBox.Show("Файл данных не найден.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex) { MessageBox.Show($"Ошибка при экспорте: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void ImportData_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Services.LocalizationManager.CurrentLanguage == "English" ? "Select data file" : "Выберите файл данных",
                DefaultExt = ".json",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
            {
                string selectedFile = dialog.FileName;
                if (!IsValidDataFile(selectedFile))
                {
                    string message = Services.LocalizationManager.CurrentLanguage == "English"
                        ? (string)FindResource("Msg_ImportInvalidFile")
                        : (string)FindResource("Msg_ImportInvalidFile");
                    MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                try
                {
                    System.IO.File.Copy(selectedFile, MainViewModel.DataFilePath, overwrite: true);
                    var vm = (MainViewModel)DataContext;
                    vm.ReloadData();
                    vm.MigrateTopListScores();
                    string successMessage = Services.LocalizationManager.CurrentLanguage == "English"
                        ? (string)FindResource("Msg_ImportSuccess")
                        : (string)FindResource("Msg_ImportSuccess");
                    MessageBox.Show(successMessage, "Anime Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                    vm.SelectedTabIndex = 0;
                }
                catch (Exception ex) { MessageBox.Show($"Ошибка при импорте: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private static bool IsValidDataFile(string filePath)
        {
            try
            {
                string json = System.IO.File.ReadAllText(filePath);
                var data = System.Text.Json.JsonSerializer.Deserialize<AnimeTracker.Models.SaveData>(json);
                return data != null && data.AnimeList != null && data.TopList != null;
            }
            catch { return false; }
        }

        private void ResetData_Click(object sender, RoutedEventArgs e)
        {
            var confirmWindow = new Views.ResetDataConfirmWindow { Owner = this };
            if (confirmWindow.ShowDialog() == true && confirmWindow.Confirmed)
            {
                var vm = (MainViewModel)DataContext;
                vm.ResetAllData();
                vm.SelectedTabIndex = 0;
                string message = Services.LocalizationManager.CurrentLanguage == "English"
                    ? "All data has been reset."
                    : "Все данные были сброшены.";
                MessageBox.Show(message, "Anime Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        #endregion

        #region Theme/Language Sync
        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            {
                string? key = item.Tag as string;
                if (!string.IsNullOrEmpty(key) && key != App.CurrentThemeKey)
                {
                    App.ApplyTheme(key);

                    // Диаграммы хранят цвета в самих сериях (не в ресурсах) — пересоздаём их под новую тему
                    Dispatcher.BeginInvoke(new Action(() => (DataContext as MainViewModel)?.UpdateCharts()),
                        DispatcherPriority.Background);
                }
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            {
                string? lang = item.Content?.ToString();
                if (!string.IsNullOrEmpty(lang) && lang != Services.LocalizationManager.CurrentLanguage)
                {
                    Services.LocalizationManager.SetLanguage(lang);
                    PopulateThemeComboBox();
                    UpdateNavigationButtons(_isSidebarExpanded);
                }
            }
        }

        public void SyncLanguageComboBox()
        {
            string currentLang = LocalizationManager.CurrentLanguage;
            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Content.ToString() == currentLang)
                {
                    LanguageComboBox.SelectedItem = item;
                    return;
                }
            }
            LanguageComboBox.SelectedIndex = 0;
        }
        #endregion

        #region Description Toggle
        private void DescriptionTextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateDescriptionButtonVisibility(sender as TextBlock);
        }

        private void DescriptionTextBlock_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateDescriptionButtonVisibility(sender as TextBlock);
        }

        /// <summary>
        /// Строки таблицы переиспользуются (VirtualizationMode=Recycling): тот же TextBlock показывает уже другое аниме,
        /// а событие Loaded повторно не возникает. Поэтому при смене DataContext возвращаем описание
        /// в свёрнутый вид и пересчитываем кнопку «…».
        /// </summary>
        private void DescriptionTextBlock_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not TextBlock textBlock || !textBlock.IsLoaded) return; // до Loaded всё сделает Loaded
            textBlock.TextWrapping = TextWrapping.NoWrap;
            textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            UpdateDescriptionButtonVisibility(textBlock);
        }

        // Кэш ширины текста: ширина строки без переноса не меняется, пока не изменились текст и шрифт.
        // Раньше для каждой строки при каждом Loaded/SizeChanged выполнялся TextBlock.Measure(∞) —
        // принудительный пересчёт раскладки, который заметно тормозил прокрутку.
        private static readonly Dictionary<(string Text, double Size, string Family), double> TextWidthCache = new();

        private static double GetTextWidth(TextBlock textBlock, string text)
        {
            var key = (text, textBlock.FontSize, textBlock.FontFamily?.Source ?? "");
            if (TextWidthCache.TryGetValue(key, out double cached))
                return cached;

            var typeface = new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch);
            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, textBlock.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(textBlock).PixelsPerDip);

            double width = formatted.WidthIncludingTrailingWhitespace;
            if (TextWidthCache.Count > 5000) TextWidthCache.Clear(); // защита от неограниченного роста
            TextWidthCache[key] = width;
            return width;
        }

        /// <summary>Показывает кнопку «…» (развернуть описание), только если текст не помещается в одну строку.</summary>
        private void UpdateDescriptionButtonVisibility(TextBlock? textBlock)
        {
            if (textBlock == null) return;
            var grid = textBlock.Parent as Grid;
            var button = grid?.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == "DescriptionToggle");
            if (button == null) return;

            if (textBlock.TextWrapping == TextWrapping.Wrap)
            {
                button.Visibility = Visibility.Visible;
                button.Content = "▲";
                return;
            }

            // Текст берём из модели, а не из textBlock.Text: при переиспользовании строки привязка может обновиться позже
            string text = (textBlock.DataContext as Anime)?.Description ?? textBlock.Text ?? "";
            double actualWidth = textBlock.ActualWidth - textBlock.Padding.Left - textBlock.Padding.Right;
            bool isOverflow = text.Length > 0 && GetTextWidth(textBlock, text) > actualWidth;

            button.Visibility = isOverflow ? Visibility.Visible : Visibility.Collapsed;
            button.Content = "…";
        }

        private void DescriptionButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var grid = button?.Parent as Grid;
            var textBlock = grid?.Children.OfType<TextBlock>().FirstOrDefault();
            if (textBlock == null) return;

            if (textBlock.TextWrapping == TextWrapping.NoWrap)
            {
                textBlock.TextWrapping = TextWrapping.Wrap;
                textBlock.TextTrimming = TextTrimming.None;
            }
            else
            {
                textBlock.TextWrapping = TextWrapping.NoWrap;
                textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            }

            UpdateDescriptionButtonVisibility(textBlock);

            var row = FindParent<DataGridRow>(textBlock);
            row?.InvalidateMeasure();
            row?.UpdateLayout();
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindParent<T>(parentObject);
        }
        #endregion

        #region Episodes Toggle
        private void EpisodesTextBlock_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateEpisodesButtonVisibility(sender as TextBlock);
        }

        private void EpisodesTextBlock_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateEpisodesButtonVisibility(sender as TextBlock);
        }

        /// <summary>Строка таблицы переиспользована для другого аниме (см. <see cref="DescriptionTextBlock_DataContextChanged"/>).</summary>
        private void EpisodesTextBlock_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.IsLoaded)
                UpdateEpisodesButtonVisibility(textBlock);
        }

        // Максимум строк состава, показываемых без раскрытия
        private const int CollapsedEpisodeLines = 4;

        /// <summary>
        /// Если состав занимает больше четырёх строк, обрезает его и показывает кнопку «…» (раскрыть).
        /// Число строк считается по модели — это те же строки, что формирует AnimeEpisodesConverter
        /// (по строке на сезон + по одной на фильмы, OVA и ONA), без разбора текста.
        /// </summary>
        private void UpdateEpisodesButtonVisibility(TextBlock? textBlock)
        {
            if (textBlock == null) return;
            var grid = textBlock.Parent as Grid;
            var button = grid?.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == "EpisodesToggle");
            if (button == null) return;

            var anime = textBlock.DataContext as Anime;
            if (anime == null) return;

            if (anime.IsEpisodesExpanded)
            {
                button.Visibility = Visibility.Visible;
                button.Content = "▲";
                textBlock.MaxHeight = double.PositiveInfinity;
                return;
            }

            int lineCount = anime.Seasons.Count
                            + (anime.Movies.Count > 0 ? 1 : 0)
                            + (anime.Ovas.Count > 0 ? 1 : 0)
                            + (anime.Onas.Count > 0 ? 1 : 0);

            if (lineCount > CollapsedEpisodeLines)
            {
                button.Visibility = Visibility.Visible;
                button.Content = "…";
                textBlock.MaxHeight = textBlock.FontSize * 1.2 * CollapsedEpisodeLines - 2;
            }
            else
            {
                button.Visibility = Visibility.Collapsed;
                textBlock.MaxHeight = double.PositiveInfinity;
            }
        }

        private void EpisodesButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var grid = button?.Parent as Grid;
            var textBlock = grid?.Children.OfType<TextBlock>().FirstOrDefault();
            if (textBlock == null) return;

            var anime = textBlock.DataContext as Anime;
            if (anime == null) return;

            anime.IsEpisodesExpanded = !anime.IsEpisodesExpanded;
            UpdateEpisodesButtonVisibility(textBlock);

            var row = FindParent<DataGridRow>(textBlock);
            row?.InvalidateMeasure();
            row?.UpdateLayout();
        }
        #endregion

        #region Hover effects
        // Кисти и эффект наведения создаются один раз и замораживаются: раньше они создавались заново
        // при каждом наведении курсора на карточку.
        private static readonly Brush CardHoverBrush = CreateFrozenBrush(Color.FromRgb(0x30, 0x30, 0x48));
        private static readonly Brush TopItemHoverBrush = CreateFrozenBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        private static readonly Brush TopItemNormalBrush = CreateFrozenBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        private static readonly Effect CardHoverEffect = CreateFrozenHoverEffect();

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Effect CreateFrozenHoverEffect()
        {
            var effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black };
            effect.Freeze();
            return effect;
        }

        private void Card_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = CardHoverBrush;
                border.Effect = CardHoverEffect;
            }
        }

        private void Card_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = (SolidColorBrush)FindResource("PanelBackground");
                border.Effect = (DropShadowEffect)FindResource("CardShadow");
            }
        }

        private void TopItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
                border.Background = TopItemHoverBrush;
        }

        private void TopItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
                border.Background = TopItemNormalBrush;
        }
        #endregion

        #region Hyperlink
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть ссылку: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Helper
        private void ResetScrollPosition(FrameworkElement page)
        {
            if (page == null) return;
            var scrollViewer = FindVisualChild<ScrollViewer>(page);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToTop();
            }
        }
        #endregion

        private void MainWindow_Closed(object? sender, EventArgs e) => Application.Current.Shutdown();

        // ========== НОВЫЕ МЕТОДЫ ДЛЯ СКРУГЛЕНИЯ УГЛОВ ДИАГРАММ ==========
        private readonly List<CartesianChart> _roundedCharts = new();

        private void OnChartLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not CartesianChart chart) return;

            if (!_roundedCharts.Contains(chart))
                _roundedCharts.Add(chart);

            chart.SizeChanged -= Chart_RoundedCornersInvalidated;
            chart.SizeChanged += Chart_RoundedCornersInvalidated;
            chart.IsVisibleChanged -= Chart_IsVisibleChanged;
            chart.IsVisibleChanged += Chart_IsVisibleChanged;

            ScheduleRoundedCorners(chart);
        }

        private void Chart_RoundedCornersInvalidated(object sender, SizeChangedEventArgs e)
        {
            if (sender is CartesianChart chart)
                ScheduleRoundedCorners(chart);
        }

        private void Chart_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is CartesianChart chart && chart.IsVisible)
                ScheduleRoundedCorners(chart);
        }

        private void ReapplyRoundedCornersToAllCharts()
        {
            foreach (var chart in _roundedCharts)
                ScheduleRoundedCorners(chart);
        }

        // Активные таймеры по диаграммам: новый запрос отменяет предыдущий, поэтому серия быстрых
        // обновлений данных не плодит десятки таймеров (раньше каждый запуск жил до конца своих попыток).
        private readonly Dictionary<CartesianChart, DispatcherTimer> _roundedTimers = new();

        /// <summary>
        /// Планирует скругление углов столбцов диаграммы. Визуальное дерево LiveCharts строится не сразу,
        /// поэтому при неудаче попытка повторяется (до 12 раз с интервалом 250 мс).
        /// </summary>
        private void ScheduleRoundedCorners(CartesianChart chart, int attempt = 0)
        {
            const int maxAttempts = 12;

            if (_roundedTimers.TryGetValue(chart, out var previous))
                previous.Stop(); // отменяем устаревший запуск

            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _roundedTimers[chart] = timer;

            timer.Tick += (s, args) =>
            {
                timer.Stop();
                // Запуск уже вытеснен более новым — ничего не делаем
                if (!_roundedTimers.TryGetValue(chart, out var current) || !ReferenceEquals(current, timer)) return;

                int rounded = ApplyRoundedCornersToChart(chart, 6);
                if (rounded == 0 && attempt < maxAttempts)
                {
                    ScheduleRoundedCorners(chart, attempt + 1);
                }
            };
            timer.Start();
        }

        /// <summary>
        /// Обходит визуальное дерево диаграммы: скругляет прямоугольники и обрезает контуры (Path) по скруглённой рамке.
        /// Возвращает количество изменённых элементов (0 — дерево ещё не построено).
        /// </summary>
        private int ApplyRoundedCornersToChart(CartesianChart chart, double cornerRadius)
        {
            if (chart == null) return 0;

            int rectRounded = 0, pathClipped = 0;

            void Walk(DependencyObject node)
            {
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);

                    switch (child)
                    {
                        case System.Windows.Shapes.Rectangle rect:
                            rect.RadiusX = cornerRadius;
                            rect.RadiusY = cornerRadius;
                            rectRounded++;
                            break;
                        case System.Windows.Shapes.Path path when path.Data != null:
                            var b = path.Data.Bounds;
                            if (b.Width > 0 && b.Height > 0)
                            {
                                path.Clip = new RectangleGeometry(b, cornerRadius, cornerRadius);
                                pathClipped++;
                            }
                            break;
                    }

                    Walk(child);
                }
            }
            Walk(chart);

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[RoundedCorners] rectRounded={rectRounded} pathClipped={pathClipped}");
#endif

            return rectRounded + pathClipped;
        }
    }

    /// <summary>
    /// Плавная прокрутка ScrollViewer. Два режима:
    /// <list type="bullet">
    /// <item><see cref="ScrollBy"/> — колесо мыши: позиция каждый кадр приближается к накопленной цели
    /// (экспоненциальное сглаживание). Это непрерывное движение без «перезапусков» анимации на каждый щелчок.</item>
    /// <item><see cref="AnimateToAsync"/> — автоматический переезд к нужной строке (анимация с ускорением и замедлением).</item>
    /// </list>
    /// У ScrollViewer нельзя анимировать VerticalOffset напрямую (свойство только для чтения), поэтому для
    /// второго режима используется вспомогательное присоединяемое свойство: анимация меняет его значение,
    /// а обработчик на каждом кадре вызывает ScrollToVerticalOffset.
    /// </summary>
    public static class SmoothScroller
    {
        public static readonly DependencyProperty AnimatedOffsetProperty =
            DependencyProperty.RegisterAttached("AnimatedOffset", typeof(double), typeof(SmoothScroller),
                new PropertyMetadata(0.0, OnAnimatedOffsetChanged));

        public static double GetAnimatedOffset(DependencyObject obj) => (double)obj.GetValue(AnimatedOffsetProperty);
        public static void SetAnimatedOffset(DependencyObject obj, double value) => obj.SetValue(AnimatedOffsetProperty, value);

        private static void OnAnimatedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
                scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
        }

        // Постоянная времени сглаживания колеса: чем меньше, тем «резче» следует за колесом (секунды)
        private const double WheelSmoothingTime = 0.09;

        // Состояние прокрутки одного ScrollViewer
        private sealed class ScrollState
        {
            // анимация автоматического переезда
            public TaskCompletionSource<bool>? Completion;
            public bool IsAnimating;
            public int Version;

            // сглаженная прокрутка колесом
            public bool WheelActive;
            public double WheelTarget;
            public TimeSpan LastRender;
            public EventHandler? RenderHandler;
        }

        private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> States = new();

        // ───────────── Колесо мыши ─────────────

        /// <summary>
        /// Прокрутка на delta пикселей (отрицательная — вверх). Серия быстрых щелчков накапливается в одной цели,
        /// а позиция плавно «догоняет» её — поэтому движение непрерывное, без рывков.
        /// </summary>
        public static void ScrollBy(ScrollViewer scrollViewer, double delta)
        {
            var state = States.GetOrCreateValue(scrollViewer);
            CancelAnimation(scrollViewer, state); // колесо важнее автоматического переезда

            double baseTarget = state.WheelActive ? state.WheelTarget : scrollViewer.VerticalOffset;
            state.WheelTarget = Math.Max(0, Math.Min(baseTarget + delta, scrollViewer.ScrollableHeight));

            if (state.WheelActive) return;

            state.WheelActive = true;
            state.LastRender = TimeSpan.Zero;
            state.RenderHandler = (s, e) => OnWheelFrame(scrollViewer, state, e);
            CompositionTarget.Rendering += state.RenderHandler;
        }

        private static void OnWheelFrame(ScrollViewer scrollViewer, ScrollState state, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            double dt = state.LastRender == TimeSpan.Zero ? 1.0 / 60 : (now - state.LastRender).TotalSeconds;
            state.LastRender = now;
            if (dt <= 0 || dt > 0.1) dt = 1.0 / 60; // защита от пропущенных кадров

            // Высота прокручиваемой области может меняться (виртуализация) — цель не должна выходить за неё
            double target = Math.Min(state.WheelTarget, scrollViewer.ScrollableHeight);
            double current = scrollViewer.VerticalOffset;
            double difference = target - current;

            if (Math.Abs(difference) < 0.5)
            {
                scrollViewer.ScrollToVerticalOffset(target);
                StopWheel(state);
                return;
            }

            // Шаг не зависит от частоты кадров: за одно и то же время проходится одна и та же доля пути
            double alpha = 1 - Math.Exp(-dt / WheelSmoothingTime);
            scrollViewer.ScrollToVerticalOffset(current + difference * alpha);
        }

        private static void StopWheel(ScrollState state)
        {
            if (state.RenderHandler != null)
                CompositionTarget.Rendering -= state.RenderHandler;
            state.RenderHandler = null;
            state.WheelActive = false;
        }

        // ───────────── Автоматический переезд к строке ─────────────

        /// <summary>
        /// Плавно прокручивает к указанной позиции. Длительность растёт с расстоянием.
        /// Задача завершается, когда анимация закончилась (true) или была прервана новой прокруткой (false).
        /// </summary>
        public static Task<bool> AnimateToAsync(ScrollViewer scrollViewer, double targetOffset)
        {
            double distance = Math.Abs(targetOffset - scrollViewer.VerticalOffset);
            double minimum = distance < 150 ? 180 : 350; // короткие «доводки» не должны тянуться долго
            double durationMs = Math.Max(minimum, Math.Min(900, 300 + distance * 0.35));
            return Run(scrollViewer, targetOffset, durationMs, new CubicEase { EasingMode = EasingMode.EaseInOut });
        }

        private static Task<bool> Run(ScrollViewer scrollViewer, double targetOffset, double durationMs, IEasingFunction easing)
        {
            var state = States.GetOrCreateValue(scrollViewer);
            StopWheel(state);                         // автоматический переезд заменяет прокрутку колесом
            CancelAnimation(scrollViewer, state);     // и вытесняет предыдущий переезд

            targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ScrollableHeight));
            double from = scrollViewer.VerticalOffset;

            if (Math.Abs(targetOffset - from) < 0.5)
                return Task.FromResult(true);

            int version = ++state.Version;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Completion = completion;
            state.IsAnimating = true;

            var animation = new DoubleAnimation(from, targetOffset, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = easing
            };
            animation.Completed += (s, e) =>
            {
                if (state.Version != version) return; // анимацию уже заменила более новая

                state.IsAnimating = false;
                state.Completion = null;

                // Сначала записываем итоговое значение, затем снимаем анимацию: так позиция не «прыгает» назад
                SetAnimatedOffset(scrollViewer, targetOffset);
                scrollViewer.BeginAnimation(AnimatedOffsetProperty, null);
                completion.TrySetResult(true);
            };

            scrollViewer.BeginAnimation(AnimatedOffsetProperty, animation);
            return completion.Task;
        }

        /// <summary>Останавливает анимацию переезда (если она идёт), оставляя таблицу в текущей позиции.</summary>
        private static void CancelAnimation(ScrollViewer scrollViewer, ScrollState state)
        {
            if (!state.IsAnimating) return;

            state.Version++;                          // Completed старой анимации будет проигнорирован
            state.IsAnimating = false;
            state.Completion?.TrySetResult(false);
            state.Completion = null;

            // Базовое значение = текущая позиция: иначе после снятия анимации таблица «прыгнула» бы на старое значение
            SetAnimatedOffset(scrollViewer, scrollViewer.VerticalOffset);
            scrollViewer.BeginAnimation(AnimatedOffsetProperty, null);
        }
    }

    // ============ Класс ColumnExtensions ============
    public static class ColumnExtensions
    {
        public static readonly DependencyProperty ResourceKeyProperty =
            DependencyProperty.RegisterAttached(
                "ResourceKey",
                typeof(string),
                typeof(ColumnExtensions),
                new PropertyMetadata(null));

        public static void SetResourceKey(DependencyObject element, string value)
            => element.SetValue(ResourceKeyProperty, value);

        public static string GetResourceKey(DependencyObject element)
            => (string)element.GetValue(ResourceKeyProperty);
    }

    // Конвертирует double (балл 0..10) в GridLength с типом Star
    public class ScoreToStarWidthConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            double d = value is double dv ? dv : 0;
            if (d < 0) d = 0;
            return new GridLength(d, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}