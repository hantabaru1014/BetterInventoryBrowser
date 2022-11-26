using System.Collections.Generic;
using HarmonyLib;
using NeosModLoader;
using FrooxEngine;
using FrooxEngine.UIX;
using BaseX;

namespace BetterInventoryBrowser
{
    public class BetterInventoryBrowser : NeosMod
    {
        public override string Name => "BetterInventoryBrowser";
        public override string Author => "hantabaru1014";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/hantabaru1014/BetterInventoryBrowser";

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<List<RecordDirectoryInfo>> PinnedDirectories = 
            new ModConfigurationKey<List<RecordDirectoryInfo>>("PinnedDirectories", "PinnedDirectories", () => new List<RecordDirectoryInfo>(), true);

        private static ModConfiguration? config;
        private static List<RecordDirectoryInfo> _pinnedDirectories = new();
        private static RectTransform? _sidebarRect;

        public override void OnEngineInit()
        {
            config = GetConfiguration();

            _pinnedDirectories = config?.GetValue(PinnedDirectories) ?? new List<RecordDirectoryInfo>();

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
        }

        private static void BuildSidebar(RectTransform rectTransform)
        {
            rectTransform.Slot.DestroyChildren();
            Msg("Build SideBar");

            var uiBuilder = new UIBuilder(rectTransform);
            var vertLayout = uiBuilder.VerticalLayout(8f, 0f, Alignment.TopCenter);
            vertLayout.ForceExpandHeight.Value = false;
            uiBuilder.Text("Pinned");

            foreach (var pinnedDir in _pinnedDirectories)
            {
                var itemBtn = uiBuilder.Button(pinnedDir.GetFriendlyPath());
                itemBtn.Slot.GetComponent<LayoutElement>().PreferredHeight.Value = BrowserDialog.DEFAULT_ITEM_SIZE * 0.5f;
                itemBtn.LocalPressed += async (IButton btn, ButtonEventData data) =>
                {
                    Msg($"Pressed item : {pinnedDir}");
                    var dir = await pinnedDir.ToRecordDirectory();
                    InventoryBrowser.CurrentUserspaceInventory.RunSynchronously(() =>
                    {
                        InventoryBrowser.CurrentUserspaceInventory?.Open(dir, SlideSwapRegion.Slide.Right);
                    }, true);
                };
            }

            var addBtn = uiBuilder.Button("Pin");
            addBtn.Slot.GetComponent<LayoutElement>().PreferredHeight.Value = BrowserDialog.DEFAULT_ITEM_SIZE * 0.5f;
            addBtn.LocalPressed += (IButton btn, ButtonEventData data) => {
                TogglePinCurrentDirectory();
            };
        }

        private static void BuildSidebar()
        {
            if (_sidebarRect is null) return;
            BuildSidebar(_sidebarRect);
        }

        private static void TogglePinCurrentDirectory()
        {
            var currentDir = new RecordDirectoryInfo(InventoryBrowser.CurrentUserspaceInventory.CurrentDirectory);
            if (_pinnedDirectories.Remove(currentDir))
            {
                Msg($"UnPinned {currentDir}");
            }
            else
            {
                _pinnedDirectories.Add(currentDir);
                config?.Set(PinnedDirectories, _pinnedDirectories);
                Msg($"AddPin: {currentDir}");
            }
            
            BuildSidebar();
        }
    }
}