using System.Collections.Generic;
using FrooxEngine;

namespace BetterInventoryBrowser
{
    public static class CloudHelper
    {
        private static Dictionary<string, string> _groupNames = new Dictionary<string, string>();

        public static string GetGroupName(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return groupId;
            if (!_groupNames.ContainsKey(groupId))
            {
                _groupNames.Clear();
                foreach (var group in Engine.Current.Cloud.CurrentUserMemberships)
                {
                    _groupNames.Add(group.GroupId, group.GroupName);
                }
            }
            if (_groupNames.TryGetValue(groupId, out string groupName))
            {
                return groupName;
            }
            else
            {
                return groupId;
            }
        }
    }
}
