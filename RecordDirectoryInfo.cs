using FrooxEngine;
using Newtonsoft.Json;
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
            Path = recordDirectory.Path;
        }

        public async Task<RecordDirectory> ToRecordDirectory()
        {
            if (_cache.TryGetValue(this, out var cachedRecordDir))
            {
                return cachedRecordDir;
            }
            var rootName = Path.Split('\\')[0];
            var rootRecord = new RecordDirectory(RootOwnerId, rootName, Engine.Current);
            var result = rootName != Path ? await rootRecord.GetSubdirectoryAtPath(Path.Substring(rootName.Length + 1)) : rootRecord;
            _cache.Add(this, result);
            return result;
        }

        public static void ClearCache()
        {
            _cache.Clear();
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (obj is RecordDirectory rd)
            {
                return RootOwnerId == rd.GetRootDirectory().OwnerId && Path == rd.Path;
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
