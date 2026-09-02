using FrooxEngine;
using Newtonsoft.Json;
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BetterInventoryBrowser
{
    public class RecordDirectoryInfo : IEquatable<RecordDirectoryInfo>
    {
        public string RootOwnerId { get; }
        public string Path { get; }

        private static Dictionary<RecordDirectoryInfo, RecordDirectory> _cache = new();

        [JsonConstructor]
        public RecordDirectoryInfo(string rootOwnerId, string path)
        {
            RootOwnerId = rootOwnerId;
            Path = path;
        }

        public RecordDirectoryInfo(RecordDirectory recordDirectory)
        {
            RootOwnerId = recordDirectory.GetRootDirectory()?.OwnerId ?? "";
            Path = recordDirectory.GetRelativePath(true);
        }

        public async Task<RecordDirectory?> ToRecordDirectory()
        {
            if (_cache.TryGetValue(this, out var cachedRecordDir))
            {
                return cachedRecordDir;
            }
            var segments = Path.Split('\\');
            var current = GetRootRecordDirectory(segments[0]);
            for (var i = 1; i < segments.Length; i++)
            {
                var next = await FindSubdirectory(current, segments[i]);
                if (next is null)
                {
                    ResoniteMod.Warn($"Directory not found: {segments[i]} in {current.Path}");
                    return null;
                }
                current = next;
            }
            _cache.TryAdd(this, current);
            return current;
        }

        private RecordDirectory GetRootRecordDirectory(string rootName)
        {
            var engine = Engine.Current;
            // 既にブラウザが持っているツリーを再利用して、同じディレクトリを二重にロードしないようにする
            var browserRoot = InventoryBrowser.CurrentUserspaceInventory?.CurrentDirectory?.GetRootDirectory();
            if (browserRoot is not null && browserRoot.OwnerId == RootOwnerId && browserRoot.Name == rootName)
            {
                return browserRoot;
            }
            var inventoryRoot = engine.Cloud.InventoryRootDirectory;
            if (inventoryRoot is not null && inventoryRoot.OwnerId == RootOwnerId && inventoryRoot.Name == rootName)
            {
                return inventoryRoot;
            }
            return new RecordDirectory(RootOwnerId, rootName, engine);
        }

        private static async Task<RecordDirectory?> FindSubdirectory(RecordDirectory directory, string name)
        {
            // https://github.com/hantabaru1014/BetterInventoryBrowser/issues/13
            // RecordDirectory.GetSubdirectoryAtPath は各階層をクラウドから直接フルロードするため、
            // ローカルキャッシュとマージされず、ディレクトリ内の全レコードが LocalDB に再キャッシュされて
            // 数秒後に大量の DB 書き込みが走り、フレームレートが落ちる。
            // vanilla の InventoryBrowser.OpenDirectory と同様に、まずローカルキャッシュから読み込む。
            if (directory.CurrentLoadState == RecordDirectory.LoadState.NotLoaded)
            {
                try
                {
                    await directory.TryLocalCacheLoad();
                }
                catch (Exception e)
                {
                    ResoniteMod.Warn($"Failed to load local cache of {directory.Path}: {e}");
                }
            }
            if (directory.CurrentLoadState != RecordDirectory.LoadState.NotLoaded)
            {
                var cached = directory.GetSubdirectory(name);
                if (cached is not null)
                {
                    return cached;
                }
            }
            await directory.EnsureFullyLoaded();
            return directory.GetSubdirectory(name);
        }

        public static void ClearCache()
        {
            _cache.Clear();
        }

        public bool RemoveCache()
        {
            return _cache.Remove(this);
        }

        public void RegisterCache(RecordDirectory recordDirectory)
        {
            if (_cache.ContainsKey(this))
            {
                _cache[this] = recordDirectory;
            }
            else
            {
                _cache.Add(this, recordDirectory);
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (obj is RecordDirectory rd)
            {
                return RootOwnerId == rd.GetRootDirectory()?.OwnerId && Path == rd.GetRelativePath(true);
            }
            if (GetType() != obj.GetType()) return false;
            var c = (RecordDirectoryInfo)obj;
            return RootOwnerId == c.RootOwnerId && Path == c.Path;
        }

        public override int GetHashCode()
        {
            return RootOwnerId.GetHashCode() ^ Path.GetHashCode();
        }

        public bool Equals(RecordDirectoryInfo other)
        {
            if (other is null) return false;
            return RootOwnerId == other.RootOwnerId && Path == other.Path;
        }

        public override string ToString()
        {
            return $"{GetType().Name} RootOwnerId: {RootOwnerId}, Path: {Path}";
        }

        public string GetFriendlyPath()
        {
            if (!Path.StartsWith(InventoryBrowser.INVENTORY_ROOT) || !Path.Contains("\\"))
            {
                return Path;
            }
            if (RootOwnerId == Userspace.UserspaceWorld.LocalUser.UserID)
            {
                return Path.Substring(InventoryBrowser.INVENTORY_ROOT.Length);
            }
            return CloudHelper.GetGroupName(RootOwnerId) + Path.Substring(InventoryBrowser.INVENTORY_ROOT.Length);
        }

        public bool IsSubDirectory(RecordDirectoryInfo directoryInfo)
        {
            var splitPath = directoryInfo.Path.Split('\\');
            if (splitPath.Length < 2) return false;
            if (Path + "\\" + splitPath[splitPath.Length - 1] == directoryInfo.Path) return RootOwnerId == directoryInfo.RootOwnerId;
            return false;
        }
    }
}
