using System.Collections.Generic;
using HarmonyLib;
using NeosModLoader;
using FrooxEngine;
using FrooxEngine.UIX;
using BaseX;
using CodeX;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System;

namespace BetterInventoryBrowser
{
    public class BetterInventoryBrowser : NeosMod
    {
        public override string Name => "BetterInventoryBrowser";
        public override string Author => "hantabaru1014";
        public override string Version => "0.4.0";
        public override string Link => "https://github.com/hantabaru1014/BetterInventoryBrowser";

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<List<RecordDirectoryInfo>> PinnedDirectoriesKey = 
            new ModConfigurationKey<List<RecordDirectoryInfo>>("_PinnedDirectories", "PinnedDirectories", () => new List<RecordDirectoryInfo>(), true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<int> MaxRecentDirectoryCountKey =
            new ModConfigurationKey<int>("MaxRecentDirectoryCount", "Max recent directory count", () => 6);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> StripRtfTagsOnSortKey = 
            new ModConfigurationKey<bool>("StripRtfTagsOnSort", "Strip RTF tags on sort by name", () => true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> ForceSortByDefaultKey = 
            new ModConfigurationKey<bool>("ForceSortByDefault", "Sort folders by name and files by update by \"Default\"", () => false);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<float> SidebarWidthKey =
            new ModConfigurationKey<float>("SidebarWidth", "Width of Sidebar", () => 180f);

        private static ModConfiguration? _config;
        private static RectTransform? _sidebarRect;
        private static List<RecordDirectoryInfo> _recentDirectories = new List<RecordDirectoryInfo>();
        private static RectTransform? _sortButtonsRoot;
        private static SortMethod _currentSortMethod = SortMethod.Default;

        public override void OnEngineInit()
        {
            _config = GetConfiguration();
            if (_config == null)
            {
                Error("Config Not Found!!");
                return;
            }
            _config.OnThisConfigurationChanged += OnModConfigurationChanged;

            Harmony harmony = new Harmony("dev.baru.neos.BetterInventoryBrowser");
            harmony.PatchAll();
            RecordDirectory_GetSubdirectoryAtPath_Patch.Patch(harmony);
        }

        private void OnModConfigurationChanged(ConfigurationChangedEvent configEvent)
        {
            if (configEvent.Key == SidebarWidthKey && _sidebarRect != null)
            {
                _sidebarRect.Slot.GetComponent<LayoutElement>().PreferredWidth.Value = _config?.GetValue(SidebarWidthKey) ?? 180f;
            }
        }

        [HarmonyPatch(typeof(BrowserDialog))]
        class BrowserDialog_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch("OnAttach")]
            static void OnAttach_Postfix(BrowserDialog __instance, SyncRef<SlideSwapRegion> ____swapper, SyncRef<Slot> ____buttonsRoot)
            {
                if (!(__instance is InventoryBrowser) || __instance.World != Userspace.UserspaceWorld) return;
                RectTransform sidebarRt, contentRt;
                var originalSwapperSlot = ____swapper.Target.Slot;
                var uiBuilder = new UIBuilder(originalSwapperSlot);
                uiBuilder.HorizontalLayout(0, 0, Alignment.MiddleCenter).ForceExpandWidth.Value = false;

                // Toggle Sidebar Button
                uiBuilder.Panel().Slot.GetComponent<LayoutElement>().PreferredWidth.Value = 0;
                var toggleButton = uiBuilder.Button("<<");
                toggleButton.RectTransform.AnchorMin.Value = new float2(0, 0.8f);
                toggleButton.RectTransform.OffsetMin.Value = new float2(-25, 0);
                toggleButton.RectTransform.OffsetMax.Value = new float2(-5, -5);
                toggleButton.LocalPressed += (IButton button, ButtonEventData eventData) =>
                {
                    if (_sidebarRect == null) return;
                    var nextActive = !_sidebarRect.Slot.ActiveSelf;
                    _sidebarRect.Slot.ActiveSelf = nextActive;
                    toggleButton.Slot.GetComponentInChildren<Text>().Content.Value = nextActive ? "<<" : ">>";
                };
                uiBuilder.NestOut();
                
                // Sidebar
                sidebarRt = uiBuilder.Panel();
                sidebarRt.Slot.GetComponent<LayoutElement>().PreferredWidth.Value = _config?.GetValue(SidebarWidthKey) ?? 180f;
                uiBuilder.NestOut();

                // Main Area
                contentRt = uiBuilder.Panel();
                contentRt.Slot.GetComponent<LayoutElement>().FlexibleWidth.Value = 1f;
                uiBuilder.NestOut();

                ____swapper.Target = contentRt.Slot.AttachComponent<SlideSwapRegion>(true, null);
                originalSwapperSlot.RemoveAllComponents(component => component.WorkerType != typeof(RectTransform));
                _sidebarRect = sidebarRt;
                BuildSidebar(sidebarRt);

                var header2 = ____buttonsRoot.Target.Parent;
                header2.DestroyChildren();
                var uiBuilder2 = new UIBuilder(header2);
                uiBuilder2.SplitHorizontally(0.3f, out var leftRt, out var rightRt);
                var buttonsBuilder = new UIBuilder(rightRt);
                buttonsBuilder.HorizontalLayout(0f, 0f, Alignment.MiddleRight).ForceExpandWidth.Value = false;
                ____buttonsRoot.Target = buttonsBuilder.Root;
                BuildSortButtons(leftRt);
            }

            [HarmonyPostfix]
            [HarmonyPatch("SetPath")]
            static void SetPath_Postfix(BrowserDialog __instance, SyncRef<Slot> ____pathRoot, List<string> pathChain)
            {
                if (!(__instance is InventoryBrowser inventoryBrowser) || __instance.World != Userspace.UserspaceWorld) return;
                if (pathChain is null) return;
                ____pathRoot.Target[0].GetComponent<HorizontalLayout>().ForceExpandWidth.Value = false;
                var uiBuilder = new UIBuilder(____pathRoot.Target[0]);
                var currentDir = new RecordDirectoryInfo(inventoryBrowser.CurrentDirectory);
                var pinnedDirs = _config?.GetValue(PinnedDirectoriesKey) ?? new List<RecordDirectoryInfo>();
                var btn = uiBuilder.Button(pinnedDirs.Contains(currentDir) ? "★" : "☆", color.Yellow);
                btn.LocalPressed += (IButton button, ButtonEventData eventData) =>
                {
                    TogglePinCurrentDirectory();
                    UpdatePinButtonText(btn, currentDir);
                };
                btn.Slot.GetComponent<LayoutElement>().PreferredWidth.Value = 40f;
            }

            [HarmonyTranspiler]
            [HarmonyPatch("SetPath")]
            static IEnumerable<CodeInstruction> SetPath_Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = instructions.ToList();
                var onGoUpMethod = AccessTools.Method(typeof(BrowserDialog), "OnGoUp");
                var foundOnGoUp = false;
                for (var i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Ldftn && codes[i].OperandIs(onGoUpMethod))
                    {
                        foundOnGoUp = true;
                        continue;
                    }
                    // HACK: 本当はcode.Calls(UIBuilder::Button)みたいに直接探したいが、MethodInfoがGenericのせいで上手く取得できない
                    if (foundOnGoUp && codes[i].opcode == OpCodes.Pop && codes[i-1].opcode == OpCodes.Callvirt)
                    {
                        codes[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BrowserDialog_Patch), nameof(BrowserDialog_Patch.SetFlexibleWidthToOne)));
                        Msg("Patched BrowserDialog.SetPath");
                        break;
                    }
                }
                return codes.AsEnumerable();
            }

            static void SetFlexibleWidthToOne(Button button)
            {
                button.Slot.GetComponent<LayoutElement>().FlexibleWidth.Value = 1f;
            }
        }

        [HarmonyPatch(typeof(InventoryBrowser))]
        class InventoryBrowser_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(InventoryBrowser.Open))]
            static void Open_Postfix(RecordDirectory directory)
            {
                if (directory is null)
                {
                    // ShowInventoryOwnersからnullで呼ばれたタイミングではキャッシュクリアする
                    RecordDirectoryInfo.ClearCache();
                    Msg("Cleared cache");
                    return;
                }
                var dirInfo = new RecordDirectoryInfo(directory);
                if (_recentDirectories.Contains(dirInfo))
                {
                    dirInfo.RegisterCache(directory);
                    return;
                }
                if (_recentDirectories.Count > 0 && _recentDirectories[0].IsSubDirectory(dirInfo))
                {
                    _recentDirectories[0].RemoveCache();
                    _recentDirectories[0] = dirInfo;
                    dirInfo.RegisterCache(directory);
                }
                else if ((_config?.GetValue(PinnedDirectoriesKey) ?? new List<RecordDirectoryInfo>()).Contains(dirInfo))
                {
                    dirInfo.RegisterCache(directory);
                    return;
                }
                else if (!dirInfo.Path.Contains("\\"))
                {
                    return;
                }
                else
                {
                    _recentDirectories.Insert(0, dirInfo);
                    dirInfo.RegisterCache(directory);
                }

                var maxCount = _config?.GetValue(MaxRecentDirectoryCountKey) ?? 6;
                if (_recentDirectories.Count > maxCount)
                {
                    foreach (var dirInfoToRemove in _recentDirectories.GetRange(maxCount, _recentDirectories.Count - maxCount))
                    {
                        dirInfoToRemove.RemoveCache();
                    }
                    _recentDirectories.RemoveRange(maxCount, _recentDirectories.Count - maxCount);
                }
                BuildSidebar();
            }

            [HarmonyTranspiler]
            [HarmonyPatch("UpdateDirectoryItems")]
            static IEnumerable<CodeInstruction> UpdateDirectoryItems_Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var getSubdirMethod = AccessTools.PropertyGetter(typeof(RecordDirectory), "Subdirectories");
                var getRecordsMethod = AccessTools.PropertyGetter(typeof(RecordDirectory), "Records");
                foreach (var code in instructions)
                {
                    yield return code;
                    if (code.Calls(getSubdirMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InventoryBrowser_Patch), nameof(SortDirectories)));
                        Msg("Patched InventoryBrowser.UpdateDirectoryItems for directories");
                    }
                    else if (code.Calls(getRecordsMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InventoryBrowser_Patch), nameof(SortRecords)));
                        Msg("Patched InventoryBrowser.UpdateDirectoryItems for records");
                    }
                }
            }

            static IReadOnlyList<RecordDirectory> SortDirectories(IReadOnlyList<RecordDirectory> directories)
            {
                switch (_currentSortMethod)
                {
                    case SortMethod.Updated:
                        return directories.OrderBy(d => d.EntryRecord.LastModificationTime).ToList();
                    case SortMethod.Created:
                        return directories.OrderBy(d => d.EntryRecord.CreationTime).ToList();
                    case SortMethod.Default:
                        if (_config?.GetValue(ForceSortByDefaultKey) ?? false)
                        {
                            goto case SortMethod.Name;
                        }
                        else
                        {
                            goto default;
                        }
                    case SortMethod.Name:
                        return directories.OrderBy(d => d.Name.Contains("<") && 
                            (_config?.GetValue(StripRtfTagsOnSortKey) ?? false) ? new StringRenderTree(d.Name).GetRawString() : d.Name).ToList();
                    default:
                        return directories;
                }
            }

            static IReadOnlyList<Record> SortRecords(IReadOnlyList<Record> records)
            {
                switch (_currentSortMethod)
                {
                    case SortMethod.Default:
                        if (_config?.GetValue(ForceSortByDefaultKey) ?? false)
                        {
                            goto case SortMethod.Updated;
                        }
                        else
                        {
                            goto default;
                        }
                    case SortMethod.Updated:
                        return records.OrderBy(r => r.LastModificationTime).ToList();
                    case SortMethod.Created:
                        return records.OrderBy(r => r.CreationTime).ToList();
                    case SortMethod.Name:
                        return records.OrderBy(r => r.Name.Contains("<") && 
                            (_config?.GetValue(StripRtfTagsOnSortKey) ?? false) ? new StringRenderTree(r.Name).GetRawString() : r.Name).ToList();
                    default:
                        return records;
                }
            }
        }

        class RecordDirectory_GetSubdirectoryAtPath_Patch
        {
            static readonly Type _targetInternalClass = typeof(RecordDirectory).GetNestedType("<GetSubdirectoryAtPath>d__75", BindingFlags.Instance | BindingFlags.NonPublic);

            public static void Patch(Harmony harmony)
            {
                var targetMethod = AccessTools.Method(_targetInternalClass, "MoveNext");
                var transpiler = AccessTools.Method(typeof(RecordDirectory_GetSubdirectoryAtPath_Patch), nameof(Transpiler));
                harmony.Patch(targetMethod, transpiler: new HarmonyMethod(transpiler));
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var lastLdfldIndex = -1;
                var codes = instructions.ToList();
                for (var i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Ldfld)
                    {
                        lastLdfldIndex = i;
                    }
                    if (lastLdfldIndex != -1 && codes[i].opcode == OpCodes.Stloc_3)
                    {
                        codes.Insert(lastLdfldIndex + 1, new CodeInstruction(OpCodes.Call,
                            AccessTools.Method(typeof(RecordDirectory_GetSubdirectoryAtPath_Patch), nameof(SanitizePath))));
                        Msg("Patched RecordDirectory.GetSubdirectoryAtPath");
                        break;
                    }
                }
                return codes.AsEnumerable();
            }

            static string SanitizePath(string path)
            {
                // HACK: RTFタグの閉じタグにあるスラッシュをパス区切りと判別するのが
                // 面倒なのでスラッシュでのパス区切りをひとまずサポートしない
                return path.Replace('/', 'x');
            }
        }

        public enum SortMethod
        {
            Default, Name, Updated, Created
        }

        private static void BuildSortButtons(RectTransform rootRect)
        {
            _sortButtonsRoot = rootRect;
            rootRect.Slot.DestroyChildren();
            var uiBuilder = new UIBuilder(rootRect);
            uiBuilder.HorizontalLayout(4f, 4f, Alignment.MiddleLeft);
            uiBuilder.Text("Sort:");
            uiBuilder.FitContent(SizeFit.MinSize, SizeFit.Disabled);
            foreach (SortMethod sortMethod in Enum.GetValues(typeof(SortMethod)))
            {
                var btn = uiBuilder.Button(sortMethod.ToString());
                btn.LocalPressed += (IButton btn, ButtonEventData data) =>
                {
                    UpdateSortMethod(sortMethod);
                };
                btn.Enabled = _currentSortMethod != sortMethod;
            }
        }
        private static void BuildSortButtons()
        {
            if (_sortButtonsRoot is null) return;
            BuildSortButtons(_sortButtonsRoot);
        }

        private static void UpdateSortMethod(SortMethod sortMethod)
        {
            _currentSortMethod = sortMethod;
            InventoryBrowser.CurrentUserspaceInventory.Open(InventoryBrowser.CurrentUserspaceInventory.CurrentDirectory, SlideSwapRegion.Slide.Left);
            BuildSortButtons();
        }

        private static readonly MethodInfo _generateContentMethod = AccessTools.Method(typeof(BrowserDialog), "GenerateContent");
        private static void BuildDirectoryButtons(Slot rootSlot, List<RecordDirectoryInfo> directories)
        {
            var uiBuilder = new UIBuilder(rootSlot);
            uiBuilder.ScrollArea(Alignment.TopCenter);
            uiBuilder.VerticalLayout(8f, 0f, Alignment.TopCenter).ForceExpandHeight.Value = false;
            uiBuilder.FitContent(SizeFit.Disabled, SizeFit.PreferredSize);
            foreach (var dir in directories)
            {
                var itemBtn = uiBuilder.Button(dir.GetFriendlyPath());
                itemBtn.Slot.GetComponent<LayoutElement>().MinHeight.Value = BrowserDialog.DEFAULT_ITEM_SIZE * 0.5f;
                itemBtn.LocalPressed += async (IButton btn, ButtonEventData data) =>
                {
                    Msg($"Open : {dir}");
                    _generateContentMethod.Invoke(InventoryBrowser.CurrentUserspaceInventory, new object[] { SlideSwapRegion.Slide.Right, true });
                    var recordDir = await dir.ToRecordDirectory();
                    InventoryBrowser.CurrentUserspaceInventory.RunSynchronously(() =>
                    {
                        InventoryBrowser.CurrentUserspaceInventory?.Open(recordDir, SlideSwapRegion.Slide.Right);
                    }, true);
                };
            }
        }

        private static void BuildSidebar(RectTransform rectTransform)
        {
            rectTransform.Slot.DestroyChildren();

            var uiBuilder = new UIBuilder(rectTransform);
            var vertLayout = uiBuilder.VerticalLayout(8f, 0f, Alignment.TopCenter);
            vertLayout.ForceExpandHeight.Value = false;

            uiBuilder.Text("Pinned");
            var pinnedDirsPanel = uiBuilder.Panel().Slot;
            pinnedDirsPanel.GetComponent<LayoutElement>().FlexibleHeight.Value = 1f;
            BuildDirectoryButtons(pinnedDirsPanel, _config?.GetValue(PinnedDirectoriesKey) ?? new List<RecordDirectoryInfo>());
            uiBuilder.NestOut();

            uiBuilder.Text("Recent");
            var recentDirsPanel = uiBuilder.Panel().Slot;
            recentDirsPanel.GetComponent<LayoutElement>().FlexibleHeight.Value = 1f;
            BuildDirectoryButtons(recentDirsPanel, _recentDirectories);
            uiBuilder.NestOut();
        }

        private static void BuildSidebar()
        {
            if (_sidebarRect is null) return;
            BuildSidebar(_sidebarRect);
        }

        private static void TogglePinCurrentDirectory()
        {
            var currentDir = new RecordDirectoryInfo(InventoryBrowser.CurrentUserspaceInventory.CurrentDirectory);
            var pinnedDirs = _config?.GetValue(PinnedDirectoriesKey) ?? new List<RecordDirectoryInfo>();
            if (pinnedDirs.Remove(currentDir))
            {
                Msg($"UnPinned {currentDir}");
            }
            else
            {
                pinnedDirs.Add(currentDir);
                Msg($"AddPin: {currentDir}");
            }
            _config?.Set(PinnedDirectoriesKey, pinnedDirs);

            BuildSidebar();
        }

        private static void UpdatePinButtonText(Button button, RecordDirectoryInfo directory)
        {
            var pinnedDirs = _config?.GetValue(PinnedDirectoriesKey) ?? new List<RecordDirectoryInfo>();
            button.Slot.GetComponentInChildren<Text>().Content.Value = pinnedDirs.Contains(directory) ? "★" : "☆";
        }
    }
}