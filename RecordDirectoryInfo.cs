using FrooxEngine;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace BetterInventoryBrowser
{
    public class RecordDirectoryInfo : IEquatable<RecordDirectoryInfo>
    {
        public string OwnerId { get; }
        public string Path { get; }
        public string? Url { get; }
        public RecordDirectoryInfo[]? Directories { get; }

        [JsonConstructor]
        public RecordDirectoryInfo(string ownerId, string path, string url, RecordDirectoryInfo[] directories)
        {
            OwnerId = ownerId;
            Path = path;
            Url = url;
            Directories = directories;
        }

        public RecordDirectoryInfo(string ownerId, string path, string? url = null)
        {
            OwnerId = ownerId;
            Path = path;
            Url = url;
            Directories = null;
        }

        public RecordDirectoryInfo(RecordDirectory recordDirectory)
        {
            OwnerId = recordDirectory.OwnerId;
            Path = recordDirectory.Path;
            Url = recordDirectory.IsLink ? recordDirectory.LinkRecord.URL.ToString() : null;
            var reversedDirectories = new List<RecordDirectoryInfo>();
            var currentDir = recordDirectory.ParentDirectory;
            while (currentDir is not null)
            {
                // Directories内のRecordDirectoryInfoはDirectories=nullにする
                reversedDirectories.Add(new RecordDirectoryInfo(currentDir.OwnerId, currentDir.Path, currentDir.IsLink ? currentDir.LinkRecord.URL.ToString() : null));
                currentDir = currentDir.ParentDirectory;
            }
            Directories = reversedDirectories.AsEnumerable().Reverse().ToArray();
        }

        private static MethodInfo setParentDirectoryMethod = AccessTools.PropertySetter(typeof(RecordDirectory), "ParentDirectory");
        public async Task<RecordDirectory> ToRecordDirectory()
        {
            if (Directories is null || Directories.Length == 0)
            {
                return new RecordDirectory(OwnerId, Path, Engine.Current);
            }
            var previous = await Directories[0].ToRecordDirectory();
            foreach (var d in Directories.Skip(1).Append(this))
            {
                if (d.Url is not null)
                {
                    if (Uri.TryCreate(d.Url, UriKind.Absolute, out var recordUri))
                    {
                        var record = (await Engine.Current.Cloud.GetRecord<Record>(recordUri))?.Entity;
                        previous = new RecordDirectory(record, previous, Engine.Current);
                    }
                }
                else
                {
                    var tmp = new RecordDirectory(d.OwnerId, d.Path, Engine.Current);
                    setParentDirectoryMethod.Invoke(tmp, new object[] { previous });
                    previous = tmp;
                }
            }
            return previous;
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (obj is RecordDirectory rd)
            {
                return OwnerId == rd.OwnerId && Path == rd.Path;
            }
            if (GetType() != obj.GetType()) return false;
            var c = (RecordDirectoryInfo)obj;
            return OwnerId == c.OwnerId && Path == c.Path;
        }

        public override int GetHashCode()
        {
            return OwnerId.GetHashCode() ^ Path.GetHashCode();
        }

        public bool Equals(RecordDirectoryInfo other)
        {
            if (other is null) return false;
            return OwnerId == other.OwnerId && Path == other.Path;
        }

        public override string ToString()
        {
            return $"{GetType().Name} OwnerId: {OwnerId}, Path: {Path}, Url: {Url}";
        }

        public string GetFriendlyPath()
        {
            if (!Path.StartsWith("Inventory"))
            {
                return Path;
            }
            var rootOwnerId = Directories is not null && Directories.Length > 0 ? Directories[0].OwnerId : OwnerId;
            if (rootOwnerId == Userspace.UserspaceWorld.LocalUser.UserID)
            {
                return Path.Substring(9);
            }
            return CloudHelper.GetGroupName(rootOwnerId) + Path.Substring(9);
        }
    }
}
