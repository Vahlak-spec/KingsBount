using System;
using System.IO;
using UnityEngine;

namespace KingdomBouns.Core
{
    /// <summary>
    /// Зберігає об'єкти як JSON-файли «ключ.json» у підтеці persistentDataPath.
    /// Обмеження JsonUtility: тип має бути [Serializable] класом/структурою, Dictionary не серіалізується (використовуй List пар).
    /// Запис іде через тимчасовий файл, щоб збій посеред запису не зламав попереднє збереження.
    /// </summary>
    public sealed class JsonFileStorage
    {
        private const string EXTENSION = ".json";
        private const string TEMP_EXTENSION = ".tmp";

        public string DirectoryPath { get; }

        public JsonFileStorage(string folderName)
        {
            DirectoryPath = Path.Combine(Application.persistentDataPath, folderName);
        }

        public bool Has(string key) => File.Exists(PathFor(key));

        public bool Any()
        {
            return Directory.Exists(DirectoryPath)
                && Directory.GetFiles(DirectoryPath, "*" + EXTENSION).Length > 0;
        }

        public void Write<T>(string key, T data)
        {
            string path = PathFor(key);
            string tempPath = path + TEMP_EXTENSION;

            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(tempPath, JsonUtility.ToJson(data, Application.isEditor));

                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Не вдалося записати '{key}': {e.Message}");
            }
        }

        public bool TryRead<T>(string key, out T data)
        {
            data = default;
            string path = PathFor(key);
            if (!File.Exists(path))
                return false;

            try
            {
                data = JsonUtility.FromJson<T>(File.ReadAllText(path));
                return data != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Не вдалося прочитати '{key}': {e.Message}");
                return false;
            }
        }

        public void Delete(string key)
        {
            string path = PathFor(key);
            if (File.Exists(path))
                File.Delete(path);
        }

        public void DeleteAll()
        {
            if (!Directory.Exists(DirectoryPath))
                return;

            foreach (string file in Directory.GetFiles(DirectoryPath, "*" + EXTENSION))
                File.Delete(file);
        }

        private string PathFor(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"Некоректний ключ збереження: '{key}'");

            return Path.Combine(DirectoryPath, key + EXTENSION);
        }
    }
}
