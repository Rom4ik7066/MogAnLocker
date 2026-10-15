using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace MogLocker.AdBlock
{
    public sealed class AdBlockStore
    {
        private const string MutexName = @"Local\MogLocker.AdBlock.State";
        private readonly JavaScriptSerializer serializer;

        public string RootDirectory { get; private set; }
        public string StatePath { get; private set; }

        public AdBlockStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MogLocker", "AdBlock"))
        {
        }

        public AdBlockStore(string rootDirectory)
        {
            if (String.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Не задан каталог настроек AdBlock.", "rootDirectory");
            RootDirectory = Path.GetFullPath(rootDirectory);
            StatePath = Path.Combine(RootDirectory, "settings.json");
            serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            Directory.CreateDirectory(RootDirectory);
        }

        public AdBlockState Read()
        {
            return WithMutex(delegate
            {
                return ReadUnsafe();
            });
        }

        public AdBlockState Update(Action<AdBlockState> update, bool filterRulesChanged = false)
        {
            if (update == null)
                throw new ArgumentNullException("update");
            return WithMutex(delegate
            {
                AdBlockState state = ReadUnsafe();
                update(state);
                EnsureState(state);
                state.Revision++;
                if (filterRulesChanged)
                    state.FilterRevision++;
                WriteUnsafe(state);
                return state;
            });
        }

        public string GetFilterCachePath(string listId)
        {
            if (String.IsNullOrWhiteSpace(listId) ||
                listId.Any(character => !Char.IsLetterOrDigit(character) && character != '-' && character != '_'))
                throw new ArgumentException("Недопустимый идентификатор фильтр-листа.", "listId");
            return Path.Combine(RootDirectory, listId + ".txt");
        }

        public string ReadFilterCache(string listId)
        {
            string path = GetFilterCachePath(listId);
            return File.Exists(path) ? File.ReadAllText(path) : String.Empty;
        }

        public void WriteFilterCache(string listId, string content)
        {
            string path = GetFilterCachePath(listId);
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, content, new System.Text.UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        public static List<FilterListSetting> CreateDefaultFilterLists()
        {
            return new List<FilterListSetting>
            {
                new FilterListSetting
                {
                    Id = "builtin",
                    Name = "MogLocker — базовые рекламные домены",
                    Url = String.Empty,
                    Enabled = true
                },
                new FilterListSetting
                {
                    Id = "easylist",
                    Name = "EasyList — реклама",
                    Url = "https://easylist.to/easylist/easylist.txt",
                    Enabled = true
                },
                new FilterListSetting
                {
                    Id = "easyprivacy",
                    Name = "EasyPrivacy — трекеры",
                    Url = "https://easylist.to/easylist/easyprivacy.txt",
                    Enabled = true
                }
            };
        }

        private AdBlockState ReadUnsafe()
        {
            if (!File.Exists(StatePath))
            {
                AdBlockState defaults = new AdBlockState();
                defaults.FilterLists = CreateDefaultFilterLists();
                WriteUnsafe(defaults);
                return defaults;
            }

            try
            {
                AdBlockState state = serializer.Deserialize<AdBlockState>(File.ReadAllText(StatePath));
                if (state == null)
                    throw new InvalidDataException("Файл настроек AdBlock пуст.");
                EnsureState(state);
                return state;
            }
            catch (Exception ex)
            {
                string backupPath = StatePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Copy(StatePath, backupPath, false);
                throw new InvalidDataException(
                    "Не удалось прочитать настройки AdBlock. Повреждённый файл сохранён как " +
                    backupPath + ".", ex);
            }
        }

        private void WriteUnsafe(AdBlockState state)
        {
            string temporaryPath = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, serializer.Serialize(state), new System.Text.UTF8Encoding(false));
            try
            {
                if (File.Exists(StatePath))
                    File.Replace(temporaryPath, StatePath, null);
                else
                    File.Move(temporaryPath, StatePath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static void EnsureState(AdBlockState state)
        {
            if (state.FilterLists == null)
                state.FilterLists = CreateDefaultFilterLists();
            else if (state.FilterLists.Count == 0)
                state.FilterLists = CreateDefaultFilterLists();
            else if (!state.FilterLists.Any(item =>
                String.Equals(item.Id, "builtin", StringComparison.OrdinalIgnoreCase)))
            {
                FilterListSetting builtIn = CreateDefaultFilterLists()[0];
                state.FilterLists.Insert(0, builtIn);
            }
            if (state.UserRules == null)
                state.UserRules = new List<string>();
            if (state.Allowlist == null)
                state.Allowlist = new List<string>();
            if (state.DomainCounts == null)
                state.DomainCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (state.ResourceTypeCounts == null)
                state.ResourceTypeCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (state.RecentBlocked == null)
                state.RecentBlocked = new List<BlockedRequest>();
        }

        private T WithMutex<T>(Func<T> action)
        {
            using (var mutex = new Mutex(false, MutexName))
            {
                bool acquired = false;
                try
                {
                    try
                    {
                        acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }
                    if (!acquired)
                        throw new TimeoutException("Не удалось получить блокировку файла настроек AdBlock.");
                    return action();
                }
                finally
                {
                    if (acquired)
                        mutex.ReleaseMutex();
                }
            }
        }
    }
}
