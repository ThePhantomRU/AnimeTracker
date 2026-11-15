using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace AnimeTracker.Services
{
    /// <summary>
    /// Управление резервными копиями файла данных (animeData.json).
    /// <para>
    /// • Автоматическая копия создаётся при сохранении, но не чаще раза в 30 минут (<see cref="CreateBackupIfNeeded"/>).<br/>
    /// • Хранятся не более 5 последних копий и только за последние 7 дней (<see cref="CleanupOldBackups"/>).<br/>
    /// • Копии лежат в папке «Backups» рядом с файлом данных.
    /// </para>
    /// </summary>
    public static class BackupManager
    {
        private const string BackupFolder = "Backups";
        private const string DataFileName = "animeData.json";
        private const string BackupFilePrefix = "animeData_";
        private const string BackupFilePattern = BackupFilePrefix + "*.json";
        private const string ErrorLogFileName = "error.log";

        // Политика хранения копий
        private const int MaxBackupsToKeep = 5;
        private static readonly TimeSpan MaxBackupAge = TimeSpan.FromDays(7);
        private static readonly TimeSpan BackupInterval = TimeSpan.FromMinutes(30);

        private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnimeTracker");
        private static readonly string BackupPath = Path.Combine(DataDirectory, BackupFolder);
        private static readonly string DataFilePath = Path.Combine(DataDirectory, DataFileName);

        // Защита от одновременного обращения из разных потоков (создание и очистка копий не должны пересекаться)
        private static readonly object SyncRoot = new();
        private static DateTime _lastBackupTime = DateTime.MinValue;

        // ───────────── Создание копий ─────────────

        /// <summary>
        /// Создаёт автоматическую резервную копию, если с предыдущей прошло не менее 30 минут.
        /// При ошибке сообщение показывается один раз за интервал — иначе окно с ошибкой
        /// появлялось бы после каждого сохранения.
        /// </summary>
        public static void CreateBackupIfNeeded()
        {
            lock (SyncRoot)
            {
                if (!File.Exists(DataFilePath)) return;

                DateTime now = DateTime.Now;
                if (now - _lastBackupTime < BackupInterval) return;

                try
                {
                    CopyToBackupFolder(DataFilePath, now);
                    _lastBackupTime = now;
                    CleanupOldBackups();
                }
                catch (Exception ex)
                {
                    _lastBackupTime = now; // следующая попытка — через интервал, а не при каждом сохранении
                    ShowError("Backup_CreateFailed", "Не удалось создать резервную копию: {0}", ex.Message, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// Немедленно создаёт копию указанного файла (например, перед импортом или сбросом данных).
        /// Ошибки не прерывают вызывающую операцию — они только записываются в журнал.
        /// </summary>
        public static void CreateBackup(string sourceFilePath)
        {
            lock (SyncRoot)
            {
                try
                {
                    if (!File.Exists(sourceFilePath)) return;
                    CopyToBackupFolder(sourceFilePath, DateTime.Now);
                    CleanupOldBackups();
                }
                catch (Exception ex)
                {
                    LogError($"CreateBackup error: {ex}");
                }
            }
        }

        /// <summary>Копирует файл в папку резервных копий под именем animeData_ГГГГММДД_ЧЧММСС.json.</summary>
        private static void CopyToBackupFolder(string sourceFilePath, DateTime timestamp)
        {
            Directory.CreateDirectory(BackupPath); // безопасно вызывать, если папка уже существует
            string backupFile = Path.Combine(BackupPath, $"{BackupFilePrefix}{timestamp:yyyyMMdd_HHmmss}.json");
            File.Copy(sourceFilePath, backupFile, overwrite: true);
        }

        // ───────────── Просмотр, восстановление, удаление ─────────────

        /// <summary>Возвращает список копий, от новых к старым.</summary>
        public static List<BackupInfo> GetBackups()
        {
            if (!Directory.Exists(BackupPath)) return new List<BackupInfo>();

            // EnumerateFiles сразу отдаёт FileInfo — не нужно повторно обращаться к диску за размером и датой
            return new DirectoryInfo(BackupPath)
                .EnumerateFiles(BackupFilePattern)
                .Select(fi => new BackupInfo
                {
                    FilePath = fi.FullName,
                    FileName = fi.Name,
                    Created = fi.CreationTime,
                    Size = fi.Length
                })
                .OrderByDescending(b => b.Created)
                .ToList();
        }

        /// <summary>Восстанавливает данные из копии (перезаписывает текущий файл данных). Возвращает true при успехе.</summary>
        public static bool RestoreFromBackup(string backupFilePath)
        {
            if (!File.Exists(backupFilePath))
            {
                ShowError("Backup_NotFound", "Файл резервной копии не найден.", null, MessageBoxImage.Error);
                return false;
            }

            try
            {
                File.Copy(backupFilePath, DataFilePath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                ShowError("Backup_RestoreFailed", "Не удалось восстановить данные: {0}", ex.Message, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>Удаляет копию. Ошибки удаления передаются вызывающему коду.</summary>
        public static void DeleteBackup(string backupFilePath)
        {
            if (File.Exists(backupFilePath))
                File.Delete(backupFilePath);
        }

        // ───────────── Очистка ─────────────

        /// <summary>
        /// Оставляет только свежие копии: не более <see cref="MaxBackupsToKeep"/> последних и не старше
        /// <see cref="MaxBackupAge"/>. Всё остальное удаляется. Список уже отсортирован от новых к старым,
        /// поэтому достаточно одного прохода.
        /// </summary>
        private static void CleanupOldBackups()
        {
            DateTime cutoff = DateTime.Now - MaxBackupAge;
            var backups = GetBackups();

            for (int i = 0; i < backups.Count; i++)
            {
                bool keep = i < MaxBackupsToKeep && backups[i].Created >= cutoff;
                if (keep) continue;

                try { File.Delete(backups[i].FilePath); }
                catch { /* не удалось удалить сейчас — повторим при следующей очистке */ }
            }
        }

        // ───────────── Служебные методы ─────────────

        /// <summary>
        /// Показывает сообщение об ошибке на языке интерфейса. Текст берётся из ресурсов по ключу;
        /// если ключа нет — используется русский запасной текст.
        /// </summary>
        private static void ShowError(string messageKey, string fallbackFormat, string? details, MessageBoxImage icon)
        {
            string format = Application.Current?.TryFindResource(messageKey) as string ?? fallbackFormat;
            string title = Application.Current?.TryFindResource("Msg_ErrorTitle") as string ?? "Ошибка";
            string text = details == null ? format : string.Format(format, details);
            MessageBox.Show(text, title, MessageBoxButton.OK, icon);
        }

        /// <summary>Пишет ошибку в журнал в папке данных. Сбой самого логирования подавляется.</summary>
        private static void LogError(string message)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.AppendAllText(Path.Combine(DataDirectory, ErrorLogFileName), $"{DateTime.Now}: {message}\n");
            }
            catch { /* журнал не должен становиться причиной нового сбоя */ }
        }
    }

    /// <summary>Описание одной резервной копии для списка в окне управления копиями.</summary>
    public class BackupInfo
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime Created { get; set; }
        public long Size { get; set; }
        public string DisplayDate => Created.ToString("dd.MM.yyyy HH:mm:ss");
        public string DisplaySize => Size < 1024 ? $"{Size} B" :
                                     Size < 1024 * 1024 ? $"{Size / 1024.0:F1} KB" :
                                     $"{Size / (1024.0 * 1024.0):F1} MB";
    }
}