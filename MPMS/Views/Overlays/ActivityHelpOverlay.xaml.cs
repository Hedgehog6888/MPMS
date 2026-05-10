using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MPMS.Views.Overlays;

public partial class ActivityHelpOverlay : UserControl
{
    public ActivityHelpOverlay()
    {
        InitializeComponent();
        SetPalette("Document");
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private void LegendItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
            SetPalette(key);
    }

    private void SetPalette(string key)
    {
        DataContext = key switch
        {
            "Project" => new ActivityHelpPalette("проект", "#1E40AF", "#2563EB", "#3B82F6", "#60A5FA", "Создан", "Изменён", "Удалён", "Статус", "Тёмно-синий — создан новый проект\nСиний — изменены данные проекта\nГолубой — проект удалён или архивирован\nСветло-голубой — изменён статус проекта", "#EFF6FF", "#BFDBFE", "#1E3A8A"),
            "Task" => new ActivityHelpPalette("задача", "#059669", "#10B981", "#34D399", "#6EE7B7", "Создана", "Изменена", "Удалена", "Статус", "Тёмно-зелёный — создана новая задача\nЗелёный — изменены данные задачи\nСветло-зелёный — задача удалена или архивирована\nМятный — изменён статус задачи", "#ECFDF5", "#A7F3D0", "#065F46"),
            "Stage" => new ActivityHelpPalette("этап", "#7E22CE", "#9F123B", "#BE185D", "#E11D48", "Создан", "Изменён", "Удалён", "Статус", "Тёмно-пурпурный — создан новый этап\nПурпурный — изменены данные этапа\nРозово-пурпурный — этап удалён или архивирован\nСветло-пурпурный — изменён статус этапа", "#FDF2F8", "#FBCFE8", "#831843"),
            "Material" => new ActivityHelpPalette("материал", "#CA8A04", "#E1B12C", "#F4821C", "#F97316", "Создан", "Изменён", "Списан", "Склад", "Тёмно-жёлтый — добавлен новый материал\nЖёлто-оранжевый — изменены данные материала\nОранжевый — материал удалён или списан\nЯрко-оранжевый — складское предупреждение", "#FFF7ED", "#FED7AA", "#9A3412"),
            "Equipment" => new ActivityHelpPalette("оборудование", "#0E7A86", "#089182", "#115E59", "#0F7667", "Создано", "Изменено", "Списано", "Склад", "Тёмно-бирюзовый — добавлено оборудование\nБирюзовый — изменены данные оборудования\nТёмно-зелёно-бирюзовый — оборудование удалено или списано\nТёмный бирюзовый — складское предупреждение", "#F0FDFA", "#99F6E4", "#134E4A"),
            "File" => new ActivityHelpPalette("файл", "#BE185D", "#E11D48", "#F43F5E", "#FB7185", "Загружен", "Изменён", "Удалён", "Пометка", "Тёмно-розовый — файл загружен\nРозовый — изменены данные файла\nРозово-красный — файл удалён\nСветло-розовый — файл помечен к удалению", "#FFF1F2", "#FECDD3", "#9F1239"),
            "Image" => new ActivityHelpPalette("изображение", "#4F46E5", "#6366F1", "#818CF8", "#A5B4FC", "Загружено", "Изменено", "Удалено", "Пометка", "Тёмно-индиго — изображение загружено\nИндиго — изменены данные изображения\nСветло-индиго — изображение удалено\nСветло-синий — изображение помечено к удалению", "#EEF2FF", "#C7D2FE", "#3730A3"),
            "Message" => new ActivityHelpPalette("обсуждение", "#6D28D9", "#6D28D9", "#6D28D9", "#6D28D9", "Сообщение", string.Empty, string.Empty, string.Empty, "Фиолетовый — новое сообщение или активность в обсуждении", "#F5F3FF", "#DDD6FE", "#5B21B6", true),
            "User" => new ActivityHelpPalette("пользователь", "#4B5563", "#6B7280", "#94A3B8", "#BCBFE0", "Добавлен", "Изменён", "Удалён", "Доступ", "Тёмно-серый — добавлен пользователь\nСерый — изменены данные пользователя\nСветло-серый — пользователь удалён или отключён\nСеро-голубой — изменение доступа или роли", "#F8FAFC", "#E2E8F0", "#374151"),
            _ => new ActivityHelpPalette("документ", "#65A30D", "#84CC16", "#A3E635", "#BEF264", "Загружен", "Изменён", "Удалён", "Пометка", "Тёмно-оливковый — документ загружен\nОливковый — документ изменён\nСветло-оливковый — документ удалён\nСалатовый — документ помечен к удалению", "#FCFDF8", "#D9F99D", "#365314")
        };
    }

    private sealed class ActivityHelpPalette
    {
        public ActivityHelpPalette(string entityName, string created, string updated, string deleted, string warning, string createdLabel, string updatedLabel, string deletedLabel, string warningLabel, string exampleText, string background, string border, string foreground, bool isSingleShade = false)
        {
            ExampleTitle = $"Пример: {entityName}";
            ExampleText = exampleText;
            CreatedLabel = createdLabel;
            UpdatedLabel = updatedLabel;
            DeletedLabel = deletedLabel;
            WarningLabel = warningLabel;
            CreatedBrush = Brush(created);
            UpdatedBrush = Brush(updated);
            DeletedBrush = Brush(deleted);
            WarningBrush = Brush(warning);
            ExampleBackground = Brush(background);
            ExampleBorderBrush = Brush(border);
            ExampleForeground = Brush(foreground);
            MultiShadeVisibility = isSingleShade ? Visibility.Collapsed : Visibility.Visible;
            SingleShadeVisibility = isSingleShade ? Visibility.Visible : Visibility.Collapsed;
        }

        public string ExampleTitle { get; }
        public string ExampleText { get; }
        public string CreatedLabel { get; }
        public string UpdatedLabel { get; }
        public string DeletedLabel { get; }
        public string WarningLabel { get; }
        public Brush CreatedBrush { get; }
        public Brush UpdatedBrush { get; }
        public Brush DeletedBrush { get; }
        public Brush WarningBrush { get; }
        public Brush ExampleBackground { get; }
        public Brush ExampleBorderBrush { get; }
        public Brush ExampleForeground { get; }
        public Visibility MultiShadeVisibility { get; }
        public Visibility SingleShadeVisibility { get; }

        private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    }
}
