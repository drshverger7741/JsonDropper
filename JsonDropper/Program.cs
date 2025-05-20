// Program.cs

using System.IO.Compression;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace JsonDropper
{
    class Program
    {
        class Config
        {
            public string? TargetDirectory { get; set; }
        }

        // Используем BaseDirectory, чтобы работало в single-file
        static string ExeDir => AppContext.BaseDirectory;
        static string ConfigPath => Path.Combine(ExeDir, "configJsonDropper.json");

        static Config LoadConfig()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath))
                           ?? new Config();
                }
                catch
                {
                    // Если конфиг повреждён — игнорируем
                }
            }
            return new Config();
        }

        static void SaveConfig(Config cfg)
        {
            File.WriteAllText(
                ConfigPath,
                JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true })
            );
        }

        static int Main(string[] args)
        {
            // Анотация при запуске
            Console.WriteLine("=== JsonDropper ===");
            Console.WriteLine("Запустив двойным кликом приложение, мы изменяем директорию");
            Console.WriteLine(" в которой будет происходить поиск форм в подпапках для обновления.");
            Console.WriteLine();
            Console.WriteLine("После указания директории достаточно перетаскивать формы выгруженные (.zip файлы проекта)");
            Console.WriteLine("на этот EXE и он произведет их обновление в проекте");
            Console.WriteLine();

            var cfg = LoadConfig();

            // Настройка директории при отсутствии файлов для обработки
            if (args.Length == 0)
            {
                Console.WriteLine();
                Console.WriteLine(!string.IsNullOrEmpty(cfg.TargetDirectory)
                    ? $"Текущая целевая папка: {cfg.TargetDirectory}"
                    : "Целевая папка ещё не задана.");
                Console.Write("Введите путь к целевой папке и нажмите Enter: ");
                var input = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(input) || !Directory.Exists(input))
                {
                    Console.WriteLine("Ошибка: не указан или не найден путь. Выход.");
                    Pause();
                    return 1;
                }
                cfg.TargetDirectory = input;
                SaveConfig(cfg);
                Console.WriteLine($"Сохранено: {cfg.TargetDirectory}");
                Pause();
                return 0;
            }

            // Проверяем, что конфигурация настроена
            if (string.IsNullOrEmpty(cfg.TargetDirectory) ||
                !Directory.Exists(cfg.TargetDirectory))
            {
                Console.WriteLine("Целевая папка не задана или недоступна. Запустите без аргументов для настройки.");
                Pause();
                return 1;
            }

            // Обрабатываем каждый переданный ZIP
            foreach (var zipPath in args)
            {
                if (!File.Exists(zipPath) ||
                    !zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Пропущен (не .zip или не найден): {zipPath}");
                    continue;
                }

                Console.WriteLine($"\n=== Обработка {Path.GetFileName(zipPath)} ===");
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);

                    // Читаем key "Code" из form.json внутри архива
                    var formEntry = archive.GetEntry("form.json");
                    if (formEntry == null)
                    {
                        Console.WriteLine("  В архиве нет form.json — пропускаем.");
                        continue;
                    }
                    string code;
                    using (var stream = formEntry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        var obj = JObject.Parse(reader.ReadToEnd());
                        code = obj["Code"]?.ToString()?.Trim() ?? string.Empty;
                    }
                    if (string.IsNullOrEmpty(code))
                    {
                        Console.WriteLine("  Ключ 'Code' в form.json пуст — пропускаем.");
                        continue;
                    }

                    // Поиск директорий с подходящим form.json
                    var allFormFiles = Directory.GetFiles(
                        cfg.TargetDirectory, "form.json", SearchOption.AllDirectories);
                    var matchedDirs = new List<string>();
                    foreach (var path in allFormFiles)
                    {
                        try
                        {
                            var existing = JObject.Parse(File.ReadAllText(path));
                            if (existing["Code"]?.ToString()?.Trim() == code)
                                matchedDirs.Add(Path.GetDirectoryName(path)!);
                        }
                        catch { }
                    }
                    if (!matchedDirs.Any())
                    {
                        Console.WriteLine($"  Не найдены form.json с Code='{code}'.");
                        continue;
                    }

                    // В каждой найденной папке очищаем и распаковываем ZIP
                    foreach (var dir in matchedDirs)
                    {
                        // Удаляем содержимое папки
                        foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
                        foreach (var d in Directory.GetDirectories(dir)) Directory.Delete(d, true);

                        // Распаковываем файлы
                        foreach (var entry in archive.Entries)
                        {
                            var dest = Path.Combine(dir, entry.FullName);
                            if (string.IsNullOrEmpty(entry.Name))
                                Directory.CreateDirectory(dest);
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                                entry.ExtractToFile(dest, overwrite: true);
                            }
                        }
                        Console.WriteLine($"  + Обновлена папка {dir} данными из архива");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Ошибка при обработке: {ex.Message}");
                }
            }

            Console.WriteLine("\nГотово.");
            Pause();
            return 0;
        }

        static void Pause()
        {
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey(intercept: true);
        }
    }
}
