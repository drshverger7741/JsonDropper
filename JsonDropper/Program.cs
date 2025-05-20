// Program.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace JsonDropper
{
    class Program
    {
        class Config
        {
            public List<string> TargetDirectories { get; set; } = new List<string>();
        }

        // Папка запуска приложения (работает в single-file сборках)
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
                    // Игнорируем повреждённый файл конфигурации
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
            // Аннотация при запуске
            Console.WriteLine("=== JsonDropper ===");
            Console.WriteLine("Запустив двойным кликом приложение, мы можем добавлять, изменять");
            Console.WriteLine("или удалять директорию в которой будут обновляться файлы форм проектов");
            Console.WriteLine();
            Console.WriteLine("После указания директории достаточно перетаскивать формы выгруженные (.zip файлы проекта),");
            Console.WriteLine("на этот EXE и указать в какой директории мы будем их обновлять,он произведет их обновление в проекте");
            Console.WriteLine();

            var cfg = LoadConfig();

            // Режим настройки (нет аргументов)
            if (args.Length == 0)
            {
                while (true)
                {
                    Console.WriteLine("\nСписок директорий для поиска форм:");
                    for (int i = 0; i < cfg.TargetDirectories.Count; i++)
                        Console.WriteLine($"  {i + 1}. {cfg.TargetDirectories[i]}");
                    Console.WriteLine("\nКоманды: a - добавить, d N - удалить N, e N - изменить N, q - выход");
                    Console.WriteLine("* N - индекс директории");
                    Console.Write("Введите команду: ");
                    var cmd = Console.ReadLine()?.Trim() ?? string.Empty;
                    if (cmd.Equals("q", StringComparison.OrdinalIgnoreCase))
                    {
                        SaveConfig(cfg);
                        break;
                    }
                    if (cmd.Equals("a", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Write("Введите путь для добавления: ");
                        var path = Console.ReadLine()?.Trim() ?? string.Empty;
                        if (Directory.Exists(path))
                        {
                            cfg.TargetDirectories.Add(path);
                            SaveConfig(cfg);
                            Console.WriteLine("Добавлено.");
                        }
                        else Console.WriteLine("Неверный путь.");
                    }
                    else if (cmd.StartsWith("d ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(cmd[2..], out int idx) && idx >= 1 && idx <= cfg.TargetDirectories.Count)
                        {
                            cfg.TargetDirectories.RemoveAt(idx - 1);
                            SaveConfig(cfg);
                            Console.WriteLine("Удалено.");
                        }
                        else Console.WriteLine("Неверный номер.");
                    }
                    else if (cmd.StartsWith("e ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(cmd[2..], out int idx) && idx >= 1 && idx <= cfg.TargetDirectories.Count)
                        {
                            Console.Write("Введите новый путь: ");
                            var newPath = Console.ReadLine()?.Trim() ?? string.Empty;
                            if (Directory.Exists(newPath))
                            {
                                cfg.TargetDirectories[idx - 1] = newPath;
                                SaveConfig(cfg);
                                Console.WriteLine("Изменено.");
                            }
                            else Console.WriteLine("Неверный путь.");
                        }
                        else Console.WriteLine("Неверный номер.");
                    }
                    else Console.WriteLine("Неизвестная команда.");
                }
                return 0;
            }

            // При перетаскивании файлов: сначала выводим список файлов
            Console.WriteLine("\nСписок файлов для обработки:");
            for (int i = 0; i < args.Length; i++)
            {
                Console.WriteLine($"  {i + 1}. {Path.GetFileName(args[i])}");
            }
            Console.WriteLine();

            // Выбор директории для обработки
            Console.WriteLine("Список директорий для обработки:");
            for (int i = 0; i < cfg.TargetDirectories.Count; i++)
                Console.WriteLine($"  {i + 1}. {cfg.TargetDirectories[i]}");
            Console.Write("Введите номер директории: ");
            int dirIndex;
            while (!int.TryParse(Console.ReadLine()?.Trim(), out dirIndex)
                   || dirIndex < 1 || dirIndex > cfg.TargetDirectories.Count)
            {
                Console.Write("Неверный ввод. Введите номер директории: ");
            }
            var targetDir = cfg.TargetDirectories[dirIndex - 1];

            // Обрабатываем каждый zip-файл
            foreach (var zipPath in args)
            {
                if (!File.Exists(zipPath) || !zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Пропущен (не .zip или не найден): {zipPath}");
                    continue;
                }
                Console.WriteLine($"\n=== Обработка {Path.GetFileName(zipPath)} в '{targetDir}' ===");
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);
                    var formEntry = archive.GetEntry("form.json");
                    if (formEntry == null)
                    {
                        Console.WriteLine("  В архиве нет form.json — пропускаем.");
                        continue;
                    }
                    string code;
                    using (var s = formEntry.Open())
                    using (var r = new StreamReader(s))
                        code = JObject.Parse(r.ReadToEnd())["Code"]?.ToString()?.Trim() ?? string.Empty;

                    if (string.IsNullOrEmpty(code))
                    {
                        Console.WriteLine("  Ключ 'Code' в form.json пуст — пропускаем.");
                        continue;
                    }

                    var allForms = Directory.GetFiles(targetDir, "form.json", SearchOption.AllDirectories);
                    var matched = new List<string>();
                    foreach (var path in allForms)
                    {
                        try
                        {
                            if (JObject.Parse(File.ReadAllText(path))["Code"]?.ToString()?.Trim() == code)
                                matched.Add(Path.GetDirectoryName(path)!);
                        }
                        catch { }
                    }
                    if (matched.Count == 0)
                    {
                        Console.WriteLine($"  Не найдены form.json с Code='{code}'.");
                        continue;
                    }

                    foreach (var dir in matched)
                    {
                        foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
                        foreach (var d in Directory.GetDirectories(dir)) Directory.Delete(d, true);

                        foreach (var entry in archive.Entries)
                        {
                            var dest = Path.Combine(dir, entry.FullName);
                            if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(dest);
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                                entry.ExtractToFile(dest, overwrite: true);
                            }
                        }
                        Console.WriteLine($"  + Папка обновлена: {dir}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Ошибка: {ex.Message}");
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
