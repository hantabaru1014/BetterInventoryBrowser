using System.Collections.Generic;
using HarmonyLib;
using NeosModLoader;
using FrooxEngine;
using FrooxEngine.UIX;
using BaseX;
using System.Linq;
using System.Reflection.Emit;

namespace BetterInventoryBrowser
{
    public class BetterInventoryBrowser : NeosMod
    {
        public override string Name => "BetterInventoryBrowser";
        public override string Author => "hantabaru1014";
        public override string Version => "0.1.0";
        public override string Link => "https://github.com/hantabaru1014/BetterInventoryBrowser";

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<List<RecordDirectoryInfo>> PinnedDirectoriesKey = 
            new ModConfigurationKey<List<RecordDirectoryInfo>>("_PinnedDirectories", "PinnedDirectories", () => new List<RecordDirectoryInfo>(), true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<int> MaxRecentDirectoryCountKey =
            new ModConfigurationKey<int>("MaxRecentDirectoryCount", "Max recent directory count", () => 6);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> ShouldBeClearCacheKey = 
            new ModConfigurationKey<bool>("ShouldBeClearCache", "If set to true the cache will be cleared", () => false);

        private static ModConfiguration? _config;
        private static RectTransform? _sidebarRect;
        private static List<RecordDirectoryInfo> _recentDirectories = new List<RecordDirectoryInfo>();

        public override void OnEngineInit()
        {
            _config = GetConfiguration();

            Harmony harmony = new Harmony("dev.baru.neos.BetterInventoryBrowser");
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(BrowserDialog))]
        class BrowserDialog_Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch("OnAttach")]
            static void OnAttach_Postfix(BrowserDialog __instance, SyncRef<SlideSwapRegion> ____swapper)
            {
                if (!(__instance is InventoryBrowser) || __instance.World != Userspace.UserspaceWorld) return;
                RectTransform sidebarRt, contentRt;
                var uiBuilder = new UIBuilder(____swapper.Target.Slot);
                uiBuilder.SplitHorizontally(0.125f, out sidebarRt, out contentRt);
                ____swapper.Target = contentRt.Slot.AttachComponent<SlideSwapRegion>(true, null);
                _sidebarRect = sidebarRt;
                BuildSidebar(sidebarRt);
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
                if (directory is null) return;
                var dirInfo = new RecordDirectoryInfo(directory);
                if (!dirInfo.Path.Contains("\\")) return;
                if (_recentDirectories.Count > 0 && _recentDirectories.Contains(dirInfo)) return;
                if (_recentDirectories.Count > 0 && _recentDirectories[0].IsSubDirectory(dirInfo))
                {
                    _recentDirectories[0] = dirInfo;
                }
                else if ((_config?.GetValue(PinnedDirectoriesKey) ?? new List<RecordDirectoryInfo>()).Contains(dirInfo))
                {
                    return;
                }
                else
                {
                    _recentDirectories.Insert(0, dirInfo);
                }

                var maxCount = _config?.GetValue(MaxRecentDirectoryCountKey) ?? 6;
                if (_recentDirectories.Count > maxCount)
                {
                    _recentDirectories.RemoveRange(maxCount, _recentDirectories.Count - maxCount);
                }
                BuildSidebar();
            }
        }

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
                    if (_config?.GetValue(ShouldBeClearCacheKey) ?? false)
                    {
                        RecordDirectoryInfo.ClearCache();
                        _config?.Set(ShouldBeClearCacheKey, false);
                    }
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