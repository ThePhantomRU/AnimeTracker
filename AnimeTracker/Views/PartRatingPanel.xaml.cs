using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AnimeTracker.Models;
using AnimeTracker.Services;
using AnimeTracker.ViewModels;

namespace AnimeTracker.Views
{
    /// <summary>
    /// Панель оценок одной части тайтла (сезона, фильма или OVA) на вкладке «Оценка».
    /// Блоки «Опенинги» и «Эндинги» строятся программно: для каждого опенинга/эндинга создаётся
    /// слайдер 1–10, связанный (TwoWay) с элементом коллекции оценок соответствующей части.
    /// </summary>
    public partial class PartRatingPanel : UserControl
    {
        // Язык, для которого панель была построена в последний раз. Нужен, чтобы после возвращения
        // на экран перестроить подписи, если язык менялся, пока панель была выгружена.
        private string? _builtForLanguage;

        public PartRatingPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += PartRatingPanel_Loaded;
            Unloaded += PartRatingPanel_Unloaded;
        }

        // ───────────── Жизненный цикл и подписки ─────────────

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => RebuildAll();

        private void PartRatingPanel_Loaded(object sender, RoutedEventArgs e)
        {
            // Подписка на статическое событие живёт только пока панель на экране —
            // иначе LocalizationManager удерживал бы в памяти каждую когда-либо созданную панель.
            // (Подписываемся заново при каждом Loaded: вкладки TabControl выгружаются и загружаются повторно.)
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            LocalizationManager.LanguageChanged += OnLanguageChanged;

            // Язык мог смениться, пока панель была выгружена — тогда подписи устарели.
            if (_builtForLanguage != null && _builtForLanguage != LocalizationManager.CurrentLanguage)
                RebuildAll();
        }

        private void PartRatingPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged() => RebuildAll();

        // ───────────── Публичный API панели ─────────────

        /// <summary>Полностью перестраивает блоки опенингов и эндингов для текущей части.</summary>
        public void RebuildAll()
        {
            if (DataContext is PartBase part)
            {
                RebuildOpenings(part);
                RebuildEndings(part);
                _builtForLanguage = LocalizationManager.CurrentLanguage;
            }
        }

        /// <summary>
        /// Принудительно записывает значения всех слайдеров в модель. Вызывается перед сохранением оценки,
        /// чтобы не потерять значение слайдера, который ещё находится в процессе изменения.
        /// </summary>
        public void UpdateAllBindings()
        {
            foreach (Slider slider in FindVisualChildren<Slider>(this))
                UpdateSliderSource(slider);

            // Слайдеры, добавленные программно, могут находиться как напрямую в панели, так и во вложенном StackPanel
            UpdateSlidersIn(OpeningsPanel);
            UpdateSlidersIn(EndingsPanel);

            UpdateLayout();
        }

        private static void UpdateSlidersIn(Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is Slider slider)
                {
                    UpdateSliderSource(slider);
                }
                else if (child is StackPanel inner)
                {
                    foreach (UIElement nested in inner.Children)
                        if (nested is Slider nestedSlider)
                            UpdateSliderSource(nestedSlider);
                }
            }
        }

        private static void UpdateSliderSource(Slider slider)
            => BindingOperations.GetBindingExpression(slider, Slider.ValueProperty)?.UpdateSource();

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t) yield return t;
                foreach (var nested in FindVisualChildren<T>(child))
                    yield return nested;
            }
        }

        // ───────────── Построение блоков оценок ─────────────

        private void RebuildOpenings(PartBase part)
        {
            RebuildScoreBlock(new ScoreBlockConfig
            {
                Panel = OpeningsPanel,
                Part = part,
                ItemLabel = MainViewModel.GetLocalizedString("Part_Opening"),
                ScoresPath = nameof(PartBase.OpeningScores),
                GetCount = () => part.OpeningCount,
                SetCount = value => part.OpeningCount = value,
                Rebuild = () => RebuildOpenings(part),
                // Для опенингов значение под слайдером выводится без форматирования (как и раньше)
                ValueStringFormat = null
            });
        }

        private void RebuildEndings(PartBase part)
        {
            RebuildScoreBlock(new ScoreBlockConfig
            {
                Panel = EndingsPanel,
                Part = part,
                ItemLabel = MainViewModel.GetLocalizedString("Part_Ending"),
                ScoresPath = nameof(PartBase.EndingScores),
                GetCount = () => part.EndingCount,
                SetCount = value => part.EndingCount = value,
                Rebuild = () => RebuildEndings(part),
                // Для эндингов значение под слайдером округляется до целого
                ValueStringFormat = "{0:F0}"
            });
        }

        /// <summary>Параметры блока оценок. Блоки опенингов и эндингов устроены одинаково и отличаются только этими данными.</summary>
        private sealed class ScoreBlockConfig
        {
            public Panel Panel = null!;
            public PartBase Part = null!;
            public string ItemLabel = "";
            /// <summary>Имя свойства-коллекции оценок в PartBase (используется в пути привязки).</summary>
            public string ScoresPath = "";
            public Func<int> GetCount = null!;
            public Action<int> SetCount = null!;
            /// <summary>Перестраивает этот же блок (вызывается после изменения количества).</summary>
            public Action Rebuild = null!;
            public string? ValueStringFormat;
        }

        /// <summary>
        /// Строит один блок: панель управления количеством («➖  [N]  ➕  Количество») и список слайдеров
        /// «Название N: [слайдер] значение» по числу опенингов/эндингов.
        /// </summary>
        private void RebuildScoreBlock(ScoreBlockConfig cfg)
        {
            var part = cfg.Part;
            cfg.Panel.Children.Clear();

            string countLabel = MainViewModel.GetLocalizedString("Part_Count");
            // У сезона должен быть минимум один опенинг/эндинг, у фильма и OVA — может не быть ни одного
            int minCount = part is Season ? 1 : 0;

            // ── Панель управления количеством ──
            var controlPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };

            var decreaseBtn = CreateStepButton("➖", new Thickness(0, 0, 3, 0));
            decreaseBtn.Click += (s, e) =>
            {
                if (cfg.GetCount() > minCount)
                {
                    cfg.SetCount(cfg.GetCount() - 1);
                    cfg.Rebuild();
                }
            };
            controlPanel.Children.Add(decreaseBtn);

            var countBox = new TextBox
            {
                Width = 35,
                Text = cfg.GetCount().ToString(),
                Style = (Style)FindResource("RoundedTextBox"),
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            countBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(countBox.Text, out int val) && val >= 0)
                {
                    if (val < minCount) val = minCount;
                    if (cfg.GetCount() != val)
                    {
                        cfg.SetCount(val);
                        // Перестраиваем после завершения текущего события — иначе TextBox удалится из дерева прямо во время ввода
                        Dispatcher.BeginInvoke(cfg.Rebuild);
                    }
                }
            };
            controlPanel.Children.Add(countBox);

            var increaseBtn = CreateStepButton("➕", new Thickness(3, 0, 0, 0));
            increaseBtn.Click += (s, e) =>
            {
                cfg.SetCount(cfg.GetCount() + 1);
                cfg.Rebuild();
            };
            controlPanel.Children.Add(increaseBtn);

            controlPanel.Children.Add(new TextBlock
            {
                Text = "   " + countLabel,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            });
            cfg.Panel.Children.Add(controlPanel);

            // ── Слайдеры оценок ──
            int scoresCount = GetScores(part, cfg.ScoresPath).Count;
            for (int i = 0; i < scoresCount; i++)
            {
                cfg.Panel.Children.Add(new TextBlock
                {
                    Text = $"{cfg.ItemLabel} {i + 1}:",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 5, 0, 0),
                    FontSize = 13
                });

                string bindingPath = $"{cfg.ScoresPath}[{i}]";

                var slider = new Slider { Minimum = 1, Maximum = 10, TickFrequency = 1, IsSnapToTickEnabled = true, Height = 25 };
                slider.SetBinding(Slider.ValueProperty, new Binding(bindingPath)
                {
                    Source = part,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
                cfg.Panel.Children.Add(slider);

                var valueText = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 13
                };
                var valueBinding = new Binding(bindingPath) { Source = part };
                if (cfg.ValueStringFormat != null)
                    valueBinding.StringFormat = cfg.ValueStringFormat;
                valueText.SetBinding(TextBlock.TextProperty, valueBinding);
                cfg.Panel.Children.Add(valueText);
            }
        }

        private static ObservableCollection<double> GetScores(PartBase part, string scoresPath)
            => scoresPath == nameof(PartBase.OpeningScores) ? part.OpeningScores : part.EndingScores;

        /// <summary>Создаёт квадратную кнопку «+/−» в общем стиле панели.</summary>
        private static Button CreateStepButton(string glyph, Thickness margin)
        {
            var button = new Button
            {
                Content = glyph,
                Width = 26,
                Height = 26,
                Margin = margin,
                FontSize = 12,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(0)
            };

            // Цвет кнопки берётся из текущей темы и обновляется при её смене
            // (раньше был зашит фиолетовый цвет темы по умолчанию)
            button.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
            return button;
        }

        // ───────────── Прочее ─────────────

        // Метод оставлен для совместимости: он может использоваться из XAML или внешнего кода.
        private void UpdateLayoutAndScrollViewer()
        {
            InvalidateVisual();
            UpdateLayout();
            if (Parent is ScrollViewer sv)
            {
                sv.InvalidateScrollInfo();
                sv.UpdateLayout();
            }
        }

        // Плавная прокрутка колесом мыши (обработчик может быть подключён в XAML).
        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3);
                e.Handled = true;
            }
        }
    }
}