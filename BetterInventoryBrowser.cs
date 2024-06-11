using System.Collections.Generic;
using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using FrooxEngine.Store;
using FrooxEngine.UIX;
using Elements.Core;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System;
using Elements.Assets;

namespace BetterInventoryBrowser
{
    public class BetterInventoryBrowser : ResoniteMod
    {
        public override string Name => "BetterInventoryBrowser";
        public override string Author => "hantabaru1014";
        public override string Version => "0.6.2";
        public override string Link => "https://github.com/hantabaru1014/BetterInventoryBrowser";

        private const string MOD_ID = "dev.baru.resonite.BetterInventoryBrowser";
        private const string SIDEBAR_RECT_ID = $"{MOD_ID}.SidebarRect";
        private const string RIGHT_SIDEBAR_RECT_ID = $"{MOD_ID}.RightSidebarRect";
        private const string SIDEBAR_TOGGLE_TXT_ID = $"{MOD_ID}.SidebarToggleButton.Text";
        private const string RIGHT_SIDEBAR_TOGGLE_TXT_ID = $"{MOD_ID}.RightSidebarToggleButton.Text";

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<List<RecordDirectoryInfo>> PinnedDirectoriesKey = 
            new ModConfigurationKey<List<RecordDirectoryInfo>>("_PinnedDirectories", "PinnedDirectories", () => new List<RecordDirectoryInfo>(), true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> IsOpenSidebarKey =
            new ModConfigurationKey<bool>("_IsOpenSidebar", "IsOpenSidebar", () => true, true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> IsOpenRightSidebarKey =
            new ModConfigurationKey<bool>("_IsOpenRightSidebar", "IsOpenRightSidebar", () => false, true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<SortMethod> SelectedSortMethodKey =
            new ModConfigurationKey<SortMethod>("_SelectedSortMethod", "SortMethod", () => SortMethod.Default);

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<int> MaxRecentDirectoryCountKey =
            new ModConfigurationKey<int>("MaxRecentDirectoryCount", "Max recent directory count", () => 6);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> StripRtfTagsOnSortKey = 
            new ModConfigurationKey<bool>("StripRtfTagsOnSort", "Strip RTF tags on sort by name", () => true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> ForceSortByDefaultKey = 
            new ModConfigurationKey<bool>("ForceSortByDefault", "Sort folders by name and items by update on \"Default\"", () => false);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<float> SidebarWidthKey =
            new ModConfigurationKey<float>("SidebarWidth", "Width of Sidebar", () => 180f);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<float> RightSidebarWidthKey =
            new ModConfigurationKey<float>("RightSidebarWidth", "Width of RightSidebar", () => 180f);

        private static ModConfiguration? _config;
        private static List<RecordDirectoryInfo> _recentDirectories = new List<RecordDirectoryInfo>();

        private static FieldInfo _iiui_itemField = AccessTools.Field(typeof(InventoryItemUI), "Item");
        private static FieldInfo _iiui_directoryField = AccessTools.Field(typeof(InventoryItemUI), "Directory");

        public override void OnEngineInit()
        {
            _config = GetConfiguration();
            if (_config == null)
            {
                Error("Config Not Found!!");
                return;
            }
            _config.OnThisConfigurationChanged += OnModConfigurationChanged;

            Harmony harmony = new Harmony(MOD_ID);
            harmony.PatchAll();
            RecordDirectory_GetSubdirectoryAtPath_Patch.Patch(harmony);
        }

        private void OnModConfigurationChanged(ConfigurationChangedEvent configEvent)
        {
            if (configEvent.Key == SidebarWidthKey)
            {
                foreach (var browser in GetPatchTargetBrowsers())
                {
                    GetSidebarRectTransform(browser).Slot.GetComponent<LayoutElement>().PreferredWidth.Value = _config?.GetValue(SidebarWidthKey) ?? 180f;
                    ReGridLayout(browser);
                }
            }
            if (configEvent.Key == RightSidebarWidthKey)
            {
                foreach (var browser in GetPatchTargetBrowsers())
                {
                    GetRightSidebarRectTransform(browser).Slot.GetComponent<LayoutElement>().PreferredWidth.Value = _config?.GetValue(RightSidebarWidthKey) ?? 180f;
                    ReGridLayout(browser);
                }
            }
            if (configEvent.Key == SelectedSortMethodKey)
            {
                ReOpenInventory();
            }
        }

        [HarmonyPatch(typeof(BrowserDialog))]
        class BrowserDialog_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch("OnAttach")]
            static void OnAttach_Postfix(BrowserDialog __instance, SyncRef<SlideSwapRegion> ____swapper, SyncRef<Slot> ____buttonsRoot)
            {
                if (!(__instance is InventoryBrowser) || __instance.World != Userspace.UserspaceWorld || !IsPatchTarget(__instance)) return;
                var originalSwapperSlot = ____swapper.Target.Slot;
                var uiBuilder = new UIBuilder(originalSwapperSlot);
                uiBuilder.HorizontalLayout(0, 0, Alignment.TopLeft).ForceExpandWidth.Value = false;

                // Toggle Sidebar Button
                uiBuilder.Panel().Slot.GetComponent<LayoutElement>().PreferredWidth.Value = 0;
                var isOpenSidebar = _config?.GetValue(IsOpenSidebarKey) ?? true;
                var toggleButton = uiBuilder.Button(isOpenSidebar ? "<<" : ">>");
                toggleButton.RectTransform.AnchorMin.Value = new float2(0, 0.8f);
                toggleButton.RectTransform.OffsetMin.Value = new float2(-25, 0);
                toggleButton.RectTransform.OffsetMax.Value = new float2(-5, -5);
                toggleButton.Slot.GetComponentInChildren<Text>().Slot.AttachComponent<Comment>().Text.Value = SIDEBAR_TOGGLE_TXT_ID;
                toggleButton.LocalPressed += (IButton button, ButtonEventData eventData) =>
                {
                    var nextActive = !(_config?.GetValue(IsOpenSidebarKey) ?? true);
                    foreach (var browser in GetPatchTargetBrowsers())
                    {
                        GetSidebarRectTransform(browser).Slot.ActiveSelf = nextActive;
                        browser.Slot.GetComponentInChildren<Comment>(c => c.Text.Value == SIDEBAR_TOGGLE_TXT_ID)
                            .Slot.GetComponent<Text>().Content.Value = nextActive ? "<<" : ">>";
                        ReGridLayout(browser);
                    }
                    _config?.Set(IsOpenSidebarKey, nextActive);
                };
                uiBuilder.NestOut();
                
                // Sidebar
                var sidebarRt = uiBuilder.Panel();
                sidebarRt.Slot.GetComponent<LayoutElement>().PreferredWidth.Value = _config?.GetValue(SidebarWidthKey) ?? 180f;
                sidebarRt.Slot.AttachComponent<Comment>().Text.Value = SIDEBAR_RECT_ID;
                BuildSidebar(sidebarRt);
                sidebarRt.Slot.ActiveSelf = isOpenSidebar;
                uiBuilder.NestOut();

                // Main Area
                var contentRt = uiBuilder.Panel();
                contentRt.Slot.GetComponent<LayoutElement>().FlexibleWidth.Value = 1f;
                uiBuilder.NestOut();

                // Right Sidebar
                var isOpenRightSidebar = _config?.GetValue(IsOpenRightSidebarKey) ?? false;
                var rightSidebarRt = uiBuilder.Panel();
                rightSidebarRt.Slot.GetComponent<LayoutElement>().PreferredWidth.Value = _config?.GetValue(RightSidebarWidthKey) ?? 180f;
                rightSidebarRt.Slot.AttachComponent<Comment>().Text.Value = RIGHT_SIDEBAR_RECT_ID;
                BuildRightSidebar(rightSidebarRt, ((InventoryBrowser)__instance).SelectedInventoryItem);
                rightSidebarRt.Slot.ActiveSelf = isOpenRightSidebar;
                uiBuilder.NestOut();

                // Right Sidebar Toggle Button
                uiBuilder.Panel().Slot.GetComponent<LayoutElement>().PreferredWidth.Value = 0;
                var toggleRightSidebarButton = uiBuilder.Button(isOpenRightSidebar ? ">>" : "<<");
                toggleRightSidebarButton.RectTransform.AnchorMin.Value = new float2(1, 0.8f);
                toggleRightSidebarButton.RectTransform.OffsetMin.Value = new float2(5, 0);
                toggleRightSidebarButton.RectTransform.OffsetMax.Value = new float2(25, -5);
                toggleRightSidebarButton.Slot.GetComponentInChildren<Text>().Slot.AttachComponent<Comment>().Text.Value = RIGHT_SIDEBAR_TOGGLE_TXT_ID;
                toggleRightSidebarButton.LocalPressed += (IButton button, ButtonEventData eventData) =>
                {
                    var nextActive = !(_config?.GetValue(IsOpenRightSidebarKey) ?? false);
                    foreach (var browser in GetPatchTargetBrowsers())
                    {
                        GetRightSidebarRectTransform(browser).Slot.ActiveSelf = nextActive;
                        browser.Slot.GetComponentInChildren<Comment>(c => c.Text.Value == RIGHT_SIDEBAR_TOGGLE_TXT_ID)
                            .Slot.GetComponent<Text>().Content.Value = nextActive ? ">>" : "<<";
                        ReGridLayout(browser);
                    }
                    _config?.Set(IsOpenRightSidebarKey, nextActive);
                };
                uiBuilder.NestOut();

                ____swapper.Target = contentRt.Slot.AttachComponent<SlideSwapRegion>(true, null);
                originalSwapperSlot.RemoveAllComponents(component => component.WorkerType != typeof(RectTransform));
            }

            [HarmonyPostfix]
            [HarmonyPatch("SetPath")]
            static void SetPath_Postfix(BrowserDialog __instance, SyncRef<Slot> ____pathRoot, List<string> pathChain)
            {
                if (!(__instance is InventoryBrowser inventoryBrowser) || __instance.World != Userspace.UserspaceWorld || !IsPatchTarget(__instance)) return;
                if (pathChain is null) return;
                ____pathRoot.Target[0].GetComponent<HorizontalLayout>().ForceExpandWidth.Value = false;
                var uiBuilder = new UIBuilder(____pathRoot.Target[0]);
                RadiantUI_Constants.SetupDefaultStyle(uiBuilder);
                var currentDir = new RecordDirectoryInfo(inventoryBrowser.CurrentDirectory);
                var pinnedDirs = _config?.GetValue(PinnedDirectoriesKey) ?? new List<RecordDirectoryInfo>();
                var btn = uiBuilder.Button(pinnedDirs.Contains(currentDir) ? "★" : "☆");
                btn.Label.Color.Value = colorX.Yellow;
                btn.LocalPressed += (IButton button, ButtonEventData eventData) =>
                {
                    TogglePinCurrentDirectory();
                    UpdatePinButtonText(btn, currentDir);
                };
                btn.Slot.GetComponent<LayoutElement>().PreferredWidth.Value = 40f;
            }
        }

        [HarmonyPatch(typeof(InventoryBrowser))]
        class InventoryBrowser_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch(nameof(InventoryBrowser.Open))]
            static void Open_Postfix(InventoryBrowser __instance, RecordDirectory directory)
            {
                if (!IsPatchTarget(__instance)) return;
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
                ReBuildAllSidebars();
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
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InventoryBrowser_Patch), nameof(SortDirectories)));
                        Msg("Patched InventoryBrowser.UpdateDirectoryItems for directories");
                    }
                    else if (code.Calls(getRecordsMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InventoryBrowser_Patch), nameof(SortRecords)));
                        Msg("Patched InventoryBrowser.UpdateDirectoryItems for records");
                    }
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("OnItemSelected")]
            static void OnItemSelected_Postfix(InventoryBrowser __instance, BrowserItem? currentItem)
            {
                if (!IsPatchTarget(__instance) || currentItem is null) return;
                BuildRightSidebar(GetRightSidebarRectTransform(__instance), (InventoryItemUI)currentItem);
            }

            [HarmonyPostfix]
            [HarmonyPatch("RecordManager_RecordSaved")]
            static void RecordManager_RecordSaved_Postfix(Record record)
            {
                // https://github.com/hantabaru1014/BetterInventoryBrowser/issues/4
                record.LastModificationTime = record.LastModificationTime.ToLocalTime();
            }

            static IReadOnlyList<RecordDirectory> SortDirectories(IReadOnlyList<RecordDirectory> directories, InventoryBrowser instance)
            {
                if (!IsPatchTarget(instance)) return directories;
                switch (_config?.GetValue(SelectedSortMethodKey) ?? SortMethod.Default)
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

            static IReadOnlyList<Record> SortRecords(IReadOnlyList<Record> records, InventoryBrowser instance)
            {
                if (!IsPatchTarget(instance)) return records;
                switch (_config?.GetValue(SelectedSortMethodKey) ?? SortMethod.Default)
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

            static string GetLastModTimeText(InventoryItemUI item)
            {
                var record = _iiui_itemField.GetValue(item) as Record;
                var recordDirectory = _iiui_directoryField.GetValue(item) as RecordDirectory;
                var time = record?.LastModificationTime ?? recordDirectory?.EntryRecord.LastModificationTime;
                return time.HasValue ? time.Value.ToString() : string.Empty;
            }

            static string SanitizeItemName4Detail(string name)
            {
                return name.Replace('\n', ' ').Replace("<br>", " ");
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

        public static bool IsPatchTarget(BrowserDialog instance)
        {
            // とりあえず LegacyInventory を対象にしない
            return (InventoryBrowser)instance == InventoryBrowser.CurrentUserspaceInventory 
                || instance.Slot.GetComponentInParents<UserspaceRadiantDash>() != null;
        }

        public static IEnumerable<InventoryBrowser> GetPatchTargetBrowsers()
        {
            foreach (var globallyRegistered in Userspace.UserspaceWorld.GetGloballyRegisteredComponents<InventoryBrowser>())
            {
                if (IsPatchTarget(globallyRegistered)) yield return globallyRegistered;
            }
        }

        private static RectTransform GetRectTransformById(InventoryBrowser instance, string id)
        {
            return instance.Slot.GetComponentInChildren<Comment>(c => c.Text.Value == id).Slot.GetComponent<RectTransform>();
        }

        public static RectTransform GetSidebarRectTransform(InventoryBrowser instance)
        {
            return GetRectTransformById(instance, SIDEBAR_RECT_ID);
        }

        public static RectTransform GetRightSidebarRectTransform(InventoryBrowser instance)
        {
            return GetRectTransformById(instance, RIGHT_SIDEBAR_RECT_ID);
        }

        private static readonly MethodInfo _generateContentMethod = AccessTools.Method(typeof(BrowserDialog), "GenerateContent");
        private static void BuildDirectoryButtons(Slot rootSlot, List<RecordDirectoryInfo> directories)
        {
            var uiBuilder = new UIBuilder(rootSlot);
            RadiantUI_Constants.SetupDefaultStyle(uiBuilder);
            uiBuilder.ScrollArea(Alignment.TopCenter);
            uiBuilder.VerticalLayout(8f, 0f, Alignment.TopCenter, null, false);
            uiBuilder.FitContent(SizeFit.Disabled, SizeFit.PreferredSize);
            foreach (var dir in directories)
            {
                var itemBtn = uiBuilder.Button(dir.GetFriendlyPath(), InventoryBrowser.FOLDER_COLOR);
                itemBtn.Label.AlignmentMode.Value = AlignmentMode.LineBased;
                itemBtn.Label.RectTransform.AddFixedPadding(-8f, 2f, 8f, 2f);
                itemBtn.Label.Size.Value *= 0.75f;
                itemBtn.Slot.GetComponent<LayoutElement>().MinHeight.Value = BrowserDialog.DEFAULT_ITEM_SIZE * 0.5f;
                itemBtn.LocalPressed += async (IButton btn, ButtonEventData data) =>
                {
                    if (InventoryBrowser.CurrentUserspaceInventory.Engine.Cloud.CurrentUser is null)
                    {
                        ReBuildAllSidebars();
                        return;
                    }
                    Msg($"Open : {dir}");
                    _generateContentMethod.Invoke(InventoryBrowser.CurrentUserspaceInventory, new object?[] { SlideSwapRegion.Slide.Right, null, null, true });
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

            // 未ログイン状態でアクセス権が無い場所を開くと、インベントリが壊れて操作できなくなるのでその対策
            if (rectTransform.Engine.Cloud.CurrentUser is null) return;

            var uiBuilder = new UIBuilder(rectTransform);
            var vertLayout = uiBuilder.VerticalLayout(8f, 0f, Alignment.TopCenter);
            vertLayout.ForceExpandHeight.Value = false;

            uiBuilder.Text("<color=white>Pinned");
            var pinnedDirsPanel = uiBuilder.Panel().Slot;
            pinnedDirsPanel.GetComponent<LayoutElement>().FlexibleHeight.Value = 1f;
            BuildDirectoryButtons(pinnedDirsPanel, _config?.GetValue(PinnedDirectoriesKey) ?? new List<RecordDirectoryInfo>());
            uiBuilder.NestOut();

            uiBuilder.Text("<color=white>Recent");
            var recentDirsPanel = uiBuilder.Panel().Slot;
            recentDirsPanel.GetComponent<LayoutElement>().FlexibleHeight.Value = 1f;
            BuildDirectoryButtons(recentDirsPanel, _recentDirectories);
            uiBuilder.NestOut();
        }

        private static void BuildRightSidebar(RectTransform rectTransform, InventoryItemUI? item)
        {
            if (rectTransform is null) return;
            rectTransform.Slot.DestroyChildren();
            if (item is null) return;

            var uiBuilder = new UIBuilder(rectTransform);
            var vertLayout = uiBuilder.VerticalLayout(8f, 10f, Alignment.TopCenter);
            vertLayout.ForceExpandHeight.Value = false;
            vertLayout.ForceExpandWidth.Value = true;

            Record? entryRecord = null;
            if (_iiui_itemField.GetValue(item) is Record record)
            {
                entryRecord = record;
                if (Uri.TryCreate(record.ThumbnailURI, UriKind.Absolute, out var result))
                {
                    uiBuilder.Image(result).Slot.GetComponent<LayoutElement>().MinHeight.Value = (_config?.GetValue(RightSidebarWidthKey) ?? 180f) - 10;
                }
            }
            else if (_iiui_directoryField.GetValue(item) is RecordDirectory recordDirectory)
            {
                entryRecord = recordDirectory.EntryRecord;
            }
            if (entryRecord is not null)
            {
                uiBuilder.Text($"<color=white><b>LastUpdated:</b>\n{entryRecord.LastModificationTime}").HorizontalAlign.Value = TextHorizontalAlignment.Left;
            }
        }

        private static void ReBuildAllSidebars()
        {
            foreach (var browser in GetPatchTargetBrowsers())
            {
                BuildSidebar(GetSidebarRectTransform(browser));
            }
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

            ReBuildAllSidebars();
        }

        private static void UpdatePinButtonText(Button button, RecordDirectoryInfo directory)
        {
            var pinnedDirs = _config?.GetValue(PinnedDirectoriesKey) ?? new List<RecordDirectoryInfo>();
            button.Slot.GetComponentInChildren<Text>().Content.Value = pinnedDirs.Contains(directory) ? "★" : "☆";
        }

        private static void ReGridLayout(BrowserDialog instance)
        {
            var layoutRootSlot = ((SyncRef<SlideSwapRegion>)AccessTools.Field(typeof(BrowserDialog), "_swapper").GetValue(instance)).Target.Slot;
            layoutRootSlot = layoutRootSlot.GetComponentInChildren<GridLayout>().Slot;
            var dummy = layoutRootSlot.AddSlot("dummy", false);
            layoutRootSlot.RunInUpdates(3, () =>
            {
                dummy.Destroy();
            });
        }

        private static void ReOpenInventory()
        {
            InventoryBrowser.CurrentUserspaceInventory.Open(InventoryBrowser.CurrentUserspaceInventory.CurrentDirectory, SlideSwapRegion.Slide.Left);
            foreach (var browser in GetPatchTargetBrowsers())
            {
                BuildRightSidebar(GetRightSidebarRectTransform(browser), null);
            }
        }
    }
}