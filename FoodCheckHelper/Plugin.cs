using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using ImGuiScene;

namespace FoodCheckHelper
{
    public class Plugin : IDalamudPlugin
    {
        public const string Name = "FoodCheckHelper";
        string IDalamudPlugin.Name => Name;

        public const uint StatusFood = 48;
        public const float StatusReminingMin = 10 * 60 * 1000;

        [PluginService] public static DalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static Framework Framework { get; set; } = null!;
        [PluginService] public static ChatGui ChatGui { get; set; } = null!;
        [PluginService] public static GameGui GameGui { get; set; } = null!;
        [PluginService] public static SigScanner SigScanner { get; set; } = null!;
        [PluginService] public static CommandManager CommandManager { get; set; } = null!;
        [PluginService] public static PartyList PartyList { get; set; } = null!;
        [PluginService] public static DataManager DataManager { get; set; } = null!;

		[UnmanagedFunctionPointer(CallingConvention.ThisCall, CharSet = CharSet.Ansi)]
        private delegate IntPtr CountdownTimer(IntPtr p1);

        private readonly Hook<CountdownTimer> countdownTimerhook = null!;

		private readonly TextureWrap checkTexture = null!;
		private readonly TextureWrap foodTexture = null!;

        public Plugin()
        {
            try
            {
                var countdownPtr = SigScanner.ScanText("48 89 5C 24 ?? 57 48 83 EC 40 8B 41");
                this.countdownTimerhook = new Hook<CountdownTimer>(countdownPtr, this.CountdownTimerFunc);
                this.countdownTimerhook.Enable();

#pragma warning disable CS8601 // 가능한 null 참조 할당입니다.
                this.checkTexture = DataManager.GetImGuiTexture("ui/uld/ReadyCheck_hr1.tex") ?? DataManager.GetImGuiTexture("ui/uld/ReadyCheck.tex");
                this.foodTexture = DataManager.GetImGuiTexture("ui/icon/016000/016202.tex");
#pragma warning restore CS8601 // 가능한 null 참조 할당입니다.

                PluginInterface.UiBuilder.Draw += this.UiBuilder_Draw;
            }
            catch (Exception ex)
            {
                PluginLog.LogError(ex, Name);
                this.Dispose(true);
            }
        }

        ~Plugin()
        {
            this.Dispose(false);
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool disposed = false;
        protected virtual void Dispose(bool disposing)
        {
            if (this.disposed) return;
            this.disposed = true;

            if (disposing)
            {
				this.foodTexture?.Dispose();
				this.checkTexture?.Dispose();

                this.countdownTimerhook?.Dispose();

                PluginInterface.UiBuilder.Draw -= this.UiBuilder_Draw;
            }
        }

        private readonly ManualResetEventSlim uiVisiable = new(false);
        private IntPtr CountdownTimerFunc(IntPtr value)
        {
            float countDownPointerValue = Marshal.PtrToStructure<float>((IntPtr)value + 0x2c);
            PluginLog.Verbose("CountdownTimerFunc: {0}", countDownPointerValue);

            if (countDownPointerValue < 0)
            {
                uiVisiable.Reset();
            }
            else
            {
                uiVisiable.Set();
            }

            return this.countdownTimerhook.Original(value);
        }

		protected bool mReadyCheckResultsWindowVisible = false;
		private unsafe void UiBuilder_Draw()
        {
            if (!uiVisiable.IsSet) return;

            const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration |
                                            ImGuiWindowFlags.NoSavedSettings |
                                            ImGuiWindowFlags.NoMove |
                                            ImGuiWindowFlags.NoMouseInputs |
                                            ImGuiWindowFlags.NoFocusOnAppearing |
                                            ImGuiWindowFlags.NoBackground |
                                            ImGuiWindowFlags.NoNav;

            ImGuiHelpers.ForceNextWindowMainViewport();
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().Pos);
            ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);

			if (ImGui.Begin("##FoodCheckHelperOverlay", flags))
			{
				var pPartyList = (AtkUnitBase*)GameGui.GetAddonByName("_PartyList", 1);

                var idx = 0;
                foreach (var member in PartyList)
                {
                    if (!member.Statuses.Any(e => e.StatusId == StatusFood && e.RemainingTime < StatusReminingMin))
                    {
                        this.DrawOnPartyList(idx, pPartyList, ImGui.GetWindowDrawList());
                    }
                    idx++;
                }
			}
			ImGui.End();
		}

        unsafe protected void DrawOnPartyList(int listIndex, AtkUnitBase* pPartyList, ImDrawListPtr drawList)
        {
            int partyMemberNodeIndex = 21 - listIndex;
            int iconNodeIndex = 4;

            var pPartyMemberNode = pPartyList->UldManager.NodeListSize > partyMemberNodeIndex ? (AtkComponentNode*)pPartyList->UldManager.NodeList[partyMemberNodeIndex] : (AtkComponentNode*)IntPtr.Zero;
            if ((IntPtr)pPartyMemberNode != IntPtr.Zero)
            {
                var pIconNode = pPartyMemberNode->Component->UldManager.NodeListSize > iconNodeIndex ? pPartyMemberNode->Component->UldManager.NodeList[iconNodeIndex] : (AtkResNode*)IntPtr.Zero;
                if ((IntPtr)pIconNode != IntPtr.Zero)
                {
                    //var checkPos = new Vector2(-22, 22) * pPartyList->Scale;
                    var checkPos = new Vector2(-32, 8) * pPartyList->Scale;
                    var checkSize = new Vector2(pIconNode->Width / 2, pIconNode->Height / 2) * pPartyList->Scale;

                    var foodPos = new Vector2(-32, 9) * pPartyList->Scale;
                    var foodSize = new Vector2(pIconNode->Width / 3, (int)((float)pIconNode->Width / 3 / 24 * 32)) * pPartyList->Scale;


                    var iconPos = new Vector2(pPartyList->X + pPartyMemberNode->AtkResNode.X * pPartyList->Scale + pIconNode->X * pPartyList->Scale + pIconNode->Width  * pPartyList->Scale / 2,
                                              pPartyList->Y + pPartyMemberNode->AtkResNode.Y * pPartyList->Scale + pIconNode->Y * pPartyList->Scale + pIconNode->Height * pPartyList->Scale / 2);
                    checkPos += iconPos;
                    foodPos += iconPos;

                    //drawList.AddImage(mReadyCheckIconTexture.ImGuiHandle, iconPos, iconPos + iconSize, new Vector2(0.0f, 0.0f), new Vector2(0.5f, 1.0f));
                    //drawList.AddImage(foodTexture.ImGuiHandle, foodPos, foodPos + foodSize, new Vector2(0.0f, 0.0f), new Vector2(1.0f));
                    drawList.AddImage(checkTexture.ImGuiHandle, checkPos, checkPos + checkSize, new Vector2(0.5f, 0.0f), new Vector2(1.0f));
                }
            }
        }
    }
}
